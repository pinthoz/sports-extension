# Installs Sports Overlay for the current user: publishes a Release build to
# %LocalAppData%\Programs\SportsOverlay and creates a desktop shortcut.
# Run it again after pulling changes to update the installed copy.
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $env:LOCALAPPDATA 'Programs\SportsOverlay'
$exe = Join-Path $dest 'SportsOverlayApp.exe'

# A running installed copy would lock the files we are about to overwrite.
$running = Get-Process SportsOverlayApp -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $exe }
if ($running) {
    Write-Host 'Sports Overlay is running from the install folder; close it first (tray icon > Exit).'
    exit 1
}

dotnet publish (Join-Path $repo 'SportsOverlayApp\SportsOverlayApp.csproj') -c Release -o $dest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Sports Overlay.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $dest
$shortcut.IconLocation = $exe
$shortcut.Description = 'Live sports scores floating over the taskbar'
$shortcut.Save()

Write-Host "Installed to: $dest"
Write-Host "Desktop shortcut: $shortcutPath"
