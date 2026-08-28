[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# Resolve the project root through .NET BCL so CJK segments in the script
# source are not mangled by PS 5.1's GBK default codepage.
$projectRoot = [System.IO.DirectoryInfo]::new((Join-Path $PSScriptRoot '..')).FullName
$versionFile = ([System.IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION'))).Trim()

if ([string]::IsNullOrEmpty($Version)) { $Version = $versionFile }
elseif ($Version -ne $versionFile) {
    throw "Requested version $Version does not match VERSION $versionFile."
}

if ([string]::IsNullOrEmpty($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot 'dist\portable'
}
if (-not [System.IO.Directory]::Exists($OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$buildDir   = Join-Path $projectRoot 'build\.staging\portable\win-x64'
$assetRoot  = Join-Path $projectRoot 'assets\pets'
$sln        = Join-Path $projectRoot 'src\AiPet.sln'
$appCsproj  = Join-Path $projectRoot 'src\AiPet.App\AiPet.App.csproj'
$appExe     = 'WindowsAiDesktopPet.exe'

# Clean previous staging to avoid stale files
if ([System.IO.Directory]::Exists($buildDir)) {
    Write-Output "[INFO ] cleaning previous staging: $buildDir"
    Remove-Item -LiteralPath $buildDir -Recurse -Force
}
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

# 1. Self-contained dotnet publish. The portable package must not require
#    a separately installed .NET runtime on a supported x64 Windows host.
Write-Output "[INFO ] dotnet publish (single step: build + publish; SDK 10 needs explicit RID)"
$publishLog = & dotnet publish $appCsproj -c $Configuration -r win-x64 --self-contained true -o $buildDir -p:UseAppHost=true -nologo
$publishExit = $LASTEXITCODE
$publishLog | Select-Object -First 12 | ForEach-Object { Write-Output $_ }
if ($publishExit -ne 0) { throw "dotnet publish failed with exit code $publishExit" }

# 2. Copy assets/pets/ next to the executable so AssetsResolver picks them
#    up at runtime (it walks up from AppContext.BaseDirectory).
if (Test-Path -LiteralPath $assetRoot) {
    $assetDst = Join-Path $buildDir 'assets\pets'
    if (Test-Path -LiteralPath $assetDst) {
        Remove-Item -LiteralPath $assetDst -Recurse -Force
    }
    Write-Output "[INFO ] copying assets/pets -> $assetDst"
    Copy-Item -LiteralPath $assetRoot -Destination $assetDst -Recurse -Force
} else {
    Write-Output "[WARN ] assets/pets not found at $assetRoot; runtime will show 'no assets' error."
}

$manualSource = Join-Path $projectRoot 'docs\USER_MANUAL.html'
if (-not [System.IO.File]::Exists($manualSource)) {
    throw "Offline user manual is missing: $manualSource"
}
$manualDirectory = Join-Path $buildDir 'docs'
New-Item -ItemType Directory -Path $manualDirectory -Force | Out-Null
Copy-Item -LiteralPath $manualSource -Destination (Join-Path $manualDirectory 'USER_MANUAL.html') -Force

# 3. Sanity: the exe must exist and be non-zero
$exePath = Join-Path $buildDir $appExe
if (-not [System.IO.File]::Exists($exePath)) {
    throw "Expected exe not found after publish: $exePath"
}
$exeSize = (Get-Item -LiteralPath $exePath).Length
if ($exeSize -lt 1024) {
    throw "Published exe is suspiciously small ($exeSize bytes): $exePath"
}
Write-Output "[INFO ] exe OK: $exePath ($exeSize bytes)"

# 4. Zip the portable directory
$zipName = "windows-ai-desktop-pet-v$Version-portable.zip"
$zipPath = Join-Path $OutputDirectory $zipName
if ([System.IO.File]::Exists($zipPath)) { Remove-Item -LiteralPath $zipPath -Force }
Write-Output "[INFO ] zipping -> $zipPath"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $buildDir, $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
$zipSize = (Get-Item -LiteralPath $zipPath).Length
Write-Output "[OK   ] portable asset: $zipPath ($zipSize bytes)"
Write-Output "Version: $Version"
Write-Output "Asset:   $zipPath"
