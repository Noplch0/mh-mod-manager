const { contextBridge, ipcRenderer, webUtils } = require("electron");

contextBridge.exposeInMainWorld("huntforge", {
  apiBase: () => ipcRenderer.invoke("api-base"),
  pickMods: () => ipcRenderer.invoke("pick-mods"),
  pickFolder: () => ipcRenderer.invoke("pick-folder"),
  filePath: file => {
    try { return webUtils.getPathForFile(file); }
    catch { return file.path || ""; }
  }
});
