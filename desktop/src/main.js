import { applyAppearance, defaultAppearance, readBackgroundImage, renderAppearance } from "./appearance.js";

const state = {
  api: (import.meta.env.DEV && import.meta.env.VITE_MH_MOD_MANAGER_API) || "http://127.0.0.1:17865",
  games: [],
  settings: { lastGame: "Wilds", checkGameRunning: true, installOption: 0 },
  game: null,
  groups: [],
  mods: [],
  status: "正在连接后端...",
  statusError: false,
  busy: false,
  search: "",
  activeId: null,
  activeComponentId: null,
  pane: "info",
  gameSettingsId: null,
  contextMenu: null,
  deploymentConflicts: null,
  conflictDialog: null,
  deletePrompt: null,
  renameDrafts: {},
  modRenameDrafts: {},
  bundleOpenId: null,
  equipPicker: null,
  equipCatalog: { kind: "", items: [], loading: false },
  passwordPrompt: null,
  sourcePrompt: null,
  nexusUpdates: {},
  nexusFiles: null,
  nexusAccess: null,
  nexusKeyDraft: null
};

const app = document.querySelector("#app");

import worldIcon from "./assets/icons/world.png";
import riseIcon from "./assets/icons/rise.png";
import wildsIcon from "./assets/icons/wilds.png";
import iconPlus from "lucide-static/icons/plus.svg?raw";
import iconRefresh from "lucide-static/icons/refresh-cw.svg?raw";
import iconFolderPlus from "lucide-static/icons/folder-plus.svg?raw";
import iconArrowUp from "lucide-static/icons/arrow-up.svg?raw";
import iconDownload from "lucide-static/icons/download.svg?raw";
import iconFolder from "lucide-static/icons/folder.svg?raw";
import iconLink from "lucide-static/icons/link.svg?raw";
import iconTrash from "lucide-static/icons/trash-2.svg?raw";
import iconSearch from "lucide-static/icons/search.svg?raw";
import iconGear from "lucide-static/icons/settings.svg?raw";
import iconPlay from "lucide-static/icons/play.svg?raw";
import iconMenu from "lucide-static/icons/ellipsis.svg?raw";
const gameIcons = { World: worldIcon, Rise: riseIcon, Wilds: wildsIcon };

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
  const sameGame = nextGameId != null && nextGameId === state.game?.id;
  const gameChanged = Boolean(nextGameId && state.game?.id && !sameGame);
  if (data.game) state.game = data.game;
  if (data.settings) {
    if (state.settings.nexusApiKey !== data.settings.nexusApiKey) {
      state.nexusAccess = null;
      state.nexusUpdates = {};
      state.nexusFiles = null;
    }
    state.settings = data.settings;
  }
  if (data.groups) state.groups = data.groups;
  if (data.mods) state.mods = data.mods;
  if (data.games) state.games = data.games;
  if (data.game) state.games = state.games.map(game => game.id === data.game.id ? data.game : game);
  if (Array.isArray(data.deploymentConflicts)) {
    // Bootstrap and game switches establish a baseline, not a warning.
    if (sameGame && state.deploymentConflicts && !data.error) {
      const key = item => `${item.winnerId}:${item.overwrittenId}`;
      const old = new Set(state.deploymentConflicts.map(key));
      const added = data.deploymentConflicts.filter(item => !old.has(key(item)));
      if (added.length) state.conflictDialog = { deployment: added };
    }
    state.deploymentConflicts = data.deploymentConflicts;
  }
  if (data.equipmentConflicts?.length) state.conflictDialog = { equipment: true, name: activeMod()?.name };
  state.status = status ?? data.status ?? state.status;
  // 覆盖式消息(如"设置已保存")视为正常;否则跟随后端的 Error 标记决定警告色。
  state.statusError = status != null ? false : Boolean(data.error);
  if (gameChanged) {
    state.nexusUpdates = {};
    state.nexusFiles = null;
    state.renameDrafts = {};
    state.modRenameDrafts = {};
    state.activeId = null;
    state.activeComponentId = null;
    state.bundleOpenId = null;
    if (state.pane === "mod") state.pane = "info";
  }
  const ids = new Set(state.mods.map(mod => mod.id));
  for (const id of Object.keys(state.nexusUpdates)) {
    if (!ids.has(Number(id))) delete state.nexusUpdates[id];
  }
  if (state.bundleOpenId != null && !ids.has(state.bundleOpenId)) {
    state.bundleOpenId = null;
  }
  if (state.activeId && !ids.has(state.activeId)) {
    state.activeId = null;
    if (state.pane === "mod") state.pane = "info";
  }
  if (!activeMod()?.components?.some(item => item.id === state.activeComponentId)) state.activeComponentId = null;
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
    state.statusError = true;
  } finally {
    state.busy = false;
    render();
  }
}

function filteredMods(groupId) {
  const query = state.search.trim().toLowerCase();
  return state.mods.filter(mod => {
    if (mod.groupId !== groupId) return false;
    if (!query) return true;
    const equipment = [mod, ...(mod.components ?? [])].flatMap(record =>
      (record.equipment ?? []).flatMap(item => [item.name, item.type, item.display]));
    const components = (mod.components ?? []).flatMap(item => [item.name, item.author, item.category]);
    return [mod.name, mod.category, mod.author, ...components, ...equipment].some(value => String(value ?? "").toLowerCase().includes(query));
  });
}

function visibleGroups() {
  const query = state.search.trim();
  return state.groups.filter(group => !query || filteredMods(group.id).length > 0);
}

function activeMod() {
  return state.mods.find(mod => mod.id === state.activeId) ?? null;
}

function activeComponent(mod = activeMod()) {
  return mod?.components?.find(item => item.id === state.activeComponentId) ?? null;
}

function tool(action, label, svg, { className = "btn", id, component, disabled = state.busy } = {}) {
  return `<button class="${className} tool-button" data-action="${action}"${id != null ? ` data-id="${id}"` : ""}${component != null ? ` data-comp="${component}"` : ""} title="${esc(label)}" aria-label="${esc(label)}" ${disabled ? "disabled" : ""}>${svg}</button>`;
}

function previewSrc(mod) {
  return mod?.hasPreview ? apiUrl(mod.previewUrl) : "";
}

