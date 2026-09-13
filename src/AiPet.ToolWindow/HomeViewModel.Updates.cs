using System.IO;
using System.Windows.Input;
using AiPet.Storage;
using AiPet.SystemIntegration;

namespace AiPet.ToolWindow;

public sealed record UpdateInstallerReady(string Path, string Version);

public sealed partial class HomeViewModel
{
    private IReleaseUpdateClient _updateClient = new GitHubReleaseUpdateClient();
    private CancellationTokenSource? _updateLifetimeCts;
    private CancellationTokenSource? _periodicUpdateCts;
    private Task? _startupUpdateTask;
    private Task? _periodicUpdateTask;
    private Task? _updateDownloadTask;
    private bool _updateChecksStarted;
    private bool _periodicUpdateChecksEnabled;
    private string _updateIntervalHoursText = "24";
    private bool _savedPeriodicUpdateChecksEnabled;
    private string _savedUpdateIntervalHoursText = "24";
    private string _updateStatus = "应用启动后会检查一次 GitHub Release。";
    private string _updateRouteStatus = "智能线路已开启：优先连接 GitHub 官方，连接不畅时自动切换。";
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

    public string UpdateRouteStatus
    {
        get => _updateRouteStatus;
        private set
        {
            if (_updateRouteStatus == value) return;
            _updateRouteStatus = value;
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
    public bool HasNoAvailableUpdate => _availableUpdate is null;
    public string AvailableUpdateVersion => _availableUpdate?.Version.ToString(3) ?? string.Empty;
    public string UpdateDownloadLabel => IsDownloadingUpdate
        ? $"下载中 {_updateDownloadProgress}%"
        : "下载并准备安装";
    public bool HasUnsavedUpdateSettings => _settings is not null
        && (PeriodicUpdateChecksEnabled != _savedPeriodicUpdateChecksEnabled
            || !string.Equals(UpdateIntervalHoursText, _savedUpdateIntervalHoursText, StringComparison.Ordinal));
    public string UpdateSettingsSaveLabel =>
        HasUnsavedUpdateSettings ? "保存更新设置（有修改）" : "已保存";

    public ICommand SaveUpdateSettingsCommand { get; private set; } = null!;
    public ICommand CheckForUpdatesCommand { get; private set; } = null!;
    public ICommand DownloadUpdateCommand { get; private set; } = null!;

    public event Action<UpdateInstallerReady>? UpdateInstallerReady;

    private void InitializeUpdateCommands()
    {
        SaveUpdateSettingsCommand = new RelayCommand(
            _ => SaveUpdateSettings(),
            _ => HasUnsavedUpdateSettings && !IsDownloadingUpdate);
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
        CaptureSavedUpdateSettings();
        OnPCFor(nameof(PeriodicUpdateChecksEnabled));
        OnPCFor(nameof(UpdateIntervalHoursText));
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

    public void SetPreviewUpdateStatus()
    {
        UpdateStatus = "预览模式不访问网络；正式启动时检查 GitHub Release。";
        UpdateRouteStatus = "智能线路已开启；预览模式未发起连接。";
    }

    public async Task CheckForUpdatesAsync(bool automatic)
    {
        if (_settings is null || IsCheckingForUpdates || IsDownloadingUpdate) return;
        _updateLifetimeCts ??= new CancellationTokenSource();
        IsCheckingForUpdates = true;
        UpdateStatus = automatic ? "正在检查 GitHub Release…" : "正在手动检查更新…";
        UpdateRouteStatus = "正在选择可用更新线路…";
        try
        {
            var result = await _updateClient.CheckAsync(
                SoftwareVersion,
                _updateLifetimeCts.Token);
            if (result.State != UpdateCheckState.Failed)
                _availableUpdate = result.Update;
            UpdateStatus = result.Message;
            UpdateRouteStatus = result.State == UpdateCheckState.Failed
                ? "GitHub 官方和内置加速线路均未连接成功。"
                : $"本次检查：{NormalizeRouteDisplayName(result.RouteDisplayName)}。";
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "更新检查已取消。";
            UpdateRouteStatus = "线路选择已取消；稍后可以重新检查。";
        }
        finally
        {
            IsCheckingForUpdates = false;
            OnPCFor(nameof(HasAvailableUpdate));
            OnPCFor(nameof(HasNoAvailableUpdate));
            OnPCFor(nameof(AvailableUpdateVersion));
            RaiseUpdateCommandStates();
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
        try
        {
            var settings = _settings.Load();
            settings.Updates.PeriodicEnabled = PeriodicUpdateChecksEnabled;
            settings.Updates.IntervalHours = interval;
            settings.Updates.AccelerationTemplate = string.Empty;
            _settings.Save(settings);
            UpdateIntervalHoursText = interval.ToString();
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
        UpdateRouteStatus = "正在自动选择下载线路…";
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
                progress,
                _updateLifetimeCts.Token);
            _updateDownloadTask = downloadTask;
            var result = await downloadTask;
            UpdateStatus = result.Message;
            UpdateRouteStatus = result.Success
                ? $"本次下载：{NormalizeRouteDisplayName(result.RouteDisplayName)}；安装包已校验。"
                : "内置加速线路和 GitHub 官方均下载失败。";
            if (result.Success && result.InstallerPath is not null)
                UpdateInstallerReady?.Invoke(new(result.InstallerPath, _availableUpdate.Version.ToString(3)));
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "更新下载已取消，现有版本不会改变。";
            UpdateRouteStatus = "下载线路选择已取消。";
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
        (CheckForUpdatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DownloadUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void CaptureSavedUpdateSettings()
    {
        _savedPeriodicUpdateChecksEnabled = PeriodicUpdateChecksEnabled;
        _savedUpdateIntervalHoursText = UpdateIntervalHoursText;
        RaiseUpdateSettingsSaveState();
    }

    private void RaiseUpdateSettingsSaveState()
    {
        OnPCFor(nameof(HasUnsavedUpdateSettings));
        OnPCFor(nameof(UpdateSettingsSaveLabel));
        (SaveUpdateSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static string NormalizeRouteDisplayName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "可用线路" : value;
}
