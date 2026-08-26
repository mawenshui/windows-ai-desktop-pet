[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Part,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Summary,

    [datetime]$ReleaseDate = (Get-Date)
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$versionPath = Join-Path $projectRoot 'VERSION'
$changelogPath = Join-Path $projectRoot 'CHANGELOG.md'
$currentVersion = [System.IO.File]::ReadAllText($versionPath).Trim()

if ($currentVersion -notmatch '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$') {
    throw "VERSION is not valid SemVer: $currentVersion"
}

$major = [int]$Matches.major
$minor = [int]$Matches.minor
$patch = [int]$Matches.patch

switch ($Part) {
    'Major' { $major++; $minor = 0; $patch = 0 }
    'Minor' { $minor++; $patch = 0 }
    'Patch' { $patch++ }
}

$newVersion = "$major.$minor.$patch"
$dateText = $ReleaseDate.ToString('yyyy-MM-dd')
$entry = "## [$newVersion] - $dateText`r`n`r`n### Changed`r`n`r`n- $Summary`r`n`r`n"
$changelog = [System.IO.File]::ReadAllText($changelogPath)
$firstVersionHeading = [regex]::Match($changelog, '(?m)^## \[')

if (-not $firstVersionHeading.Success) {
    throw 'CHANGELOG.md does not contain a version heading.'
}

$updatedChangelog = $changelog.Insert($firstVersionHeading.Index, $entry)

if ($PSCmdlet.ShouldProcess($projectRoot, "Bump version from $currentVersion to $newVersion")) {
    [System.IO.File]::WriteAllText($versionPath, "$newVersion`n", $utf8NoBom)
    [System.IO.File]::WriteAllText($changelogPath, $updatedChangelog, $utf8NoBom)
    Write-Output "Version bumped: $currentVersion -> $newVersion"
    Write-Output 'Update application metadata, installer metadata, README, release notes, and all affected documents before packaging.'
}
else {
    Write-Output "No files changed. Proposed version: $currentVersion -> $newVersion"
}
