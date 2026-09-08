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
    private string _globalHotkeyStatus = "等待应用注册快捷键。";
    private bool _automaticBackupEnabled = true;
    private bool _isCreatingAutomaticBackup;
    private string _automaticBackupRetentionText = AutomaticBackupService.DefaultRetentionCount.ToString();
    private string _automaticBackupStatus = "尚无自动备份；可立即创建。";

    public bool GlobalHotkeysEnabled
    {
        get => _globalHotkeysEnabled;
        set { if (_globalHotkeysEnabled == value) return; _globalHotkeysEnabled = value; OnPC(); }
    }

    public string SearchHotkeyGesture
    {
        get => _searchHotkeyGesture;
        set { if (_searchHotkeyGesture == value) return; _searchHotkeyGesture = value; OnPC(); }
    }

    public string QuickTodoHotkeyGesture
    {
        get => _quickTodoHotkeyGesture;
        set { if (_quickTodoHotkeyGesture == value) return; _quickTodoHotkeyGesture = value; OnPC(); }
    }

    public string GlobalHotkeyStatus
    {
        get => _globalHotkeyStatus;
        private set { if (_globalHotkeyStatus == value) return; _globalHotkeyStatus = value; OnPC(); }
    }

    public bool AutomaticBackupEnabled
    {
        get => _automaticBackupEnabled;
        set { if (_automaticBackupEnabled == value) return; _automaticBackupEnabled = value; OnPC(); }
    }

    public string AutomaticBackupRetentionText
    {
        get => _automaticBackupRetentionText;
        set { if (_automaticBackupRetentionText == value) return; _automaticBackupRetentionText = value; OnPC(); }
    }

    public string AutomaticBackupStatus
    {
        get => _automaticBackupStatus;
        private set { if (_automaticBackupStatus == value) return; _automaticBackupStatus = value; OnPC(); }
    }

    public ICommand SaveGlobalHotkeysCommand { get; private set; } = null!;
    public ICommand SaveAutomaticBackupSettingsCommand { get; private set; } = null!;
    public ICommand CreateAutomaticBackupNowCommand { get; private set; } = null!;
    public ICommand OpenAutomaticBackupDirectoryCommand { get; private set; } = null!;

    public event EventHandler? GlobalHotkeysChanged;

    private void InitializeEssentialCommands()
    {
        SaveGlobalHotkeysCommand = new RelayCommand(_ => SaveGlobalHotkeys(), _ => _settings is not null);
        SaveAutomaticBackupSettingsCommand = new RelayCommand(_ => SaveAutomaticBackupSettings(), _ => _settings is not null);
        CreateAutomaticBackupNowCommand = new RelayCommand(
            async _ => await CreateAutomaticBackupNowAsync(),
            _ => _settings is not null && !_isCreatingAutomaticBackup);
        OpenAutomaticBackupDirectoryCommand = new RelayCommand(_ => OpenAutomaticBackupDirectory(), _ => _settings is not null);
    }

    private void LoadEssentialSettings(AppSettings settings)
    {
        _globalHotkeysEnabled = settings.Hotkeys.Enabled;
        _searchHotkeyGesture = settings.Hotkeys.SearchGesture;
        _quickTodoHotkeyGesture = settings.Hotkeys.QuickTodoGesture;
        _automaticBackupEnabled = settings.Backup.AutomaticEnabled;
        _automaticBackupRetentionText = settings.Backup.RetentionCount.ToString();
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
}
