using System.IO;
using System.Windows.Input;
using AiPet.Storage;
using AiPet.SystemIntegration;

namespace AiPet.ToolWindow;

public sealed record UpdateInstallerReady(string Path, string Version);

public sealed partial class HomeViewModel
{
    internal const string GitHubUpdateCredentialTarget = "WindowsAiDesktopPet:Updates:GitHub";

    private IReleaseUpdateClient _updateClient = new GitHubReleaseUpdateClient();
    private CancellationTokenSource? _updateLifetimeCts;
    private CancellationTokenSource? _periodicUpdateCts;
    private Task? _startupUpdateTask;
    private Task? _periodicUpdateTask;
    private Task? _updateDownloadTask;
    private bool _updateChecksStarted;
    private bool _periodicUpdateChecksEnabled;
    private string _updateIntervalHoursText = "24";
    private string _updateAccelerationTemplate = string.Empty;
    private bool _savedPeriodicUpdateChecksEnabled;
    private string _savedUpdateIntervalHoursText = "24";
    private string _savedUpdateAccelerationTemplate = string.Empty;
    private string _updateAccessTokenInput = string.Empty;
    private bool _hasStoredUpdateAccessToken;
    private string _updateCredentialStatus = "未保存 GitHub 访问令牌。";
    private string _updateStatus = "应用启动后会检查一次 GitHub Release。";
    private bool _isCheckingForUpdates;
    private bool _isDownloadingUpdate;
    private int _updateDownloadProgress;
    private ReleaseUpdate? _availableUpdate;

    public bool PeriodicUpdateChecksEnabled
    {
        get => _periodicUpdateChecksEnabled;
        set
        {
            if (_periodicUpdateChecksEnabled == value) return;
            _periodicUpdateChecksEnabled = value;
            OnPC();
            RaiseUpdateSettingsSaveState();
        }
    }

    public string UpdateIntervalHoursText
    {
        get => _updateIntervalHoursText;
        set
        {
            if (_updateIntervalHoursText == value) return;
            _updateIntervalHoursText = value;
            OnPC();
            RaiseUpdateSettingsSaveState();
        }
    }

    public string UpdateAccelerationTemplate
    {
        get => _updateAccelerationTemplate;
        set
        {
            if (_updateAccelerationTemplate == value) return;
            _updateAccelerationTemplate = value;
            OnPC();
            RaiseUpdateSettingsSaveState();
        }
    }

    public string UpdateAccessTokenInput
    {
        get => _updateAccessTokenInput;
        set
        {
            if (_updateAccessTokenInput == value) return;
            _updateAccessTokenInput = value;
            OnPC();
            RaiseUpdateCommandStates();
        }
    }

    public bool HasStoredUpdateAccessToken
    {
        get => _hasStoredUpdateAccessToken;
        private set
        {
            if (_hasStoredUpdateAccessToken == value) return;
            _hasStoredUpdateAccessToken = value;
            OnPC();
            RaiseUpdateCommandStates();
        }
    }

