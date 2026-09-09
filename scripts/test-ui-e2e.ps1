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
$selectorEvidence = [Collections.Generic.List[object]]::new()

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class AiPetMouseInput {
    public delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    public static IntPtr[] GetTopLevelWindows(uint targetProcessId) {
        var handles = new List<IntPtr>();
        EnumWindows((handle, parameter) => {
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == targetProcessId) handles.Add(handle);
            return true;
        }, IntPtr.Zero);
        return handles.ToArray();
    }
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
            foreach ($handle in [AiPetMouseInput]::GetTopLevelWindows([uint32]$script:process.Id)) {
                $processWindow = $null
                try {
                    $processWindow = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
                } catch { }
                if ($null -eq $processWindow) { continue }
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
    try {
        $invoke = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        Write-Output ("[UI] invoke '{0}' type={1}" -f $Name, $element.Current.ControlType.ProgrammaticName)
        $invoke.Invoke()
        Start-Sleep -Milliseconds 150
        return
    } catch { }
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
    Assert-AppForeground
    [AiPetMouseInput]::SetCursorPos([int]($rect.Left + $rect.Width / 2), [int]($rect.Top + $rect.Height / 2)) | Out-Null
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
}

function Expand-Element([string]$Name) {
    $element = Find-Element $Name
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($pattern.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Expanded) {
        $pattern.Expand()
        Start-Sleep -Milliseconds 150
    }
}

function Select-VisibleChoice([string]$SelectorName, [string]$ItemName, [switch]$SkipEvidence) {
    $selector = Find-Element $SelectorName
    Assert-AppForeground
    $itemCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty, $ItemName),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem))
    $item = $selector.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCondition)
    if ($null -eq $item) {
        $label = $selector.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, $ItemName))
        $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
        while ($null -ne $label -and $label -ne $selector) {
            if ($label.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem) {
                $item = $label
                break
            }
            $label = $walker.GetParent($label)
        }
    }
    if ($null -eq $item) { throw "Visible choice was not found: $SelectorName -> $ItemName" }
    try { $item.GetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView(); Start-Sleep -Milliseconds 100 } catch { }
    $rect = $item.Current.BoundingRectangle
    if ($item.Current.IsOffscreen -or $rect.IsEmpty -or $rect.Width -le 0 -or $rect.Height -le 0) {
        throw "Visible choice is not physically clickable: $SelectorName -> $ItemName"
    }
    $owner = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($selector)
    while ($null -ne $owner -and $owner.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) {
        $owner = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($owner)
    }
    if ($null -ne $owner -and $owner.Current.NativeWindowHandle -ne 0) {
        [AiPetMouseInput]::SetForegroundWindow([IntPtr]$owner.Current.NativeWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 120
    }
    Assert-AppForeground
    [AiPetMouseInput]::SetCursorPos([int]($rect.Left + ($rect.Width / 2)), [int]($rect.Top + ($rect.Height / 2))) | Out-Null
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftDown, 0, 0, 0, [UIntPtr]::Zero)
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::LeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
    $pattern = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if (-not $pattern.Current.IsSelected) {
        throw "Physical click did not select: $SelectorName -> $ItemName"
    }
    Write-Output ("[UI] physically selected '{0}' from '{1}'" -f $ItemName, $SelectorName)
    if (-not $SkipEvidence) { $selectorEvidence.Add(@{selector=$SelectorName;input='mouse';status='PASS'}) }
}

