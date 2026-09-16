[CmdletBinding()]
param([switch]$RestartExplorer, [switch]$CI)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
Add-Type -AssemblyName System.Windows.Forms
$reportDirectory = Join-Path $projectRoot 'build\reports'
[System.IO.Directory]::CreateDirectory($reportDirectory) | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$Name, [string]$Status, [string]$Detail) {
    $results.Add([pscustomobject]@{ name = $Name; status = $Status; detail = $Detail })
    Write-Output "[$Status] $Name - $Detail"
}

try {
    & dotnet test (Join-Path $projectRoot 'src\AiPet.sln') -c Release --no-restore --nologo --filter 'FullyQualifiedName~PetWindowPositionerTests|FullyQualifiedName~PetPopoverPositionerTests|FullyQualifiedName~TrayIconTests'
    if ($LASTEXITCODE -ne 0) { throw 'system-boundary tests failed' }
    Add-Result 'multi-screen-and-scaling-geometry-contracts' 'PASS' 'negative coordinates and 100%-200% geometry contracts passed'
} catch { Add-Result 'multi-screen-and-scaling-geometry-contracts' 'FAIL' $_.Exception.GetType().Name }

try {
    & dotnet run --project (Join-Path $projectRoot 'src\AiPet.App\AiPet.App.csproj') -c Release --no-build -- --smoke --assets (Join-Path $projectRoot 'assets\pets')
    if ($LASTEXITCODE -ne 0) { throw 'smoke mode failed' }
    Add-Result 'startup-help-storage-exit' 'PASS' 'application smoke scenario completed'
} catch { Add-Result 'startup-help-storage-exit' 'FAIL' $_.Exception.GetType().Name }

if ($RestartExplorer) {
    Add-Result 'explorer-tray-recovery' 'SKIP' 'acknowledged, but Explorer restart still requires the disposable interactive lab procedure; this runner never terminates the active shell'
} else { Add-Result 'explorer-tray-recovery' 'SKIP' 'rerun with -RestartExplorer in a disposable interactive lab session to acknowledge the disruptive prerequisite' }

$screens = @([System.Windows.Forms.Screen]::AllScreens)
if ($screens.Count -gt 1) {
    Add-Result 'physical-multi-monitor-present' 'PASS' "$($screens.Count) monitors detected"
} else { Add-Result 'physical-multi-monitor-present' 'SKIP' 'only one physical monitor detected' }

if ($screens | Where-Object { $_.Bounds.Left -lt 0 -or $_.Bounds.Top -lt 0 }) {
    Add-Result 'physical-negative-coordinate-layout' 'PASS' 'a monitor with negative desktop coordinates is present'
} else { Add-Result 'physical-negative-coordinate-layout' 'SKIP' 'current desktop has no negative-coordinate monitor' }

Add-Type @'
using System.Runtime.InteropServices;
public static class AiPetSystemDpi {
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
}
'@
$primaryScale = [Math]::Round(([AiPetSystemDpi]::GetDpiForSystem() / 96.0) * 100)
foreach ($scale in @(100, 125, 150, 175, 200)) {
    if ($primaryScale -eq $scale) { Add-Result "physical-primary-scale-$scale" 'PASS' "primary display is currently configured at $scale%" }
    else { Add-Result "physical-primary-scale-$scale" 'SKIP' "current primary scale is $primaryScale%" }
}

$primary = [System.Windows.Forms.Screen]::PrimaryScreen
$taskbarEdge = if ($primary.WorkingArea.Top -gt $primary.Bounds.Top) { 'top' }
    elseif ($primary.WorkingArea.Left -gt $primary.Bounds.Left) { 'left' }
    elseif ($primary.WorkingArea.Right -lt $primary.Bounds.Right) { 'right' }
    else { 'bottom' }
Add-Result 'physical-current-taskbar-edge' 'PASS' "current primary taskbar edge: $taskbarEdge"
Add-Result 'physical-display-hotplug' 'SKIP' 'requires a technician to connect or disconnect a real display during the run'
Add-Result 'physical-sleep-resume' 'SKIP' 'requires a controlled interactive sleep/resume cycle'
Add-Result 'physical-timezone-and-clock-change' 'SKIP' 'requires a disposable account and controlled system time changes'

$displayEvidence = @($screens | ForEach-Object {
    [pscustomobject]@{
        primary = $_.Primary
        bounds = [pscustomobject]@{ x = $_.Bounds.X; y = $_.Bounds.Y; width = $_.Bounds.Width; height = $_.Bounds.Height }
        workingArea = [pscustomobject]@{ x = $_.WorkingArea.X; y = $_.WorkingArea.Y; width = $_.WorkingArea.Width; height = $_.WorkingArea.Height }
    }
})
$report = [pscustomobject]@{
    schemaVersion = 2
    createdAt = [DateTimeOffset]::Now
    environment = [pscustomobject]@{
        os = [System.Environment]::OSVersion.VersionString
        framework = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        displayCount = $screens.Count
        primaryScalePercent = $primaryScale
        displays = $displayEvidence
    }
    results = $results
}
$reportPath = Join-Path $reportDirectory 'system-e2e-report.json'
$reportJson = $report | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($reportPath, $reportJson, [System.Text.UTF8Encoding]::new($false))
Write-Output "Report: $reportPath"
if ($results.Status -contains 'FAIL') { exit 1 }
