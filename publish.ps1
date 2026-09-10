$ErrorActionPreference = "Stop"
Set-Location -LiteralPath $PSScriptRoot

function Get-GitTagVersion {
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (-not $git) {
        throw "git not found. Install Git and add it to PATH."
    }

    $raw = ""
    try {
        $raw = (git -C $PSScriptRoot describe --tags --abbrev=0 2>$null | Select-Object -First 1)
    } catch {
        $raw = ""
    }
    if (-not $raw) {
        $raw = (git -C $PSScriptRoot tag --list --sort=-v:refname | Select-Object -First 1)
    }
    if (-not $raw) {
        Write-Host "No git tag found, using 0.0.0. Create one with: git tag v1.0.0" -ForegroundColor Yellow
        return "0.0.0"
    }

    $version = $raw.ToString().Trim() -replace "^v", ""
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "git tag is empty."
    }
    return $version
}

function Get-AssemblyVersion([string] $version) {
    if ($version -match "^\d+\.\d+(?:\.\d+)?(?:\.\d+)?") {
        $parts = $Matches[0].Split(".")
        while ($parts.Length -lt 3) {
            $parts += "0"
        }
        return [string]::Join(".", $parts[0..2])
    }
    return "0.0.0"
}

$version = Get-GitTagVersion
$assemblyVersion = Get-AssemblyVersion $version
Write-Host "Git tag version: $version"

$desktop = Join-Path $PSScriptRoot "desktop"
$distRoot = Join-Path $PSScriptRoot "dist"
$outDir = Join-Path $distRoot "mh-mod-manager"
$zipPath = Join-Path $distRoot "mh-mod-manager-$version.zip"
$hostPublish = Join-Path $PSScriptRoot "src\mh-mod-manager.Host\bin\Release\net9.0-windows\win-x64\publish"
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
    $electronVersion = "37.10.3"
    $pkg = Join-Path $desktop "node_modules\electron\package.json"
    if (Test-Path -LiteralPath $pkg) {
        $electronVersion = (Get-Content -LiteralPath $pkg -Raw | ConvertFrom-Json).version
    }
    $zip = Join-Path $env:TEMP "electron-v$electronVersion-win32-x64.zip"
    $urls = @(
        "https://npmmirror.com/mirrors/electron/$electronVersion/electron-v$electronVersion-win32-x64.zip",
        "https://cdn.npmmirror.com/binaries/electron/v$electronVersion/electron-v$electronVersion-win32-x64.zip",
        "https://github.com/electron/electron/releases/download/v$electronVersion/electron-v$electronVersion-win32-x64.zip"
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
    if (Test-Path -LiteralPath $electronDist) {
        Remove-Item -LiteralPath $electronDist -Recurse -Force
    }
    Expand-Archive -LiteralPath $zip -DestinationPath $electronDist -Force
}
Pop-Location
if (-not (Test-Path -LiteralPath $electronExe)) {
    throw "electron.exe not found"
}

Write-Host "2/4 publish host (self-contained, not single-file)"
dotnet publish (Join-Path $PSScriptRoot "src\mh-mod-manager.Host\mh-mod-manager.Host.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false `
    -p:DebugType=none -p:DebugSymbols=false `
    -p:Version=$assemblyVersion -p:InformationalVersion=$version
if (-not $?) { throw "host publish failed" }
if (-not (Test-Path -LiteralPath (Join-Path $hostPublish "mh-mod-manager.Host.exe"))) {
    throw "mh-mod-manager.Host.exe not found"
}

Write-Host "3/4 build UI"
Push-Location $desktop
npx vite build
if (-not $?) { Pop-Location; throw "ui build failed" }
Pop-Location

Write-Host "4/4 assemble app and zip"
New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
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
New-Item -ItemType Directory -Force -Path (Join-Path $appDir "build") | Out-Null
Copy-Item -Path (Join-Path $desktop "electron\*") -Destination (Join-Path $appDir "electron") -Recurse -Force
Copy-Item -Path (Join-Path $uiDist "*") -Destination (Join-Path $appDir "dist") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $desktop "build\icon.ico") -Destination (Join-Path $appDir "build\icon.ico") -Force
$packageJson = @"
{
  "name": "mh-mod-manager",
  "version": "$version",
  "main": "electron/main.cjs"
}
"@
$packageJson | Set-Content -LiteralPath (Join-Path $appDir "package.json") -Encoding ascii

$hostDir = Join-Path $outDir "resources\host"
New-Item -ItemType Directory -Force -Path $hostDir | Out-Null
Copy-Item -Path (Join-Path $hostPublish "*") -Destination $hostDir -Recurse -Force

$singleFileHint = Join-Path $hostDir "mh-mod-manager.Host.exe"
if (-not (Test-Path -LiteralPath $singleFileHint)) {
    throw "mh-mod-manager.Host.exe not found in app host folder"
}
$hostDll = Join-Path $hostDir "mh-mod-manager.Host.dll"
if (-not (Test-Path -LiteralPath $hostDll)) {
    throw "Publish looks like a single-file exe (mh-mod-manager.Host.dll missing). Keep PublishSingleFile=false."
}

$outElectron = Join-Path $outDir "electron.exe"
$appExe = Join-Path $outDir "mh-mod-manager.exe"
if (Test-Path -LiteralPath $outElectron) {
    Move-Item -LiteralPath $outElectron -Destination $appExe -Force
}
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "mh-mod-manager.exe not found"
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "ZIP archive was not created"
}

Remove-Item -LiteralPath $outDir -Recurse -Force

if (Test-Path -LiteralPath $uiDist) {
    Remove-Item -LiteralPath $uiDist -Recurse -Force
}
if (Test-Path -LiteralPath $hostPublish) {
    Remove-Item -LiteralPath $hostPublish -Recurse -Force
}

Write-Host ""
Write-Host "Done"
Write-Host "Version : $version"
Write-Host "Zip     : $zipPath"
