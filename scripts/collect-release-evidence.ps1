[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('automated','ui','system','performance','package-smoke','signatures','hardware')][string]$Gate,
    [string]$EvidenceDirectory
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'release-evidence.ps1')
if (-not $EvidenceDirectory) { $EvidenceDirectory=Join-Path $root 'build/reports/release-evidence' }
$context=Get-ReleaseContext $root
$started=[DateTimeOffset]::UtcNow
$checks=@(); $assets=@(); $status='FAIL'
try {
    if ($Gate -in @('package-smoke','signatures','hardware')) {
        $assets=@(foreach ($entry in @(@('portable','portable.zip'),@('installer','setup.exe'))) {
            $name="windows-ai-desktop-pet-v$($context.version)-$($entry[1])"
            @{ name=$name; sha256=(Get-FileHash -LiteralPath (Join-Path $root "dist/$($entry[0])/$name") -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
    }
    switch ($Gate) {
        'automated' {
            & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test.ps1') -CI
            if ($LASTEXITCODE -ne 0) { throw 'Automated suite failed.' }
            $checks=@($script:ReleaseChecks.automated | ForEach-Object { @{name=$_;status='PASS'} }); $status='PASS'
        }
        'ui' {
            & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-ui-e2e.ps1') -CI -NoBuild
            if ($LASTEXITCODE -ne 0) { throw 'Independent UI runner failed.' }
            $report=Get-Content -LiteralPath (Join-Path $root 'build/reports/ui-e2e/ui-e2e-report.json') -Raw | ConvertFrom-Json
            if ($report.status -ne 'PASS' -or [DateTimeOffset]$report.startedAtUtc -lt $started) { throw 'UI report is stale or failed.' }
            $checks=@($report.checks); $status='PASS'
        }
        'system' {
            & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-system-e2e.ps1') -CI
            if ($LASTEXITCODE -ne 0) { throw 'System runner failed.' }
            $report=Get-Content -LiteralPath (Join-Path $root 'build/reports/system-e2e-report.json') -Raw | ConvertFrom-Json
            if ([DateTimeOffset]$report.createdAt -lt $started) { throw 'System report is stale.' }
            $checks=@($report.results | Where-Object name -In $script:ReleaseChecks.system)
            $status=if ($checks.Count -eq 2 -and @($checks | Where-Object status -ne 'PASS').Count -eq 0) {'PASS'} else {'FAIL'}
        }
        'performance' {
            & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test-performance.ps1') -Enforce -NoBuild
            if ($LASTEXITCODE -ne 0) { throw 'Performance runner failed.' }
            $report=Get-Content -LiteralPath (Join-Path $root 'build/reports/performance/performance-report.json') -Raw | ConvertFrom-Json
            if ([DateTimeOffset]$report.createdAtUtc -lt $started) { throw 'Performance report is stale.' }
            $checks=@($report.checks | ForEach-Object { @{name=$_.metric;status=$_.status;actual=$_.actual;budget=$_.budget} }); $status='PASS'
        }
        'package-smoke' {
            & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'smoke-release.ps1') -Version $context.version
            if ($LASTEXITCODE -ne 0) { throw 'Package smoke failed.' }
            $checks=@($script:ReleaseChecks['package-smoke'] | ForEach-Object { @{name=$_;status='PASS'} }); $status='PASS'
        }
        'signatures' {
            $extraction=Join-Path $root ('build/signature-check/'+[Guid]::NewGuid().ToString('N'))
            Expand-Archive -LiteralPath (Join-Path $root "dist/portable/$($assets[0].name)") -DestinationPath $extraction
            $paths=@((Join-Path $root "dist/installer/$($assets[1].name)"),(Join-Path $extraction 'WindowsAiDesktopPet.exe'))
            $checks=@(for($i=0;$i -lt 2;$i++) {
                $signature=Get-AuthenticodeSignature -LiteralPath $paths[$i]
                @{name=$script:ReleaseChecks.signatures[$i];status=if($signature.Status -eq 'Valid'){'PASS'}elseif($signature.Status -eq 'NotSigned'){'SKIP'}else{'FAIL'};detail=$signature.Status.ToString()}
            })
            $status=if($checks.status -contains 'FAIL'){'FAIL'}elseif($checks.status -contains 'SKIP'){'SKIP'}else{'PASS'}
        }
        'hardware' {
            $checks=@($script:ReleaseChecks.hardware | ForEach-Object { @{name=$_;status='SKIP';detail='Requires witnessed physical-device or disposable-account validation, bound to these assets.'} }); $status='SKIP'
        }
    }
} catch {
    $checks+=@{name='runner';status='FAIL';detail=$_.Exception.GetType().Name}
    $status='FAIL'
    Write-Output "[FAIL] $Gate runner: $($_.Exception.Message)"
} finally {
    Write-ReleaseEvidence -Root $root -Directory $EvidenceDirectory -Gate $Gate -Status $status -Context $context -StartedAt $started -Checks $checks -Assets $assets
}
Write-Output "[$status] $Gate evidence: $(Join-Path $EvidenceDirectory "$Gate.json")"
if ($status -eq 'FAIL') { exit 1 }
