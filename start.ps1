Set-Location -LiteralPath $PSScriptRoot
if (-not (Test-Path "desktop\node_modules")) {
    Set-Location desktop
    npm install
    Set-Location ..
}
Set-Location desktop
npm start
