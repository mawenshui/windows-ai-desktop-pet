$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-evidence.ps1')
$directory = Join-Path $PSScriptRoot ('../build/gate-tests/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$context = @{version='1.2.3';sourceCommit=('a'*40);inputFingerprint=('b'*64)}
$assets = @(@{name='portable.zip';sha256=('c'*64)}, @{name='setup.exe';sha256=('d'*64)})
$now = [DateTimeOffset]::UtcNow
$template = @{schemaVersion=1;version=$context.version;sourceCommit=$context.sourceCommit;inputFingerprint=$context.inputFingerprint;status='PASS';startedAtUtc=$now.AddMinutes(-2).ToString('O');completedAtUtc=$now.AddMinutes(-1).ToString('O');checks=@(@{name='fixture';status='PASS'});assets=$assets}
function Save-Fixture([string]$gate, [hashtable]$changes = @{}) {
    $report = $template.Clone(); $report.gate=$gate
    $report.environment=@{os='fixture';runtime='fixture'}
    $report.checks=@($script:ReleaseChecks[$gate] | ForEach-Object { @{name=$_;status='PASS'} })
    foreach ($key in $changes.Keys) { $report[$key]=$changes[$key] }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $directory "$gate.json") -Encoding utf8
}
function Assert-Rejected([string]$case) {
    $rejected = $false
    try { $null = Test-ReleaseEvidenceSet $directory $context $assets $now } catch { $rejected=$true }
    if (-not $rejected) { throw "Invalid evidence accepted: $case" }
}
Assert-Rejected 'missing'
foreach ($gate in $script:RequiredReleaseGates) { Save-Fixture $gate }
if (-not (Test-ReleaseEvidenceSet $directory $context $assets $now)) { throw 'Valid fixture rejected.' }
foreach ($change in @(@{status='FAIL'},@{status='SKIP'},@{version='1.2.2'},@{sourceCommit=('f'*40)},@{inputFingerprint=('e'*64)},@{startedAtUtc=$now.AddDays(-4).ToString('O')},@{completedAtUtc=$now.AddHours(1).ToString('O')},@{checks=@()},@{checks=@(@{name='not-run';status='SKIP'})})) {
    Save-Fixture 'automated' $change; Assert-Rejected ($change.Keys -join ','); Save-Fixture 'automated'
}
Save-Fixture 'package-smoke' @{assets=@(@{name='portable.zip';sha256=('e'*64)})}; Assert-Rejected 'asset-mismatch'
Save-Fixture 'package-smoke'
Save-Fixture 'automated' @{checks=@(@{name='arbitrary-pass';status='PASS'})}; Assert-Rejected 'missing-required-check'
Write-Output '[PASS] release evidence rejects missing, failed, skipped, stale, expired, incomplete and mismatched reports.'
