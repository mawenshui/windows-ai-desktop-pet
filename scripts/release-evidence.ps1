# Shared report contract. Reports are observations, never an approval substitute.
Set-StrictMode -Version Latest

$script:ReleaseChecks = @{
    automated = @('project-validation','unit-integration','evidence-rejection')
    ui = @('eight-selectors-mouse','eight-selectors-keyboard','unsaved-navigation','focus-restore','tray-actions')
    system = @('startup-help-storage-exit','multi-screen-and-scaling-geometry-contracts')
    performance = @('coldStartupMs','toolWindowRevealP95Ms','searchFirstPageP95Ms','idleCpuPercent','idleWorkingSetMb','dragObservedFps','handleCount')
    'package-smoke' = @('portable','install','installed-start','uninstall')
    signatures = @('installer-signature','portable-executable-signature')
    hardware = @('multi-monitor','scaling-100-200','display-hotplug','sleep-resume','timezone-clock','explorer-restart','upgrade-data-retention')
}

# A formal release is blocked only by the automated regression suite and by
# proving that both distributed forms can install/start/uninstall normally.
# The remaining collectors are optional diagnostics and never an approval
# substitute for these two required gates.
$script:RequiredReleaseGates = @('automated','package-smoke')

function Get-ReleaseInputFingerprint {
    param([Parameter(Mandatory)][string]$Root)
    $paths = [Collections.Generic.List[string]]::new()
    foreach ($directory in @('src','tests','scripts','packaging','config','assets','.github/workflows')) {
        Get-ChildItem -LiteralPath (Join-Path $Root $directory) -Recurse -File | Where-Object {
            $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        } | ForEach-Object { $paths.Add([IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\','/')) }
    }
    foreach ($file in @('VERSION','global.json','NuGet.Config','Directory.Build.props','docs/USER_MANUAL.html')) { $paths.Add($file) }
    $lines = foreach ($path in ($paths | Sort-Object -CaseSensitive -Unique)) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $Root $path) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$path $hash"
    }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($lines -join "`n"))).ToLowerInvariant()
}

function Get-ReleaseContext {
    param([Parameter(Mandatory)][string]$Root)
    $commit = (& git -C $Root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
    @{
        version = [IO.File]::ReadAllText((Join-Path $Root 'VERSION')).Trim()
        sourceCommit = $commit
        inputFingerprint = Get-ReleaseInputFingerprint $Root
    }
}

function Write-ReleaseEvidence {
    param([string]$Root, [string]$Directory, [string]$Gate,
        [ValidateSet('PASS','FAIL','SKIP')][string]$Status, [hashtable]$Context,
        [DateTimeOffset]$StartedAt, [object[]]$Checks = @(), [object[]]$Assets = @())
    $current = Get-ReleaseContext $Root
    if ($current.inputFingerprint -ne $Context.inputFingerprint -or $current.sourceCommit -ne $Context.sourceCommit) {
        $Status = 'FAIL'
        $Checks += @{ name = 'source-unchanged'; status = 'FAIL'; detail = 'Inputs changed during validation.' }
    }
    $report = [ordered]@{ schemaVersion=1; gate=$Gate; version=$Context.version;
        sourceCommit=$Context.sourceCommit; inputFingerprint=$Context.inputFingerprint;
        status=$Status; startedAtUtc=$StartedAt.ToUniversalTime().ToString('O');
        completedAtUtc=[DateTimeOffset]::UtcNow.ToString('O');
        environment=@{ os=[Environment]::OSVersion.VersionString; runtime=[Environment]::Version.ToString();
            interactive=[Environment]::UserInteractive }; checks=@($Checks); assets=@($Assets) }
    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory "$Gate.json"), ($report | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function Test-ReleaseEvidenceSet {
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][hashtable]$Context,
        [Parameter(Mandatory)][object[]]$Assets, [DateTimeOffset]$Now = [DateTimeOffset]::UtcNow,
        [int]$MaximumAgeHours = 72)
    foreach ($gate in $script:RequiredReleaseGates) {
        $file = Join-Path $Directory "$gate.json"
        if (-not [IO.File]::Exists($file)) { throw "Missing release evidence: $gate" }
        $report = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        if ($report.schemaVersion -ne 1 -or $report.gate -ne $gate -or $report.status -ne 'PASS') { throw "Release gate did not pass: $gate" }
        foreach ($key in @('version','sourceCommit','inputFingerprint')) {
            if ($report.$key -cne $Context[$key]) { throw "Stale $key in release evidence: $gate" }
        }
        $start = [DateTimeOffset]::Parse($report.startedAtUtc)
        $end = [DateTimeOffset]::Parse($report.completedAtUtc)
        if ($start -gt $end -or $end -gt $Now.AddMinutes(5) -or $start -lt $Now.AddHours(-$MaximumAgeHours)) { throw "Expired or invalid report time: $gate" }
        if (@($report.checks).Count -eq 0 -or @($report.checks | Where-Object status -ne 'PASS').Count -gt 0) { throw "Incomplete checks: $gate" }
        foreach ($required in $script:ReleaseChecks[$gate]) {
            if (@($report.checks | Where-Object name -CEQ $required).Count -ne 1) { throw "Missing or duplicated check: $gate/$required" }
        }
        if (-not $report.environment.os -or -not $report.environment.runtime) { throw "Missing environment: $gate" }
        if ($gate -eq 'package-smoke') {
            if (@($report.assets).Count -ne $Assets.Count) { throw "Missing asset bindings: $gate" }
            foreach ($asset in $Assets) {
                $bound = @($report.assets | Where-Object name -CEQ $asset.name)
                if ($bound.Count -ne 1 -or $bound[0].sha256 -cne $asset.sha256) { throw "Asset mismatch: $gate" }
            }
        }
    }
    return $true
}
