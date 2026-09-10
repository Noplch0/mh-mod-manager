const { contextBridge, ipcRenderer, webUtils } = require("electron");

  contextBridge.exposeInMainWorld("mhModManager", {
  apiBase: () => ipcRenderer.invoke("api-base"),
  pickMods: () => ipcRenderer.invoke("pick-mods"),
  pickUpdate: () => ipcRenderer.invoke("pick-update"),
  pickFolder: () => ipcRenderer.invoke("pick-folder"),
  openPath: folder => ipcRenderer.invoke("open-path", folder),
  filePath: file => {
    try { return webUtils.getPathForFile(file); }
    catch { return file.path || ""; }
  }
});