function Assert-AppForeground {
    $window=Find-Element '小方工具袋'
    [AiPetMouseInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 150
    [uint32]$foregroundProcess=0
    [AiPetMouseInput]::GetWindowThreadProcessId([AiPetMouseInput]::GetForegroundWindow(),[ref]$foregroundProcess) | Out-Null
    if ($foregroundProcess -ne $script:process.Id) { throw 'Interactive desktop cannot foreground the app; input was not sent.' }
}

function Test-KeyboardChoice([string]$SelectorName) {
    Assert-AppForeground
    $selector=Find-Element $SelectorName
    $selector.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait('{HOME}')
    Start-Sleep -Milliseconds 150
    $selection=$selector.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
    $first=@($selection.GetCurrentSelection())
    if ($first.Count -ne 1) { throw "Keyboard selection missing: $SelectorName" }
    [System.Windows.Forms.SendKeys]::SendWait('{END}')
    Start-Sleep -Milliseconds 150
    $last=@($selection.GetCurrentSelection())
    if ($last.Count -ne 1 -or $first[0].Equals($last[0])) { throw "Keyboard did not change selection: $SelectorName" }
    $selectorEvidence.Add(@{selector=$SelectorName;input='keyboard';status='PASS'})
}

function Select-Tab([string]$Name) {
    $tab = Find-Element $Name
    $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
    Start-Sleep -Milliseconds 200
    Write-Output ("[UI] selected tab '{0}'" -f $Name)
}

function Get-Value([string]$Name) {
    $element = Find-Element $Name
    return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Wait-UiProbe([scriptblock]$Condition, [string]$FailureMessage, [int]$TimeoutSeconds = 5) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            if (Test-Path -LiteralPath $script:probePath) {
                $probe = Get-Content -LiteralPath $script:probePath -Raw | ConvertFrom-Json
                if (& $Condition $probe) { return $probe }
            }
        } catch { }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $FailureMessage
}

