# HuntForge

HuntForge 是一个面向《怪物猎人：世界》《怪物猎人：崛起》《怪物猎人：荒野》的 Windows MOD 管理器。它从仓库中的 `狩技MOD盒子.exe` 提取并重做了三款游戏的核心部署规则，界面使用 Avalonia 11，数据存放在程序目录下的 `data/`。

## 已提取的游戏规则

| 游戏 | Steam App ID | 游戏目录 | 普通 MOD 目录 | PAK 补丁命名 |
| --- | ---: | --- | --- | --- |
| 世界 | 582010 | `Monster Hunter World` | `nativePC` | 不使用 PAK 补丁编号 |
| 崛起 | 1446780 | `MonsterHunterRise` | `natives`、`reframework` | `re_chunk_000.pak.patch_###.pak` |
| 荒野 | 2246340 | `MonsterHunterWilds` | `natives`、`reframework` | `re_chunk_000.pak.sub_000.pak.patch_###.pak` |

解析器保留了原程序的常见 MOD 包布局：世界识别 `nativePC` 或 `pl/wp/plugins`，崛起识别 `natives`、`reframework`、`weapon/player`，荒野识别 `natives`、`reframework`、`art/gamedesign`。RE Engine 游戏还识别 `.pak`、`reframework/autorun`、`reframework/plugins`、Lua 和 DLL 插件。

启用时会按分组顺序再按组内顺序部署。靠后的分组覆盖靠前的分组，组内靠后的 MOD 覆盖靠前的 MOD。崛起和荒野的 PAK 补丁编号会读取游戏目录里已有的 `patch_` 文件，从最大编号后面接着分配，因此游戏更新增加官方 PAK 后仍可用。覆盖游戏已有文件前会先备份，禁用最后一个占用该文件的 MOD 后会还原。游戏 `.exe` 不会被覆盖。每个游戏都有一个默认排在最下的「未分组」，自定义分组可全开或全关。

带 `ModuleConfig.xml` 的包会按原工具的默认规则安装：必选文件、Required/Recommended 选项，以及单选组的第一项。当前没有选项选择界面，因此不会把互斥选项全部复制进去。

## 构建和运行

环境要求：Windows、.NET 9 SDK。

```powershell
dotnet restore
dotnet build src/MHSeries.ModManager/MHSeries.ModManager.csproj
dotnet run --project src/MHSeries.ModManager/MHSeries.ModManager.csproj
dotnet test tests/MHSeries.ModManager.Tests/MHSeries.ModManager.Tests.csproj
```

首次进入游戏时可使用设置面板选择安装目录，也可以由 Steam 注册表和 `libraryfolders.vdf` 自动探测。导入支持 `.zip`、`.7z`、`.rar`，并会尝试处理嵌套压缩包。
