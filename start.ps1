Set-Location -LiteralPath $PSScriptRoot
if (-not (Test-Path "desktop\node_modules")) {
    Set-Location desktop
    npm install
    Set-Location ..
}
# electron 的 postinstall 可能没跑完整（比如 dist 是 publish.ps1 手动下载的），path.txt 缺失时会报
# "Electron failed to install correctly"，这里补上标记文件。
$electronPathFile = "desktop\node_modules\electron\path.txt"
if ((Test-Path "desktop\node_modules\electron\dist\electron.exe") -and -not (Test-Path $electronPathFile)) {
    Set-Content -Path $electronPathFile -Value "electron.exe" -NoNewline -Encoding ascii
}
Set-Location desktop
npm start
