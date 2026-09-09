using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Text.Json;
using Microsoft.Win32;
using AiPet.AI;
using AiPet.Common;
using AiPet.Pet;
using AiPet.Search;
using AiPet.Secrets;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.SystemIntegration;
using AiPet.Todos;
using AppToolWindow = AiPet.ToolWindow.PetToolWindow;
using AiPet.ToolWindow;

namespace AiPet.App;

public partial class App : System.Windows.Application
{
    // App.xaml is intentionally not part of the build (renamed to
    // App.xaml.disabled): we own Main in Program.cs to apply [STAThread]
    // and unhandled-exception wiring before the WPF runtime starts.

    private SingleInstance? _singleInstance;
    private TrayIcon? _tray;
    private PetWindow? _pet;
    private AppToolWindow? _tool;
    private Thread? _wakeThread;
    private CancellationTokenSource? _wakeCts;
    private CancellationTokenSource? _applicationIndexCts;
    private Task? _applicationIndexTask;
    private Task? _automaticBackupTask;
    private bool _shutdownRequested;

    private SearchService? _search;
    private ShortcutStore? _shortcuts;
    private OpenAiCompatibleClient? _ai;
    private OpenAiCompatibleTodoClient? _todoAi;
    private TodoStore? _todoStore;
    private ReminderScheduler? _reminderScheduler;
    private GlobalHotkeyService? _globalHotkeys;
    private SettingsStore? _settingsStore;
    private HomeViewModel? _homeVm;

