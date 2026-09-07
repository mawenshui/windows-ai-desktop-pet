[CmdletBinding()]
param([string]$EvidenceDirectory = $env:AIPET_RELEASE_EVIDENCE_DIR)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'release-evidence.ps1')
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) { throw 'Set AIPET_RELEASE_EVIDENCE_DIR to reviewed, current release evidence.' }
$context = Get-ReleaseContext $root
$dirty = @(& git -C $root status --porcelain --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -gt 0) { throw 'Release requires a clean committed workspace, including assets and documentation.' }
$assets = foreach ($entry in @(@('portable','portable.zip'),@('installer','setup.exe'))) {
    $name = "windows-ai-desktop-pet-v$($context.version)-$($entry[1])"
    @{name=$name;sha256=(Get-FileHash -LiteralPath (Join-Path $root "dist/$($entry[0])/$name") -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$null = Test-ReleaseEvidenceSet -Directory $EvidenceDirectory -Context $context -Assets $assets
$checksumLines = @(Get-Content -LiteralPath (Join-Path $root 'dist/checksums/SHA256SUMS.txt') | Where-Object { $_.Trim() })
if ($checksumLines.Count -ne $assets.Count) { throw 'Checksum manifest must contain exactly the current release assets.' }
foreach ($asset in $assets) {
    $matched = @($checksumLines | Where-Object { $_ -match '^([0-9a-fA-F]{64})\s+\*?(.+)$' -and $Matches[2] -ceq $asset.name -and $Matches[1].ToLowerInvariant() -ceq $asset.sha256 })
    if ($matched.Count -ne 1) { throw "Checksum manifest mismatch: $($asset.name)" }
}
Write-Output '[PASS] all current release gates, source and asset bindings verified.'
