# AGENTS.md — mh-mod-manager 开发指南

本文件是给 coding agent 的项目总说明,由原 `.workbuddy/`(项目记忆)与 `.zcode/`(会话计划)内容合并源码通读结果整理而成。

## 项目是什么

面向《怪物猎人》系列的 Windows MOD 管理器,支持三款游戏:

| GameId | SteamAppId | 游戏 | 部署模式 |
|---|---|---|---|
| `World` | 582010 | 怪物猎人:世界 | `nativePC/` 直接覆盖,无 pak_mods |
| `Rise` | 1446780 | 怪物猎人:崛起 | `natives/`+`reframework/` + **pak_mods** |
| `Wilds` | 2246340 | 怪物猎人:荒野 | `natives/`+`reframework/` + **pak_mods** |

绿色免安装,用户 MOD 数据集中在程序目录旁的 `data\`。核心功能:启用/禁用、分组与优先级(列表靠后覆盖靠前)、组件化 MOD、原地更新、改对应装备、Nexus 来源识别、封面预览。

## 架构(三层,固定端口 HTTP 串联)

```
src/MHSeries.ModManager      C# 核心类库(net9.0-windows,AssemblyName=MhModManager,
                             根命名空间 MhModManager,唯一依赖 SharpCompress 0.39.0)
src/mh-mod-manager.Host      ASP.NET Core 最小 API,只绑定 http://127.0.0.1:17865
desktop/                     Electron 37 + Vite + 原生 JS(无前端框架)
```

- Electron(`desktop/electron/main.cjs`)启动时 spawn 后端:dev 用 `dotnet run --project src/mh-mod-manager.Host`,打包用 `resources/host/mh-mod-manager.Host.exe`;轮询 `/api/bootstrap` 就绪后加载 Vite UI。
- **Host 所有 handler 在一个全局 `lock (gate)` 内执行**——后端单线程串行,不要假设可并发。
- **变更端点出错也返回 HTTP 200**,错误承载在 DTO 的 `Status`(中文消息)+ `Error=true` 字段;仅 preview/文件夹缺失返回 404。前端依赖此约定。
- 外链白名单:Electron `open-external` 只放行 `https://www.nexusmods.com/`。
- data 目录解析:Electron 便携版用 `PORTABLE_EXECUTABLE_DIR\data`;Host 侧优先级 `--data` 参数 > 环境变量 `MH_MOD_MANAGER_DATA` > 向上找 `MHSeries.ModManager.slnx` 取 `<仓库根>\data` > 兜底 exe 旁 `data`。

### 目录速查

```
src/MHSeries.ModManager/Core/            业务核心(ModService、ModLayoutParser、NexusNames、
                                         ArchivePasswordStripper、PakModsManager、ModRepository、BundleMigration…)
src/MHSeries.ModManager/Core/Equipment/  装备改写子系统
src/MHSeries.ModManager/Models/          纯数据模型
src/MHSeries.ModManager/Data/Equipment/  嵌入资源:装备 CSV、文件清单 .txt.gz、avp 模板 .bin
src/mh-mod-manager.Host/                 Program.cs(全部端点)+ ApiMapper.cs
desktop/electron/main.cjs                Electron 主进程;desktop/src/main.js 前端逻辑
_extract/                                逆向研究底稿(见下文「清理红线」)
tests/MHSeries.ModManager.Tests/         xUnit 测试(全局禁用并行)
MHSeries.ModManager.slnx                 XML 格式解决方案文件
publish.ps1 / start.ps1                  打包 / 本地联调入口
```

## 构建与测试

需要 Windows、.NET 9 SDK、Node.js 20+。

```powershell
dotnet test                                  # 跑全部测试(测试共享真实 settings.json,已禁并行)
.\start.ps1                                  # 本地联调:npm install + vite + electron + dotnet host
.\publish.ps1                                # 出包:dist\mh-mod-manager-<版本号>.zip(需 7z 在 PATH)
```

- `publish.ps1` 流程:npm install → dotnet publish(自包含、**必须 `PublishSingleFile=false`**,脚本会校验 Host.dll 存在)→ vite build → 组装 Electron 目录 → 7z 压缩。
- Electron 运行时下载失败时有 npmmirror 镜像兜底逻辑;`start.ps1` 会补写缺失的 `node_modules\electron\path.txt`。

## 版本与发布(约定)

