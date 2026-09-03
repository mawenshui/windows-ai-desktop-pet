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
    throw 'Portable packaging adapter is missing.'
}
if (-not (Test-Path -LiteralPath $installerAdapter -PathType Leaf)) {
    throw 'Installer packaging adapter is missing.'
}

# Every packaging run receives an isolated, versioned workspace. Adapters
# refuse to reuse it, so stale files from an older build cannot leak into a
# release candidate even when previous build/ or dist/ content still exists.
$runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfff') + "-$PID"
$stagingRoot = Join-Path $projectRoot "build\$Version\package-$runId"
$candidatePortableDirectory = Join-Path $stagingRoot 'release\portable'
$candidateInstallerDirectory = Join-Path $stagingRoot 'release\installer'
$portablePayloadDirectory = Join-Path $stagingRoot 'payload\portable\win-x64'
$installerPayloadDirectory = Join-Path $stagingRoot 'payload\installer\win-x64'
[System.IO.Directory]::CreateDirectory($candidatePortableDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($candidateInstallerDirectory) | Out-Null

& $portableAdapter `
    -Version $Version `
    -OutputDirectory $candidatePortableDirectory `
    -StagingDirectory $portablePayloadDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $installerAdapter `
    -Version $Version `
    -OutputDirectory $candidateInstallerDirectory `
    -StagingDirectory $installerPayloadDirectory
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$portableName = "windows-ai-desktop-pet-v$Version-portable.zip"
$installerName = "windows-ai-desktop-pet-v$Version-setup.exe"
$candidatePortableAsset = Join-Path $candidatePortableDirectory $portableName
$candidateInstallerAsset = Join-Path $candidateInstallerDirectory $installerName
$candidateAssets = @($candidatePortableAsset, $candidateInstallerAsset)

foreach ($asset in $candidateAssets) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
        throw "Expected release candidate was not generated: $asset"
    }
    if ((Get-Item -LiteralPath $asset).Length -eq 0) {
        throw "Release candidate is empty: $asset"
    }
}

$checksumLines = foreach ($asset in $candidateAssets) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($asset))"
}
$candidateChecksum = Join-Path $stagingRoot 'release\SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($candidateChecksum, $checksumLines, $utf8NoBom)

function Publish-FileAtomically {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $destinationDirectory = [System.IO.Path]::GetDirectoryName($Destination)
    [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    $temporaryDestination = Join-Path $destinationDirectory `
        ([System.IO.Path]::GetFileName($Destination) + ".$runId.tmp")
    if ([System.IO.File]::Exists($temporaryDestination)) {
        throw "Refusing to reuse publication temporary file: $temporaryDestination"
    }
    [System.IO.File]::Copy($Source, $temporaryDestination, $false)
    [System.IO.File]::Move($temporaryDestination, $Destination, $true)
}

$portableAsset = Join-Path $projectRoot "dist\portable\$portableName"
$installerAsset = Join-Path $projectRoot "dist\installer\$installerName"
$checksumPath = Join-Path $projectRoot 'dist\checksums\SHA256SUMS.txt'

Publish-FileAtomically -Source $candidatePortableAsset -Destination $portableAsset
Publish-FileAtomically -Source $candidateInstallerAsset -Destination $installerAsset
Publish-FileAtomically -Source $candidateChecksum -Destination $checksumPath

Write-Output "Packaging workspace:          $stagingRoot"
Write-Output "Generated portable asset:     $portableAsset"
Write-Output "Generated installer asset:    $installerAsset"
Write-Output "Generated checksum manifest:  $checksumPath"
