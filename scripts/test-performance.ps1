[CmdletBinding()]
param([switch]$Enforce, [switch]$NoBuild)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$budgets = Get-Content -LiteralPath (Join-Path $projectRoot 'config\performance-budgets.json') -Raw | ConvertFrom-Json
$reportRoot = Join-Path $projectRoot 'build\reports\performance'
[System.IO.Directory]::CreateDirectory($reportRoot) | Out-Null
$searchReportPath = Join-Path $reportRoot 'search-performance.json'
$reportPath = Join-Path $reportRoot 'performance-report.json'

if (-not $NoBuild) {
    & dotnet build (Join-Path $projectRoot 'src\AiPet.sln') -c Release --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}

& dotnet run --project (Join-Path $projectRoot 'tests\performance\AiPet.Performance\AiPet.Performance.csproj') -c Release -- $searchReportPath $budgets.datasetItems
if ($LASTEXITCODE -ne 0) { throw 'Search performance probe failed.' }
$search = Get-Content -LiteralPath $searchReportPath -Raw | ConvertFrom-Json

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AiPetPerformanceMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
}
'@

function Find-Element([string]$Name, [int]$TimeoutMilliseconds = 10000, [switch]$Visible) {
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        $item = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $item -and (-not $Visible -or -not $item.Current.IsOffscreen)) { return $item }
        Start-Sleep -Milliseconds 5
    } while ($watch.ElapsedMilliseconds -lt $TimeoutMilliseconds)
    throw "Performance UI element was not found: $Name"
}

function Click-At([double]$X, [double]$Y) {
    [AiPetPerformanceMouse]::SetCursorPos([int]$X, [int]$Y) | Out-Null
    [AiPetPerformanceMouse]::mouse_event([AiPetPerformanceMouse]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    [AiPetPerformanceMouse]::mouse_event([AiPetPerformanceMouse]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
}

$process = $null
try {
    $exe = Join-Path $projectRoot 'src\AiPet.App\bin\Release\net8.0-windows\WindowsAiDesktopPet.exe'
    $isolatedData = Join-Path $projectRoot ('build\performance-data\' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($isolatedData) | Out-Null
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($exe, '--preview')
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['AIPET_UI_TEST'] = '1'
    $startInfo.Environment['AIPET_ASSETS'] = (Join-Path $projectRoot 'assets\pets')
    $startInfo.Environment['APPDATA'] = $isolatedData
    $startInfo.Environment['LOCALAPPDATA'] = $isolatedData
    $startInfo.Environment['AIPET_APP_DATA_ROOT'] = $isolatedData
    $startupWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $tool = Find-Element '小方工具袋' 15000 -Visible
    $startupWatch.Stop()
    $pet = Find-Element '桌面宠物' 5000 -Visible
    $petRect = $pet.Current.BoundingRectangle

    $samples = [System.Collections.Generic.List[double]]::new()
    for ($i = 0; $i -lt $budgets.toolWindowRevealIterations; $i++) {
        $close = Find-Element '收起工具窗口'
        $close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 20
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        Click-At ($petRect.Left + $petRect.Width / 2) ($petRect.Top + $petRect.Height / 2)
        $null = Find-Element '小方工具袋' 2000 -Visible
        $watch.Stop()
        $samples.Add($watch.Elapsed.TotalMilliseconds)
    }
    $ordered = @($samples | Sort-Object)
    $p95Index = [Math]::Max(0, [Math]::Ceiling($ordered.Count * 0.95) - 1)
    $revealP95 = $ordered[$p95Index]

    $cpuBefore = $process.TotalProcessorTime
    Start-Sleep -Seconds 3
    $process.Refresh()
    $cpuAfter = $process.TotalProcessorTime
    $idleCpu = (($cpuAfter - $cpuBefore).TotalMilliseconds / 3000 / [Environment]::ProcessorCount) * 100
    $workingSetMb = $process.WorkingSet64 / 1MB
    $handles = $process.HandleCount

    $tool = Find-Element '小方工具袋'
    if (-not $tool.Current.IsOffscreen) { (Find-Element '收起工具窗口').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 50 }
    $dragWatch = [System.Diagnostics.Stopwatch]::StartNew()
    $dragObservations = 0
    [AiPetPerformanceMouse]::SetCursorPos([int]($petRect.Left + $petRect.Width / 2), [int]($petRect.Top + $petRect.Height / 2)) | Out-Null
    [AiPetPerformanceMouse]::mouse_event([AiPetPerformanceMouse]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    for ($i = 1; $i -le 60; $i++) {
        [AiPetPerformanceMouse]::SetCursorPos([int]($petRect.Left + $petRect.Width / 2 - $i * 2), [int]($petRect.Top + $petRect.Height / 2)) | Out-Null
        Start-Sleep -Milliseconds 16
        $dragObservations++
    }
    [AiPetPerformanceMouse]::mouse_event([AiPetPerformanceMouse]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    $dragWatch.Stop()
    $dragFps = $dragObservations / $dragWatch.Elapsed.TotalSeconds

    $metrics = [ordered]@{
        coldStartupMs = $startupWatch.Elapsed.TotalMilliseconds
        toolWindowRevealP95Ms = $revealP95
        searchFirstPageP95Ms = [double]$search.searchFirstPage.p95Ms
        idleCpuPercent = $idleCpu
        idleWorkingSetMb = $workingSetMb
        dragObservedFps = $dragFps
        handleCount = $handles
    }
    $checks = foreach ($property in $budgets.budgets.PSObject.Properties) {
        $actual = [double]$metrics[$property.Name]
        $target = [double]$property.Value
        $minimumMetric = $property.Name -eq 'dragObservedFps'
        [pscustomobject]@{ metric = $property.Name; actual = $actual; budget = $target; status = if (($minimumMetric -and $actual -ge $target) -or (-not $minimumMetric -and $actual -le $target)) { 'PASS' } else { 'FAIL' } }
    }
    $report = [ordered]@{
        schemaVersion = 1
        createdAtUtc = [DateTimeOffset]::UtcNow
        environment = [ordered]@{ os = [Environment]::OSVersion.VersionString; framework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription; logicalProcessors = [Environment]::ProcessorCount }
        dataset = $search.dataset
        metrics = $metrics
        checks = @($checks)
    }
    [System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 6), [System.Text.UTF8Encoding]::new($false))
    $checks | Format-Table -AutoSize
    Write-Output "Performance report: $reportPath"
    if ($Enforce -and $checks.Status -contains 'FAIL') { throw 'One or more frozen performance budgets were exceeded.' }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
