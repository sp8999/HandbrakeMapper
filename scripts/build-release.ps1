param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Redist = Join-Path $Root "installer\redist"
$Dist = Join-Path $Root "dist\app"
$InstallerOut = Join-Path $Root "dist\installer"

New-Item -ItemType Directory -Force -Path $Redist, $Dist, $InstallerOut | Out-Null

$VJoyUrl = "https://github.com/BrunnerInnovation/vJoy/releases/download/v2.2.2.0/vJoySetup_v2.2.2.0_Win10_Win11.exe"
$HidHideUrl = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"
$VJoyFile = Join-Path $Redist "vJoySetup_v2.2.2.0_Win10_Win11.exe"
$HidHideFile = Join-Path $Redist "HidHide_1.5.230_x64.exe"

if (!(Test-Path $VJoyFile)) {
    Write-Host "Downloading official vJoy 2.2.2.0 (BrunnerInnovation signed build)..."
    Invoke-WebRequest -Uri $VJoyUrl -OutFile $VJoyFile
}
if (!(Test-Path $HidHideFile)) {
    Write-Host "Downloading official HidHide 1.5.230..."
    Invoke-WebRequest -Uri $HidHideUrl -OutFile $HidHideFile
}

Write-Host "Publishing Handbrake Mapper..."
dotnet publish (Join-Path $Root "src\HandbrakeMapper\HandbrakeMapper.csproj") `
    -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$IsccCandidates = @(
    "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$Iscc) { throw "Inno Setup 6 not found. Install it first." }

Write-Host "Building installer..."
& $Iscc (Join-Path $Root "installer\HandbrakeMapper.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed" }

Write-Host "Done. See: $InstallerOut"