function Save-Screenshot([string]$Path) {
    # Capture only this application's foreground window, never a lock/PIN screen or unrelated app.
    Assert-AppForeground
    $rect=(Find-Element '小方工具袋').Current.BoundingRectangle
    $bounds=[System.Drawing.Rectangle]::new([int]$rect.Left,[int]$rect.Top,[int]$rect.Width,[int]$rect.Height)
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $temporaryPath = "$Path.$PID.tmp.png"
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($bounds.Left, $bounds.Top, 0, 0, $bounds.Size) }
        finally { $graphics.Dispose() }
        $bitmap.Save($temporaryPath, [System.Drawing.Imaging.ImageFormat]::Png)
        [System.IO.File]::Move($temporaryPath, $Path, $true)
    } finally {
        $bitmap.Dispose()
        if (Test-Path -LiteralPath $temporaryPath) { [System.IO.File]::Delete($temporaryPath) }
    }
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
    $profiles=@(foreach($id in @('fixture-a','fixture-b')) { @{id=$id;displayName=$id;providerId='deepseek';endpoint='https://example.invalid';model='fixture';secretTargetName="WindowsAiDesktopPet:AI:$id";lastStatus='Untested'} })
    @{schemaVersion=5;search=@{onboardingCompleted=$true};ai=@{activeProfileId='fixture-a';profiles=$profiles};hotkeys=@{enabled=$false;searchGesture='Ctrl+Alt+Space';quickTodoGesture='Ctrl+Alt+T'};backup=@{automaticEnabled=$false;retentionCount=7}} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $isolatedData 'settings.json') -Encoding utf8NoBOM
    $todoFixtures=@(
        @{id=[Guid]::NewGuid().ToString();title='UI fixture one';notes='anonymous fixture';createdAt='2026-09-01T10:01:00+08:00';status='Pending'},
        @{id=[Guid]::NewGuid().ToString();title='UI fixture two';notes='anonymous fixture';createdAt='2026-09-01T10:02:00+08:00';status='Pending'}
    )
    @{schemaVersion=4;items=$todoFixtures} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $isolatedData 'todos.json') -Encoding utf8NoBOM
    $draftPath=Join-Path $isolatedData 'draft-fixture.json'
    @{Operation=2;TargetTitle='UI fixture'} | ConvertTo-Json | Set-Content -LiteralPath $draftPath -Encoding utf8NoBOM
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($exe, '--preview --ui-e2e')
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['AIPET_UI_TEST'] = '1'
    $startInfo.Environment['AIPET_ASSETS'] = (Join-Path $projectRoot 'assets\pets')
    $startInfo.Environment['APPDATA'] = $isolatedData
    $startInfo.Environment['LOCALAPPDATA'] = $isolatedData
    $startInfo.Environment['AIPET_APP_DATA_ROOT'] = $isolatedData
    $startInfo.Environment['AIPET_UI_E2E_PROBE'] = $probePath
    $startInfo.Environment['AIPET_UI_E2E_DRAFT'] = $draftPath
    $fixtureDate = [DateTime]::Today.AddHours(9)
    $fixtureOffset = [TimeZoneInfo]::Local.GetUtcOffset($fixtureDate)
    $startInfo.Environment['AIPET_UI_E2E_NOW'] = [DateTimeOffset]::new($fixtureDate, $fixtureOffset).ToString('O')
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $toolWindow = Find-Element '小方工具袋' 15

    $stage = 'inline-choice-selection'
    $stage = 'inline-search-scope'
    Select-VisibleChoice 'SearchScopeSelector' '仅应用'
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).SelectedSearchScopeId -ne 'apps') { throw 'Search scope did not update.' }
    Test-KeyboardChoice 'SearchScopeSelector'
    $stage = 'inline-result-category'
    Select-VisibleChoice 'CategoryFilterSelector' '图片'
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).Category -ne '图片') { throw 'Result category did not update.' }
    Test-KeyboardChoice 'CategoryFilterSelector'

    $stage = 'keyboard-navigation'
    Select-Tab '待办'
    $stage = 'inline-ai-target'
    $targetSelector=Find-Element 'AiTargetSelector'
    $targetItems=$targetSelector.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
    if ($targetItems.Count -lt 2) { throw 'Two anonymous AI target fixtures are required.' }
    Select-VisibleChoice 'AiTargetSelector' $targetItems[0].Current.Name
    Test-KeyboardChoice 'AiTargetSelector'
    if (-not (Get-Content $probePath -Raw | ConvertFrom-Json).SelectedAiTargetId) { throw 'AI target did not update.' }
    $stage = 'inline-todo-filter'
    Select-VisibleChoice 'TodoFilterSelector' '已完成'
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).todoFilterId -ne 'completed') { throw 'Todo filter did not update.' }
    Test-KeyboardChoice 'TodoFilterSelector'

    $stage = 'today-plan-selection'
    Select-VisibleChoice 'TodayPlanCandidateSelector' '今日安排候选 UI fixture one' -SkipEvidence
    Select-VisibleChoice 'TodayPlanCandidateSelector' '今日安排候选 UI fixture two' -SkipEvidence
    $null = Wait-UiProbe { param($probe) $probe.todayPlanSelectedCount -eq 2 } 'Today plan selection did not update.'

    $stage = 'today-plan-local-draft'
    Click-Element 'GenerateLocalTodayPlanButton'
    $null = Wait-UiProbe { param($probe) $probe.todayPlanDraftCount -eq 2 } 'Local today plan draft was not generated.'
    $excludedDraft = Find-Element '今日安排草稿 UI fixture two'
    Click-Element '今日安排草稿 UI fixture two'
    $toggle = $excludedDraft.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { throw 'Today plan draft item was not excluded.' }

    $stage = 'today-plan-apply-undo'
    Click-Element 'ApplyTodayPlanButton'
    $null = Wait-UiProbe { param($probe) $probe.CanUndoTodayPlan -eq $true } 'Today plan apply did not enable batch undo.'
    $savedTodos = @((Get-Content -LiteralPath (Join-Path $isolatedData 'todos.json') -Raw | ConvertFrom-Json).items)
    if (@($savedTodos | Where-Object { $_.plannedStartAt }).Count -ne 1) { throw 'Today plan did not persist exactly one included item.' }
    Click-Element 'UndoTodayPlanButton'
    $null = Wait-UiProbe { param($probe) $probe.CanUndoTodayPlan -eq $false } 'Today plan batch undo did not complete.'
    $restoredTodos = @((Get-Content -LiteralPath (Join-Path $isolatedData 'todos.json') -Raw | ConvertFrom-Json).items)
    if (@($restoredTodos | Where-Object { $_.plannedStartAt }).Count -ne 0) { throw 'Today plan undo did not restore original items.' }

    Select-Tab '设置'
    $stage = 'inline-theme'
    Select-VisibleChoice 'ThemeSelector' '深色'
    if ((Get-Content $probePath -Raw | ConvertFrom-Json).ThemePreference -ne 'dark') { throw 'Theme did not update.' }
    Test-KeyboardChoice 'ThemeSelector'
    Expand-Element 'AI 接入设置'
    $stage='inline-ai-template'
    Select-VisibleChoice 'AiTemplateSelector' '通义千问 Qwen'
    Test-KeyboardChoice 'AiTemplateSelector'
    $stage='inline-ai-saved'
    $saved=Find-Element 'SavedAiConfigurationSelector'
    $savedItems=$saved.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem))
    Select-VisibleChoice 'SavedAiConfigurationSelector' $savedItems[$savedItems.Count-1].Current.Name
    Test-KeyboardChoice 'SavedAiConfigurationSelector'
    $stage='inline-ai-provider'
    Select-VisibleChoice 'ProviderSelector' '通义千问 Qwen'
    Test-KeyboardChoice 'ProviderSelector'
    if (@($selectorEvidence | Where-Object input -eq 'mouse').Count -ne 8 -or @($selectorEvidence | Where-Object input -eq 'keyboard').Count -ne 8) { throw 'Not all eight selectors were exercised.' }

    $stage = 'dialog-and-focus-restore'
    Assert-AppForeground
    [System.Windows.Forms.SendKeys]::SendWait('^{TAB}')
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

    $stage = 'tray-actions'
    $trayCondition=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'Windows AI Desktop Pet · 方块伙伴')
    $tray=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$trayCondition)
    if ($null -eq $tray -or $tray.Current.IsOffscreen) { throw 'Actual notification-area icon must be visible for tray validation.' }
    $rect=$tray.Current.BoundingRectangle
    [AiPetMouseInput]::SetCursorPos([int]($rect.Left+$rect.Width/2),[int]($rect.Top+$rect.Height/2)) | Out-Null
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::RightDown,0,0,0,[UIntPtr]::Zero)
    [AiPetMouseInput]::mouse_event([AiPetMouseInput]::RightUp,0,0,0,[UIntPtr]::Zero)
    Start-Sleep -Milliseconds 250
    Click-Element '退出'
    if (-not $process.WaitForExit(5000)) { throw 'Application did not exit through its context menu.' }

    $checks=@('eight-selectors-mouse','eight-selectors-keyboard','today-plan-selection','today-plan-local-draft','today-plan-partial-apply','today-plan-batch-undo','unsaved-navigation','focus-restore','tray-actions') | ForEach-Object { @{name=$_;status='PASS'} }
    $report = [ordered]@{ schemaVersion = 1; status = 'PASS'; startedAtUtc = $startedAt; completedAtUtc = [DateTimeOffset]::UtcNow; selectors=@($selectorEvidence.ToArray()); checks=@($checks); environment=@{os=[Environment]::OSVersion.VersionString;displayCount=[System.Windows.Forms.Screen]::AllScreens.Count} }
    [System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
    Write-Output "[PASS] independent desktop UI automation completed: $reportPath"
}
catch {
    Write-Output ("[UI] failure: {0}" -f $_.Exception.Message)
    $capturedScreenshot = $null
    try {
        if (Test-Path -LiteralPath $screenshotPath) { [System.IO.File]::Delete($screenshotPath) }
        Save-Screenshot $screenshotPath
        if (Test-Path -LiteralPath $screenshotPath) { $capturedScreenshot = 'ui-e2e-failure.png' }
    } catch { }
    $report = [ordered]@{ schemaVersion = 1; status = 'FAIL'; failedStage = $stage; stableError = 'UI_E2E_FAILED'; startedAtUtc = $startedAt; completedAtUtc = [DateTimeOffset]::UtcNow; screenshot = $capturedScreenshot }
    [System.IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
    Write-Error "UI E2E failed at stage '$stage'. See the redacted report and screenshot under build/reports/ui-e2e."
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
}
