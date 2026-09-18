[CmdletBinding()]
param(
    [switch]$AllowSavedCredential,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $AllowSavedCredential) {
    Write-Error 'Live AI testing is opt-in. Rerun with -AllowSavedCredential after the user authorizes access to the saved application credential.'
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $projectRoot 'build\reports\live-ai-report.json'
}

$startedAt = [DateTimeOffset]::UtcNow
$checks = [System.Collections.Generic.List[object]]::new()
$apiKey = $null
$overallStatus = 'FAIL'
$exitCode = 1

function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Category,
        [int]$LatencyMs = 0,
        [int]$ResultCount = 0
    )

    $checks.Add([ordered]@{
        name = $Name
        status = $Status
        category = $Category
        latencyMs = $LatencyMs
        resultCount = $ResultCount
    })
    Write-Output "[$Status] $Name - category=$Category; latencyMs=$LatencyMs; resultCount=$ResultCount"
}

try {
    $aiBin = (Resolve-Path -LiteralPath (Join-Path $projectRoot 'src\AiPet.AI\bin\Release\net8.0-windows')).Path
    Add-Type -Path (Join-Path $aiBin 'AiPet.Todos.dll')
    Add-Type -Path (Resolve-Path -LiteralPath (Join-Path $projectRoot 'src\AiPet.Storage\bin\Release\net8.0\AiPet.Storage.dll'))
    Add-Type -Path (Resolve-Path -LiteralPath (Join-Path $projectRoot 'src\AiPet.Secrets\bin\Release\net8.0-windows\AiPet.Secrets.dll'))
    Add-Type -Path (Join-Path $aiBin 'AiPet.AI.dll')

    $settings = [AiPet.Storage.SettingsStore]::new().Load()
    $profile = $settings.Ai.Profiles |
        Where-Object { $_.Id -eq $settings.Ai.ActiveProfileId } |
        Select-Object -First 1

    if ($null -eq $profile) {
        $overallStatus = 'SKIP'
        $exitCode = 2
        Add-Check 'saved-profile' 'SKIP' 'NoActiveProfile'
        throw [InvalidOperationException]::new('LIVE_AI_SKIP')
    }

    $apiKey = [AiPet.Secrets.WindowsCredentialStore]::Load($profile.SecretTargetName)
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        $overallStatus = 'SKIP'
        $exitCode = 2
        Add-Check 'saved-credential' 'SKIP' 'CredentialUnavailable'
        throw [InvalidOperationException]::new('LIVE_AI_SKIP')
    }

    Add-Check 'saved-profile-and-credential' 'PASS' 'Redacted'

    $connectionClient = [AiPet.AI.OpenAiCompatibleClient]::new()
    $connection = $connectionClient.TestConnectionAsync(
        $profile.Endpoint,
        $profile.Model,
        $apiKey,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()

    if ($connection.Status -eq [AiPet.AI.AiConnectionStatus]::Connected) {
        Add-Check 'model-list' 'PASS' $connection.ErrorCategory.ToString() $connection.LatencyMs
    }
    else {
        # Some OpenAI-compatible providers accept a configured model for
        # generation without returning that alias from GET /models. Keep this
        # result separate so a successful generation test is not mislabeled.
        Add-Check 'model-list' 'NOT_CONFIRMED' $connection.ErrorCategory.ToString() $connection.LatencyMs
    }

    $generation = $connectionClient.VerifyGenerationAsync(
        $profile.Endpoint,
        $profile.Model,
        $apiKey,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $generationPassed = $generation.Status -eq [AiPet.AI.AiConnectionStatus]::Connected
    Add-Check 'structured-todo-draft' $(if ($generationPassed) { 'PASS' } else { 'FAIL' }) $generation.ErrorCategory.ToString() $generation.LatencyMs

    $todoClient = [AiPet.AI.OpenAiCompatibleTodoClient]::new()
    $now = [DateTimeOffset]::Now
    $item = [AiPet.AI.TodayPlanItemInput]::new(
        [Guid]::NewGuid(),
        '自动化测试任务',
        '',
        $now.AddHours(3),
        $now)
    [AiPet.AI.TodayPlanItemInput[]]$items = @($item)
    $request = [AiPet.AI.TodayPlanRequest]::new(
        $items,
        $now,
        [TimeZoneInfo]::Local.Id,
        512,
        $null)
    $plan = $todoClient.PlanTodayAsync(
        $profile.Endpoint,
        $profile.Model,
        $apiKey,
        $request,
        [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $planPassed = $plan.Status -eq [AiPet.AI.TodayPlanStatus]::DraftReady -and $plan.Blocks.Count -eq 1
    Add-Check 'today-plan-draft' $(if ($planPassed) { 'PASS' } else { 'FAIL' }) $plan.ErrorCategory.ToString() 0 $plan.Blocks.Count

    if ($generationPassed -and $planPassed) {
        $overallStatus = 'PASS'
        $exitCode = 0
    }
}
catch [InvalidOperationException] {
    if ($_.Exception.Message -ne 'LIVE_AI_SKIP') {
        Add-Check 'live-ai-runner' 'FAIL' $_.Exception.GetType().Name
    }
}
catch [System.ComponentModel.Win32Exception] {
    $overallStatus = 'SKIP'
    $exitCode = 2
    Add-Check 'live-ai-runner' 'SKIP' ("WindowsCredentialError{0}" -f $_.Exception.NativeErrorCode)
}
catch {
    Add-Check 'live-ai-runner' 'FAIL' $_.Exception.GetType().Name
}
finally {
    # Never serialize or echo settings, endpoint, model, profile name, target
    # name, response content, or credential. The report is deliberately limited
    # to stable categories and aggregate counts.
    $apiKey = $null
    [GC]::Collect()

    $reportDirectory = Split-Path -Parent $ReportPath
    [System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
    [ordered]@{
        schemaVersion = 1
        status = $overallStatus
        startedAtUtc = $startedAt
        completedAtUtc = [DateTimeOffset]::UtcNow
        savedValuesRedacted = $true
        writesUserData = $false
        checks = @($checks)
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding utf8NoBOM
    Write-Output "[$overallStatus] live AI report: $ReportPath"
}

exit $exitCode
