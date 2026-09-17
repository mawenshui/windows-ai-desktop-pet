using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using AiPet.Storage;
using AiPet.SystemIntegration;

namespace AiPet.ToolWindow;

public sealed partial class HomeViewModel
{
    private bool _globalHotkeysEnabled = true;
    private string _searchHotkeyGesture = "Ctrl+Alt+Space";
    private string _quickTodoHotkeyGesture = "Ctrl+Alt+T";
    private bool _savedGlobalHotkeysEnabled = true;
    private string _savedSearchHotkeyGesture = "Ctrl+Alt+Space";
    private string _savedQuickTodoHotkeyGesture = "Ctrl+Alt+T";
    private string _globalHotkeyStatus = "等待应用注册快捷键。";
    private bool _automaticBackupEnabled = true;
    private bool _isCreatingAutomaticBackup;
    private string _automaticBackupRetentionText = AutomaticBackupService.DefaultRetentionCount.ToString();
    private bool _savedAutomaticBackupEnabled = true;
    private string _savedAutomaticBackupRetentionText = AutomaticBackupService.DefaultRetentionCount.ToString();
    private string _automaticBackupStatus = "尚无自动备份；可立即创建。";

    public bool GlobalHotkeysEnabled
    {
        get => _globalHotkeysEnabled;
        set { if (_globalHotkeysEnabled == value) return; _globalHotkeysEnabled = value; OnPC(); RaiseGlobalHotkeySaveState(); }
    }

    public string SearchHotkeyGesture
    {
        get => _searchHotkeyGesture;
        set { if (_searchHotkeyGesture == value) return; _searchHotkeyGesture = value; OnPC(); RaiseGlobalHotkeySaveState(); }
    }

    public string QuickTodoHotkeyGesture
    {
        get => _quickTodoHotkeyGesture;
        set { if (_quickTodoHotkeyGesture == value) return; _quickTodoHotkeyGesture = value; OnPC(); RaiseGlobalHotkeySaveState(); }
    }

    public string GlobalHotkeyStatus
    {
        get => _globalHotkeyStatus;
        private set { if (_globalHotkeyStatus == value) return; _globalHotkeyStatus = value; OnPC(); }
    }

    public bool AutomaticBackupEnabled
    {
        get => _automaticBackupEnabled;
        set { if (_automaticBackupEnabled == value) return; _automaticBackupEnabled = value; OnPC(); RaiseAutomaticBackupSaveState(); }
    }

    public string AutomaticBackupRetentionText
    {
        get => _automaticBackupRetentionText;
        set { if (_automaticBackupRetentionText == value) return; _automaticBackupRetentionText = value; OnPC(); RaiseAutomaticBackupSaveState(); }
    }

    public string AutomaticBackupStatus
    {
        get => _automaticBackupStatus;
        private set { if (_automaticBackupStatus == value) return; _automaticBackupStatus = value; OnPC(); }
    }

    public bool HasUnsavedGlobalHotkeyChanges => _settings is not null
        && (GlobalHotkeysEnabled != _savedGlobalHotkeysEnabled
            || !string.Equals(SearchHotkeyGesture, _savedSearchHotkeyGesture, StringComparison.Ordinal)
            || !string.Equals(QuickTodoHotkeyGesture, _savedQuickTodoHotkeyGesture, StringComparison.Ordinal));
    public string GlobalHotkeySaveLabel =>
        HasUnsavedGlobalHotkeyChanges ? "保存并应用（有修改）" : "已保存";
    public bool HasUnsavedAutomaticBackupChanges => _settings is not null
        && (AutomaticBackupEnabled != _savedAutomaticBackupEnabled
            || !string.Equals(AutomaticBackupRetentionText, _savedAutomaticBackupRetentionText, StringComparison.Ordinal));
    public string AutomaticBackupSaveLabel =>
        HasUnsavedAutomaticBackupChanges ? "保存设置（有修改）" : "已保存";

    public ICommand SaveGlobalHotkeysCommand { get; private set; } = null!;
    public ICommand SaveAutomaticBackupSettingsCommand { get; private set; } = null!;
    public ICommand CreateAutomaticBackupNowCommand { get; private set; } = null!;
    public ICommand OpenAutomaticBackupDirectoryCommand { get; private set; } = null!;

    public event EventHandler? GlobalHotkeysChanged;

    private void InitializeEssentialCommands()
    {
        SaveGlobalHotkeysCommand = new RelayCommand(_ => SaveGlobalHotkeys(), _ => HasUnsavedGlobalHotkeyChanges);
        SaveAutomaticBackupSettingsCommand = new RelayCommand(_ => SaveAutomaticBackupSettings(), _ => HasUnsavedAutomaticBackupChanges);
        CreateAutomaticBackupNowCommand = new RelayCommand(
            async _ => await CreateAutomaticBackupNowAsync(),
            _ => _settings is not null && !_isCreatingAutomaticBackup);
        OpenAutomaticBackupDirectoryCommand = new RelayCommand(_ => OpenAutomaticBackupDirectory(), _ => _settings is not null);
        InitializeLowDistractionCommands();
        InitializeUpdateCommands();
    }

