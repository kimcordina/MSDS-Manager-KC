# Publishes MSDS Manager KC for the Windows work PC and optionally
# creates a Desktop shortcut. Library path stays in %LocalAppData%\MSDSManagerKC\settings.json.
#
# Usage (from the repo root, PowerShell):
#   .\scripts\publish-desktop.ps1
#   .\scripts\publish-desktop.ps1 -SelfContained
#   .\scripts\publish-desktop.ps1 -SkipShortcut

[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$SkipShortcut,
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) {
    $OutputDir = Join-Path $root "publish\MSDSManager"
}

$project = Join-Path $root "src\MSDSManager\MSDSManager.csproj"
$rid = "win-x64"

Write-Host "Publishing MSDS Manager KC ($rid, Release)..."
$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", $rid,
    "-o", $OutputDir,
    "/p:PublishSingleFile=false",
    "/p:IncludeNativeLibrariesForSelfExtract=true"
)

if ($SelfContained) {
    $publishArgs += @("--self-contained", "true")
} else {
    $publishArgs += @("--self-contained", "false")
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exe = Join-Path $OutputDir "MSDSManager.exe"
if (-not (Test-Path $exe)) {
    throw "Expected $exe after publish."
}

Write-Host "Published to $OutputDir"
Write-Host "The SDS library folder is remembered in %LocalAppData%\MSDSManagerKC\settings.json"

if (-not $SkipShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shortcutPath = Join-Path $desktop "MSDS Manager KC.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $OutputDir
    $shortcut.Description = "MSDS Manager KC — SDS library and Outlook attach"
    $shortcut.Save()
    Write-Host "Desktop shortcut created: $shortcutPath"
}

Write-Host ""
Write-Host "Next: start Outlook, run the app, confirm the OneDrive MSDS folder, then Re-index if prompted."