function renderEquipment(mod, component) {
  const items = (component ?? mod).equipment ?? [];
  if (items.length === 0) {
    return "";
  }

  const canEdit = !mod.enabled;
  return `<div class="field"><span>游戏内装备</span><div class="equip-list">${items.map(item => `
    <div class="equip">
      <div>
        <div class="muted">${esc(item.type)}${item.isPfb ? " [pfb]" : ""}${item.isPak ? " [pak]" : ""}</div>
        <div>${esc(item.name)}</div>
      </div>
      <button class="btn" data-action="change-equip" data-kind="${esc(item.kind)}" data-id="${item.id}" data-pfb="${item.isPfb ? "true" : "false"}" ${!canEdit || state.busy ? "disabled" : ""} title="${canEdit ? "修改对应套装" : "请先禁用 MOD"}">修改</button>
    </div>`).join("")}</div></div>`;
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

function renderPasswordPrompt() {
  const prompt = state.passwordPrompt;
  if (!prompt) return "";
  const file = String(prompt.file ?? "").split(/[\\/]/).pop();
  return `
    <div class="modal-mask" data-action="close-password">
      <div class="modal" data-stop="true">
        <div class="eyebrow">ARCHIVE PASSWORD</div>
        <h2 class="h1" style="font-size:18px;margin:8px 0 12px">输入压缩包密码</h2>
        <div class="muted" style="margin-bottom:10px">${esc(file)}${prompt.message ? ` · ${esc(prompt.message)}` : ""}</div>
        <input id="import-password" type="password" placeholder="压缩包密码" autocomplete="off" spellcheck="false" style="width:100%;box-sizing:border-box" />
        <div class="actions" style="margin-top:12px">
          <button class="btn" data-action="close-password">取消</button>
          <button class="primary" data-action="submit-password">确认</button>
        </div>
      </div>
    </div>`;
}

function promptPassword(file, message) {
  return new Promise(resolve => {
    state.passwordPrompt = { file, message, resolve };
    render();
    document.getElementById("import-password")?.focus();
  });
}

function closePasswordPrompt(result) {
  const prompt = state.passwordPrompt;
  if (!prompt) return;
  state.passwordPrompt = null;
  prompt.resolve(result);
  render();
}

function submitPassword() {
  const input = document.getElementById("import-password");
  const value = (input?.value ?? "").trim();
  closePasswordPrompt(value || null);
}

// 来源地址:详情页按钮打开弹窗,填写后保存到 MOD 并用默认浏览器打开。
function renderSourcePrompt() {
  const prompt = state.sourcePrompt;
  if (!prompt) return "";
  return `
    <div class="modal-mask" data-action="close-source">
      <div class="modal" data-stop="true">
        <div class="eyebrow">MOD SOURCE</div>
        <h2 class="h1" style="font-size:18px;margin:8px 0 12px">来源地址</h2>
        <div class="muted" style="margin-bottom:10px">填写 MOD 的来源网页（Nexus 页面、作者发布页等），留空则不保存。</div>
        <input id="source-url" placeholder="https://www.nexusmods.com/monsterhunterwilds/mods/1234" value="${esc(prompt.value)}" autocomplete="off" spellcheck="false" style="width:100%;box-sizing:border-box" />
        ${prompt.hint ? `<div class="status-error" style="margin-top:8px;font-size:12px">${esc(prompt.hint)}</div>` : ""}
        <div class="actions" style="margin-top:12px">
          <button class="btn" data-action="close-source">取消</button>
          <button class="primary" data-action="apply-source">打开来源网页</button>
        </div>
      </div>
    </div>`;
}

function normalizeSourceUrl(value) {
  const text = String(value ?? "").trim();
  if (!text) return "";
  if (/^https?:\/\/\S+$/i.test(text)) return text;
  // 允许直接粘贴 "nexusmods.com/xxx" 这类省略协议的网址。
  if (/^[\w.-]+\.[a-z]{2,}(?:[/?#]\S*)?$/i.test(text)) return `https://${text}`;
  return "";
}

async function applySourceUrl() {
  const prompt = state.sourcePrompt;
  if (!prompt || !state.game) return;
  const input = document.getElementById("source-url");
  const raw = (input?.value ?? prompt.value ?? "").trim();
  const url = normalizeSourceUrl(raw);
  if (!url) {
    state.sourcePrompt = {
      ...prompt,
      value: raw,
      hint: raw ? "网址格式不正确，请填写 http:// 或 https:// 链接" : "请先填写来源网址"
    };
    render();
    document.getElementById("source-url")?.focus();
    return;
  }

  state.sourcePrompt = null;
  try {
    const workspace = await request(`/api/games/${state.game.id}/mods/${prompt.modId}`, {
      method: "PATCH",
      body: JSON.stringify({ homeUrl: url })
    });
    applyWorkspace(workspace, "已保存来源地址");
  } catch (error) {
    state.status = error.message || "来源地址保存失败";
    state.statusError = true;
  }

  render();
  openExternal(url);
}

// Nexus 更新:检查结果内联区、文件选择弹窗、验证/检查/下载动作。
// 未设置 API Key 时禁止一切 Nexus 相关请求(检查/验证/下载),只给出配置提示。
function nexusKeyMissing() {
  const key = state.settings.nexusApiKey ?? "";
  if (key.trim().length > 0) return false;
  state.status = "请先在设置中配置 Nexus API Key";
  state.statusError = true;
  render();
  return true;
}

function fmtSize(bytes) {
  if (!bytes || bytes <= 0) return "?";
  const mb = bytes / 1048576;
  return mb >= 1024 ? `${(mb / 1024).toFixed(1)} GB` : `${mb.toFixed(1)} MB`;
}

function renderNexusUpdate(mod) {
  const info = state.nexusUpdates[mod.id];
  return `
    <div class="field">
      <span>Nexus 更新</span>
      ${info ? (info.available
        ? `<div class="upd-found">发现新版本 ${esc(info.latestVersion || "?")}</div>
           <div class="muted" style="font-size:12px;margin-top:4px">${esc(info.latestFileName)} · ${fmtSize(info.latestFileSize)} · ${esc(info.latestUploadedAt)}</div>`
        : '<div class="muted">已是最新版本</div>')
        : `<div class="muted">${(state.settings.nexusApiKey ?? "").trim() ? "未检查" : "未配置 API Key"}</div>`}
    </div>`;
}

function nexusControls() {
  const hasKey = Boolean(state.settings.nexusApiKey?.trim());
  const canDownload = hasKey && state.nexusAccess?.allowed === true;
  return {
    checkDisabled: !hasKey || state.busy,
    downloadDisabled: !canDownload || state.busy,
    checkTitle: hasKey ? "检查 Nexus 更新" : "检查 Nexus 更新（未配置 API Key）",
    downloadTitle: canDownload ? "选择 Nexus 文件下载" : !hasKey ? "选择 Nexus 文件下载（未配置 API Key）"
      : state.nexusAccess?.checking ? "选择 Nexus 文件下载（正在验证权限）" : "选择 Nexus 文件下载（需要已验证的 Premium Key）"
  };
}

function renderNexusFiles() {
  const picker = state.nexusFiles;
  if (!picker) return "";
  return `
    <div class="modal-mask" data-action="close-nexus-files">
      <div class="modal" data-stop="true">
        <div class="eyebrow">NEXUS FILES</div>
        <h2 class="h1" style="font-size:18px;margin:8px 0 12px">选择要下载的文件</h2>
        <div class="equip-options">
          ${picker.files.map(file => `
            <button class="equip-option${file.fileId === picker.selectedFileId ? " active" : ""}" data-action="pick-nexus-file" data-id="${file.fileId}">
              <span>${esc(file.fileName)}</span>
              <span class="muted">${esc(file.version || "?")} · ${fmtSize(file.sizeBytes)} · ${esc(file.uploadedAt)}${file.categoryId === 1 || file.isPrimary ? " · 主文件" : ""}</span>
            </button>`).join("") || '<div class="empty">没有可用文件</div>'}
        </div>
        <div class="muted" style="font-size:12px;margin-top:8px">API 下载需要 Nexus Premium;免费账户请在 N 网页面手动下载后使用「更新 MOD」。</div>
        <div class="actions" style="margin-top:12px">
          <button class="btn" data-action="close-nexus-files">取消</button>
          <button class="primary" data-action="apply-nexus-download" ${state.busy || !picker.selectedFileId ? "disabled" : ""}>下载并更新</button>
        </div>
      </div>
    </div>`;
}

async function checkNexusUpdate(modId, selectFile = false) {
  if (state.busy || !state.game || nexusKeyMissing()) return;
  if (selectFile && nexusControls().downloadDisabled) return;
  state.busy = true;
  render();
  try {
    const data = (selectFile && state.nexusUpdates[modId]) ||
      await request(`/api/games/${state.game.id}/mods/${modId}/nexus/update-check`);
    if (data.error) {
      state.status = data.status;
      state.statusError = true;
    } else {
      state.nexusUpdates[modId] = data;
      state.status = data.available ? `发现新版本 ${data.latestVersion || "?"}` : "已是最新版本";
      state.statusError = false;
      if (selectFile) {
        const files = data.files ?? [];
        state.nexusFiles = {
          modId,
          files,
          selectedFileId: files.find(file => file.fileId === data.latestFileId)?.fileId ?? files[0]?.fileId
        };
      }
    }
  } catch (error) {
    state.status = error.message || "检查更新失败";
    state.statusError = true;
  } finally {
    state.busy = false;
    render();
  }
}

async function checkAllNexusUpdates() {
  if (state.busy || !state.game || nexusKeyMissing()) return;
  const targets = state.mods.filter(mod => mod.nexusId > 0);
  if (targets.length === 0) {
    state.status = "没有可检查的 MOD（需从 Nexus 文件名识别来源）";
    state.statusError = false;
    render();
    return;
  }

  state.busy = true;
  let available = 0;
  let lastError = "";
  try {
    for (let i = 0; i < targets.length; i++) {
      state.status = `正在检查更新 (${i + 1}/${targets.length})...`;
      render();
      try {
        const data = await request(`/api/games/${state.game.id}/mods/${targets[i].id}/nexus/update-check`);
        if (data.error) {
          lastError = `${targets[i].name}: ${data.status}`;
        } else {
          state.nexusUpdates[targets[i].id] = data;
          if (data.available) available++;
        }
      } catch (error) {
        lastError = `${targets[i].name}: ${error.message || "失败"}`;
      }
    }
    state.status = lastError
      ? `${available} 个 MOD 有更新。${lastError}`
      : `${available} 个 MOD 有更新`;
    state.statusError = Boolean(lastError);
  } finally {
    state.busy = false;
    render();
  }
}

async function applyNexusDownload() {
  const picker = state.nexusFiles;
  if (!picker?.selectedFileId || !state.game || nexusControls().downloadDisabled) return;
  const label = picker.files.find(file => file.fileId === picker.selectedFileId)?.fileName ?? "Nexus 文件";
  state.nexusFiles = null;
  state.busy = true;
  const passwords = [];
  try {
    while (true) {
      state.status = `正在通过 Nexus 下载更新...`;
      state.statusError = false;
      render();
      const data = await request(`/api/games/${state.game.id}/mods/${picker.modId}/nexus/download`, {
        method: "POST",
        body: JSON.stringify({ fileId: picker.selectedFileId, passwords })
      });
      applyWorkspace(data);
      if (!data.needPassword) {
        if (!data.error) {
          await refreshNexusUpdate(picker.modId);
        }
        break;
      }
      const password = await promptPassword(label, data.status);
      if (password == null) {
        state.status = "已取消下载";
        state.statusError = true;
        break;
      }
      passwords.push(password);
    }
  } catch (error) {
    state.status = error.message || "下载失败";
    state.statusError = true;
  } finally {
    state.busy = false;
    render();
  }
}

function syncNexusControls() {
  const { checkDisabled, downloadDisabled, checkTitle, downloadTitle } = nexusControls();
  for (const [action, disabled, title] of [
    ["check-nexus-update", checkDisabled, checkTitle], ["open-nexus-files", downloadDisabled, downloadTitle]
  ]) {
    const button = app.querySelector(`[data-action="${action}"]`);
    if (!button) continue;
    button.disabled = disabled;
    button.title = title;
    button.setAttribute("aria-label", title);
  }
  const account = app.querySelector('[data-role="nexus-account"]');
  if (account) account.textContent = nexusAccountLabel();
}

function nexusAccountLabel() {
  return state.nexusAccess?.label ?? "未配置 API Key";
}

async function refreshNexusAccess() {
  const key = state.settings.nexusApiKey?.trim();
  if (!key) return;
  const access = state.nexusAccess = { checking: true, allowed: false, label: "正在验证 Nexus Key…" };
  syncNexusControls();
  try {
    const data = await request("/api/nexus/validate", { method: "POST" });
    if (data.error) throw new Error(data.status || "验证失败");
    access.allowed = data.isPremium === true;
    access.label = `Nexus 账户：${data.name}（${access.allowed ? "可直接下载" : "不可直接下载"}）`;
  } catch (error) {
    access.label = error.message || "验证失败";
  } finally {
    access.checking = false;
    if (state.nexusAccess === access) syncNexusControls();
  }
}

function switchClass(on) {
  return `switch${on ? " on" : ""}`;
}

function componentCounts(mod) {
  const comps = mod.components ?? [];
  return { total: comps.length, enabled: comps.filter(item => item.enabled).length };
}

function renderComponentRows(mod) {
  const comps = mod.components ?? [];
  return comps.map((comp, index) => `
    <div class="comp-row${state.activeId === mod.id && state.activeComponentId === comp.id ? " active" : ""}" data-mod="${mod.id}" data-comp="${comp.id}"${comp.hasPreview ? ` data-preview="${esc(apiUrl(comp.previewUrl))}"` : ""}>
      <button class="comp-select" data-action="open-component" data-id="${mod.id}" data-comp="${comp.id}" title="${esc(comp.name)}">
        ${comp.hasPreview ? `<span class="comp-cover"><img src="${esc(previewSrc(comp))}" alt="" /></span>` : ""}
        <span class="comp-name">${esc(comp.name)}</span>
      </button>
      <span class="comp-tools">
        ${index === 0 ? "" : `<button class="icon" data-action="comp-up" data-mod="${mod.id}" data-comp="${comp.id}" title="上移（靠后覆盖靠前）">↑</button>`}
        ${index === comps.length - 1 ? "" : `<button class="icon" data-action="comp-down" data-mod="${mod.id}" data-comp="${comp.id}" title="下移（靠后覆盖靠前）">↓</button>`}
        <button class="${switchClass(comp.enabled)}" data-action="comp-enable" data-mod="${mod.id}" data-comp="${comp.id}" data-on="${comp.enabled}"></button>
        ${tool("component-menu", "组件菜单", iconMenu, { className: "icon", id: mod.id, component: comp.id })}
      </span>
    </div>`).join("");
}

function renderContextMenu() {
  const menu = state.contextMenu;
  if (!menu) return "";
  const game = state.games.find(item => item.id === menu.gameId);
  const x = Math.max(8, Math.min(menu.x, window.innerWidth - 184));
  const y = Math.max(8, Math.min(menu.y, window.innerHeight - 90));
  return `<div class="context-menu" style="left:${x}px;top:${y}px" role="group" aria-label="${game ? "游戏菜单" : "组件菜单"}">
    ${game ? `<button data-action="game-settings" data-id="${game.id}">${iconGear}游戏设置</button>
      <button data-action="game-launch" data-id="${game.id}" ${game.installed ? "" : "disabled"}>${iconPlay}运行游戏</button>`
      : `<button data-action="open-mod-folder" data-id="${menu.modId}" data-comp="${menu.componentId}">${iconFolder}查看文件</button>`}
  </div>`;
}

function renderConflictDialog() {
  const conflict = state.conflictDialog;
  if (!conflict) return "";
  const equipment = Boolean(conflict.equipment);
  return `<div class="modal-mask conflict-mask">
    <div class="modal conflict-modal" role="alertdialog" aria-modal="true" aria-labelledby="conflict-title" data-stop="true">
      <h2 id="conflict-title" class="detail-title">${equipment ? "装备替换冲突" : "MOD 文件覆盖"}</h2>
      <p>${equipment ? `${conflict.name ? `「${esc(conflict.name)}」` : "此 MOD"} 的目标文件已存在，或多个文件要写入同一位置。未修改 MOD。` : "列表靠后的 MOD 优先生效。"}</p>
      ${equipment ? "" : `<div class="conflict-list">${conflict.deployment.map(item =>
        `<section><strong>${esc(item.winner)}</strong> 覆盖 <strong>${esc(item.overwritten)}</strong></section>`).join("")}</div>`}
      <div class="actions"><button class="primary" data-action="close-conflict">知道了</button></div>
    </div>
  </div>`;
}

let toastKey = "";
let toastVisible = false;
let toastTimer;

function renderToast() {
  const key = `${state.busy}:${state.statusError}:${state.status}`;
  if (key !== toastKey) {
    clearTimeout(toastTimer);
    toastKey = key;
    toastVisible = state.busy || (state.status && state.status !== "就绪");
    if (toastVisible && !state.busy) {
      toastTimer = setTimeout(() => {
        toastVisible = false;
        app.querySelector(".status-toast")?.remove();
      }, state.statusError ? 9000 : 5000);
    }
  }
  return toastVisible && !state.conflictDialog ? `<div class="status-toast${!state.busy && state.statusError ? " status-error" : ""}" role="status">
    <span>${state.busy ? "处理中…" : esc(state.status)}</span>
    ${state.busy ? "" : `<button class="icon" data-action="close-toast" aria-label="关闭提示">${iconClose}</button>`}
  </div>` : "";
}

function renderModDetail(mod, component) {
  const detail = component ?? mod;
  const kv = (label, value, color = "") => value ? `<div class="kv"><span>${label}</span><span class="${color}">${esc(value)}</span></div>` : "";
  const ownerLabel = component ? "主 MOD" : "MOD";
  const nexus = nexusControls();
  return `<div class="detail-toolbar" role="toolbar" aria-label="MOD 操作">
    ${tool("update-mod", `更新 ${ownerLabel}`, iconRefresh, { id: mod.id, className: "primary" })}
    ${!mod.isBundle || component ? tool("open-mod-folder", "查看文件", iconFolder, { id: mod.id, component: component?.id }) : ""}
    ${tool("open-source", "来源地址", iconLink, { id: mod.id })}
    ${mod.nexusId > 0 ? tool("check-nexus-update", nexus.checkTitle, iconArrowUp, { id: mod.id, disabled: nexus.checkDisabled }) : ""}
    ${mod.nexusId > 0 ? tool("open-nexus-files", nexus.downloadTitle, iconDownload, { id: mod.id, disabled: nexus.downloadDisabled }) : ""}
    ${tool("uninstall", `删除 ${ownerLabel}`, iconTrash, { id: mod.id, className: "btn danger" })}
  </div>
  ${component ? `<button class="parent-link" data-action="open-mod" data-id="${mod.id}">${esc(mod.name)}</button>` : ""}
  <h2 class="detail-title" title="${esc(detail.name)}">${esc(detail.name)}</h2>
  ${detail.hasPreview ? `<div class="preview-box"><img src="${esc(previewSrc(detail))}" alt="" /></div>` : ""}
  <div class="detail-metadata">
    ${kv("类型", detail.category, "kv-accent")}
    ${kv("版本", detail.version)}
    ${kv("作者", detail.author)}
    ${kv("文件数", `${detail.fileCount} 个`)}
    ${component ? "" : kv("分组", mod.groupName) + kv("导入时间", mod.installedAt)}
  </div>
  ${renderEquipment(mod, component)}
  ${mod.nexusId > 0 ? renderNexusUpdate(mod) : ""}`;
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
  const active = activeMod();
  const pane = state.pane;
  const component = activeComponent(active);
  const settingsGame = state.games.find(item => item.id === state.gameSettingsId);
  const detailScroll = pane === "settings" ? app.querySelector('.detail[data-pane="settings"]')?.scrollTop : null;
  const nexusInput = app.querySelector('[data-role="nexus-key"]');
  const nexusCaret = pane === "settings" && nexusInput && document.activeElement === nexusInput ? nexusInput.selectionStart : null;
  applyAppearance(currentAppearance(), backgroundUrl());
  const list = app.querySelector(".list");
  const listScrollTop = list?.scrollTop ?? 0;
  const listScrollLeft = list?.scrollLeft ?? 0;
  const componentPanel = app.querySelector(".comp-panel");
  const componentScrollTop = componentPanel?.scrollTop ?? 0;
  const componentScrollModId = componentPanel?.dataset.mod ?? null;
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
              <button class="game${game?.id === item.id ? " active" : ""}" data-action="select-game" data-id="${item.id}" title="${esc(item.shortName)}" aria-label="${esc(item.displayName)}" aria-pressed="${game?.id === item.id}">
                <span class="game-icon">${icon ? `<img src="${icon}" alt="" />` : esc(item.shortName.slice(0, 1))}</span>
              </button>`;
          }).join("")}
          <div class="sidebar-settings${pane === "settings" ? " active" : ""}">${tool("global-settings", "通用设置", iconGear, { className: "icon" })}</div>
        </aside>
        <main class="workspace">
          <div class="ws-header">
            <div class="list-toolbar" role="toolbar" aria-label="MOD 列表操作">
              ${tool("import", "添加 MOD", iconPlus, { className: "primary import-tool" })}
              <div class="list-secondary-tools">
                ${tool("refresh", "刷新", iconRefresh)}
                ${tool("create-group", "新建分组", iconFolderPlus)}
                ${tool("check-all-updates", "检查所有 Nexus MOD 更新", iconArrowUp, { disabled: nexusControls().checkDisabled })}
              </div>
            </div>
            <div class="mod-count" title="已启用 / 全部"><b>${state.mods.filter(m => m.enabled).length}</b><span>/</span><strong>${state.mods.length}</strong></div>
            <label class="ws-search">
              ${iconSearch}
              <input id="search" aria-label="搜索 MOD" placeholder="搜索 MOD 名称、类型、作者或装备" value="${esc(state.search)}" />
            </label>
          </div>
          <div class="list">
            ${visibleGroups().map(group => {
              const mods = filteredMods(group.id);
              const show = !group.collapsed || state.search.trim();
              const name = state.renameDrafts[group.id] ?? group.name;
              return `
                <section class="group">
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
                    <article class="mod${state.activeId === mod.id ? " active" : ""}" data-action="open-mod" data-id="${mod.id}">
                      <div class="thumb">${mod.hasPreview ? `<img src="${esc(previewSrc(mod))}" alt="" />` : esc(mod.category)}</div>
                      <div class="meta">
                        <input class="name" data-role="mod-name" data-id="${mod.id}" value="${esc(state.modRenameDrafts[mod.id] ?? mod.name)}" />
                        <div class="sub">
                          ${state.nexusUpdates[mod.id]?.available ? '<span class="upd-badge">有更新</span>' : ""}
                          <span class="muted">${esc(mod.version)}</span>
                          <span class="muted">${mod.fileCount} 个文件</span>
                          <select data-role="mod-group" data-id="${mod.id}">
                            ${state.groups.map(item => `<option value="${item.id}" ${item.id === mod.groupId ? "selected" : ""}>${esc(item.name)}</option>`).join("")}
                          </select>
                        </div>
                      </div>
                      <div class="row-actions">
                        ${mod.isBundle ? (() => {
                          const counts = componentCounts(mod);
                          const open = state.bundleOpenId === mod.id;
                          return `<button class="comp-toggle" data-action="toggle-components" data-id="${mod.id}" title="展开组件列表">${open ? "▴" : "▾"} 组件 ${counts.enabled}/${counts.total}</button>`;
                        })() : ""}
                        <button class="icon" data-action="mod-up" data-id="${mod.id}" title="上移（靠后覆盖靠前）">↑</button>
                        <button class="icon" data-action="mod-down" data-id="${mod.id}" title="下移（靠后覆盖靠前）">↓</button>
                        <button class="${switchClass(mod.enabled)}" data-action="mod-enable" data-id="${mod.id}" data-on="${mod.enabled}"></button>
                      </div>
                    </article>
                    ${state.bundleOpenId === mod.id ? `<div class="comp-panel" data-mod="${mod.id}">${renderComponentRows(mod)}</div>` : ""}`).join("") || '<div class="empty">这个分组还没有 MOD</div>'}</div>` : ""}
                </section>`;
            }).join("") || '<div class="empty">还没有导入任何 MOD</div>'}
          </div>
        </main>
        <aside class="detail" data-pane="${pane}">
          ${pane === "game-settings" && settingsGame ? `
            <h2 class="detail-title">${esc(settingsGame.shortName)} 设置</h2>
            <div class="field">
              <span>游戏目录</span>
              <div class="detail-path">${esc(settingsGame.path || "未设置游戏目录")}</div>
              <div class="actions stack">
                <button class="btn" data-action="pick-game" ${state.busy ? "disabled" : ""}>选择游戏目录</button>
                <button class="btn" data-action="open-folder" ${settingsGame.path ? "" : "disabled"}>打开游戏目录</button>
              </div>
            </div>
            <div class="field">
              <span>部署</span>
              <div class="detail-hint">${esc(settingsGame.pakState)}</div>
              <div class="actions"><button class="btn danger" data-action="clean" ${state.busy || !settingsGame.installed ? "disabled" : ""}>清理部署文件</button></div>
            </div>
          ` : pane === "settings" ? `
            <h2 class="detail-title">通用设置</h2>
            <div class="field">
              <span>部署行为</span>
              <label class="check"><input type="checkbox" data-role="setting" data-key="checkGameRunning" ${state.settings.checkGameRunning ? "checked" : ""} /> 游戏运行时阻止修改</label>
            </div>
            <div class="field">
              <span>安装源文件</span>
              <select class="field-select" data-role="install-option">
                <option value="0" ${state.settings.installOption === 0 ? "selected" : ""}>不复制，保留原位置</option>
                <option value="1" ${state.settings.installOption === 1 ? "selected" : ""}>复制到工作区</option>
                <option value="2" ${state.settings.installOption === 2 ? "selected" : ""}>移动到工作区</option>
              </select>
            </div>
            <div class="field">
              <span>Nexus API Key</span>
              <input class="field-input" data-role="nexus-key" aria-label="Nexus API Key" type="password" value="${esc(state.nexusKeyDraft ?? state.settings.nexusApiKey ?? "")}" autocomplete="off" spellcheck="false" />
              <div class="actions">
                <button class="btn" data-action="validate-nexus-key" ${state.busy ? "disabled" : ""}>保存并验证 Key</button>
              </div>
              <div class="detail-hint" data-role="nexus-account" aria-live="polite">${esc(nexusAccountLabel())}</div>
            </div>
            ${renderAppearance(currentAppearance(), Boolean(state.settings.backgroundRevision), state.busy)}
          ` : pane === "mod" && active ? renderModDetail(active, component) : `
            <div class="detail-empty">
              <div class="detail-empty-icon">◈</div>
              <div>点击左侧 MOD 查看详情</div>
            </div>
          `}
        </aside>
      </div>
      ${renderToast()}
      ${renderContextMenu()}
      <div class="dropmask" id="dropmask"><div class="dropcard">松开以批量导入 MOD</div></div>
      ${renderEquipPicker()}
      ${renderPasswordPrompt()}
      ${renderSourcePrompt()}
      ${renderNexusFiles()}
      ${state.deletePrompt ? `<div class="modal-mask" data-action="cancel-delete"><div class="modal" role="alertdialog" aria-modal="true" aria-labelledby="delete-title" data-stop="true">
        <h2 id="delete-title" class="detail-title">删除 ${state.deletePrompt.isBundle ? "主 " : ""}MOD</h2>
        <p>确认删除「${esc(state.deletePrompt.name)}」${state.deletePrompt.isBundle ? "及其全部组件" : ""}？</p>
        <div class="actions"><button class="btn" data-action="cancel-delete">取消</button><button class="btn danger" data-action="confirm-delete">删除</button></div>
      </div></div>` : ""}
      ${renderConflictDialog()}
    </div>
  `;
  const nextList = app.querySelector(".list");
  if (nextList) {
    nextList.scrollTop = listScrollTop;
    nextList.scrollLeft = listScrollLeft;
  }
  const nextComponentPanel = app.querySelector(".comp-panel");
  if (nextComponentPanel && nextComponentPanel.dataset.mod === componentScrollModId) {
    nextComponentPanel.scrollTop = componentScrollTop;
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
  if (detailScroll != null) app.querySelector(".detail").scrollTop = detailScroll;
  if (nexusCaret != null) {
    const input = app.querySelector('[data-role="nexus-key"]');
    input?.focus({ preventScroll: true });
    input?.setSelectionRange(nexusCaret, nexusCaret);
  }
  if (state.conflictDialog) app.querySelector('[data-action="close-conflict"]')?.focus();
}

function onClick(event) {
  const target = event.target.closest("[data-action]");
  if (state.contextMenu) {
    state.contextMenu = null;
    app.querySelector(".context-menu")?.remove();
  }
  if (!target || target.disabled || event.target.closest("select, option, input, textarea, label")) return;
  event.stopPropagation();
  const id = Number(target.dataset.id);
  const action = target.dataset.action;
  const gameId = state.game?.id;

  if (action === "select-game") {
    if (target.dataset.id === gameId) return;
    run(async () => {
      const data = await request(`/api/games/${target.dataset.id}/select`, { method: "POST" });
      if (state.pane === "game-settings") state.pane = "info";
      return data;
    });
    return;
  }
  if (action === "global-settings") {
    state.pane = "settings";
    render();
    return;
  }
  if (action === "appearance-pick") {
    app.querySelector("#appearance-image")?.click();
    return;
  }
  if (action === "appearance-remove") {
    changeBackground();
    return;
  }
  if (action === "appearance-theme" || action === "appearance-fit" || action === "appearance-reset") {
    const changes = action === "appearance-reset" ? defaultAppearance :
      { [action === "appearance-theme" ? "theme" : "fit"]: target.dataset.value };
    saveAppearance(changes);
    return;
  }
  if (action === "game-settings") {
    state.gameSettingsId = target.dataset.id;
    state.pane = "game-settings";
    render();
    return;
  }
  if (action === "game-launch") {
    run(async () => {
      const result = await request(`/api/games/${target.dataset.id}/launch`, { method: "POST" });
      return { status: result.status, error: result.error };
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
    state.activeComponentId = null;
    state.pane = "mod";
    render();
    return;
  }
  if (action === "open-component") {
    state.activeId = id;
    state.activeComponentId = Number(target.dataset.comp);
    state.pane = "mod";
    render();
    return;
  }
  if (action === "component-menu") {
    const rect = target.getBoundingClientRect();
    openContextMenu({ modId: id, componentId: Number(target.dataset.comp), x: rect.right, y: rect.bottom + 4 });
    return;
  }
  if (action === "toggle-components") {
    state.bundleOpenId = state.bundleOpenId === id ? null : id;
    // 展开哪个主 MOD,右侧详情就显示哪个;收起不改选中。
    if (state.bundleOpenId === id) {
      state.activeId = id;
      state.activeComponentId = null;
      state.pane = "mod";
    }
    render();
    return;
  }
  if (action === "comp-enable") {
    const modId = Number(target.dataset.mod);
    const compId = Number(target.dataset.comp);
    run(() => request(`/api/games/${gameId}/mods/${modId}/components/${compId}/enable`, {
      method: "POST",
      body: JSON.stringify({ enabled: target.dataset.on !== "true" })
    }));
    return;
  }
  if (action === "comp-up" || action === "comp-down") {
    const modId = Number(target.dataset.mod);
    const compId = Number(target.dataset.comp);
    run(() => request(`/api/games/${gameId}/mods/${modId}/components/${compId}/move`, {
      method: "POST",
      body: JSON.stringify({ delta: action === "comp-up" ? -1 : 1 })
    }));
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
    openModFolder(id, target.dataset.comp);
    return;
  }
  if (action === "open-source") {
    const mod = state.mods.find(item => item.id === id);
    state.sourcePrompt = { modId: id, value: mod?.homeUrl ?? "", hint: "" };
    render();
    const input = document.getElementById("source-url");
    input?.focus();
    input?.select();
    return;
  }
  if (action === "close-source") {
    if (target.classList.contains("modal-mask") && event.target.closest("[data-stop]")) return;
    state.sourcePrompt = null;
    render();
    return;
  }
  if (action === "apply-source") {
    applySourceUrl();
    return;
  }
  if (action === "clean") {
    mutateGameSettings(state.gameSettingsId, "clean", { method: "POST" });
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
    state.deletePrompt = state.mods.find(mod => mod.id === id);
    render();
    app.querySelector('.modal button[data-action="cancel-delete"]')?.focus();
    return;
  }
  if (action === "cancel-delete") {
    if (target.classList.contains("modal-mask") && event.target.closest("[data-stop]")) return;
    state.deletePrompt = null;
    render();
    return;
  }
  if (action === "confirm-delete") {
    const mod = state.deletePrompt;
    state.deletePrompt = null;
    if (mod) run(() => request(`/api/games/${gameId}/mods/${mod.id}`, { method: "DELETE" }));
    return;
  }
  if (action === "close-conflict") {
    state.conflictDialog = null;
    render();
    app.querySelector("#equip-search")?.focus();
    return;
  }
  if (action === "close-toast") {
    toastVisible = false;
    app.querySelector(".status-toast")?.remove();
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
  if (action === "close-password") {
    if (target.classList.contains("modal-mask") && event.target.closest("[data-stop]")) return;
    closePasswordPrompt(null);
    return;
  }
  if (action === "submit-password") {
    submitPassword();
    return;
  }
  if (action === "validate-nexus-key") {
    saveSettings(true);
    return;
  }
  if (action === "check-nexus-update") {
    checkNexusUpdate(Number(target.dataset.id));
    return;
  }
  if (action === "check-all-updates") {
    checkAllNexusUpdates();
    return;
  }
  if (action === "open-nexus-files") {
    checkNexusUpdate(id, true);
    return;
  }
  if (action === "pick-nexus-file") {
    if (state.nexusFiles) {
      state.nexusFiles.selectedFileId = Number(target.dataset.id);
      render();
    }
    return;
  }
  if (action === "close-nexus-files") {
    if (target.classList.contains("modal-mask") && event.target.closest("[data-stop]")) return;
    state.nexusFiles = null;
    render();
    return;
  }
  if (action === "apply-nexus-download") {
    applyNexusDownload();
    return;
  }
  if (action === "apply-equip") {
    applyEquipChange();
  }
}

function openContextMenu(menu) {
  if (state.busy) return;
  state.contextMenu = menu;
  render();
  app.querySelector(".context-menu button:not(:disabled)")?.focus();
}

function onContextMenu(event) {
  const game = event.target.closest(".game");
  const row = event.target.closest(".comp-row");
  if (!game && !row) return;
  event.preventDefault();
  const rect = (game ?? row).getBoundingClientRect();
  const position = { x: event.clientX || rect.right, y: event.clientY || rect.bottom };
  openContextMenu(game ? { ...position, gameId: game.dataset.id }
    : { ...position, modId: Number(row.dataset.mod), componentId: Number(row.dataset.comp) });
}

function onInput(event) {
  const { target } = event;
  if (target.dataset.appearance) {
    const appearance = { ...currentAppearance(), [target.dataset.appearance]: Number(target.value) };
    applyAppearance(appearance, backgroundUrl());
    target.parentElement.querySelector("output").value = target.value + target.dataset.unit;
    return;
  }
  if (target.dataset.role === "nexus-key") {
    state.nexusKeyDraft = target.value;
    return;
  }
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
    return;
  }
  if (event.target.id === "source-url" && state.sourcePrompt) {
    // 只记草稿,不重渲染,避免输入框失焦。
    state.sourcePrompt.value = event.target.value;
  }
}

function onChange(event) {
  const target = event.target;
  if (target.id === "appearance-image") {
    if (target.files[0]) changeBackground(target.files[0]);
    return;
  }
  if (target.dataset.appearance) {
    saveAppearance({ [target.dataset.appearance]: Number(target.value) });
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

async function saveSettings(saveKey = false) {
  const checkGameRunning = app.querySelector('[data-key="checkGameRunning"]')?.checked ?? state.settings.checkGameRunning;
  const installOption = Number(app.querySelector("[data-role='install-option']")?.value ?? state.settings.installOption);
  const nexusApiKey = saveKey ? (state.nexusKeyDraft ?? state.settings.nexusApiKey ?? "").trim() : undefined;
  let saved = false;
  await run(async () => {
    const data = await request("/api/settings", {
      method: "PUT",
      body: JSON.stringify({ checkGameRunning, installOption, nexusApiKey })
    });
    if (saveKey) state.nexusKeyDraft = null;
    saved = true;
    return { ...data.workspace, status: "设置已保存" };
  });
  if (saved && saveKey) void refreshNexusAccess();
}

function currentAppearance() {
  return state.settings.appearance ?? defaultAppearance;
}

function backgroundUrl() {
  const revision = state.settings.backgroundRevision;
  return revision ? apiUrl(`/api/settings/appearance/image?v=${encodeURIComponent(revision)}`) : "";
}

function saveAppearance(changes) {
  return run(async () => {
    const data = await request("/api/settings/appearance", {
      method: "PUT",
      body: JSON.stringify({ ...currentAppearance(), ...changes })
    });
    if (data.error) return data;
    state.settings.appearance = data.appearance;
    return { status: "外观已保存" };
  });
}

function changeBackground(file) {
  return run(async () => {
    const data = await request("/api/settings/appearance/image", file
      ? { method: "PUT", body: JSON.stringify({ image: await readBackgroundImage(file) }) }
      : { method: "DELETE" });
    if (data.error) return data;
    state.settings.backgroundRevision = data.backgroundRevision;
    return { status: file ? "背景已保存" : "已移除背景图片" };
  });
}

async function openEquipPicker(kind, id, isPfb) {
  const picker = { gameId: state.game.id, modId: state.activeId, componentId: state.activeComponentId,
    kind, fromId: id, toId: id, isPfb, query: "", withTex: false, filterUsed: true };
  state.equipPicker = picker;
  state.equipCatalog = { kind, items: [], loading: true };
  render();
  try {
    const data = await request(`/api/games/${picker.gameId}/equipment?kind=${encodeURIComponent(kind)}&excludeMod=${picker.modId}`);
    if (state.equipPicker !== picker) return;
    state.equipCatalog = { kind, items: data.items ?? [] };
  } catch (error) {
    if (state.equipPicker !== picker) return;
    state.status = error.message || "无法加载装备列表";
    state.statusError = true;
    state.equipPicker = null;
  }
  render();
  document.getElementById("equip-search")?.focus();
}

async function applyEquipChange() {
  const picker = state.equipPicker;
  if (!picker || picker.toId === picker.fromId) return;
  await run(async () => {
    const data = await request(`/api/games/${picker.gameId}/mods/${picker.modId}/equipment`, {
      method: "POST",
      body: JSON.stringify({
        kind: picker.kind,
        fromId: picker.fromId,
        toId: picker.toId,
        isPfb: picker.isPfb,
        withTex: picker.withTex,
        componentId: picker.componentId
      })
    });
    if (!data.error) state.equipPicker = null;
    return data;
  }, "修改装备失败");
}

// 更新成功后立即复查一次(仅对已有检查结果的 MOD):服务端文件列表有缓存,通常不消耗 API 次数。
async function refreshNexusUpdate(modId) {
  // 更新后的自动复查:未配置 Key 时静默跳过,不打断更新流程。
  const key = state.settings.nexusApiKey ?? "";
  if (!key.trim() || !state.nexusUpdates[modId] || !state.game) return;
  try {
    const data = await request(`/api/games/${state.game.id}/mods/${modId}/nexus/update-check`);
    if (!data.error) {
      state.nexusUpdates[modId] = data;
    }
  } catch {
    // 复查失败保留旧结果,可手动再检查
  }
}

async function updateMod(id) {
  if (state.busy || !state.game) return;
  state.busy = true;
  try {
    // 弹出文件选择框之前先预检游戏是否运行,避免选完文件才被拒绝。
    const check = await request(`/api/games/${state.game.id}/game-running`);
    if (check.running) {
      state.status = "游戏正在运行，请先关闭游戏";
      state.statusError = true;
      return;
    }

    const path = window.mhModManager?.pickUpdate ? await window.mhModManager.pickUpdate() : "";
    if (!path) return;
    const passwords = [];
    let updated = false;
    while (true) {
      state.status = "正在更新 MOD...";
      state.statusError = false;
      render();
      const data = await request(`/api/games/${state.game.id}/mods/${id}/update`, {
        method: "POST",
        body: JSON.stringify({ paths: [path], passwords })
      });
      applyWorkspace(data);
      if (!data.needPassword) {
        updated = !data.error;
        break;
      }
      const password = await promptPassword(path, data.status);
      if (password == null) {
        state.status = "已取消更新：压缩包需要密码";
        state.statusError = true;
        break;
      }
      passwords.push(password);
    }

    if (updated) {
      await refreshNexusUpdate(id);
    }
  } catch (error) {
    state.status = error.message || "更新失败";
    state.statusError = true;
  } finally {
    state.busy = false;
    render();
  }
}

async function importMods(paths) {
  const picked = paths ?? (window.mhModManager ? await window.mhModManager.pickMods() : []);
  if (!picked?.length || state.busy) return;
  state.busy = true;
  let pending = [...picked];
  const passwords = [];
  try {
    while (pending.length) {
      state.status = `正在导入 ${pending.length} 个 MOD...`;
      state.statusError = false;
      render();
      const data = await request(`/api/games/${state.game.id}/import`, {
        method: "POST",
        body: JSON.stringify({ paths: pending, passwords })
      });
      applyWorkspace(data);
      if (!data.needPassword) break;
      // 后端在加密包处中断批次:已导入的保留,输完密码从 pendingPaths 继续。
      const password = await promptPassword(data.passwordFile, data.status);
      if (password == null) {
        state.status = "已取消导入：压缩包需要密码";
        state.statusError = true;
        break;
      }
      passwords.push(password);
      pending = data.pendingPaths ?? [];
    }
  } catch (error) {
    state.status = error.message || "导入失败";
    state.statusError = true;
  } finally {
    state.busy = false;
    render();
  }
}

async function pickGame() {
  const gameId = state.gameSettingsId;
  const folder = window.mhModManager ? await window.mhModManager.pickFolder() : "";
  if (!folder) return;
  await mutateGameSettings(gameId, "path", {
    method: "PUT",
    body: JSON.stringify({ path: folder })
  });
}

async function mutateGameSettings(gameId, action, options) {
  if (!gameId) return;
  await run(async () => {
    const data = await request(`/api/games/${gameId}/${action}`, options);
    if (gameId === state.game?.id) return data;
    if (data.game) state.games = state.games.map(item => item.id === gameId ? data.game : item);
    return { status: data.status, error: data.error };
  });
}

async function openGameFolder() {
  const folder = state.games.find(item => item.id === state.gameSettingsId)?.path;
  if (!folder) {
    state.status = "未设置游戏目录";
    state.statusError = true;
    render();
    return;
  }
  if (!window.mhModManager?.openPath) {
    state.status = "当前环境无法打开文件夹";
    state.statusError = true;
    render();
    return;
  }
  const error = await window.mhModManager.openPath(folder);
  state.status = error ? `无法打开目录: ${error}` : "已打开游戏目录";
  state.statusError = Boolean(error);
  render();
}

function openExternal(url) {
  if (!url) return;
  if (window.mhModManager?.openExternal) {
    window.mhModManager.openExternal(url).then(error => {
      if (error) {
        state.status = `无法打开链接: ${error}`;
        state.statusError = true;
      }
      render();
    });
    return;
  }
  window.open(url, "_blank", "noopener");
}

async function openModFolder(id, component) {
  if (!window.mhModManager?.openPath || !state.game) {
    state.status = "当前环境无法打开文件夹";
    state.statusError = true;
    render();
    return;
  }

  try {
    const query = component ? `?component=${encodeURIComponent(component)}` : "";
    const result = await request(`/api/games/${state.game.id}/mods/${id}/folder${query}`);
    const error = await window.mhModManager.openPath(result.path);
    state.status = error ? `无法打开 MOD 文件夹: ${error}` : "已打开 MOD 文件夹";
    state.statusError = Boolean(error);
  } catch (error) {
    state.status = error.message || "无法打开 MOD 文件夹";
    state.statusError = true;
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

// 悬停组件行时,把右侧详情大图换成该组件封面;移出后恢复为主 MOD 封面。
// 纯视觉态:直接换 img.src,不走 render(),任何重渲染后自然回到主 MOD 封面。
function setupHoverPreview() {
  const restore = () => {
    const img = app.querySelector(".preview-box img");
    const active = activeMod();
    if (img && active) img.src = previewSrc(active);
  };
  app.addEventListener("mouseover", event => {
    const row = event.target.closest(".comp-row");
    const img = app.querySelector(".preview-box img");
    const active = activeMod();
    if (!row || !img || !active) return;
    const url = row.dataset.preview;
    // 仅当右侧详情显示的正是该组件所属主 MOD 时才换图;无封面的组件不换。
    if (!url || active.id !== Number(row.dataset.mod)) return;
    if (img.getAttribute("src") !== url) img.src = url;
  });
  app.addEventListener("mouseout", event => {
    const row = event.target.closest(".comp-row");
    if (!row) return;
    const next = event.relatedTarget;
    if (next && next.closest && next.closest(".comp-row") === row) return;
    restore();
  });
}

async function start() {
  app.addEventListener("click", onClick);
  app.addEventListener("input", onInput);
  app.addEventListener("change", onChange);
  app.addEventListener("contextmenu", onContextMenu);
  setupHoverPreview();
  window.addEventListener("keydown", event => {
    if (event.key === "Enter" && !event.isComposing && event.target?.dataset?.role === "nexus-key") {
      event.preventDefault();
      saveSettings(true);
      return;
    }
    if (state.conflictDialog) {
      if (event.key === "Escape") app.querySelector('[data-action="close-conflict"]')?.click();
      if (event.key === "Tab") event.preventDefault();
      return;
    }
    if (state.contextMenu && event.key === "Escape") {
      state.contextMenu = null;
      render();
      return;
    }
    if (state.deletePrompt && event.key === "Escape") {
      state.deletePrompt = null;
      render();
      return;
    }
    if (state.passwordPrompt) {
      // 密码弹窗要在 busy 期间可交互:Esc 取消,回车提交。
      if (event.key === "Escape" && !event.isComposing) {
        event.preventDefault();
        closePasswordPrompt(null);
      } else if (event.key === "Enter" && !event.isComposing) {
        event.preventDefault();
        submitPassword();
      }
      return;
    }
    if (state.sourcePrompt) {
      // 来源地址弹窗:Esc 关闭,输入框回车等同于「打开来源网页」。
      if (event.key === "Escape" && !event.isComposing) {
        event.preventDefault();
        state.sourcePrompt = null;
        render();
        return;
      }
      if (event.key === "Enter" && !event.isComposing && event.target?.id === "source-url") {
        event.preventDefault();
        applySourceUrl();
        return;
      }
    }
    if (event.key === "Escape" && !state.busy) {
      if (state.equipPicker) {
        state.equipPicker = null;
      } else if (state.nexusFiles) {
        state.nexusFiles = null;
      } else if (state.bundleOpenId != null) {
        state.bundleOpenId = null;
      } else {
        return;
      }
      render();
    }
  });
  render();
  setupDrop();
  try {
    await loadBootstrap();
    void refreshNexusAccess();
    state.status = "就绪";
    state.statusError = false;
  } catch (error) {
    state.status = error.message || "无法连接后端";
    state.statusError = true;
  }
  render();
}

start();
