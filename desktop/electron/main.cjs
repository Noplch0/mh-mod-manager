const { app, BrowserWindow, dialog, ipcMain } = require("electron");
const path = require("path");
const { spawn } = require("child_process");

const API = "http://127.0.0.1:17865";
const UI = process.env.VITE_DEV_SERVER_URL || "http://127.0.0.1:5173";
const dataDir = path.resolve(__dirname, "../../data");

let host;
let win;

function startHost() {
  const project = path.resolve(__dirname, "../../src/HuntForge.Host/HuntForge.Host.csproj");
  host = spawn("dotnet", ["run", "--project", project, "--", "--data", dataDir], {
    windowsHide: true,
    stdio: "pipe"
  });
  host.stdout.on("data", chunk => process.stdout.write(chunk));
  host.stderr.on("data", chunk => process.stderr.write(chunk));
}

async function waitForHost() {
  for (let i = 0; i < 80; i++) {
    try {
      const res = await fetch(`${API}/api/bootstrap`);
      if (res.ok) return;
    } catch {
    }
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error("HuntForge 后端未能启动");
}

function createWindow() {
  win = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 1100,
    minHeight: 720,
    backgroundColor: "#0A0D12",
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });
  win.loadURL(UI);
}

ipcMain.handle("pick-mods", async () => {
  const result = await dialog.showOpenDialog(win, {
    title: "批量导入 MOD",
    properties: ["openFile", "multiSelections"],
    filters: [
      { name: "MOD 压缩包", extensions: ["zip", "7z", "rar"] },
      { name: "所有文件", extensions: ["*"] }
    ]
  });
  return result.canceled ? [] : result.filePaths;
});

ipcMain.handle("pick-folder", async () => {
  const result = await dialog.showOpenDialog(win, {
    title: "选择游戏安装目录",
    properties: ["openDirectory"]
  });
  return result.canceled ? "" : result.filePaths[0];
});

ipcMain.handle("api-base", () => API);

app.whenReady().then(async () => {
  startHost();
  await waitForHost();
  createWindow();
});

app.on("window-all-closed", () => {
  if (host && !host.killed) host.kill();
  app.quit();
});

app.on("before-quit", () => {
  if (host && !host.killed) host.kill();
});
