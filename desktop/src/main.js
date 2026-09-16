const state = {
  api: "http://127.0.0.1:17865",
  games: [],
  settings: { lastGame: "Wilds", checkGameRunning: true, fixPakNumber: true, usePakModsDir: false, installOption: 0 },
  game: null,
  groups: [],
  mods: [],
  status: "正在连接后端...",
  busy: false,
  search: "",
  selectedIds: new Set(),
  activeId: null,
  pane: "info",
  moveTarget: 0,
  renameDrafts: {},
  modRenameDrafts: {},
  equipPicker: null,
  equipCatalog: { kind: "", items: [], loading: false }
};

const app = document.querySelector("#app");

import worldIcon from "./assets/icons/world.png";
import riseIcon from "./assets/icons/rise.png";
import wildsIcon from "./assets/icons/wilds.png";
const gameIcons = { World: worldIcon, Rise: riseIcon, Wilds: wildsIcon };

const iconGear = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 15.5A3.5 3.5 0 1 0 12 8.5a3.5 3.5 0 0 0 0 7Zm7.4-2.6.1-.9-.1-.9 1.9-1.5-1.8-3.2-2.3.8a7 7 0 0 0-1.6-.9L15.2 3h-3.7l-.4 2.3c-.6.2-1.1.5-1.6.9l-2.3-.9-1.8 3.2 1.9 1.5-.1.9.1.9-1.9 1.5 1.8 3.2 2.3-.8c.5.4 1 .7 1.6.9l.4 2.3h3.7l.4-2.3c.6-.2 1.1-.5 1.6-.9l2.3.8 1.8-3.2-1.9-1.5Z"/></svg>`;
const iconPlay = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5.5v13l11-6.5-11-6.5Z"/></svg>`;
const iconMinimize = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14"/></svg>`;
const iconMaximize = `<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="6" y="6" width="12" height="12" rx="1.5"/></svg>`;
const iconClose = `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/></svg>`;

function apiUrl(path) {
  return `${state.api}${path}`;
}

async function request(path, options = {}) {
  const res = await fetch(apiUrl(path), {
    headers: { "Content-Type": "application/json" },
    ...options
  });
  if (!res.ok) {
    throw new Error(`请求失败 (${res.status})`);
  }
  return res.json();
}

function applyWorkspace(data, status) {
  const nextGameId = data.game?.id;
  const gameChanged = Boolean(nextGameId && state.game?.id && nextGameId !== state.game.id);
  if (data.game) state.game = data.game;
  if (data.settings) state.settings = data.settings;
  if (data.groups) state.groups = data.groups;
  if (data.mods) state.mods = data.mods;
  if (data.games) state.games = data.games;
  state.status = status ?? data.status ?? state.status;
  if (gameChanged) {
    state.renameDrafts = {};
    state.modRenameDrafts = {};
    state.selectedIds = new Set();
    state.activeId = null;
    if (state.pane === "mod") state.pane = "info";
  }
  if (!state.groups.some(group => group.id === state.moveTarget)) {
    state.moveTarget = state.groups[0]?.id ?? 0;
  }
  const ids = new Set(state.mods.map(mod => mod.id));
  state.selectedIds = new Set([...state.selectedIds].filter(id => ids.has(id)));
  if (state.activeId && !ids.has(state.activeId)) {
    state.activeId = null;
    if (state.pane === "mod") state.pane = "info";
  }
  for (const id of Object.keys(state.renameDrafts)) {
    const group = state.groups.find(item => item.id === Number(id));
    if (!group || group.name === state.renameDrafts[id]) {
      delete state.renameDrafts[id];
    }
  }
  for (const id of Object.keys(state.modRenameDrafts)) {
    const mod = state.mods.find(item => item.id === Number(id));
    if (!mod || mod.name === state.modRenameDrafts[id]) {
      delete state.modRenameDrafts[id];
    }
  }
}

async function loadBootstrap() {
  if (window.mhModManager?.apiBase) {
    state.api = await window.mhModManager.apiBase();
  }
  const data = await request("/api/bootstrap");
  state.games = data.games;
  state.settings = data.settings;
  applyWorkspace(data.workspace, "就绪");
}