    public string[]? LaunchArgs { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // No InitializeComponent(): the App class has no XAML resources
        // in the current build. If we add a global ResourceDictionary later, restore
        // App.xaml and the corresponding InitializeComponent call here.
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            DebugLog($"[AppDomain] unhandled: {ex?.GetType().Name ?? "unknown"}");
        };
        base.OnStartup(e);
        LaunchArgs = e.Args;
        var isUiE2e = e.Args.Contains("--ui-e2e", StringComparer.OrdinalIgnoreCase);
        var isPreview = e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase)
            || string.Equals(
                Environment.GetEnvironmentVariable("AIPET_UI_TEST"),
                "1",
                StringComparison.Ordinal);
        DebugLog($"[App] OnStartup enter; mode={(e.Args.Contains("--smoke") ? "smoke" : e.Args.Contains("--preview") ? "preview" : "normal")}");

        // --smoke is a self-test mode used by the README and the
        // `package.ps1` post-build hook: it runs a fixed scenario, writes
        // a report, then exits without entering the WPF message loop.
        if (e.Args.Length > 0 && e.Args[0] == "--smoke")
        {
            // Allow the test runner to point at a dev assets tree
            // without copying it next to the EXE: --smoke --assets <path>
            for (var i = 1; i < e.Args.Length - 1; i++)
            {
                if (e.Args[i] == "--assets")
                {
                    Environment.SetEnvironmentVariable("AIPET_ASSETS", e.Args[i + 1]);
                    break;
                }
            }
            var exitCode = RunSmokeAndExit();
            Shutdown(exitCode);
            return;
        }

        // Acquire the account lock before any stores or maintenance can write.
        _singleInstance = new SingleInstance(isPreview ? $"UITest_{Environment.ProcessId}" : null);
        if (!_singleInstance.IsFirstInstance)
        {
            SingleInstance.SignalFirstInstance();
            _singleInstance.Dispose();
            Shutdown();
            return;
        }

        // --- 1. Locate assets / settings ---
        var petsRoot = AssetsResolver.FindPetsRoot();
        DebugLog($"[App] pet assets resolved={petsRoot is not null}");
        if (petsRoot is null)
        {
            System.Windows.MessageBox.Show(
                "找不到 pets 资源目录。\n\n" +
                "请确认安装包完整,或在环境变量 AIPET_ASSETS 中设置 assets 目录(例如 D:\\MyApps\\AiPet\\assets)。\n\n" +
                "请重新安装，或在开发模式下设置 AIPET_ASSETS。",
                "Windows AI Desktop Pet",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var petJson = System.IO.File.Exists(System.IO.Path.Combine(petsRoot, "pet.json"))
            ? System.IO.Path.Combine(petsRoot, "pet.json")
            : System.IO.Path.Combine(petsRoot, "rgs-8dir", "pet.json");
        if (!System.IO.File.Exists(petJson))
        {
            // Try the "any subdir" discovery we use in --smoke.
            foreach (var sub in System.IO.Directory.EnumerateDirectories(petsRoot))
            {
                var p = System.IO.Path.Combine(sub, "pet.json");
                if (System.IO.File.Exists(p)) { petJson = p; break; }
            }
        }
        DebugLog($"[App] pet manifest exists={System.IO.File.Exists(petJson)}");
        PetManifest manifest;
        try
        {
            manifest = PetManifestLoader.Load(petJson);
        }
        catch (Exception ex)
        {
            DebugLog($"[App] pet manifest load failed: {ex.GetType().Name}");
            System.Windows.MessageBox.Show(
                "读取桌宠资源失败，请检查安装包是否完整。",
                "Windows AI Desktop Pet",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _settingsStore = new SettingsStore();
        try
        {
            var maintenance = new DataMaintenanceService(_settingsStore.AppDataDir, Path.GetDirectoryName(ApplicationDataPaths.GetDiagnosticLogPath()));
            var result = maintenance.ApplyPending();
            if (result.Errors.Count > 0)
                System.Windows.MessageBox.Show("恢复未成功，已回滚原数据。维护快照保留在本机数据目录。", "本地数据", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            System.Windows.MessageBox.Show("维护校验或回滚未完成，已停止启动以保护数据。请保留本机 .maintenance 目录并检查备份。", "本地数据", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        var settingsV = _settingsStore.Load();
        Func<DateTimeOffset>? todoNow = null;
        if (isUiE2e
            && DateTimeOffset.TryParse(
                Environment.GetEnvironmentVariable("AIPET_UI_E2E_NOW"),
                out var uiE2eNow))
            todoNow = () => uiE2eNow;
        var preferred = settingsV.Pet.PreferredCharacter;
        if (string.IsNullOrEmpty(preferred) || !manifest.FrameInventory?.Characters.Contains(preferred) == true)
            preferred = manifest.FrameInventory?.Preferred ?? "hero";

        var petPackageRoot = System.IO.Path.GetDirectoryName(petJson)
            ?? throw new InvalidOperationException("pet.json 缺少父目录");
        var cache = new PetFrameCache(petPackageRoot, manifest, preferred);

        // --- 2. Wire up all MVP subsystems ---
        _search = new SearchService(_settingsStore.IndexPath);
        _applicationIndexCts = new CancellationTokenSource();
        _applicationIndexTask = IndexApplicationsSafelyAsync(_search, _applicationIndexCts.Token);
        _shortcuts = new ShortcutStore(_settingsStore.AppDataDir);
        _ai = new OpenAiCompatibleClient();
        _todoAi = new OpenAiCompatibleTodoClient();
        _todoStore = new TodoStore(_settingsStore.AppDataDir);
        // _homeVm is attached after the XAML resource graph is built
        // (see TryAttachHomeViewModel below) so the resource lookup
        // can find the XAML-declared instance instead of us creating
        // a second one.

        // --- 3. Build windows ---
        DebugLog("[App] building windows");
        try
        {
            _tool = new AppToolWindow();
            _tool.ApplyWindowPreferences(
                settingsV.ToolWindow.StayOpen,
                settingsV.ToolWindow.AlwaysOnTop);
            _tool.ApplyTheme(settingsV.Appearance.Theme);
            _tool.WindowPreferencesChanged += (_, _) => SaveToolWindowPreferences();
            DebugLog("[App] tool window ctor done");
            _pet = new PetWindow(cache, manifest, preferred, _settingsStore);
            DebugLog("[App] pet window ctor done");
            if (isPreview)
            {
                // WPF layered windows are intentionally absent from Windows
                // accessibility/capture APIs. Preview mode uses equivalent
                // opaque hosts so the real visual tree can be automated.
                _tool.AllowsTransparency = false;
                _tool.Background = System.Windows.Media.Brushes.White;
                _tool.Topmost = false;
                _tool.WindowStyle = WindowStyle.SingleBorderWindow;
                _pet.AllowsTransparency = false;
                _pet.Background = System.Windows.Media.Brushes.White;
                _pet.Topmost = false;
                _pet.WindowStyle = WindowStyle.SingleBorderWindow;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"[App] window creation failed: {ex.GetType().Name}");
            System.Windows.MessageBox.Show(
                "窗口初始化失败，请重新启动应用。",
                "Windows AI Desktop Pet",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // The XAML declared `<local:HomeViewModel x:Key="HomeVM" />`,
        // so a parameterless instance is already alive in the tool
        // window's resource dictionary. Pull it out and Attach() our
        // real services to it so its ObservableCollections bind to
        // the same backing stores the App layer uses.
        if (_tool.FindResource("HomeVM") is HomeViewModel fromXaml)
        {
            fromXaml.Attach(
                _search,
                _shortcuts,
                _ai,
                _settingsStore,
                todoStore: _todoStore,
                todoAiClient: _todoAi,
                todoNow: todoNow);
            fromXaml.CharacterChanged += character => _pet?.SetCharacter(character);
            fromXaml.AppearanceChanged += appearance =>
            {
                _tool?.ApplyTheme(appearance.Theme);
                _pet?.ApplyAppearancePreferences(appearance);
            };
            _homeVm = fromXaml;
            _homeVm.MaintenanceExitRequested += async (_, _) => await RequestShutdownAsync();
            _homeVm.UpdateInstallerReady += OnUpdateInstallerReady;
            DebugLog("[App] home vm attached from XAML resource");
        }
        else
        {
            DebugLog("[App] WARN: HomeVM resource not found");
        }

        _pet.PetClicked += (_, _) => ToggleToolWindow();
        _pet.ToolWindowToggleRequested += (_, _) => ToggleToolWindow();
        _pet.HideRequested += (_, _) => HidePet();
        _pet.VisualPositionChanged += (_, _) => RepositionVisibleToolWindow();
        _pet.PointerInteractionStarted += (_, _) => _tool?.BeginAnchorInteraction();
        _pet.PointerInteractionCompleted += (_, _) => _tool?.EndAnchorInteraction();
        _tool.IsVisibleChanged += (_, _) =>
            _pet?.SetCompanionWindowVisible(_tool.IsVisible);

        // --- 4. Tray ---
        _tray = new TrayIcon(GetTrayState);
        _tray.BalloonClicked += (_, _) => ShowTodoPage();
        _tray.PetVisibilityClicked += (_, _) => TogglePetVisibility();
        _tray.ShowPetRequested += (_, _) => ShowPet();
        _tray.ToolWindowClicked += (_, _) => ToggleToolWindow();
        _tray.SearchRequested += (_, _) => ShowSearch();
        _tray.QuickTodoRequested += (_, _) => ShowQuickTodo();
        _tray.SettingsClicked += (_, _) => ShowSettings();
        _tray.AutostartClicked += (_, _) => ToggleAutostartFromTray();
        _tray.HelpClicked += (_, _) =>
        {
            var result = HelpLauncher.OpenManual();
            if (result.Kind != HelpOpenKind.Ok)
                _tray.ShowBalloon("帮助暂不可用", result.Message ?? "找不到离线用户手册。", ToolTipIcon.Warning);
        };
        _tray.ExitClicked += async (_, _) => await RequestShutdownAsync();
        _notifications = new NotificationCenter(_settingsStore.AppDataDir);
        _homeVm?.Todo.AttachNotificationCenter(_notifications);
        _reminderScheduler = new ReminderScheduler(_todoStore, queuesNotifications: true);
        _reminderScheduler.Delivered += _ => Dispatcher.Invoke(() => { _notificationStartupReconciled = false; _homeVm?.Todo.RefreshItems(); _homeVm?.Todo.RefreshNotifications(); });
        _reminderScheduler.Start(notification => _notifications.Enqueue(notification));
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _notificationTimer.Tick += (_, _) => ProcessNotifications();
        _notificationTimer.Start();
        ProcessNotifications();
        SystemEvents.TimeChanged += OnSystemTimeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        if (isPreview)
        {
            _homeVm?.SetGlobalHotkeyRuntimeStatus(new(false, "预览模式不注册系统级快捷键。"));
            _homeVm?.SetPreviewUpdateStatus();
            _homeVm?.SetAutomaticBackupStatus(new(
                AutomaticBackupOutcome.Skipped,
                "预览模式不创建自动备份。",
                new AutomaticBackupService(_settingsStore.AppDataDir).BackupDirectory));
        }
        else
        {
            try
            {
                _globalHotkeys = new GlobalHotkeyService();
                if (_homeVm is not null)
                    _homeVm.GlobalHotkeysChanged += (_, _) => ApplyGlobalHotkeys(notifyFailure: true);
                ApplyGlobalHotkeys(notifyFailure: false);
            }
            catch (Exception ex)
            {
                DebugLog($"[App] global hotkey initialization failed: {ex.GetType().Name}");
                _homeVm?.SetGlobalHotkeyRuntimeStatus(new(false, "系统未能初始化全局快捷键；仍可通过桌宠或托盘打开功能。"));
            }

            _automaticBackupTask = RunAutomaticBackupAsync();
            _homeVm?.StartUpdateChecks();
        }
        // Patch: the "设置" item is now wired to open the home page
        // (the integrated ToolWindow already shows settings as a tab).
        // The "开机自启" item toggles AutoStart. Both are best-effort;
        // if they fail we swallow so the user can still exit.

        // --- 5. Listen for second-instance wake events ---
        _wakeCts = new CancellationTokenSource();
        _wakeThread = new Thread(() => WakeLoop(_singleInstance.WakeEvent!, _wakeCts.Token))
        {
            IsBackground = true,
            Name = "AiPet.WakeLoop"
        };
        _wakeThread.Start();

        if (isPreview)
        {
            // Preview mode is also the real-window UI automation entry point.
            // Layered, taskbar-hidden WPF windows are intentionally omitted by
            // Windows accessibility enumerators and cannot be activated by UI
            // drivers, so expose non-layered hosts only here. The visual tree,
            // bindings, commands and relative placement remain production code.
            _pet.ShowInTaskbar = true;
            _tool.ShowInTaskbar = true;
            _tool.AutoHideOnDeactivate = isUiE2e;
        }

        _pet.Show();
        _tray.SetPetVisible(true);
        if (isPreview)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ShowToolWindow(showSettings: false);
                if (isUiE2e && _homeVm is not null)
                {
                    var fixturePath = Environment.GetEnvironmentVariable("AIPET_UI_E2E_DRAFT");
                    if (!string.IsNullOrWhiteSpace(fixturePath) && File.Exists(fixturePath))
                    {
                        var draft = JsonSerializer.Deserialize<AiTodoDraft>(File.ReadAllText(fixturePath));
                        if (draft is not null) _homeVm.Todo.PreviewAiDraft(draft);
                    }
                    AttachUiE2eProbe(_homeVm);
                }
                DebugLog($"[App] preview shown; visible={_tool?.IsVisible} active={_tool?.IsActive}");
            }), DispatcherPriority.Loaded);
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void AttachUiE2eProbe(HomeViewModel viewModel)
    {
        var reportPath = Environment.GetEnvironmentVariable("AIPET_UI_E2E_PROBE");
        if (string.IsNullOrWhiteSpace(reportPath)) return;
        void WriteProbe()
        {
            try
            {
                var payload = new
                {
                    schemaVersion = 1,
                    viewModel.SelectedSearchScopeId,
                    viewModel.Category,
                    todoFilterId = viewModel.Todo.SelectedFilterId,
                    viewModel.ThemePreference,
                    viewModel.SelectedAiConfigurationId,
                    viewModel.Todo.SelectedAiTargetId,
                    todayPlanSelectedCount = viewModel.Todo.TodayPlanSelectedCount,
                    todayPlanDraftCount = viewModel.Todo.TodayPlanDraft.Count,
                    viewModel.Todo.CanUndoTodayPlan,
                    aiTemplateId = viewModel.SelectedAiTemplate?.Id,
                    viewModel.Provider,
                    viewModel.Endpoint,
                    viewModel.Model,
                    viewModel.HasUnsavedAiChanges,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, JsonSerializer.Serialize(payload));
            }
            catch { }
        }
        viewModel.PropertyChanged += (_, _) => WriteProbe();
        viewModel.Todo.PropertyChanged += (_, _) => WriteProbe();
        WriteProbe();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DebugLog($"[App] unhandled: {e.Exception.GetType().Name}");
        Console.Error.WriteLine($"[AiPet] unhandled: {e.Exception.GetType().Name}");
        try { _tray?.ShowBalloon("Windows AI Desktop Pet", "发生错误,已记录到诊断日志。", ToolTipIcon.Warning); } catch { }
        e.Handled = true;
    }

    private void ToggleToolWindow()
    {
        if (_tool is null || _pet is null) return;
        if (_tool.IsVisible)
        {
            _tool.HideToTray();
            return;
        }
        ShowToolWindow(showSettings: false);
    }

    private void TogglePetVisibility()
    {
        if (_pet?.IsVisible == true) HidePet();
        else ShowPet();
    }

    private void HidePet()
    {
        _tool?.HideToTray();
        _pet?.Hide();
        _tray?.SetPetVisible(false);
    }

    private void ShowPet()
    {
        if (_pet is null) return;
        if (!_pet.IsVisible) _pet.Show();
        _tray?.SetPetVisible(true);
    }

    private void ShowSettings()
    {
        ShowToolWindow(showSettings: true);
    }

    private void ShowSearch()
    {
        ShowPet();
        ShowToolWindow(showSettings: false);
    }

    private void ShowQuickTodo()
    {
        ShowPet();
        ShowToolWindow(showSettings: _homeVm?.HasUnsavedAiChanges == true);
        _tool?.StartQuickTodo();
    }

    private void ApplyGlobalHotkeys(bool notifyFailure)
    {
        if (_globalHotkeys is null || _settingsStore is null) return;
        GlobalHotkeyApplyResult result;
        var settings = _settingsStore.Load().Hotkeys;
        if (!settings.Enabled)
        {
            result = _globalHotkeys.Apply(Array.Empty<GlobalHotkeyDefinition>());
        }
        else if (!GlobalHotkeyGesture.TryParsePair(settings.SearchGesture, settings.QuickTodoGesture,
                     out var searchGesture, out var todoGesture, out var error))
        {
            _globalHotkeys.Apply(Array.Empty<GlobalHotkeyDefinition>());
            result = new(false, error + " 已保持全部全局快捷键停用。");
        }
        else
        {
            result = _globalHotkeys.Apply(new[]
            {
                new GlobalHotkeyDefinition(0xA101, searchGesture!, ShowSearch),
                new GlobalHotkeyDefinition(0xA102, todoGesture!, ShowQuickTodo),
            });
        }

        _homeVm?.SetGlobalHotkeyRuntimeStatus(result);
        if (notifyFailure && !result.Success)
            _tray?.ShowBalloon("快捷键未启用", result.Message, ToolTipIcon.Warning);
    }

    private async Task RunAutomaticBackupAsync()
    {
        if (_settingsStore is null) return;
        var settings = _settingsStore.Load().Backup;
        var root = _settingsStore.AppDataDir;
        var logDirectory = Path.GetDirectoryName(ApplicationDataPaths.GetDiagnosticLogPath());
        var result = await Task.Run(() =>
            new AutomaticBackupService(root, logDirectory)
                .Run(settings.AutomaticEnabled, settings.RetentionCount));
        _homeVm?.SetAutomaticBackupStatus(result);
        if (result.Outcome == AutomaticBackupOutcome.Failed)
            _tray?.ShowBalloon("自动备份未完成", result.Message, ToolTipIcon.Warning);
    }

    private async void OnUpdateInstallerReady(UpdateInstallerReady update)
    {
        if (_shutdownRequested) return;
        var decision = _tool is null
            ? MessageBoxResult.No
            : System.Windows.MessageBox.Show(
                _tool,
                $"版本 {update.Version} 已下载并通过 SHA-256 校验。\n\n现在启动安装程序并退出桌宠吗？",
                "准备安装更新",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes) return;
        if (_tool is not null && !_tool.ConfirmApplicationClose()) return;
        try
        {
            Process.Start(new ProcessStartInfo(update.Path, "/SP-") { UseShellExecute = true });
            await RequestShutdownAsync();
        }
        catch
        {
            _tray?.ShowBalloon(
                "无法启动更新安装器",
                "下载文件仍保留在本机更新目录，可稍后重试。",
                System.Windows.Forms.ToolTipIcon.Warning);
        }
    }

    private void ShowTodoPage()
    {
        ShowPet();
        ShowToolWindow(showSettings: _homeVm?.HasUnsavedAiChanges == true);
        _tool?.SelectTodoTab();
    }

    private void ShowToolWindow(bool showSettings)
    {
        if (_tool is null || _pet is null) return;
        var petBounds = _pet.GetVisualBounds();
        var workArea = GetWorkAreaFor(_pet);
        _tool.ShowNear(petBounds, workArea, showSettings);
    }

    private void RepositionVisibleToolWindow()
    {
        if (_tool?.IsVisible != true || _pet is null) return;
        _tool.RepositionNear(_pet.GetVisualBounds(), GetWorkAreaFor(_pet));
    }

    private void SaveToolWindowPreferences()
    {
        if (_tool is null || _settingsStore is null) return;
        try
        {
            var settings = _settingsStore.Load();
            settings.ToolWindow.StayOpen = _tool.StayOpen;
            settings.ToolWindow.AlwaysOnTop = _tool.AlwaysOnTop;
            _settingsStore.Save(settings);
        }
        catch (Exception ex)
        {
            DebugLog($"[App] tool-window preferences save failed: {ex.GetType().Name}");
        }
    }

    private static Rect GetWorkAreaFor(Window anchor)
    {
        var handle = new WindowInteropHelper(anchor).Handle;
        // With PerMonitorV2 enabled, WinForms exposes Screen.WorkingArea in
        // the same logical coordinate space used by WPF Window.Left/Top.
        // Applying TransformFromDevice here would scale the monitor twice
        // and clamp popovers onto the wrong horizontal position.
        var workArea = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        return new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
    }

    private void ToggleAutostartFromTray()
    {
        if (_homeVm is null) return;
        _homeVm.AutostartEnabled = !_homeVm.AutostartEnabled;
        if (_homeVm.ToggleAutostartCommand.CanExecute(null))
            _homeVm.ToggleAutostartCommand.Execute(null);
    }

    private TrayState GetTrayState()
    {
        var autostartEnabled = _homeVm?.AutostartEnabled ?? false;
        try { autostartEnabled = AutoStart.IsEnabled(out _); }
        catch { }
        return new TrayState(
            _pet?.IsVisible == true,
            _tool?.IsVisible == true,
            autostartEnabled);
    }

    private async Task RequestShutdownAsync()
    {
        if (_shutdownRequested) return;
        if (_tool is not null && !_tool.ConfirmApplicationClose()) return;

        _shutdownRequested = true;
        _notificationTimer?.Stop();
        _homeVm?.CancelBackgroundWork();
        _applicationIndexCts?.Cancel();
        _wakeCts?.Cancel();
        try { _reminderScheduler?.Dispose(); } catch { }

        var pending = new List<Task>();
        if (_reminderScheduler is not null) pending.Add(_reminderScheduler.WaitForIdleAsync());
        if (_homeVm is not null)
            pending.Add(_homeVm.WaitForBackgroundWorkAsync(TimeSpan.FromSeconds(4)));
        if (_applicationIndexTask is { IsCompleted: false } applicationIndexTask)
            pending.Add(applicationIndexTask);
        if (_automaticBackupTask is { IsCompleted: false } automaticBackupTask)
            pending.Add(automaticBackupTask);
        if (_wakeThread is { IsAlive: true } wakeThread)
            pending.Add(Task.Run(() => wakeThread.Join(TimeSpan.FromSeconds(4))));

        if (pending.Count > 0)
        {
            var allPending = Task.WhenAll(pending);
            if (await Task.WhenAny(allPending, Task.Delay(TimeSpan.FromSeconds(5))) != allPending)
                DebugLog("[App] shutdown wait timed out; continuing with bounded cleanup");
        }

        _tool?.AllowClose();
        Shutdown();
    }

    private void WakeLoop(EventWaitHandle wh, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!WakeSignalWaiter.Wait(wh, ct)) return;
                Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowPet();
                    ShowToolWindow(showSettings: false);
                }));
            }
            catch { return; }
        }
    }

    private static async Task IndexApplicationsSafelyAsync(SearchService search, CancellationToken cancellationToken)
    {
        try { await search.IndexApplicationsAsync(ct: cancellationToken); }
        catch (OperationCanceledException) { }
        catch { /* Application discovery is best-effort; folder search remains available. */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { SystemEvents.TimeChanged -= OnSystemTimeChanged; } catch { }
        try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
        try { _tool?.AllowClose(); } catch { }
        try { _homeVm?.CancelBackgroundWork(); } catch { }
        try { _applicationIndexCts?.Cancel(); } catch { }
        try { _wakeCts?.Cancel(); } catch { }
        try { _reminderScheduler?.Dispose(); } catch { }
        try { _globalHotkeys?.Dispose(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _singleInstance?.Dispose(); } catch { }
        try { _search?.Dispose(); } catch { }
        try { _applicationIndexCts?.Dispose(); } catch { }
        try { _wakeCts?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private void OnSystemTimeChanged(object? sender, EventArgs e) => _reminderScheduler?.Reschedule();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _reminderScheduler?.Reschedule();
    }

    // ============== --smoke mode ==============

    private static void DebugLog(string line)
    {
        try
        {
            var path = ApplicationDataPaths.GetDiagnosticLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
        }
        catch { }
    }

    private int RunSmokeAndExit()
    {
        var report = new System.Text.StringBuilder();
        var ok = true;
        void Line(string s) { Console.WriteLine(s); report.AppendLine(s); }
        Line("=== Windows AI Desktop Pet --smoke ===");
        Line($"timestamp: {DateTimeOffset.Now:O}");

        // 1. Pet resource resolution
        try
        {
            var petsRoot = AssetsResolver.FindPetsRoot();
            if (petsRoot is null) throw new InvalidOperationException("AssetsResolver returned null (env + walkup both failed)");
            // Discover the per-pack subdirectory by enumeration, so
            // RGS_8Directional / rgs-8dir / future pack names all work.
            string? petJsonPath = System.IO.File.Exists(System.IO.Path.Combine(petsRoot, "pet.json"))
                ? System.IO.Path.Combine(petsRoot, "pet.json")
                : null;
            foreach (var sub in System.IO.Directory.EnumerateDirectories(petsRoot))
            {
                var p = System.IO.Path.Combine(sub, "pet.json");
                if (System.IO.File.Exists(p)) { petJsonPath = p; break; }
            }
            if (petJsonPath is null) throw new InvalidOperationException("no pet.json found under " + petsRoot);
            var manifest = PetManifestLoader.Load(petJsonPath);
            Line("[OK ] assets/pets: package resolved");
            Line($"[OK ] pet.json: id={manifest.Id}, characters=[{string.Join(",", manifest.FrameInventory?.Characters ?? new())}], totalFrames={manifest.FrameInventory?.TotalFrames}");
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] pet.json: {ex.Message}"); }

        // 2. Search index round-trip
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "aipet-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            var tmpStore = new SettingsStore(tmpDir);
            var fixtureRoot = Path.Combine(tmpDir, "search-fixture");
            var nested = Path.Combine(fixtureRoot, "nested", "level-two");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "recursive-smoke-target.txt"), "fixture");
            using var s = new SearchService(tmpStore.IndexPath, appProvider: () => []);
            s.AddRange(fixtureRoot);
            var added = s.ListRanges().First(r => r.Path == fixtureRoot);
            s.IndexRangeAsync(added.Id).GetAwaiter().GetResult();
            int n = s.CountItems(added.Id);
            var found = s.Search("recursive-smoke-target", SearchItemKind.Document);
            if (found.Count != 1) throw new InvalidOperationException($"expected one recursive filename hit, got {found.Count}");
            Line($"[OK ] search: indexed {n} isolated fixture items and found the nested filename");
            s.RemoveRange(added.Id);
            s.Dispose();
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] search: {ex.Message}"); }

        // 3. Shortcut store round-trip
        try
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "aipet-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            var tmp = new ShortcutStore(tmpDir);
            var s = new ShortcutItem
            {
                Kind = ShortcutKind.Application,
                TargetPath = @"C:\Windows\System32\notepad.exe",
                DisplayName = "notepad",
            };
            tmp.Add(s);
            var all = tmp.Load();
            if (all.Count != 1) throw new InvalidOperationException($"expected 1 shortcut, got {all.Count}");
            tmp.Remove(s.Id);
            Line("[OK ] shortcuts: add/remove round-trip works");
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] shortcuts: {ex.Message}"); }

        // 4. AI: 8 categories of error mapping (via fake endpoint if reachable; otherwise structural check)
        try
        {
            // We do NOT hit a real provider in --smoke. Instead we verify
            // the 8 builtin providers are loaded and the descriptor shape
            // is correct.
            var providers = AiProviders.Builtin;
            if (providers.Count != 9) throw new InvalidOperationException($"expected 9 providers, got {providers.Count}");
            var ids = providers.Select(p => p.Id).ToHashSet();
            foreach (var required in new[] { "deepseek", "zhipu", "qwen", "moonshot", "qianfan", "hunyuan", "yi", "siliconflow", "custom" })
                if (!ids.Contains(required)) throw new InvalidOperationException($"missing provider: {required}");
            Line($"[OK ] ai providers: {providers.Count} presets loaded ({string.Join(",", providers.Select(p => p.Id))})");
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] ai providers: {ex.Message}"); }

        // 5. Secrets: write/read/delete a small secret in an isolated target
        try
        {
            var target = "WindowsAiDesktopPet:SMOKE:" + Guid.NewGuid().ToString("N");
            var secret = "hello-smoke-" + Guid.NewGuid().ToString("N");
            WindowsCredentialStore.Save(target, secret);
            var loaded = WindowsCredentialStore.Load(target);
            if (loaded != secret) throw new InvalidOperationException("secret round-trip mismatch");
            WindowsCredentialStore.Delete(target);
            var afterDelete = WindowsCredentialStore.Load(target);
            if (afterDelete is not null) throw new InvalidOperationException("delete did not take effect");
            Line("[OK ] secrets: save/load/delete round-trip works");
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] secrets: {ex.Message}"); }

        // 6. AutoStart: read actual state (we do not actually toggle it in --smoke)
        try
        {
            var enabled = AutoStart.IsEnabled(out var cmd);
            Line($"[OK ] autostart: registry present, current value = {(enabled ? "enabled" : "disabled")}");
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] autostart: {ex.Message}"); }

        // 7. Help: just check we can locate the manual
        try
        {
            var path = HelpLauncher.FindManualPath();
            Line(path is not null
                ? "[OK ] help: manual found"
                : "[WARN] help: manual not found (expected for this build, run package.ps1 to copy docs)");
        }
        catch (Exception ex) { ok = false; Line($"[FAIL] help: {ex.Message}"); }

        Line(ok ? "=== smoke OK ===" : "=== smoke FAILED ===");

        // Always write the report to %TEMP%/aipet-smoke.txt for headless
        // verification by package.ps1 or a CI step.
        try
        {
            var reportPath = Path.Combine(Path.GetTempPath(), "aipet-smoke.txt");
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine($"[INFO ] smoke report written to {reportPath}");
        }
        catch { /* ignore */ }

        return ok ? 0 : 2;
    }
}
