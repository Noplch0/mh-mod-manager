$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot

$desktop = Join-Path $PSScriptRoot "desktop"
$outDir = Join-Path $PSScriptRoot "dist\HuntForge"
$hostPublish = Join-Path $PSScriptRoot "src\HuntForge.Host\bin\Release\net9.0-windows\win-x64\publish"
$uiDist = Join-Path $desktop "dist"
$electronDist = Join-Path $desktop "node_modules\electron\dist"

Write-Host "1/4 npm install"
Push-Location $desktop
if (-not (Test-Path -LiteralPath (Join-Path $desktop "node_modules"))) {
    npm install
    if (-not $?) { Pop-Location; throw "npm install failed" }
}
$electronExe = Join-Path $desktop "node_modules\electron\dist\electron.exe"
if (-not (Test-Path -LiteralPath $electronExe)) {
    Write-Host "download electron runtime"
    $version = "37.10.3"
    $pkg = Join-Path $desktop "node_modules\electron\package.json"
    if (Test-Path -LiteralPath $pkg) {
        $version = (Get-Content -LiteralPath $pkg -Raw | ConvertFrom-Json).version
    }
    $zip = Join-Path $env:TEMP "electron-v$version-win32-x64.zip"
    $urls = @(
        "https://npmmirror.com/mirrors/electron/$version/electron-v$version-win32-x64.zip",
        "https://cdn.npmmirror.com/binaries/electron/v$version/electron-v$version-win32-x64.zip",
        "https://github.com/electron/electron/releases/download/v$version/electron-v$version-win32-x64.zip"
    )
    $downloaded = $false
    foreach ($url in $urls) {
        Write-Host "try $url"
        & curl.exe -L --connect-timeout 20 --retry 2 -o $zip $url
        if ($? -and (Test-Path -LiteralPath $zip) -and ((Get-Item -LiteralPath $zip).Length -gt 1MB)) {
            $downloaded = $true
            break
        }
    }
    if (-not $downloaded) { Pop-Location; throw "electron download failed" }
    $dist = Join-Path $desktop "node_modules\electron\dist"
    if (Test-Path -LiteralPath $dist) {
        Remove-Item -LiteralPath $dist -Recurse -Force
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $dist -Force
}
Pop-Location
if (-not (Test-Path -LiteralPath $electronExe)) {
    throw "electron.exe not found"
}

Write-Host "2/4 publish host (self-contained, not single-file)"
dotnet publish (Join-Path $PSScriptRoot "src\HuntForge.Host\HuntForge.Host.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false
if (-not $?) { throw "host publish failed" }
if (-not (Test-Path -LiteralPath (Join-Path $hostPublish "HuntForge.Host.exe"))) {
    throw "HuntForge.Host.exe not found"
}

Write-Host "3/4 build UI"
Push-Location $desktop
npx vite build
if (-not $?) { Pop-Location; throw "ui build failed" }
Pop-Location

Write-Host "4/4 assemble app folder"
if (Test-Path -LiteralPath $outDir) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
}
Copy-Item -LiteralPath $electronDist -Destination $outDir -Recurse

$defaultApp = Join-Path $outDir "resources\default_app.asar"
if (Test-Path -LiteralPath $defaultApp) {
    Remove-Item -LiteralPath $defaultApp -Force
}

$appDir = Join-Path $outDir "resources\app"
New-Item -ItemType Directory -Force -Path (Join-Path $appDir "electron") | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $appDir "dist") | Out-Null
Copy-Item -Path (Join-Path $desktop "electron\*") -Destination (Join-Path $appDir "electron") -Recurse -Force
Copy-Item -Path (Join-Path $uiDist "*") -Destination (Join-Path $appDir "dist") -Recurse -Force
@'
{
  "name": "huntforge",
  "version": "1.0.0",
  "main": "electron/main.cjs"
}
'@ | Set-Content -LiteralPath (Join-Path $appDir "package.json") -Encoding ascii

$hostDir = Join-Path $outDir "resources\host"
New-Item -ItemType Directory -Force -Path $hostDir | Out-Null
Copy-Item -Path (Join-Path $hostPublish "*") -Destination $hostDir -Recurse -Force

$electronExe = Join-Path $outDir "electron.exe"
$appExe = Join-Path $outDir "HuntForge.exe"
if (Test-Path -LiteralPath $electronExe) {
    Move-Item -LiteralPath $electronExe -Destination $appExe -Force
}

if (-not (Test-Path -LiteralPath $appExe)) {
    throw "HuntForge.exe not found"
}

Write-Host ""
Write-Host "Done (folder build, not single-file exe)"
Write-Host $appExe