- **版本号单一来源是 git tag**:`publish.ps1` 用 `git describe --tags --abbrev=0` 读取;csproj 里是静态 1.0.0,不要改它来发版。
- 发布 = 推 `v*` 标签触发 `.github/workflows/release.yml`;CI checkout 必须 `fetch-depth: 0`(浅克隆拿不到 tag)。也支持 workflow_dispatch 指定已有标签重发。
- 发布物是**目录版压缩包**(自包含 .NET + Electron,非单文件 exe),附件带 `.sha256`;发布说明放 `docs/release-notes/<tag>.md`,缺失则 `--generate-notes` 回退。
- 远程:`git@github.com:Noplch0/mh-mod-manager.git`(SSH),主分支 `master`。

## 部署核心规则(不可破坏的不变量)

1. **无备份、直接覆盖**:备份机制已移除。禁用 = 直接删除游戏目录中该 MOD 部署的文件;启用 = 按优先级把所有文件相交的已启用 MOD 依序**重部署**(`FindOverlapping` + `GroupOrder.Ordered`)。改部署逻辑必须维持"重部署相交集合",否则残留/错覆盖。启动时会清理旧版 `games/<appId>/backups` 目录。
2. **覆盖顺序**:分组 Index → 组内 MOD Index → MOD Id(`GroupOrder`);同文件冲突时**列表靠后覆盖靠前**,组件排序同理,调序立即重部署生效。
3. **pak 只进 pak_mods**:崛起/荒野的 `.pak` 只能部署到 `pak_mods/X{0000编号}-{安全名}[-N].pak`(多 pak 加 `-1..-N`,单 pak 无后缀);映射存在 `ModRecord.OverwriteFiles`。游戏根目录官方 pak(`re_chunk_000.pak*`)永不触碰。World 的 pak 按原文件名部署到游戏根目录。
4. **孤儿 pak 清理靠 `PakAllocator.PrefixHash`**(前 1MB MD5 + 文件长度):映射丢失也能清干净;用户自放的 X 命名 pak 因哈希不匹配会被保留(测试钉住)。
5. **组件化 MOD**:多组件压缩包(多个顶层目录/嵌套压缩包/散装 pak,候选 ≥2)导入为一个 MOD,组件存储为 `files\c{n}\...`,封面 `covers\c{n}.<ext>`;组件顺序按名称字母排序确定。`c{n}/` 前缀贯穿全链(存储、部署剥前缀、装备改写按组件目录、迁移)。单 pak / 单包不组件化。
6. **旧同源分组自动迁移**(`BundleMigration`):非默认组内 `SourceFile` 同名且文件大小一致的成员合并为组件化 MOD;手动分组不受影响。
7. **装备改写前提**:`ChangeEquipment` 要求 MOD 处于**禁用状态**;防具改写必须同时改 avp 内容(荒野 `_avp.user.3` 二进制重建,只改文件名不生效)+ 骨骼 lua(`reframework/data/bonesystem/` 等)路径。
8. **Nexus 文件名解析顺序**:新站内空格格式(`Name <id> <ver> <日期> <密钥>`,`ParseNexusDownloadStem`)**必须先于**旧连字符格式(`ParseNexusStem`,以末尾 9~13 位时间戳为锚从后向前解析),否则日期数字段被误当 id。旧格式版本段可含非数字词(如 `Name-17-REF-1-3-1-时间戳`),数字段前的连续词块并入版本、词块左侧数字段才是 id。`cmg_` 前缀 → caimogu 链接。
9. **加密压缩包**:导入/更新遇加密包抛 `PasswordRequiredException`(`ArchiveExtractor.Extract` 接受候选密码列表逐个尝试,`IsEncrypted` 不解压即可探测 ZipCrypto/AES/7z/Rar);Host 以 `NeedPassword`+`PasswordFile`+`PendingPaths` 返回,前端弹密码框后把密码累加进请求的 `passwords` 重发(批次从断点续导,同一批自动复用已输密码);**密码只在请求内内存传递、不落盘**。复制/移动模式下导入成功后 `KeepSource` 调 `ArchivePasswordStripper.Strip` 把 downloads 里的存储源重建为**无密码 zip**(SharpCompress 只能写 zip;嵌套加密包递归重建为同名 zip,未加密包字节不动);原位模式(选项 0)不动用户文件,去密码失败静默保留加密副本。
10. **游戏根零杂项**:游戏根(exe 同级)不允许出现 MOD 的非加载文件——封面图、说明 txt、多余预览图等在解析期丢弃(`ModLayoutParser.IsRootJunk`,`ParseUnit` 过滤),旧记录里的此类条目部署时跳过、禁用时随 `Undeploy` 清除;豁免:世界 pak 按原文件名放根、崛起/荒野 pak 走 pak_mods 通道,加载器/插件 dll(世界任意根 dll、崛起/荒野 GameDlls 白名单)必须在根。

