const { app, BrowserWindow, dialog, ipcMain, shell } = require("electron");
const path = require("path");
const { spawn } = require("child_process");

const API = "http://127.0.0.1:17865";
const isDev = !app.isPackaged;
const dataDir = process.env.PORTABLE_EXECUTABLE_DIR
  ? path.join(process.env.PORTABLE_EXECUTABLE_DIR, "data")
  : isDev
    ? path.resolve(__dirname, "../../data")
    : path.join(path.dirname(app.getPath("exe")), "data");

let host;
let win;

function startHost() {
  if (isDev) {
    const project = path.resolve(__dirname, "../../src/mh-mod-manager.Host/mh-mod-manager.Host.csproj");
    host = spawn("dotnet", ["run", "--project", project, "--", "--data", dataDir], {
      windowsHide: true,
      stdio: "pipe"
    });
  } else {
    const exe = path.join(process.resourcesPath, "host", "mh-mod-manager.Host.exe");
    host = spawn(exe, ["--data", dataDir], {
      windowsHide: true,
      stdio: "pipe"
    });
  }

  host.stdout.on("data", chunk => process.stdout.write(chunk));
  host.stderr.on("data", chunk => process.stderr.write(chunk));
}

function stopHost() {
  if (!host || host.killed) return;
  if (process.platform === "win32" && host.pid) {
    spawn("taskkill", ["/pid", String(host.pid), "/t", "/f"], { windowsHide: true });
  } else {
    host.kill();
  }
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
  throw new Error("mh-mod-manager 后端未能启动");
}

function createWindow() {
  win = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 1100,
    minHeight: 720,
    backgroundColor: "#F2F6FB",
    frame: false,
    autoHideMenuBar: true,
    icon: path.join(app.getAppPath(), "build/icon.ico"),
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false
    }
  });

  if (isDev) {
    win.loadURL(process.env.VITE_DEV_SERVER_URL || "http://127.0.0.1:5173");
  } else {
    win.loadFile(path.join(__dirname, "../dist/index.html"));
  }
}

ipcMain.handle("win-minimize", () => win?.minimize());
ipcMain.handle("win-toggle-maximize", () => {
  if (!win) return;
  if (win.isMaximized()) win.unmaximize();
  else win.maximize();
});
ipcMain.handle("win-close", () => win?.close());

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

ipcMain.handle("pick-update", async () => {
  const result = await dialog.showOpenDialog(win, {
    title: "选择更新用的 MOD 压缩包",
    properties: ["openFile"],
    filters: [
      { name: "MOD 压缩包", extensions: ["zip", "7z", "rar"] },
      { name: "所有文件", extensions: ["*"] }
    ]
  });
  return result.canceled ? "" : result.filePaths[0];
});

ipcMain.handle("pick-folder", async () => {
  const result = await dialog.showOpenDialog(win, {
    title: "选择游戏安装目录",
    properties: ["openDirectory"]
  });
  return result.canceled ? "" : result.filePaths[0];
});

ipcMain.handle("open-path", async (_event, folder) => {
  if (!folder) return "路径为空";
  return shell.openPath(folder);
});

ipcMain.handle("api-base", () => API);

app.whenReady().then(async () => {
  startHost();
  await waitForHost();
  createWindow();
});

app.on("window-all-closed", () => {
  stopHost();
  app.quit();
});

app.on("before-quit", () => {
  stopHost();
});
