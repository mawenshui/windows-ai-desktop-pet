[CmdletBinding()]
param([switch]$CI, [switch]$NoBuild)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$reportRoot = Join-Path $projectRoot 'build\reports\ui-e2e'
[System.IO.Directory]::CreateDirectory($reportRoot) | Out-Null
$reportPath = Join-Path $reportRoot 'ui-e2e-report.json'
$screenshotPath = Join-Path $reportRoot 'ui-e2e-failure.png'
$stage = 'initialize'
$process = $null
$startedAt = [DateTimeOffset]::UtcNow

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AiPetMouseInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    public const uint RightDown = 0x0008;
    public const uint RightUp = 0x0010;
    public const uint LeftDown = 0x0002;
    public const uint LeftUp = 0x0004;
}
'@

function Find-Element([string]$Name, [int]$TimeoutSeconds = 10) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $automationIdCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Name)
    $identityCondition = [System.Windows.Automation.OrCondition]::new(
        $nameCondition,
        $automationIdCondition)
    $processCondition = if ($null -ne $script:process) {
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:process.Id)
    } else { $null }
    do {
        if ($null -eq $processCondition) {
            $found = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants, $identityCondition)
            if ($null -ne $found) { return $found }
        } else {
            $processWindows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children, $processCondition)
            foreach ($processWindow in $processWindows) {
                if ($processWindow.Current.Name -eq $Name -or $processWindow.Current.AutomationId -eq $Name) {
                    return $processWindow
                }
                $found = $processWindow.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants, $identityCondition)
                if ($null -ne $found) { return $found }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI element was not found: $Name"
}

function Click-Element([string]$Name) {
    $element = Find-Element $Name
    $rect = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $rect.IsEmpty -or $rect.Width -le 0 -or $rect.Height -le 0) {
        throw "UI element is not clickable: $Name"
    }
    Write-Output ("[UI] click '{0}' type={1} bounds={2},{3},{4},{5}" -f
        $Name,
        $element.Current.ControlType.ProgrammaticName,
        [int]$rect.Left,
        [int]$rect.Top,
        [int]$rect.Width,
        [int]$rect.Height)
    [AiPetMouseInput]::SetCursorPos([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2)) | Out-Null
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
}

function Click-ScreenPoint([int]$X, [int]$Y, [string]$Label) {
    Write-Output ("[UI] click '{0}' point={1},{2}" -f $Label, $X, $Y)
    if ($script:toolWindowHandle -ne [IntPtr]::Zero) {
        [AiPetMouseInput]::SetForegroundWindow($script:toolWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 100
    }
    [AiPetMouseInput]::SetCursorPos($X, $Y) | Out-Null
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
}

function Select-ComboByKeyboard([int]$DownCount) {
    $keys = '{HOME}' + ((1..$DownCount | ForEach-Object { '{DOWN}' }) -join '') + '{ENTER}'
    $lastError = $null
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        try {
            [System.Windows.Forms.SendKeys]::SendWait($keys)
            $lastError = $null
            break
        } catch {
            $lastError = $_
            Start-Sleep -Milliseconds 150
        }
    }
    if ($null -ne $lastError) { throw $lastError }
    Start-Sleep -Milliseconds 200
}

function Expand-Element([string]$Name) {
    $element = Find-Element $Name
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        $pattern.Expand()
        Start-Sleep -Milliseconds 150
    }
}

function Select-ComboItem([string]$ComboName, [string]$ItemName) {
    Click-Element $ComboName
    Click-Element $ItemName
}

function Get-Value([string]$Name) {
    $element = Find-Element $Name
    return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Save-Screenshot([string]$Path) {
    $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size) }
        finally { $graphics.Dispose() }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $bitmap.Dispose() }
}