    private void LoadEssentialSettings(AppSettings settings)
    {
        _globalHotkeysEnabled = settings.Hotkeys.Enabled;
        _searchHotkeyGesture = settings.Hotkeys.SearchGesture;
        _quickTodoHotkeyGesture = settings.Hotkeys.QuickTodoGesture;
        _automaticBackupEnabled = settings.Backup.AutomaticEnabled;
        _automaticBackupRetentionText = settings.Backup.RetentionCount.ToString();
        LoadLowDistractionSettings(settings);
        CaptureSavedGlobalHotkeySettings();
        CaptureSavedAutomaticBackupSettings();
        LoadUpdateSettings(settings);
        OnPCFor(nameof(GlobalHotkeysEnabled));
        OnPCFor(nameof(SearchHotkeyGesture));
        OnPCFor(nameof(QuickTodoHotkeyGesture));
        OnPCFor(nameof(AutomaticBackupEnabled));
        OnPCFor(nameof(AutomaticBackupRetentionText));

        if (_settings is not null)
        {
            var status = new AutomaticBackupService(_settings.AppDataDir)
                .GetStatus(settings.Backup.AutomaticEnabled);
            SetAutomaticBackupStatus(status);
        }
    }

    private void SaveGlobalHotkeys()
    {
        if (_settings is null) return;
        var parsed = GlobalHotkeyGesture.TryParsePair(SearchHotkeyGesture, QuickTodoHotkeyGesture,
            out var searchGesture, out var todoGesture, out var error);
        if (GlobalHotkeysEnabled && !parsed)
        {
            GlobalHotkeyStatus = error;
            return;
        }

        if (parsed)
        {
            SearchHotkeyGesture = searchGesture!.DisplayText;
            QuickTodoHotkeyGesture = todoGesture!.DisplayText;
        }
        try
        {
            var settings = _settings.Load();
            settings.Hotkeys.Enabled = GlobalHotkeysEnabled;
            settings.Hotkeys.SearchGesture = SearchHotkeyGesture;
            settings.Hotkeys.QuickTodoGesture = QuickTodoHotkeyGesture;
            _settings.Save(settings);
            GlobalHotkeyStatus = "快捷键设置已保存，正在应用。";
            CaptureSavedGlobalHotkeySettings();
            GlobalHotkeysChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            GlobalHotkeyStatus = "快捷键设置未保存；请检查数据目录权限。";
        }
    }

    private void SaveAutomaticBackupSettings()
    {
        if (_settings is null) return;
        if (!TryReadRetention(out var retention)) return;
        try
        {
            var settings = _settings.Load();
            settings.Backup.AutomaticEnabled = AutomaticBackupEnabled;
            settings.Backup.RetentionCount = retention;
            _settings.Save(settings);
            AutomaticBackupRetentionText = retention.ToString();
            CaptureSavedAutomaticBackupSettings();
            AutomaticBackupStatus = AutomaticBackupEnabled
                ? $"每日自动备份已启用 · 最多保留 {retention} 份"
                : "每日自动备份已关闭。";
        }
        catch
        {
            AutomaticBackupStatus = "自动备份设置未保存；请检查数据目录权限。";
        }
    }

    private async Task CreateAutomaticBackupNowAsync()
    {
        if (_settings is null || !TryReadRetention(out var retention)) return;
        _isCreatingAutomaticBackup = true;
        (CreateAutomaticBackupNowCommand as RelayCommand)?.RaiseCanExecuteChanged();
        AutomaticBackupStatus = "正在创建本地备份…";
        try
        {
            var root = _settings.AppDataDir;
            var result = await Task.Run(() =>
                new AutomaticBackupService(root).Run(enabled: true, retention, force: true));
            SetAutomaticBackupStatus(result);
        }
        finally
        {
            _isCreatingAutomaticBackup = false;
            (CreateAutomaticBackupNowCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void OpenAutomaticBackupDirectory()
    {
        if (_settings is null) return;
        try
        {
            var directory = new AutomaticBackupService(_settings.AppDataDir).BackupDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch
        {
            AutomaticBackupStatus = "无法打开备份文件夹；请检查目录权限或文件关联。";
        }
    }

    private bool TryReadRetention(out int retention)
    {
        if (!int.TryParse(AutomaticBackupRetentionText.Trim(), out retention) || retention is < 1 or > 30)
        {
            AutomaticBackupStatus = "保留份数请输入 1 到 30 的整数。";
            return false;
        }
        return true;
    }

    public void SetGlobalHotkeyRuntimeStatus(GlobalHotkeyApplyResult result) =>
        GlobalHotkeyStatus = result.Message;

    public void SetAutomaticBackupStatus(AutomaticBackupResult result) =>
        AutomaticBackupStatus = result.Message;

    private void CaptureSavedGlobalHotkeySettings()
    {
        _savedGlobalHotkeysEnabled = GlobalHotkeysEnabled;
        _savedSearchHotkeyGesture = SearchHotkeyGesture;
        _savedQuickTodoHotkeyGesture = QuickTodoHotkeyGesture;
        RaiseGlobalHotkeySaveState();
    }

    private void CaptureSavedAutomaticBackupSettings()
    {
        _savedAutomaticBackupEnabled = AutomaticBackupEnabled;
        _savedAutomaticBackupRetentionText = AutomaticBackupRetentionText;
        RaiseAutomaticBackupSaveState();
    }

    private void RaiseGlobalHotkeySaveState()
    {
        OnPCFor(nameof(HasUnsavedGlobalHotkeyChanges));
        OnPCFor(nameof(GlobalHotkeySaveLabel));
        (SaveGlobalHotkeysCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void RaiseAutomaticBackupSaveState()
    {
        OnPCFor(nameof(HasUnsavedAutomaticBackupChanges));
        OnPCFor(nameof(AutomaticBackupSaveLabel));
        (SaveAutomaticBackupSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
