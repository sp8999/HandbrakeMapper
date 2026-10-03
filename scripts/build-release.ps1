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

# Locate Inno Setup robustly. GitHub-hosted Windows runners install the
# Chocolatey package under Program Files (x86), but Chocolatey's PATH update
# is not guaranteed to be visible in the same job step.
$Iscc = $null

$Command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
if ($Command) {
    $Iscc = $Command.Source
}

if (!$Iscc) {
    $ProgramFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $ProgramFiles64 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)

    $IsccCandidates = @(
        (Join-Path $ProgramFilesX86 "Inno Setup 6\ISCC.exe"),
        (Join-Path $ProgramFiles64 "Inno Setup 6\ISCC.exe"),
        "C:\ProgramData\chocolatey\bin\ISCC.exe",
        "C:\tools\InnoSetup\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) }

    $Iscc = $IsccCandidates | Select-Object -First 1
}

if (!$Iscc) {
    Write-Host "Checked common Inno Setup locations:"
    Write-Host "  ProgramFilesX86 = $([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86))"
    Write-Host "  ProgramFiles    = $([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))"
    throw "Inno Setup 6 (ISCC.exe) not found after installation."
}

Write-Host "Using Inno Setup compiler: $Iscc"
Write-Host "Building installer..."
& $Iscc (Join-Path $Root "installer\HandbrakeMapper.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup build failed" }

Write-Host "Done. See: $InstallerOut"
