# mh-mod-manager

mh-mod-manager 是一个面向《怪物猎人：世界》《怪物猎人：崛起》《怪物猎人：荒野》的 Windows MOD 管理器。部署规则仍由 C# 核心处理，界面改为 Electron + 本地 API。

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

禁用 MOD 后，可以在右侧详情的“游戏内装备”区域将识别到的装备改为另一套游戏内装备。装备名称和文件清单会嵌入程序，不依赖仓库根目录的运行时 `data/` 目录。

## 发布

双击 `publish.bat`，或：

```powershell
.\publish.ps1
```

脚本会读取最新 git tag 作为版本号（没有 tag 时用 `0.0.0`），构建**目录版**程序（自包含 .NET 后端，不是单文件 exe），压缩完成后删除临时展开目录，只在 `dist\` 保留压缩包：

- `dist\mh-mod-manager-<version>.zip`：压缩包内含整个 `mh-mod-manager` 目录

数据目录在 exe 旁边的 `data\`。打包前可先打标签：

```powershell
git tag v1.0.0
```