    public string UpdateCredentialStatus
    {
        get => _updateCredentialStatus;
        private set
        {
            if (_updateCredentialStatus == value) return;
            _updateCredentialStatus = value;
            OnPC();
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            if (_updateStatus == value) return;
            _updateStatus = value;
            OnPC();
        }
    }

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (_isCheckingForUpdates == value) return;
            _isCheckingForUpdates = value;
            OnPC();
            RaiseUpdateCommandStates();
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (_isDownloadingUpdate == value) return;
            _isDownloadingUpdate = value;
            OnPC();
            OnPCFor(nameof(UpdateDownloadLabel));
            RaiseUpdateCommandStates();
        }
    }

    public bool HasAvailableUpdate => _availableUpdate is not null;
    public string AvailableUpdateVersion => _availableUpdate?.Version.ToString(3) ?? string.Empty;
    public string UpdateDownloadLabel => IsDownloadingUpdate
        ? $"下载中 {_updateDownloadProgress}%"
        : "下载并准备安装";
    public bool HasUnsavedUpdateSettings => _settings is not null
        && (PeriodicUpdateChecksEnabled != _savedPeriodicUpdateChecksEnabled
            || !string.Equals(UpdateIntervalHoursText, _savedUpdateIntervalHoursText, StringComparison.Ordinal)
            || !string.Equals(UpdateAccelerationTemplate, _savedUpdateAccelerationTemplate, StringComparison.Ordinal));
    public string UpdateSettingsSaveLabel =>
        HasUnsavedUpdateSettings ? "保存更新设置（有修改）" : "已保存";

    public ICommand SaveUpdateSettingsCommand { get; private set; } = null!;
    public ICommand SaveUpdateAccessTokenCommand { get; private set; } = null!;
    public ICommand ClearUpdateAccessTokenCommand { get; private set; } = null!;
    public ICommand CheckForUpdatesCommand { get; private set; } = null!;
    public ICommand DownloadUpdateCommand { get; private set; } = null!;

    public event Action<UpdateInstallerReady>? UpdateInstallerReady;

    private void InitializeUpdateCommands()
    {
        SaveUpdateSettingsCommand = new RelayCommand(
            _ => SaveUpdateSettings(),
            _ => HasUnsavedUpdateSettings && !IsDownloadingUpdate);
        SaveUpdateAccessTokenCommand = new RelayCommand(
            _ => SaveUpdateAccessToken(),
            _ => _settings is not null && !string.IsNullOrWhiteSpace(UpdateAccessTokenInput) && !IsDownloadingUpdate);
        ClearUpdateAccessTokenCommand = new RelayCommand(
            _ => ClearUpdateAccessToken(),
            _ => _settings is not null && (HasStoredUpdateAccessToken || !string.IsNullOrEmpty(UpdateAccessTokenInput)) && !IsDownloadingUpdate);
        CheckForUpdatesCommand = new RelayCommand(
            async _ => await CheckForUpdatesAsync(automatic: false),
            _ => _settings is not null && !IsCheckingForUpdates && !IsDownloadingUpdate);
        DownloadUpdateCommand = new RelayCommand(
            async _ => await DownloadAvailableUpdateAsync(),
            _ => _settings is not null && HasAvailableUpdate && !IsCheckingForUpdates && !IsDownloadingUpdate);
    }

    private void LoadUpdateSettings(AppSettings settings)
    {
        _periodicUpdateChecksEnabled = settings.Updates.PeriodicEnabled;
        _updateIntervalHoursText = settings.Updates.IntervalHours.ToString();
        _updateAccelerationTemplate = settings.Updates.AccelerationTemplate;
        CaptureSavedUpdateSettings();
        RefreshUpdateCredentialStatus();
        OnPCFor(nameof(PeriodicUpdateChecksEnabled));
        OnPCFor(nameof(UpdateIntervalHoursText));
        OnPCFor(nameof(UpdateAccelerationTemplate));
    }

    public void SetUpdateClient(IReleaseUpdateClient updateClient) =>
        _updateClient = updateClient ?? throw new ArgumentNullException(nameof(updateClient));

    public void StartUpdateChecks()
    {
        if (_settings is null || _updateChecksStarted) return;
        _updateChecksStarted = true;
        _updateLifetimeCts = new CancellationTokenSource();
        _startupUpdateTask = RunStartupUpdateCheckAsync(_updateLifetimeCts.Token);
    }

    public void SetPreviewUpdateStatus() =>
        UpdateStatus = "预览模式不访问网络；正式启动时检查 GitHub Release。";

    public async Task CheckForUpdatesAsync(bool automatic)
    {
        if (_settings is null || IsCheckingForUpdates || IsDownloadingUpdate) return;
        _updateLifetimeCts ??= new CancellationTokenSource();
        IsCheckingForUpdates = true;
        UpdateStatus = automatic ? "正在检查 GitHub Release…" : "正在手动检查更新…";
        try
        {
            var result = await _updateClient.CheckAsync(
                SoftwareVersion,
                UpdateAccelerationTemplate,
                GetUpdateAccessToken(),
                _updateLifetimeCts.Token);
            if (result.State != UpdateCheckState.Failed)
                _availableUpdate = result.Update;
            UpdateStatus = result.Message;
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "更新检查已取消。";
        }
        finally
        {
            IsCheckingForUpdates = false;
            OnPCFor(nameof(HasAvailableUpdate));
            OnPCFor(nameof(AvailableUpdateVersion));
            RaiseUpdateCommandStates();
        }
    }

    private void SaveUpdateAccessToken()
    {
        if (!GitHubReleaseUpdateClient.TryNormalizeAccessToken(UpdateAccessTokenInput, out var token)
            || token is null)
        {
            UpdateCredentialStatus = "令牌格式无效；请输入 20–512 个不含空格的字符。";
            return;
        }

        try
        {
            _secretStore.Save(GitHubUpdateCredentialTarget, token);
            UpdateAccessTokenInput = string.Empty;
            HasStoredUpdateAccessToken = true;
            UpdateCredentialStatus = "GitHub 访问令牌已安全保存到 Windows 凭据管理器。";
            UpdateStatus = "私有仓库访问已启用；检查更新时只连接 GitHub 官方地址或系统代理。";
        }
        catch
        {
            UpdateCredentialStatus = "令牌未保存；请检查 Windows 凭据管理器是否可用。";
        }
    }

    private void ClearUpdateAccessToken()
    {
        try
        {
            _secretStore.Delete(GitHubUpdateCredentialTarget);
            UpdateAccessTokenInput = string.Empty;
            HasStoredUpdateAccessToken = false;
            UpdateCredentialStatus = "未保存 GitHub 访问令牌。";
            UpdateStatus = "GitHub 访问令牌已清除。";
        }
        catch
        {
            UpdateCredentialStatus = "令牌未清除；请检查 Windows 凭据管理器。";
        }
    }

    private string? GetUpdateAccessToken()
    {
        try { return _secretStore.Load(GitHubUpdateCredentialTarget); }
        catch { return null; }
    }

    private void RefreshUpdateCredentialStatus()
    {
        try
        {
            HasStoredUpdateAccessToken = !string.IsNullOrWhiteSpace(_secretStore.Load(GitHubUpdateCredentialTarget));
            UpdateCredentialStatus = HasStoredUpdateAccessToken
                ? "已在 Windows 凭据管理器中保存 GitHub 访问令牌。"
                : "未保存 GitHub 访问令牌。";
        }
        catch
        {
            HasStoredUpdateAccessToken = false;
            UpdateCredentialStatus = "无法读取 Windows 凭据管理器。";
        }
    }

    private void SaveUpdateSettings()
    {
        if (_settings is null) return;
        if (!int.TryParse(UpdateIntervalHoursText, out var interval) || interval is < 1 or > 168)
        {
            UpdateStatus = "周期必须是 1–168 小时之间的整数。";
            return;
        }
        if (!GitHubReleaseUpdateClient.TryNormalizeAccelerationTemplate(
                UpdateAccelerationTemplate, out var template, out var error))
        {
            UpdateStatus = error!;
            return;
        }

        try
        {
            var settings = _settings.Load();
            settings.Updates.PeriodicEnabled = PeriodicUpdateChecksEnabled;
            settings.Updates.IntervalHours = interval;
            settings.Updates.AccelerationTemplate = template ?? string.Empty;
            _settings.Save(settings);
            UpdateIntervalHoursText = interval.ToString();
            UpdateAccelerationTemplate = template ?? string.Empty;
            CaptureSavedUpdateSettings();
            UpdateStatus = PeriodicUpdateChecksEnabled
                ? $"更新设置已保存；每 {interval} 小时检查一次。"
                : "更新设置已保存；仅在启动时检查一次。";
            RestartPeriodicUpdateChecks();
        }
        catch
        {
            UpdateStatus = "更新设置未保存；请检查数据目录权限。";
        }
    }

    private async Task DownloadAvailableUpdateAsync()
    {
        if (_settings is null || _availableUpdate is null || IsDownloadingUpdate) return;
        _updateLifetimeCts ??= new CancellationTokenSource();
        IsDownloadingUpdate = true;
        _updateDownloadProgress = 0;
        OnPCFor(nameof(UpdateDownloadLabel));
        UpdateStatus = $"正在下载 {_availableUpdate.Version.ToString(3)} 安装器…";
        var progress = new Progress<int>(value =>
        {
            _updateDownloadProgress = value;
            OnPCFor(nameof(UpdateDownloadLabel));
        });
        try
        {
            var downloadTask = _updateClient.DownloadInstallerAsync(
                _availableUpdate,
                Path.Combine(_settings.AppDataDir, "updates"),
                UpdateAccelerationTemplate,
                GetUpdateAccessToken(),
                progress,
                _updateLifetimeCts.Token);
            _updateDownloadTask = downloadTask;
            var result = await downloadTask;
            UpdateStatus = result.Message;
            if (result.Success && result.InstallerPath is not null)
                UpdateInstallerReady?.Invoke(new(result.InstallerPath, _availableUpdate.Version.ToString(3)));
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "更新下载已取消，现有版本不会改变。";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private async Task RunStartupUpdateCheckAsync(CancellationToken cancellationToken)
    {
        await CheckForUpdatesAsync(automatic: true);
        if (!cancellationToken.IsCancellationRequested) RestartPeriodicUpdateChecks();
    }

    private void RestartPeriodicUpdateChecks()
    {
        _periodicUpdateCts?.Cancel();
        _periodicUpdateCts?.Dispose();
        _periodicUpdateCts = null;
        _periodicUpdateTask = null;
        if (!_updateChecksStarted || !PeriodicUpdateChecksEnabled || _updateLifetimeCts is null) return;
        if (!int.TryParse(UpdateIntervalHoursText, out var hours)) hours = 24;
        hours = Math.Clamp(hours, 1, 168);
        _periodicUpdateCts = CancellationTokenSource.CreateLinkedTokenSource(_updateLifetimeCts.Token);
        _periodicUpdateTask = PeriodicUpdateLoopAsync(TimeSpan.FromHours(hours), _periodicUpdateCts.Token);
    }

    private async Task PeriodicUpdateLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, cancellationToken);
                await CheckForUpdatesAsync(automatic: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void CancelUpdateWork()
    {
        _periodicUpdateCts?.Cancel();
        _updateLifetimeCts?.Cancel();
    }

    private void AddUpdateBackgroundTasks(List<Task> tasks)
    {
        if (_startupUpdateTask is { IsCompleted: false }) tasks.Add(_startupUpdateTask);
        if (_periodicUpdateTask is { IsCompleted: false }) tasks.Add(_periodicUpdateTask);
        if (_updateDownloadTask is { IsCompleted: false }) tasks.Add(_updateDownloadTask);
    }

    private void RaiseUpdateCommandStates()
    {
        (SaveUpdateSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveUpdateAccessTokenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearUpdateAccessTokenCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CheckForUpdatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DownloadUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void CaptureSavedUpdateSettings()
    {
        _savedPeriodicUpdateChecksEnabled = PeriodicUpdateChecksEnabled;
        _savedUpdateIntervalHoursText = UpdateIntervalHoursText;
        _savedUpdateAccelerationTemplate = UpdateAccelerationTemplate;
        RaiseUpdateSettingsSaveState();
    }

    private void RaiseUpdateSettingsSaveState()
    {
        OnPCFor(nameof(HasUnsavedUpdateSettings));
        OnPCFor(nameof(UpdateSettingsSaveLabel));
        (SaveUpdateSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
