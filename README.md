# HuntForge

HuntForge 是一个面向《怪物猎人：世界》《怪物猎人：崛起》《怪物猎人：荒野》的 Windows MOD 管理器。部署规则仍由 C# 核心处理，界面改为 Electron + 本地 API。

数据目录是仓库根下的 `data/`，与界面实现无关。

## 环境

- Windows
- .NET 9 SDK
- Node.js 20+

## 开发运行

```powershell
dotnet test tests/MHSeries.ModManager.Tests/MHSeries.ModManager.Tests.csproj
cd desktop
npm install
npm start
```

首次进入游戏时可在设置里选择安装目录，也可以由 Steam 注册表和 `libraryfolders.vdf` 自动探测。导入支持 `.zip`、`.7z`、`.rar`，可多选或拖放到窗口。

## 发布

```powershell
.\publish.ps1
```

会生成目录版程序 `dist\HuntForge\HuntForge.exe`（自包含 .NET 后端，不是单文件 exe）。直接运行即可，数据目录在 exe 旁边的 `data\`。