## 装备改写子系统(Core/Equipment/)

入口 `ModService.ChangeEquipment` → `EquipmentRemapper.Apply`(对 `files\` 直接操作,bundle 按组件目录处理)。

- `EquipmentResolver`:从文件路径推断对应装备;三游戏前缀严格不同;World 防具**文件名 id 优先于文件夹 id**;pak 用 `PakReader.ListInternalPaths` 展开后再解析。
- `EquipmentPathRewriter`:纯函数路径改写;骨骼 lua 直接令牌替换,其余改写后必须能被 Resolver 解析回目标装备,否则拒绝。
- `AvpPatcher`:荒野防具专属,avp 内容按内嵌模板重建(女款在固定偏移写目标 id);Rise 无 avp。
- `PakReader`/`PakHash`/`PakFileIndex`:只读 pak 条目表,**从不解压数据**。哈希 = Murmur3 x86-32、UTF-16LE 输入、64 位 = 大写哈希<<32 | 小写哈希。支持 `PKPA` v2(24B 条目)/v4(48B:hash@0、offset@8、uncompressedSize@16、compressedSize@24);feature 8/24 时表用 `data[i] ^ (byte)(i + key[i%32]*key[i%29])` XOR 混淆。内容替换仅 v4、追加式写文件尾。清单反查只内置荒野/崛起;**World 无清单 → World 的 pak 不参与装备改写**。
- 装备 id 编码三游戏不一致(荒野防具 `first*1000+second`、武器/猫 `*10000+second`、World 武器掺 `op*10+isBs`),改动需同步 Catalog/Resolver/PathRewriter 三处。

## 数据模型与磁盘布局

- `ModRecord`(`mods/<id>/info.json`):Id(从 1001 起)、GroupId、NexusId、Name/DisplayName、Enabled、Index、Version、Author、Category、SourceFile、HomeUrl、PreviewImage、`Files`(存储相对路径,bundle 带 `c{n}/` 前缀)、`OverwriteFiles`(pak_mods 路径映射,OrdinalIgnoreCase)、`Components`(`IsBundle` = Components.Count>0,不落盘)。`DeployPath(stored)` 是存储路径→游戏路径的唯一函数。
- `ModGroup`(`games/<appId>/groups.json`):默认组"未分组",孤儿 MOD 归入;Index 重排为 1..N 连续。
- `AppSettings`(`settings.json`):LastGame(默认 Wilds)、CheckGameRunning(默认 true)、InstallOption(0 原位/1 复制到 downloads/2 移动)、GamePaths 按 SteamAppId 键。
- `JsonUtil.Load` **捕获所有异常返回 fallback——损坏的 JSON 会被静默重置**,改动需谨慎。
- 预览图:stem 为 screenshot/preview/cover* 且在 files 根的文件不算部署文件;MOD 封面统一存 `files\screenshot.<ext>`。
- 游戏目录定位 `SteamLocator`:注册表卸载项 → Steam 库 VDF 枚举;有效性 = 目录存在且 `<dir>\<ExeName>` 存在。

## Host API(前缀 /api,变更端点返回完整 Workspace DTO)

| 端点 | 用途 |
|---|---|
| GET `/api/bootstrap` | 设置 + 游戏 + 工作区(UI 首调) |
| GET `/api/workspace/{gameId}` | 某游戏工作区 |
| POST `/api/games/{g}/select` / PUT `/path` / PUT `/api/settings` | 切游戏 / 设目录 / 设置 |
| POST `/api/games/{g}/refresh` | 清缓存重读磁盘 |
| POST `/api/games/{g}/import` | 批量导入 `{paths:[]}` |
| POST `/api/games/{g}/mods/{m}/enable` `/move` `/update` `/group` `/equipment`; PATCH `/{m}`; DELETE `/{m}` | MOD 操作 |
| POST `.../mods/{m}/components/{c}/enable` `/move` | 组件开关/排序 |
| POST `/api/games/{g}/groups`(建)/ PATCH DELETE `/{id}` / `enable` `move` `mods` | 分组操作 |
| POST `/api/games/{g}/clean` | 清理当前启用的部署文件 |
| POST `/api/games/{g}/launch` | `steam://run/<appId>` |
| GET `.../mods/{m}/preview?component=` / `.../mods/{m}/folder` / GET `.../equipment?kind=` | 图片流 / 目录路径 / 装备列表(较慢,会展开 pak) |

## 清理红线(执行清理任务前必读)

**不可删除 / 不可再生:**
- `_extract/` —— **逆向研究底稿,不是构建产物**(costura 解包产物、反编译 .cs/.il/.resources、mh/mp/paktest/tables/zstd 子目录),是行为对齐原版工具的权威参考。已 gitignore 但必须排除在一切清理之外。
- `sample.exe` —— 分析用样例输入,gitignored,不可再生。
- `data/` —— 运行时用户数据(MOD 库、设置)。仓库中通常不存在,一旦出现绝不可清理。

**可安全清理(均可再生成):** `desktop/node_modules`(npm ci)、各处 `bin`/`obj`(dotnet build)、`dist/*.zip`(publish.ps1 重打,注意无 tag 时版本号为 0.0.0)、`desktop/dist`、`desktop/release`。

## 其他已知坑

- `net9.0-windows` + 注册表访问,仅 Windows 可跑。`<InternalsVisibleTo Include="MHSeries.ModManager.Tests" />`,测试可访问 internal 类型(EquipKind、PakReader、ModuleConfigParser 等)。
- 嵌入资源逻辑名前缀 `MhModManager.Data.Equipment.`;清单是 gzip `.txt.gz`(崛起清单用反斜杠分隔);缺资源抛 InvalidOperationException,但 AvpPatcher 缺模板静默跳过。
- `ArchiveExtractor.ExtractNested` 解嵌套压缩包的目标目录名是 `文件名 + "files"`(无点),成功后删除原压缩包;zip-slip 有防护;`UnwrapRoot` 最多剥 6 层单子目录包装,遇部署根名(nativePC/natives/reframework/autorun/plugins/pl/wp/…)即停。
- 游戏运行时(`CheckGameRunning` 开启且按 exe 名找到进程)拒绝一切切换操作。
- 导入布局识别 `ModLayoutParser`:World 有 `nativePC/` 用之,`pl|wp|plugins` 映射进 `nativePC/`;崛起/荒野 `natives|reframework` 直接用,`autorun`→`reframework/autorun`、`plugins`→`reframework/plugins`,散装 .lua/.dll 分别归入;`GameDlls` 白名单(dinput8.dll 等 24 个)放游戏根,其他 exe 拒绝;不安全路径在解析期直接抛 InvalidDataException。
- 测试直接读写真实 `settings.json`(AppPaths 根 = 测试二进制旁 `data`),用随机 appId(9,000,001–9,900,000)隔离但共享同一文件,且全局禁并行——新增测试沿用此模式。
- 加密压缩包测试夹具在 `tests/MHSeries.ModManager.Tests/Testdata/`(ZipCrypto/AES 加密 zip + 加密 7z,密码 `mhmod123`,内含 `nativePC/hello.bin`),由 csproj 的 `None CopyToOutputDirectory` 拷到输出目录;重新生成用 7z CLI:`7z a -tzip -p<密码> -mem=ZipCrypto|-mem=AES256`(SharpCompress 只能读加密不能写加密)。
- **git-bash(MSYS)冒烟测试 Host 的坑**:curl 等原生 exe 的 JSON 参数里 `\\` 会被 MSYS 吃成 `\`,请求体变成非法 JSON、端点返回 400,极易误判为后端绑定问题——测试请求里的 Windows 路径一律写正斜杠;前端(Electron fetch + `JSON.stringify`)不受影响。
- 前端弹窗统一 state 驱动渲染(`renderEquipPicker`/`renderPasswordPrompt`):`.modal-mask` 外壳点按关闭 + `.modal[data-stop]` 内部屏蔽,Esc/回车在全局 keydown 处理;`render()` 整体重建 innerHTML,弹窗输入框要在 render 后显式聚焦。