async function run(task, fallback = "操作失败") {
  if (state.busy) return;
  state.busy = true;
  render();
  try {
    const data = await task();
    applyWorkspace(data);
  } catch (error) {
    state.status = error.message || fallback;
  } finally {
    state.busy = false;
    render();
  }
}

function selectedMods() {
  return state.mods.filter(mod => state.selectedIds.has(mod.id));
}

function filteredMods(groupId) {
  const query = state.search.trim().toLowerCase();
  return state.mods.filter(mod => {
    if (mod.groupId !== groupId) return false;
    if (!query) return true;
    const equipment = (mod.equipment ?? []).flatMap(item => [item.name, item.type, item.display]);
    return [mod.name, mod.category, mod.author, ...equipment].some(value => String(value ?? "").toLowerCase().includes(query));
  });
}

function visibleGroups() {
  const query = state.search.trim();
  return state.groups.filter(group => !query || filteredMods(group.id).length > 0);
}

function activeMod() {
  return state.mods.find(mod => mod.id === state.activeId) ?? null;
}

function previewSrc(mod) {
  return mod?.hasPreview ? apiUrl(mod.previewUrl) : "";
}

function renderEquipment(mod) {
  const items = mod?.equipment ?? [];
  if (items.length === 0) {
    return '<div class="muted">未能从文件识别对应装备</div>';
  }

  const canEdit = !mod.enabled;
  return `<div class="equip-list">${items.map(item => `
    <div class="equip">
      <div>
        <div class="muted">${esc(item.type)}${item.isPfb ? " [pfb]" : ""}${item.isPak ? " [pak]" : ""}</div>
        <div>${esc(item.name)}</div>
      </div>
      <button class="btn" data-action="change-equip" data-kind="${esc(item.kind)}" data-id="${item.id}" data-pfb="${item.isPfb ? "true" : "false"}" ${!canEdit || state.busy ? "disabled" : ""} title="${canEdit ? "修改对应套装" : "请先禁用 MOD"}">修改</button>
    </div>`).join("")}</div>`;
}

function filteredEquipOptions() {
  const picker = state.equipPicker;
  if (!picker) return [];
  const query = picker.query.trim().toLowerCase();
  return (state.equipCatalog.items ?? []).filter(item => {
    if (picker.filterUsed && item.used && item.id !== picker.fromId) return false;
    if (!query) return true;
    return [item.name, item.type, String(item.id)].some(value => String(value ?? "").toLowerCase().includes(query));
  });
}

function renderEquipPicker() {
  const picker = state.equipPicker;
  if (!picker) return "";
  const options = filteredEquipOptions();
  const current = state.equipCatalog.items.find(item => item.id === picker.fromId);
  const catalogReady = state.equipCatalog.kind === picker.kind && !state.equipCatalog.loading;
  return `
    <div class="modal-mask" data-action="close-equip-picker">
      <div class="modal" data-stop="true">
        <div class="eyebrow">CHANGE EQUIPMENT</div>
        <h2 class="h1" style="font-size:18px;margin:8px 0 12px">修改对应套装</h2>
        <div class="muted" style="margin-bottom:10px">当前：${esc(current?.type ?? picker.kind)} · ${esc(current?.name ?? String(picker.fromId))}${picker.isPfb ? " [pfb]" : ""}</div>
        <label class="search">
          <span class="muted">⌕</span>
          <input id="equip-search" placeholder="搜索装备名称" value="${esc(picker.query)}" />
        </label>
         <div class="equip-count muted">${state.equipCatalog.loading ? "正在加载装备列表..." : `${options.length} 项匹配装备`}</div>
         <div class="equip-options">
           ${!catalogReady ? '<div class="empty">正在加载装备列表...</div>' : options.map(item => `
            <button class="equip-option${item.id === picker.toId ? " active" : ""}" data-action="pick-equip" data-id="${item.id}">
              <span>${esc(item.name)}</span>
              <span class="muted">${item.id}${item.used ? " · 已占用" : ""}</span>
             </button>`).join("") || '<div class="empty">没有匹配的装备</div>'}
        </div>
        <label class="check"><input type="checkbox" data-role="equip-with-tex" ${picker.withTex ? "checked" : ""} /> 同时修改贴图（可能影响外观）</label>
        <label class="check"><input type="checkbox" data-role="equip-filter-used" ${picker.filterUsed ? "checked" : ""} /> 过滤已被其他 MOD 占用的装备</label>
        <div class="actions" style="margin-top:12px">
          <button class="btn" data-action="close-equip-picker">取消</button>
           <button class="primary" data-action="apply-equip" ${state.busy || !catalogReady || picker.toId === picker.fromId ? "disabled" : ""}>确认修改</button>
        </div>
      </div>
    </div>`;
}

