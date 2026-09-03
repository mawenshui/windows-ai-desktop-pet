[CmdletBinding()]
param([string]$Version)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim() }
$zip = Join-Path $projectRoot "dist\portable\windows-ai-desktop-pet-v$Version-portable.zip"
$setup = Join-Path $projectRoot "dist\installer\windows-ai-desktop-pet-v$Version-setup.exe"
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + "-$PID"
$root = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "build\$Version\release-smoke-$runId"))
$buildRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'build'))
if (-not $root.StartsWith($buildRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke root escaped build/.' }
$portable = Join-Path $root 'portable'
$installed = Join-Path $root 'installed'
[System.IO.Directory]::CreateDirectory($portable) | Out-Null

Expand-Archive -LiteralPath $zip -DestinationPath $portable
$portableExe = Join-Path $portable 'WindowsAiDesktopPet.exe'
$portableSmoke = Start-Process -FilePath $portableExe -ArgumentList @('--smoke') -Wait -PassThru -WindowStyle Hidden
if ($portableSmoke.ExitCode -ne 0) { throw "Portable smoke failed with exit code $($portableSmoke.ExitCode)." }

$installLog = Join-Path $root 'install.log'
$installArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$installed", "/LOG=$installLog")
$install = Start-Process -FilePath $setup -ArgumentList $installArgs -Wait -PassThru -WindowStyle Hidden
if ($install.ExitCode -ne 0) { throw "Installer failed with exit code $($install.ExitCode)." }
$installedExe = Join-Path $installed 'WindowsAiDesktopPet.exe'
if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) { throw 'Installed executable is missing.' }
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe)
if ($versionInfo.ProductVersion -notlike "$Version*") { throw "Installed product version mismatch: $($versionInfo.ProductVersion)" }
$installedSmoke = Start-Process -FilePath $installedExe -ArgumentList @('--smoke') -Wait -PassThru -WindowStyle Hidden
if ($installedSmoke.ExitCode -ne 0) { throw "Installed smoke failed with exit code $($installedSmoke.ExitCode)." }

$uninstaller = Join-Path $installed 'unins000.exe'
$uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru -WindowStyle Hidden
if ($uninstall.ExitCode -ne 0) { throw "Uninstaller failed with exit code $($uninstall.ExitCode)." }
Write-Output "[PASS] portable, install, installed smoke and uninstall: $root"