try {
    if (-not $NoBuild) {
        $stage = 'build'
        & dotnet build (Join-Path $projectRoot 'src\AiPet.sln') -c Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    }

    $stage = 'launch'
    $exe = Join-Path $projectRoot 'src\AiPet.App\bin\Release\net8.0-windows\WindowsAiDesktopPet.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Application executable is missing.' }
    $isolatedData = Join-Path $projectRoot ('build\ui-e2e-data\' + [Guid]::NewGuid().ToString('N'))
    $probePath = Join-Path $isolatedData 'ui-probe.json'
    [System.IO.Directory]::CreateDirectory($isolatedData) | Out-Null
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($exe, '--preview --ui-e2e')
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['AIPET_UI_TEST'] = '1'
    $startInfo.Environment['AIPET_ASSETS'] = (Join-Path $projectRoot 'assets\pets')
    $startInfo.Environment['APPDATA'] = $isolatedData
    $startInfo.Environment['LOCALAPPDATA'] = $isolatedData
    $startInfo.Environment['AIPET_APP_DATA_ROOT'] = $isolatedData
    $startInfo.Environment['AIPET_UI_E2E_PROBE'] = $probePath
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $toolWindow = Find-Element '小方工具袋' 15
    $toolWindowHandle = [IntPtr]$toolWindow.Current.NativeWindowHandle

    $stage = 'dropdown-selection'
    $stage = 'dropdown-search-scope'
    $toolBounds = $toolWindow.Current.BoundingRectangle
    Click-ScreenPoint ([int]($toolBounds.Left + 102)) ([int]($toolBounds.Top + 141)) 'SearchScopeSelector'
    Select-ComboByKeyboard 1
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).SelectedSearchScopeId -ne 'apps') { throw 'Search scope did not update.' }
    $stage = 'dropdown-result-category'
    Click-ScreenPoint ([int]($toolBounds.Left + 340)) ([int]($toolBounds.Top + 461)) 'CategoryFilterSelector'
    Select-ComboByKeyboard 4
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).Category -ne '图片') { throw 'Result category did not update.' }

    $stage = 'keyboard-navigation'
    Click-Element '待办'
    $stage = 'dropdown-todo-filter'
    Click-ScreenPoint ([int]($toolBounds.Left + 216)) ([int]($toolBounds.Top + 280)) 'TodoFilterSelector'
    Select-ComboByKeyboard 3
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).todoFilterId -ne 'completed') { throw 'Todo filter did not update.' }
    Click-Element '设置'
    $stage = 'dropdown-theme'
    Click-ScreenPoint ([int]($toolBounds.Left + 200)) ([int]($toolBounds.Top + 360)) 'ThemeSelector'
    Select-ComboByKeyboard 2
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).ThemePreference -ne 'dark') { throw 'Theme did not update.' }

    $stage = 'dialog-and-focus-restore'
    Click-Element '主页'
    $null = Find-Element 'AI 配置尚未保存'
    Click-Element '取消'
    Click-Element '收起工具窗口'
    $null = Find-Element 'AI 配置尚未保存'
    Click-Element '取消'
    $toolWindow = Find-Element '小方工具袋'
    $toolWindow.SetFocus()
    Click-Element '收起工具窗口'
    $null = Find-Element 'AI 配置尚未保存'
    Click-Element '放弃修改'

    $stage = 'tray-equivalent-and-exit'
    $pet = Find-Element '桌面宠物'
    $rect = $pet.Current.BoundingRectangle
    [AiPetMouseInput]::SetCursorPos([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2)) | Out-Null
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::RightDown, 0, 0, 0, [UIntPtr]::Zero)
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::RightUp, 0, 0, 0, [UIntPtr]::Zero)
    Click-Element '退出'
    if (-not $process.WaitForExit(5000)) { throw 'Application did not exit through its context menu.' }

    $report = [ordered]@{ schemaVersion = 1; status = 'PASS'; startedAtUtc = $startedAt; completedAtUtc = [DateTimeOffset]::UtcNow; stages = @('launch','keyboard-navigation','dropdown-selection','dialog-and-focus-restore','tray-equivalent-and-exit') }
    [System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
    Write-Output "[PASS] independent desktop UI automation completed: $reportPath"
}
catch {
    Write-Output ("[UI] failure: {0}" -f $_.Exception.Message)
    try { Save-Screenshot $screenshotPath } catch { }
    $report = [ordered]@{ schemaVersion = 1; status = 'FAIL'; failedStage = $stage; stableError = 'UI_E2E_FAILED'; startedAtUtc = $startedAt; completedAtUtc = [DateTimeOffset]::UtcNow; screenshot = 'ui-e2e-failure.png' }
    [System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
    Write-Error "UI E2E failed at stage '$stage'. See the redacted report and screenshot under build/reports/ui-e2e."
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