function switchClass(on) {
  return `switch${on ? " on" : ""}`;
}

function esc(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function render() {
  const game = state.game;
  const selected = selectedMods();
  const active = activeMod();
  const pane = state.pane === "settings" ? "settings" : active && state.pane === "mod" ? "mod" : "info";
  const list = app.querySelector(".list");
  const listScrollTop = list?.scrollTop ?? 0;
  const listScrollLeft = list?.scrollLeft ?? 0;
  const searchFocus = document.activeElement?.id === "search";
  const searchPos = searchFocus ? document.activeElement.selectionStart : null;
  const equipSearchFocus = document.activeElement?.id === "equip-search";
  const equipSearchPos = equipSearchFocus ? document.activeElement.selectionStart : null;
  app.innerHTML = `
    <div class="app">
      <header class="titlebar">
        <div class="brand"><span class="mark">M</span>MH-MOD-MANAGER</div>
        <div class="win-controls">
          <button class="win-btn" data-action="win-minimize" title="最小化" aria-label="最小化">${iconMinimize}</button>
          <button class="win-btn" data-action="win-maximize" title="最大化/还原" aria-label="最大化/还原">${iconMaximize}</button>
          <button class="win-btn close" data-action="win-close" title="关闭" aria-label="关闭">${iconClose}</button>
        </div>
      </header>
      <div class="shell">
        <aside class="sidebar">
          ${state.games.map(item => {
            const icon = gameIcons[item.id];
            return `
              <div class="game${game?.id === item.id ? " active" : ""}" data-action="select-game" data-id="${item.id}" role="button" tabindex="0">
                <span class="bar"></span>
                <span class="game-icon">${icon ? `<img src="${icon}" alt="" />` : esc(item.shortName.slice(0, 1))}</span>
                <span>
                  <div>${esc(item.shortName)}</div>
                  <div class="muted">${item.installed ? "已安装" : "未配置"}</div>
                </span>
                <span class="game-tools">
                  ${item.installed ? '<span class="dot"></span>' : ""}
                  <button class="icon" data-action="game-settings" data-id="${item.id}" title="设置" aria-label="设置">${iconGear}</button>
                  <button class="icon" data-action="game-launch" data-id="${item.id}" title="启动游戏" aria-label="启动游戏" ${item.installed ? "" : "disabled"}>${iconPlay}</button>
                </span>
              </div>`;
          }).join("")}
        </aside>
        <main class="workspace">
          <div class="toolbar">
            <h1 class="h1">${esc(game?.displayName ?? "mh-mod-manager")}</h1>
          </div>
          <div class="searchrow">
            <label class="search">
              <span class="muted">⌕</span>
              <input id="search" placeholder="搜索 MOD 名称、类型、作者或装备" value="${esc(state.search)}" />
            </label>
            <div class="stats">
              <div class="stat"><b>${state.mods.length}</b><span class="muted">全部</span></div>
              <div class="stat"><b style="color:var(--accent)">${state.mods.filter(m => m.enabled).length}</b><span class="muted">已启用</span></div>
            </div>
          </div>
          <div class="batch card">
            <button class="btn" data-action="refresh" ${state.busy ? "disabled" : ""}>刷新</button>
            <button class="btn" data-action="create-group" ${state.busy ? "disabled" : ""}>新建分组</button>
            <button class="btn" data-action="select-all">全选</button>
            <button class="btn" data-action="clear-selection">清除选择</button>
            <span class="muted">已选 ${selected.length} 个 MOD</span>
            <span class="muted">移动到</span>
            <select id="move-target">
              ${state.groups.map(group => `<option value="${group.id}" ${group.id === state.moveTarget ? "selected" : ""}>${esc(group.name)}</option>`).join("")}
            </select>
            <button class="primary" data-action="move-selected" ${state.busy || selected.length === 0 ? "disabled" : ""}>批量移动</button>
            <button class="primary" data-action="import" ${state.busy ? "disabled" : ""}>导入</button>
          </div>
          <div class="list">
            ${visibleGroups().map(group => {
              const mods = filteredMods(group.id);
              const show = !group.collapsed || state.search.trim();
              const name = state.renameDrafts[group.id] ?? group.name;
              return `
                <section class="card group">
                  <div class="group-head">
                    <div class="group-title">
                      <button class="icon" data-action="toggle-group" data-id="${group.id}">${show ? "▾" : "▸"}</button>
                      <input data-role="group-name" data-id="${group.id}" value="${esc(name)}" />
                      <span class="muted">${group.enabledCount} / ${group.totalCount} 已启用</span>
                    </div>
                    <div class="row-actions">
                      <button class="icon" data-action="group-up" data-id="${group.id}">↑</button>
                      <button class="icon" data-action="group-down" data-id="${group.id}">↓</button>
                      ${group.isDefault ? "" : `<button class="icon" data-action="delete-group" data-id="${group.id}" title="删除分组" aria-label="删除分组"><svg class="trash-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/></svg></button>`}
                      <button class="${switchClass(group.totalCount > 0 && group.enabledCount === group.totalCount)}" data-action="group-enable" data-id="${group.id}" data-on="${group.totalCount > 0 && group.enabledCount === group.totalCount}"></button>
                    </div>
                  </div>
                  ${show ? `<div class="mods">${mods.map(mod => `
                    <article class="mod${state.selectedIds.has(mod.id) ? " selected" : ""}${state.activeId === mod.id ? " active" : ""}" data-action="open-mod" data-id="${mod.id}">
                      <input type="checkbox" data-role="select-mod" data-id="${mod.id}" ${state.selectedIds.has(mod.id) ? "checked" : ""} />
                      <div class="thumb">${mod.hasPreview ? `<img src="${esc(previewSrc(mod))}" alt="" />` : esc(mod.category)}</div>
                      <div class="meta">
                        <input class="name" data-role="mod-name" data-id="${mod.id}" value="${esc(state.modRenameDrafts[mod.id] ?? mod.name)}" />
                        <div class="sub">
                          <span class="muted">${esc(mod.version)}</span>
                          <span class="muted">${mod.fileCount} 个文件</span>
                          <select data-role="mod-group" data-id="${mod.id}">
                            ${state.groups.map(item => `<option value="${item.id}" ${item.id === mod.groupId ? "selected" : ""}>${esc(item.name)}</option>`).join("")}
                          </select>
                        </div>
                      </div>
                      <div class="row-actions">
                        <button class="icon" data-action="mod-up" data-id="${mod.id}">↑</button>
                        <button class="icon" data-action="mod-down" data-id="${mod.id}">↓</button>
                        <button class="${switchClass(mod.enabled)}" data-action="mod-enable" data-id="${mod.id}" data-on="${mod.enabled}"></button>
                      </div>
                    </article>`).join("") || '<div class="empty">这个分组还没有 MOD</div>'}</div>` : ""}
                </section>`;
            }).join("") || '<div class="empty">还没有导入任何 MOD</div>'}
          </div>
        </main>
        <aside class="detail">
          ${pane === "settings" ? `
            <div class="field">
              <span>游戏目录</span>
              <div class="muted">${esc(game?.path || "未设置游戏目录")}</div>
              <div class="actions stack">
                <button class="btn" data-action="pick-game">选择游戏目录</button>
                <button class="btn" data-action="open-folder" ${game?.path ? "" : "disabled"}>打开游戏目录</button>
              </div>
            </div>
            <div class="field">
              <span>部署行为</span>
              <label class="check"><input type="checkbox" data-role="setting" data-key="checkGameRunning" ${state.settings.checkGameRunning ? "checked" : ""} /> 游戏运行时阻止修改</label>
              <label class="check"><input type="checkbox" data-role="setting" data-key="fixPakNumber" ${state.settings.fixPakNumber ? "checked" : ""} /> 自动修复 PAK 编号</label>
              <label class="check"><input type="checkbox" data-role="setting" data-key="usePakModsDir" ${state.settings.usePakModsDir ? "checked" : ""} /> 使用 pak_mods 文件夹（崛起/荒野）</label>
              <div class="muted" style="font-size:12px">开启后 PAK 按列表顺序命名为 X0000-MOD名.pak 部署到游戏根目录 pak_mods；关闭则按原方式重命名后放根目录。</div>
            </div>
            <div class="field">
              <span>安装源文件</span>
              <select data-role="install-option">
                <option value="0" ${state.settings.installOption === 0 ? "selected" : ""}>不复制，保留原位置</option>
                <option value="1" ${state.settings.installOption === 1 ? "selected" : ""}>复制到工作区</option>
                <option value="2" ${state.settings.installOption === 2 ? "selected" : ""}>移动到工作区</option>
              </select>
            </div>
          ` : pane === "mod" && active ? `
            <div class="eyebrow">MOD PREVIEW</div>
            <h2 class="h1" style="font-size:21px;margin:8px 0 0">${esc(active.name)}</h2>
            <div class="preview-box">${active.hasPreview ? `<img src="${esc(previewSrc(active))}" alt="" />` : '<span class="muted">没有预览图</span>'}</div>
             <div class="card">
                <div class="kv"><span class="muted">类型</span><span style="color:var(--accent)">${esc(active.category)}</span></div>
               <div class="kv"><span class="muted">版本</span><span>${esc(active.version)}</span></div>
              <div class="kv"><span class="muted">作者</span><span>${esc(active.author)}</span></div>
              <div class="kv"><span class="muted">文件</span><span>${active.fileCount} 个文件</span></div>
              <div class="kv"><span class="muted">分组</span><span>${esc(active.groupName)}</span></div>
               <div class="kv"><span class="muted">导入时间</span><span>${esc(active.installedAt)}</span></div>
             </div>
             <div class="field">
               <span>游戏内装备</span>
               ${renderEquipment(active)}
                ${active.enabled ? '<div class="muted">禁用 MOD 后可修改对应套装</div>' : '<div class="muted">将文件重定向到另一套游戏内装备，与原版盒子一致</div>'}
             </div>
             <div class="actions mod-tools">
               <button class="primary" data-action="update-mod" data-id="${active.id}" ${state.busy ? "disabled" : ""}>↻ 更新 MOD</button>
               <button class="btn" data-action="open-mod-folder" data-id="${active.id}" ${state.busy ? "disabled" : ""}>▣ 查看文件</button>
               <button class="btn danger" data-action="uninstall" data-id="${active.id}" title="删除 MOD" ${state.busy ? "disabled" : ""}><svg class="trash-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3"/></svg><span>删除 MOD</span></button>
             </div>
           ` : `
            <div class="empty-detail"></div>
           `}
        </aside>
      </div>
      <footer class="footer">
        <span class="muted">${state.busy ? "处理中..." : esc(state.status)}</span>
        <button class="btn" data-action="clean" ${state.busy || !game?.installed ? "disabled" : ""}>清理部署文件</button>
      </footer>
      <div class="dropmask" id="dropmask"><div class="dropcard">松开以批量导入 MOD</div></div>
      ${renderEquipPicker()}
    </div>
  `;
  const nextList = app.querySelector(".list");
  if (nextList) {
    nextList.scrollTop = listScrollTop;
    nextList.scrollLeft = listScrollLeft;
  }
  if (searchFocus) {
    const input = app.querySelector("#search");
    input?.focus();
    if (searchPos != null) input.setSelectionRange(searchPos, searchPos);
  }
  if (equipSearchFocus) {
    const input = app.querySelector("#equip-search");
    input?.focus();
    if (equipSearchPos != null) input.setSelectionRange(equipSearchPos, equipSearchPos);
  }
}

function onClick(event) {
  if (event.target.closest("select, option, input, textarea, label")) return;
  const target = event.target.closest("[data-action]");
  if (!target || !state.game) return;
  if (target.closest("[data-stop]") && !event.target.closest("[data-action]")) return;
  event.stopPropagation();
  const id = Number(target.dataset.id);
  const action = target.dataset.action;
  const gameId = state.game.id;

  if (action === "select-game") {
    run(() => request(`/api/games/${target.dataset.id}/select`, { method: "POST" }));
    return;
  }
  if (action === "game-settings") {
    run(async () => {
      const workspace = await request(`/api/games/${target.dataset.id}/select`, { method: "POST" });
      state.pane = "settings";
      return workspace;
    });
    return;
  }
  if (action === "game-launch") {
    run(async () => {
      await request(`/api/games/${target.dataset.id}/select`, { method: "POST" });
      const result = await request(`/api/games/${target.dataset.id}/launch`, { method: "POST" });
      const workspace = await request(`/api/workspace/${target.dataset.id}`);
      workspace.status = result.status || "已通过 Steam 启动游戏";
      return workspace;
    });
    return;
  }
  if (action === "win-minimize") {
    window.mhModManager?.minimize?.();
    return;
  }
  if (action === "win-maximize") {
    window.mhModManager?.toggleMaximize?.();
    return;
  }
  if (action === "win-close") {
    window.mhModManager?.close?.();
    return;
  }
  if (action === "open-mod") {
    state.activeId = id;
    state.pane = "mod";
    render();
    return;
  }
  if (action === "select-all") {
    visibleGroups().forEach(group => {
      if (group.collapsed && !state.search.trim()) return;
      filteredMods(group.id).forEach(mod => state.selectedIds.add(mod.id));
    });
    render();
    return;
  }
  if (action === "clear-selection") {
    state.selectedIds.clear();
    render();
    return;
  }
  if (action === "refresh") {
    run(() => request(`/api/games/${gameId}/refresh`, { method: "POST" }));
    return;
  }
  if (action === "create-group") {
    run(() => request(`/api/games/${gameId}/groups`, { method: "POST", body: JSON.stringify({ name: "新分组" }) }));
    return;
  }
  if (action === "import") {
    importMods();
    return;
  }
  if (action === "pick-game") {
    pickGame();
    return;
  }
  if (action === "open-folder") {
    openGameFolder();
    return;
  }
  if (action === "open-mod-folder") {
    openModFolder(id);
    return;
  }
  if (action === "clean") {
    run(() => request(`/api/games/${gameId}/clean`, { method: "POST" }));
    return;
  }
  if (action === "move-selected") {
    run(() => request(`/api/games/${gameId}/groups/${state.moveTarget}/mods`, {
      method: "POST",
      body: JSON.stringify({ modIds: [...state.selectedIds] })
    }));
    return;
  }
  if (action === "toggle-group") {
    const group = state.groups.find(item => item.id === id);
    run(() => request(`/api/games/${gameId}/groups/${id}`, {
      method: "PATCH",
      body: JSON.stringify({ collapsed: !group.collapsed })
    }));
    return;
  }
  if (action === "group-up" || action === "group-down") {
    run(() => request(`/api/games/${gameId}/groups/${id}/move`, {
      method: "POST",
      body: JSON.stringify({ delta: action === "group-up" ? -1 : 1 })
    }));
    return;
  }
  if (action === "delete-group") {
    run(() => request(`/api/games/${gameId}/groups/${id}`, { method: "DELETE" }));
    return;
  }
  if (action === "group-enable") {
    run(() => request(`/api/games/${gameId}/groups/${id}/enable`, {
      method: "POST",
      body: JSON.stringify({ enabled: target.dataset.on !== "true" })
    }));
    return;
  }
  if (action === "mod-up" || action === "mod-down") {
    run(() => request(`/api/games/${gameId}/mods/${id}/move`, {
      method: "POST",
      body: JSON.stringify({ delta: action === "mod-up" ? -1 : 1 })
    }));
    return;
  }
  if (action === "update-mod") {
    updateMod(id);
    return;
  }
  if (action === "uninstall") {
    run(() => request(`/api/games/${gameId}/mods/${id}`, { method: "DELETE" }));
    return;
  }
  if (action === "mod-enable") {
    run(() => request(`/api/games/${gameId}/mods/${id}/enable`, {
      method: "POST",
      body: JSON.stringify({ enabled: target.dataset.on !== "true" })
    }));
    return;
  }
  if (action === "change-equip") {
    openEquipPicker(target.dataset.kind, Number(target.dataset.id), target.dataset.pfb === "true");
    return;
  }
  if (action === "close-equip-picker") {
    if (target.classList.contains("modal-mask") && event.target.closest("[data-stop]")) return;
    state.equipPicker = null;
    render();
    return;
  }
  if (action === "pick-equip") {
    if (state.equipPicker) {
      state.equipPicker.toId = Number(target.dataset.id);
      render();
    }
    return;
  }
  if (action === "apply-equip") {
    applyEquipChange();
  }
}

function onInput(event) {
  if (event.target.id === "search") {
    state.search = event.target.value;
    render();
    return;
  }
  if (event.target.dataset.role === "group-name") {
    state.renameDrafts[Number(event.target.dataset.id)] = event.target.value;
    return;
  }
  if (event.target.dataset.role === "mod-name") {
    state.modRenameDrafts[Number(event.target.dataset.id)] = event.target.value;
    return;
  }
  if (event.target.id === "equip-search" && state.equipPicker) {
    state.equipPicker.query = event.target.value;
    render();
  }
}

function onChange(event) {
  const target = event.target;
  if (target.id === "move-target") {
    state.moveTarget = Number(target.value);
    return;
  }
  if (target.dataset.role === "group-name" && state.game) {
    run(() => request(`/api/games/${state.game.id}/groups/${target.dataset.id}`, {
      method: "PATCH",
      body: JSON.stringify({ name: target.value })
    }));
    return;
  }
  if (target.dataset.role === "mod-name" && state.game) {
    const id = Number(target.dataset.id);
    run(() => request(`/api/games/${state.game.id}/mods/${id}`, {
      method: "PATCH",
      body: JSON.stringify({ name: target.value })
    }));
    return;
  }
  if (target.dataset.role === "select-mod") {
    const id = Number(target.dataset.id);
    if (target.checked) state.selectedIds.add(id);
    else state.selectedIds.delete(id);
    render();
    return;
  }
  if (target.dataset.role === "mod-group" && state.game) {
    run(() => request(`/api/games/${state.game.id}/mods/${target.dataset.id}/group`, {
      method: "POST",
      body: JSON.stringify({ groupId: Number(target.value) })
    }));
    return;
  }
  if (target.dataset.role === "setting" || target.dataset.role === "install-option") {
    saveSettings();
    return;
  }
  if (target.dataset.role === "equip-with-tex" && state.equipPicker) {
    state.equipPicker.withTex = target.checked;
    return;
  }
  if (target.dataset.role === "equip-filter-used" && state.equipPicker) {
    state.equipPicker.filterUsed = target.checked;
    render();
  }
}

async function saveSettings() {
  const checkGameRunning = app.querySelector('[data-key="checkGameRunning"]')?.checked ?? state.settings.checkGameRunning;
  const fixPakNumber = app.querySelector('[data-key="fixPakNumber"]')?.checked ?? state.settings.fixPakNumber;
  const usePakModsDir = app.querySelector('[data-key="usePakModsDir"]')?.checked ?? state.settings.usePakModsDir;
  const installOption = Number(app.querySelector("[data-role='install-option']")?.value ?? state.settings.installOption);
  await run(async () => {
    const data = await request("/api/settings", {
      method: "PUT",
      body: JSON.stringify({ checkGameRunning, fixPakNumber, usePakModsDir, installOption })
    });
    applyWorkspace(data.workspace, "设置已保存");
    return data.workspace;
  });
}

async function openEquipPicker(kind, id, isPfb) {
  state.equipPicker = { kind, fromId: id, toId: id, isPfb, query: "", withTex: false, filterUsed: true };
  state.equipCatalog = { kind, items: [], loading: true };
  render();
  try {
    const data = await request(`/api/games/${state.game.id}/equipment?kind=${encodeURIComponent(kind)}`);
    if (!state.equipPicker || state.equipPicker.kind !== kind) return;
    state.equipCatalog = { kind, items: data.items ?? [] };
  } catch (error) {
    state.status = error.message || "无法加载装备列表";
    state.equipPicker = null;
  }
  render();
}

async function applyEquipChange() {
  const picker = state.equipPicker;
  if (!picker || !state.game || !state.activeId) return;
  await run(async () => {
    const data = await request(`/api/games/${state.game.id}/mods/${state.activeId}/equipment`, {
      method: "POST",
      body: JSON.stringify({
        kind: picker.kind,
        fromId: picker.fromId,
        toId: picker.toId,
        isPfb: picker.isPfb,
        withTex: picker.withTex
      })
    });
    if (!data.error) state.equipPicker = null;
    return data;
  }, "修改装备失败");
}

async function updateMod(id) {
  const path = window.mhModManager?.pickUpdate ? await window.mhModManager.pickUpdate() : "";
  if (!path) return;
  state.status = "正在更新 MOD...";
  await run(() => request(`/api/games/${state.game.id}/mods/${id}/update`, {
    method: "POST",
    body: JSON.stringify({ paths: [path] })
  }), "更新失败");
}

async function importMods(paths) {
  const picked = paths ?? (window.mhModManager ? await window.mhModManager.pickMods() : []);
  if (!picked?.length) return;
  state.status = `正在导入 ${picked.length} 个 MOD...`;
  await run(() => request(`/api/games/${state.game.id}/import`, {
    method: "POST",
    body: JSON.stringify({ paths: picked })
  }), "导入失败");
}

async function pickGame() {
  const folder = window.mhModManager ? await window.mhModManager.pickFolder() : "";
  if (!folder) return;
  await run(() => request(`/api/games/${state.game.id}/path`, {
    method: "PUT",
    body: JSON.stringify({ path: folder })
  }));
}

async function openGameFolder() {
  const folder = state.game?.path;
  if (!folder) {
    state.status = "未设置游戏目录";
    render();
    return;
  }
  if (!window.mhModManager?.openPath) {
    state.status = "当前环境无法打开文件夹";
    render();
    return;
  }
  const error = await window.mhModManager.openPath(folder);
  state.status = error ? `无法打开目录: ${error}` : "已打开游戏目录";
  render();
}

async function openModFolder(id) {
  if (!window.mhModManager?.openPath || !state.game) {
    state.status = "当前环境无法打开文件夹";
    render();
    return;
  }

  try {
    const result = await request(`/api/games/${state.game.id}/mods/${id}/folder`);
    const error = await window.mhModManager.openPath(result.path);
    state.status = error ? `无法打开 MOD 文件夹: ${error}` : "已打开 MOD 文件夹";
  } catch (error) {
    state.status = error.message || "无法打开 MOD 文件夹";
  }
  render();
}

function setupDrop() {
  const mask = () => document.querySelector("#dropmask");
  window.addEventListener("dragover", event => {
    event.preventDefault();
    mask()?.classList.add("show");
  });
  window.addEventListener("dragleave", event => {
    if (event.relatedTarget === null) mask()?.classList.remove("show");
  });
  window.addEventListener("drop", event => {
    event.preventDefault();
    mask()?.classList.remove("show");
    const paths = [...(event.dataTransfer?.files ?? [])]
      .map(file => window.mhModManager?.filePath?.(file) || file.path)
      .filter(Boolean);
    if (paths.length) importMods(paths);
  });
}

async function start() {
  app.addEventListener("click", onClick);
  app.addEventListener("input", onInput);
  app.addEventListener("change", onChange);
  window.addEventListener("keydown", event => {
    if (event.key === "Escape" && state.equipPicker && !state.busy) {
      state.equipPicker = null;
      render();
    }
  });
  render();
  setupDrop();
  try {
    await loadBootstrap();
    state.status = "就绪";
  } catch (error) {
    state.status = error.message || "无法连接后端";
  }
  render();
}

start();
