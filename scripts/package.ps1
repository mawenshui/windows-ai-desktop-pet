[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$versionFile = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $versionFile
}
elseif ($Version -ne $versionFile) {
    throw "Requested package version $Version does not match VERSION $versionFile."
}

$portableAdapter = Join-Path $projectRoot 'packaging/build-portable.ps1'
$installerAdapter = Join-Path $projectRoot 'packaging/build-installer.ps1'

if (-not (Test-Path -LiteralPath $portableAdapter -PathType Leaf)) {
    throw 'Portable packaging adapter is missing. Implement packaging/build-portable.ps1 after the application stack is selected.'
}

if (-not (Test-Path -LiteralPath $installerAdapter -PathType Leaf)) {
    throw 'Installer packaging adapter is missing. Implement packaging/build-installer.ps1 after the application stack is selected.'
}

$portableDirectory = Join-Path $projectRoot 'dist/portable'
$installerDirectory = Join-Path $projectRoot 'dist/installer'
$checksumDirectory = Join-Path $projectRoot 'dist/checksums'

& $portableAdapter -Version $Version -OutputDirectory $portableDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $installerAdapter -Version $Version -OutputDirectory $installerDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$portableAsset = Join-Path $portableDirectory "windows-ai-desktop-pet-v$Version-portable.zip"
$installerAsset = Join-Path $installerDirectory "windows-ai-desktop-pet-v$Version-setup.exe"

foreach ($asset in @($portableAsset, $installerAsset)) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
        throw "Expected release asset was not generated: $asset"
    }
    if ((Get-Item -LiteralPath $asset).Length -eq 0) {
        throw "Release asset is empty: $asset"
    }
}

$checksumLines = foreach ($asset in @($portableAsset, $installerAsset)) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($asset))"
}

$checksumPath = Join-Path $checksumDirectory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, $utf8NoBom)

Write-Output "Generated portable asset: $portableAsset"
Write-Output "Generated installer asset: $installerAsset"
Write-Output "Generated checksum manifest: $checksumPath"
