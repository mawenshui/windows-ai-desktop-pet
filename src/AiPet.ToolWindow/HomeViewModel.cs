using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AiPet.AI;
using AiPet.Search;
using AiPet.Secrets;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.SystemIntegration;
using AiPet.Todos;

namespace AiPet.ToolWindow;

/// <summary>
/// View-model for the home page. Owns all the MVP subsystems visible
/// from a single page: search input, category filter, search results,
/// shortcut strip, AI status, autostart toggle, and search-range list.
/// </summary>
public sealed class HomeViewModel : INotifyPropertyChanged
{
    // Mutable so the XAML-resolved parameterless instance can be
    // upgraded in place via Attach() once the App layer has built the
    // real services. They are still initialised in the service ctor.
    private SearchService? _search;
    private ShortcutStore? _shortcuts;
    private IAiClient? _ai;
    private SettingsStore? _settings;
    private IAiSecretStore _secretStore = new WindowsAiSecretStore();

    private CancellationTokenSource? _searchCts;
    private int _searchGeneration;
    private bool _isSearching;

    public TodoViewModel Todo { get; } = new();

    public HomeViewModel(
        SearchService search,
        ShortcutStore shortcuts,
        IAiClient ai,
        SettingsStore settings,
        IAiSecretStore? secretStore = null,
        TodoStore? todoStore = null,
        ITodoAiClient? todoAiClient = null)
    {
        _search = search;
        _shortcuts = shortcuts;
        _ai = ai;
        _settings = settings;
        _secretStore = secretStore ?? new WindowsAiSecretStore();
        var loaded = settings.Load();
        _selectedCharacter = loaded.Pet.PreferredCharacter;
        _enableWildcardSearch = loaded.Search.EnableWildcardSearch;
        _enableRegexSearch = loaded.Search.EnableRegexSearch;
        _selectedSearchScopeId = loaded.Search.LastScopeId;

        InitializeCommands();

        ReloadShortcuts();
        ReloadRanges();
        ReloadAi();
        ReloadAutostart();
        if (todoStore is not null && todoAiClient is not null)
            Todo.Attach(todoStore, todoAiClient, CreateTodoAiConnection);
    }

    public ObservableCollection<SearchItem> Results { get; } = new();
    public ObservableCollection<ShortcutItem> Shortcuts { get; } = new();
    public ObservableCollection<SearchRangeSummary> Ranges { get; } = new();
    public ObservableCollection<SearchScopeOption> SearchScopes { get; } = new();
    public IReadOnlyList<string> Categories { get; } =
        new[] { "全部", "文件夹", "文档", "应用", "图片", "视频", "音频" };
    public IReadOnlyList<PetCharacterOption> PetCharacters { get; } = new[]
    {
        new PetCharacterOption("hero", "红帽勇者"),
        new PetCharacterOption("base", "方块伙伴"),
        new PetCharacterOption("skeleton", "骷髅小队长"),
        new PetCharacterOption("monster", "森林怪兽"),
    };

    public bool HasShortcuts => Shortcuts.Count > 0;
    public bool HasRanges => Ranges.Count > 0;
    public bool HasResults => Results.Count > 0;
    public bool IsSearching => _isSearching;

    /// <summary>
    /// Copy that is displayed below the result list.  Keeping this in the
    /// view-model makes the empty state useful for both a first-time user and
    /// a user whose current query simply has no matches.
    /// </summary>
    public string ResultEmptyMessage
    {
        get
        {
            if (IsSearching) return "正在查找…";
            if (string.IsNullOrWhiteSpace(Query) && Category == "全部")
                return "输入名称即可搜索已授权的文件、文件夹和应用";
            if (Status.Contains("范围", StringComparison.Ordinal)
                && Status.Contains("准备", StringComparison.Ordinal))
                return "搜索范围正在准备，稍后会显示可用结果";
            if (Status.Contains("失败", StringComparison.Ordinal)
                || Status.Contains("无效", StringComparison.Ordinal))
                return Status;
            return "没有找到匹配项，试试换个关键词或搜索范围";
        }
    }

    private string _query = string.Empty;
    public string Query
    {
        get => _query;
        set
        {
            if (_query == value) return;
            _query = value;
            OnPC();
            OnPCFor(nameof(ResultEmptyMessage));
            RestartSearch();
        }
    }
    private string _category = "全部";
    public string Category
    {
        get => _category;
        set
        {
            if (_category == value) return;
            _category = value;
            OnPC();
            OnPCFor(nameof(ResultEmptyMessage));
            RestartSearch(immediate: true);
        }
    }

    private string _selectedSearchScopeId = "all";
    public string SelectedSearchScopeId
    {
        get => _selectedSearchScopeId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || _selectedSearchScopeId == value) return;
            _selectedSearchScopeId = value;
            OnPC();
            OnPCFor(nameof(SelectedSearchScope));
            OnPCFor(nameof(ResultEmptyMessage));
            SaveSearchPreferences();
            RestartSearch(immediate: true);
        }
    }

    /// <summary>
    /// Object selection used by the homepage ComboBox.  Binding the selected
    /// option itself avoids a transient blank value while the scope list is
    /// refreshed (for example after a range is added or removed).
    /// </summary>
    public SearchScopeOption? SelectedSearchScope
    {
        get => SearchScopes.FirstOrDefault(scope => scope.Id == _selectedSearchScopeId);
        set
        {
            if (value is not null) SelectedSearchScopeId = value.Id;
        }
    }

    private bool _enableWildcardSearch;
    public bool EnableWildcardSearch
    {
        get => _enableWildcardSearch;
        set
        {
            if (_enableWildcardSearch == value) return;
            _enableWildcardSearch = value;
            OnPC();
            SaveSearchPreferences();
            RestartSearch();
        }
    }

    private bool _enableRegexSearch;
    public bool EnableRegexSearch
    {
        get => _enableRegexSearch;
        set
        {
            if (_enableRegexSearch == value) return;
            _enableRegexSearch = value;
            OnPC();
            SaveSearchPreferences();
            RestartSearch();
        }
    }

    private string _status = "请添加搜索范围或输入关键词。";
    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPC();
            OnPCFor(nameof(ResultEmptyMessage));
        }
    }
    private string _aiStatus = "未配置";
    public string AiStatus { get => _aiStatus; set { _aiStatus = value; OnPC(); } }
    private string _aiStatusDetail = string.Empty;
    public string AiStatusDetail { get => _aiStatusDetail; set { _aiStatusDetail = value; OnPC(); } }

    private bool _isTestingAi;
    private bool _aiConfigurationDirty;
    private AiConfigurationSnapshot? _verifiedAiConfiguration;
    private DateTimeOffset? _verifiedAiAt;

    public bool IsTestingAi => _isTestingAi;
    public string TestButtonLabel => IsTestingAi ? "测试中…" : "测试连接";
    public bool CanSaveAiConfig =>
        !IsTestingAi
        && _settings is not null
        && _aiConfigurationDirty
        && _verifiedAiConfiguration == CurrentAiConfiguration();
    public string AiActionHint
    {
        get
        {
            if (IsTestingAi) return "正在验证当前配置，请稍候。";
            if (!HasCompleteAiConfiguration) return "请完整填写服务地址、模型和 API Key。";
            if (CanSaveAiConfig) return "测试已通过，现在可以安全保存这组配置。";
            if (!_aiConfigurationDirty) return "当前加载的是已保存配置；修改后需重新测试。";
            if (AiStatus == "连接异常") return "连接未通过，输入内容已保留；修正后请重新测试。";
            return "请先测试当前配置，测试通过后才能保存。";
        }
    }

    private string _provider = "deepseek";
    private bool _loadingAiConfiguration;
    public string Provider
    {
        get => _provider;
        set
        {
            if (_provider == value) return;
            _provider = value;
            OnPC();
            OnPCFor(nameof(SelectedProvider));
            if (!_loadingAiConfiguration) ApplyProviderDefaults(overwriteExisting: true);
            MarkAiConfigurationChanged();
        }
    }
    public IReadOnlyList<AiProviderDescriptor> Providers => AiProviders.Builtin;
    public AiProviderDescriptor? SelectedProvider
    {
        get => AiProviders.FindById(_provider);
        set
        {
            if (value is not null) Provider = value.Id;
        }
    }

    private string _endpoint = string.Empty;
    public string Endpoint { get => _endpoint; set { if (_endpoint == value) return; _endpoint = value; OnPC(); MarkAiConfigurationChanged(); } }
    private string _model = string.Empty;
    public string Model { get => _model; set { if (_model == value) return; _model = value; OnPC(); MarkAiConfigurationChanged(); } }
    private string _apiKey = string.Empty;
    public string ApiKey { get => _apiKey; set { if (_apiKey == value) return; _apiKey = value; OnPC(); MarkAiConfigurationChanged(); } }

    private bool HasCompleteAiConfiguration =>
        !string.IsNullOrWhiteSpace(_endpoint)
        && !string.IsNullOrWhiteSpace(_model)
        && !string.IsNullOrWhiteSpace(_apiKey);
    public bool CanTest => HasCompleteAiConfiguration && !IsTestingAi && _ai is not null;

    private bool _autostartEnabled;
    public bool AutostartEnabled { get => _autostartEnabled; set { if (_autostartEnabled == value) return; _autostartEnabled = value; OnPC(); } }

    private string _newRangePath = string.Empty;
    public string NewRangePath { get => _newRangePath; set { _newRangePath = value; OnPC(); } }

    private string _selectedCharacter = "hero";
    public string SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (_selectedCharacter == value || string.IsNullOrWhiteSpace(value)) return;
            _selectedCharacter = value;
            OnPC();
            CharacterChanged?.Invoke(value);
            Status = "桌宠形象已切换";
            if (_settings is null) return;
            try
            {
                var settings = _settings.Load();
                settings.Pet.PreferredCharacter = value;
                _settings.Save(settings);
            }
            catch
            {
                Status = "桌宠形象已切换；偏好将在下次保存时重试";
            }
        }
    }

    public ICommand SearchCommand { get; private set; } = null!;
    public ICommand AddShortcutCommand { get; private set; } = null!;
    public ICommand EditShortcutCommand { get; private set; } = null!;
    public ICommand RelocateShortcutCommand { get; private set; } = null!;
    public ICommand MoveShortcutUpCommand { get; private set; } = null!;
    public ICommand MoveShortcutDownCommand { get; private set; } = null!;
    public ICommand RemoveShortcutCommand { get; private set; } = null!;
    public ICommand LaunchShortcutCommand { get; private set; } = null!;
    public ICommand TestConnectionCommand { get; private set; } = null!;
    public ICommand SaveAiConfigCommand { get; private set; } = null!;
    public ICommand ToggleAutostartCommand { get; private set; } = null!;
    public ICommand AddRangeCommand { get; private set; } = null!;
    public ICommand RemoveRangeCommand { get; private set; } = null!;
    public ICommand RefreshRangesCommand { get; private set; } = null!;
    public ICommand OpenHelpCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        SearchCommand = new RelayCommand(_ => RestartSearch(immediate: true));
        AddShortcutCommand = new RelayCommand(_ => AddShortcutRequested?.Invoke(this, EventArgs.Empty));
        EditShortcutCommand = new RelayCommand(p => EditShortcutRequested?.Invoke(ShortcutById(p)), p => p is Guid && _shortcuts is not null);
        RelocateShortcutCommand = new RelayCommand(p => RelocateShortcutRequested?.Invoke(ShortcutById(p)), p => p is Guid && _shortcuts is not null);
        MoveShortcutUpCommand = new RelayCommand(p => MoveShortcut((Guid)p!, -1), p => p is Guid id && CanMoveShortcutUp(id));
        MoveShortcutDownCommand = new RelayCommand(p => MoveShortcut((Guid)p!, 1), p => p is Guid id && CanMoveShortcutDown(id));
        RemoveShortcutCommand = new RelayCommand(p => RemoveShortcut((Guid)p!), p => p is Guid && _shortcuts is not null);
        LaunchShortcutCommand = new RelayCommand(p => LaunchShortcut((Guid)p!), p => p is Guid && _shortcuts is not null);
        TestConnectionCommand = new RelayCommand(async _ => await TestConnectionAsync(), _ => CanTest);
        SaveAiConfigCommand = new RelayCommand(_ => SaveAiConfig(), _ => CanSaveAiConfig);
        ToggleAutostartCommand = new RelayCommand(async _ => await ToggleAutostartAsync(), _ => _settings is not null);
        AddRangeCommand = new RelayCommand(_ => AddRange(), _ => _search is not null && _settings is not null);
        RemoveRangeCommand = new RelayCommand(p => RemoveRange((Guid)p!), p => p is Guid && _search is not null);
        RefreshRangesCommand = new RelayCommand(_ => RefreshRanges(), _ => _search is not null);
        OpenHelpCommand = new RelayCommand(_ => OpenHelp());
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new ICommand[]
        {
            SearchCommand, AddShortcutCommand, EditShortcutCommand,
            RelocateShortcutCommand, MoveShortcutUpCommand, MoveShortcutDownCommand,
            RemoveShortcutCommand, LaunchShortcutCommand, TestConnectionCommand,
            SaveAiConfigCommand, ToggleAutostartCommand, AddRangeCommand,
            RemoveRangeCommand, RefreshRangesCommand, OpenHelpCommand,
        })
        {
            (command as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    // ---------------- search ----------------

    private async void RestartSearch(bool immediate = false)
    {
        // Text input is debounced, while explicit filter/scope changes apply
        // immediately so the dropdown never appears inert.
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var generation = Interlocked.Increment(ref _searchGeneration);
        try
        {
            if (!immediate) await Task.Delay(300, token);
            await RunSearchAsync(token, generation);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RunSearchAsync(CancellationToken ct, int generation)
    {
        if (_settings is null || _search is null) return;
        if (!IsCurrentSearch(generation, ct)) return;
        if (string.IsNullOrWhiteSpace(Query) && Category == "全部")
        {
            SetSearching(false);
            Status = "请输入关键词，或选择类别。";
            Results.Clear();
            OnPCFor(nameof(HasResults));
            OnPCFor(nameof(ResultEmptyMessage));
            return;
        }
        try
        {
            SetSearching(true);
            Status = "正在搜索…";
            // Capture one immutable request.  Scope/category changes can
            // happen while SQLite is queried on the worker thread; applying
            // the result only for this request prevents stale rows winning a
            // race with a newer selection.
            var query = Query;
            var category = Category;
            var selectedScope = SelectedSearchScopeId;
            var enableWildcard = EnableWildcardSearch;
            var enableRegex = EnableRegexSearch;
            var kind = category == "全部" ? (SearchItemKind?)null : MapCategory(category);
            var scope = SearchScopes.FirstOrDefault(x => x.Id == selectedScope)?.RangeId;
            var options = new SearchQueryOptions(
                enableWildcard,
                enableRegex,
                scope,
                Limit: 100);
            var rows = await Task.Run(() => _search.Search(query, kind, options), ct);
            if (!IsCurrentSearch(generation, ct)) return;
            Results.Clear();
            foreach (var r in rows) Results.Add(r);
            OnPCFor(nameof(HasResults));
            OnPCFor(nameof(ResultEmptyMessage));
            Status = $"命中 {Results.Count} 条";
        }
        catch (OperationCanceledException) { }
        catch (SearchQueryException ex)
        {
            if (!IsCurrentSearch(generation, ct)) return;
            Status = ex.Message;
            Results.Clear();
            OnPCFor(nameof(HasResults));
            OnPCFor(nameof(ResultEmptyMessage));
        }
        catch (Exception ex)
        {
            if (!IsCurrentSearch(generation, ct)) return;
            Status = "搜索失败: " + ex.Message;
            OnPCFor(nameof(ResultEmptyMessage));
        }
        finally
        {
            if (generation == Volatile.Read(ref _searchGeneration)) SetSearching(false);
        }
    }

    private bool IsCurrentSearch(int generation, CancellationToken ct) =>
        !ct.IsCancellationRequested && generation == Volatile.Read(ref _searchGeneration);

    private void SetSearching(bool value)
    {
        if (_isSearching == value) return;
        _isSearching = value;
        OnPCFor(nameof(IsSearching));
        OnPCFor(nameof(ResultEmptyMessage));
    }

    private static SearchItemKind? MapCategory(string cat) => cat switch
    {
        "文件夹" => SearchItemKind.Folder,
        "文档" => SearchItemKind.Document,
        "应用" => SearchItemKind.Application,
        "图片" => SearchItemKind.Image,
        "视频" => SearchItemKind.Video,
        "音频" => SearchItemKind.Audio,
        _ => null,
    };

    public void OpenResult(SearchItem item)
    {
        if (!item.ExistsNow) { Status = "目标已不存在,需重新索引"; return; }
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = item.FullPath,
                UseShellExecute = true,
            };
            if (item.Kind == SearchItemKind.Folder) psi.FileName = item.FullPath;
            System.Diagnostics.Process.Start(psi);
        }
        catch { Status = "打开失败，请检查权限或默认程序。"; }
    }

    // ---------------- shortcuts ----------------

    private void ReloadShortcuts()
    {
        Shortcuts.Clear();
        foreach (var s in _shortcuts!.Load()) Shortcuts.Add(s);
        OnPCFor(nameof(HasShortcuts));
        (MoveShortcutUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveShortcutDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public bool HasShortcutTarget(string path) =>
        _shortcuts is not null && _shortcuts.ContainsTarget(path);

    public bool AddShortcut(string path, bool allowDuplicate = false)
    {
        path = (path ?? string.Empty).Trim();
        var isFolder = System.IO.Directory.Exists(path);
        if (!isFolder && !System.IO.File.Exists(path)) { Status = "选择的目标不存在。"; return false; }
        try
        {
            var displayName = GetDefaultShortcutName(path, isFolder);
            _shortcuts!.Add(new ShortcutItem
            {
                Kind = isFolder
                    ? ShortcutKind.Folder
                    : GetShortcutKind(path),
                TargetPath = path,
                DisplayName = displayName,
                // The target name is a useful default description and avoids
                // showing an unhelpful generic sentence in the tooltip.
                Description = displayName,
            }, allowDuplicate);
            ReloadShortcuts();
            Status = "快捷入口已添加";
            return true;
        }
        catch (InvalidOperationException ex) { Status = ex.Message; return false; }
        catch (UnauthorizedAccessException) { Status = "无法写入快捷入口，请检查当前账户权限。"; return false; }
        catch (System.IO.IOException) { Status = "快捷入口保存失败，已保留原有数据，请重试。"; return false; }
        catch { Status = "快捷入口添加失败，请重试。"; return false; }
    }

    private static string GetDefaultShortcutName(string path, bool isFolder)
    {
        var name = isFolder
            ? System.IO.Path.GetFileName(path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar))
            : System.IO.Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private static ShortcutKind GetShortcutKind(string path)
    {
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".exe" or ".lnk" => ShortcutKind.Application,
            ".url" => ShortcutKind.Url,
            _ => ShortcutKind.File,
        };
    }

    private ShortcutItem? ShortcutById(object? parameter) =>
        parameter is Guid id ? Shortcuts.FirstOrDefault(item => item.Id == id) : null;

    /// <summary>
    /// Saves user-facing shortcut edits while keeping the target immutable.
    /// Passing an empty icon path clears a custom icon and restores the
    /// dynamic Windows icon fallback.
    /// </summary>
    public bool UpdateShortcut(Guid id, string displayName, string? description, string? iconSourcePath)
    {
        if (_shortcuts is null) return false;
        var item = ShortcutById(id);
        if (item is null) { Status = "快捷入口不存在。"; return false; }
        displayName = (displayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName)) { Status = "名称不能为空，请输入快捷入口名称。"; return false; }

        try
        {
            var iconPath = item.IconPath;
            if (string.IsNullOrWhiteSpace(iconSourcePath))
                iconPath = null;
            else
            {
                iconSourcePath = iconSourcePath.Trim();
                if (!ShellIconProvider.TryValidateCustomIcon(iconSourcePath))
                {
                    Status = "无法读取图标，请选择可用的 ICO、PNG、JPG 或 JPEG 图片。";
                    return false;
                }
                if (!string.Equals(iconSourcePath, iconPath, StringComparison.OrdinalIgnoreCase))
                {
                    iconPath = _shortcuts.CopyIcon(iconSourcePath);
                    if (iconPath is null)
                    {
                        Status = "图标复制失败，请重新选择图片。";
                        return false;
                    }
                }
            }

            _shortcuts.Update(new ShortcutItem
            {
                Id = item.Id,
                Kind = item.Kind,
                TargetPath = item.TargetPath,
                DisplayName = displayName,
                Description = string.IsNullOrWhiteSpace(description) ? displayName : description.Trim(),
                IconPath = iconPath,
                Order = item.Order,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
            });
            ReloadShortcuts();
            Status = "快捷入口已更新";
            return true;
        }
        catch (UnauthorizedAccessException) { Status = "无法保存快捷入口，请检查当前账户权限。"; return false; }
        catch (System.IO.IOException) { Status = "快捷入口保存失败，原有设置仍保留。"; return false; }
        catch { Status = "快捷入口更新失败，请重试。"; return false; }
    }

    /// <summary>Rebinds a stale shortcut to a newly selected target.</summary>
    public void RelocateShortcut(Guid id, string path)
    {
        if (_shortcuts is null) return;
        path = (path ?? string.Empty).Trim();
        var isFolder = System.IO.Directory.Exists(path);
        if (!isFolder && !System.IO.File.Exists(path)) { Status = "选择的目标不存在。"; return; }
        var item = ShortcutById(id);
        if (item is null) { Status = "快捷入口不存在。"; return; }
        try
        {
            var kind = isFolder ? ShortcutKind.Folder : GetShortcutKind(path);
            _shortcuts.Update(new ShortcutItem
            {
                Id = item.Id,
                Kind = kind,
                TargetPath = path,
                DisplayName = item.DisplayName,
                Description = item.Description,
                IconPath = item.IconPath,
                Order = item.Order,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt,
            });
            ReloadShortcuts();
            Status = "快捷入口目标已更新";
        }
        catch { Status = "快捷入口定位失败，请重试。"; }
    }

    private void MoveShortcut(Guid id, int offset)
    {
        if (_shortcuts is null) return;
        var ordered = Shortcuts.Select(item => item.Id).ToList();
        var current = ordered.IndexOf(id);
        var target = current + offset;
        if (current < 0 || target < 0 || target >= ordered.Count) return;
        (ordered[current], ordered[target]) = (ordered[target], ordered[current]);
        try
        {
            _shortcuts.Reorder(ordered);
            ReloadShortcuts();
            Status = "快捷入口顺序已更新";
        }
        catch { Status = "快捷入口排序失败，请重试。"; }
    }

    public bool CanMoveShortcutUp(Guid id) => CanMoveShortcut(id, -1);

    public bool CanMoveShortcutDown(Guid id) => CanMoveShortcut(id, 1);

    private bool CanMoveShortcut(Guid id, int offset)
    {
        var current = Shortcuts.Select(item => item.Id).ToList().IndexOf(id);
        var target = current + offset;
        return _shortcuts is not null
            && current >= 0
            && target >= 0
            && target < Shortcuts.Count;
    }

    private void RemoveShortcut(Guid id)
    {
        _shortcuts!.Remove(id);
        ReloadShortcuts();
    }

    private void LaunchShortcut(Guid id)
    {
        var s = _shortcuts!.Load().FirstOrDefault(x => x.Id == id);
        if (s is null) return;
        var outcome = _shortcuts!.Launch(s);
        Status = outcome.Result switch
        {
            ShortcutLaunchResult.Success => $"已启动 {s.DisplayName}",
            ShortcutLaunchResult.TargetMissing => "目标已不存在",
            ShortcutLaunchResult.NoAssociation => "无默认关联程序",
            ShortcutLaunchResult.PermissionDenied => "无权限启动",
            _ => $"启动失败: {outcome.Message}",
        };
    }

    // ---------------- AI ----------------

    private void ReloadAi()
    {
        var s = _settings!.Load();
        _loadingAiConfiguration = true;
        try
        {
            Provider = string.IsNullOrEmpty(s.Ai.ProviderId) ? "deepseek" : s.Ai.ProviderId;
            Endpoint = s.Ai.Endpoint ?? string.Empty;
            Model = s.Ai.Model ?? string.Empty;
            ApplyProviderDefaults(overwriteExisting: false);
            try
            {
                ApiKey = _secretStore.Load(s.Ai.SecretTargetName) ?? string.Empty;
            }
            catch { ApiKey = string.Empty; }
        }
        finally
        {
            _loadingAiConfiguration = false;
        }
        _aiConfigurationDirty = false;
        _verifiedAiConfiguration = null;
        _verifiedAiAt = null;
        AiStatus = s.Ai.LastStatus switch
        {
            "Connected" => "连接正常",
            "Failed" => "连接异常",
            _ => "未验证",
        };
        AiStatusDetail = s.Ai.LastVerifiedAt is { } t
            ? $"上次验证: {t.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "尚未执行连接测试";
        RaiseAiStateChanged();
    }

    private void ApplyProviderDefaults(bool overwriteExisting)
    {
        var p = SelectedProvider;
        if (p is null || p.Id == AiProviders.CustomId) return;
        if (overwriteExisting || string.IsNullOrWhiteSpace(Endpoint)) Endpoint = p.DefaultEndpoint;
        if (overwriteExisting || string.IsNullOrWhiteSpace(Model)) Model = p.DefaultModel;
    }

    private void SaveAiConfig()
    {
        if (!CanSaveAiConfig || _settings is null) return;
        var targetName = $"WindowsAiDesktopPet:AI:{Provider}";
        try
        {
            if (string.IsNullOrEmpty(ApiKey)) _secretStore.Delete(targetName);
            else _secretStore.Save(targetName, ApiKey);

            var s = _settings.Load();
            s.Ai.ProviderId = Provider;
            s.Ai.Endpoint = Endpoint;
            s.Ai.Model = Model;
            s.Ai.SecretTargetName = targetName;
            s.Ai.LastStatus = "Connected";
            s.Ai.LastVerifiedAt = _verifiedAiAt ?? DateTimeOffset.UtcNow;
            _settings.Save(s);

            _aiConfigurationDirty = false;
            AiStatus = "已保存";
            AiStatusDetail = "连接测试已通过，配置和凭据已安全保存。";
            Status = "AI 配置已保存";
        }
        catch (Exception ex)
        {
            AiStatus = "保存失败";
            AiStatusDetail = "配置保存失败，输入内容仍保留: " + ex.Message;
            Status = "AI 配置保存失败，请重试";
        }
        RaiseAiStateChanged();
    }

    private async Task TestConnectionAsync()
    {
        if (!CanTest || _ai is null) return;
        var testedConfiguration = CurrentAiConfiguration();
        _isTestingAi = true;
        _verifiedAiConfiguration = null;
        AiStatus = "测试中…";
        AiStatusDetail = "正在验证服务地址、模型和凭据。";
        RaiseAiStateChanged();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var r = await _ai.TestConnectionAsync(
                testedConfiguration.Endpoint,
                testedConfiguration.Model,
                testedConfiguration.ApiKey,
                timeout.Token);

            if (testedConfiguration != CurrentAiConfiguration())
            {
                AiStatus = "待测试";
                AiStatusDetail = "测试期间配置已修改，请重新测试当前内容。";
                Status = "AI 配置已修改，需要重新测试";
            }
            else if (r.Status == AiConnectionStatus.Connected)
            {
                _verifiedAiConfiguration = testedConfiguration;
                _verifiedAiAt = r.TestedAt;
                AiStatus = "连接正常";
                AiStatusDetail = $"耗时 {r.LatencyMs} ms；可以保存配置。";
                Status = $"AI 连接正常 ({r.LatencyMs} ms)";
            }
            else
            {
                AiStatus = "连接异常";
                AiStatusDetail = r.Suggestion ?? r.LocalizedMessage ?? "未知错误";
                Status = "AI 连接异常: " + (r.LocalizedMessage ?? "未知错误");
            }
        }
        catch (OperationCanceledException)
        {
            AiStatus = "连接异常";
            AiStatusDetail = "连接测试超时，请检查网络与服务状态。";
            Status = "AI 连接测试超时";
        }
        catch (Exception ex)
        {
            AiStatus = "连接异常";
            AiStatusDetail = "连接测试失败，输入内容已保留: " + ex.Message;
            Status = "AI 连接测试失败";
        }
        finally
        {
            _isTestingAi = false;
            RaiseAiStateChanged();
        }
    }

    private AiConfigurationSnapshot CurrentAiConfiguration() =>
        new(Provider, Endpoint, Model, ApiKey);

    private void MarkAiConfigurationChanged()
    {
        if (_loadingAiConfiguration) return;
        _aiConfigurationDirty = true;
        _verifiedAiConfiguration = null;
        _verifiedAiAt = null;
        AiStatus = "待测试";
        AiStatusDetail = "配置已修改，请重新测试连接。";
        RaiseAiStateChanged();
    }

    private void RaiseAiStateChanged()
    {
        OnPCFor(nameof(IsTestingAi));
        OnPCFor(nameof(CanTest));
        OnPCFor(nameof(CanSaveAiConfig));
        OnPCFor(nameof(TestButtonLabel));
        OnPCFor(nameof(AiActionHint));
        (TestConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAiConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ---------------- autostart ----------------

    private void ReloadAutostart()
    {
        AutostartEnabled = AutoStart.IsEnabled(out _);
    }

    private async Task ToggleAutostartAsync()
    {
        try
        {
            if (AutostartEnabled) AutoStart.Enable();
            else AutoStart.Disable();
            // Re-read actual state from registry (PRD AUTO-02 rollback).
            await Task.Delay(50);
            ReloadAutostart();
            var s = _settings!.Load();
            s.Autostart.Enabled = AutostartEnabled;
            _settings!.Save(s);
            Status = AutostartEnabled ? "已开启开机自启" : "已关闭开机自启";
        }
        catch (Exception ex)
        {
            // Roll back UI to actual state.
            ReloadAutostart();
            Status = "自启设置失败: " + ex.Message;
        }
    }

    // ---------------- search ranges ----------------

    private void RefreshRanges()
    {
        var savedPaths = _settings!.Load().Search.Ranges.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Ranges.Clear();
        var s = _search!.ListRanges();
        foreach (var r in s)
        {
            if (!savedPaths.Contains(r.Path)) continue;
            Ranges.Add(SearchRangeSummary.From(r, _search!.CountItems(r.Id)));
        }
        OnPCFor(nameof(HasRanges));
        RefreshSearchScopes();
    }

    private void RefreshSearchScopes()
    {
        var selected = _selectedSearchScopeId;
        SearchScopes.Clear();
        SearchScopes.Add(new SearchScopeOption("all", "全部范围", null));
        SearchScopes.Add(new SearchScopeOption("apps", "仅应用", Guid.Empty));
        foreach (var range in Ranges)
        {
            var name = System.IO.Path.GetFileName(range.Path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = range.Path;
            SearchScopes.Add(new SearchScopeOption(range.Id.ToString("D"), name, range.Id));
        }
        if (SearchScopes.All(x => x.Id != selected)) selected = "all";
        var selectionChanged = !string.Equals(_selectedSearchScopeId, selected, StringComparison.Ordinal);
        _selectedSearchScopeId = selected;
        OnPCFor(nameof(SelectedSearchScopeId));
        OnPCFor(nameof(SelectedSearchScope));
        OnPCFor(nameof(ResultEmptyMessage));
        // Removing a range or repairing a stale persisted selection changes
        // the effective query even though the ComboBox setter is not called.
        // Re-run the current non-empty query so results never belong to the
        // previous scope.
        if (selectionChanged && (!string.IsNullOrWhiteSpace(Query) || Category != "全部"))
            RestartSearch(immediate: true);
    }

    private void SaveSearchPreferences()
    {
        if (_settings is null) return;
        try
        {
            var settings = _settings.Load();
            settings.Search.EnableWildcardSearch = EnableWildcardSearch;
            settings.Search.EnableRegexSearch = EnableRegexSearch;
            settings.Search.LastScopeId = SelectedSearchScopeId;
            _settings.Save(settings);
        }
        catch (UnauthorizedAccessException) { Status = "搜索设置无法保存，请检查当前账户权限。"; }
        catch (System.IO.IOException) { Status = "搜索设置保存失败，已保留原有数据。"; }
    }

    private void ReloadRanges()
    {
        var s = _settings!.Load();
        if (s.Search.Ranges.Count == 0) { Status = "请在下方添加搜索范围(例如 桌面/文档/下载)"; }
        var indexedRanges = _search!.ListRanges();
        var indexedPaths = indexedRanges.Select(range => range.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in s.Search.Ranges.Where(System.IO.Directory.Exists))
        {
            if (!indexedPaths.Contains(path)) _search.AddRange(path);
            var added = _search.ListRanges().First(range =>
                string.Equals(range.Path, path, StringComparison.OrdinalIgnoreCase));
            if (added.State != SearchRangeState.Ready)
                _ = _search.IndexRangeAsync(added.Id);
        }
        RefreshRanges();
    }

    private void AddRange()
    {
        var path = (NewRangePath ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(path)) { Status = "路径不能为空"; return; }
        if (!System.IO.Directory.Exists(path)) { Status = "目录不存在或无访问权限"; return; }
        try
        {
            var s = _settings!.Load();
            if (s.Search.Ranges.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            { Status = "该目录已在范围列表中"; return; }
            s.Search.Ranges.Add(path);
            _settings!.Save(s);
            NewRangePath = string.Empty;
            _search!.AddRange(path);
            // Find the just-added range id
            var added = _search!.ListRanges().FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            if (added is not null) _ = _search!.IndexRangeAsync(added.Id);
            ReloadRanges();
            Status = "搜索范围已添加，正在建立索引。";
        }
        catch (UnauthorizedAccessException) { Status = "无法添加该文件夹，请检查访问权限。"; }
        catch (System.IO.IOException) { Status = "搜索范围保存失败，已保留原有数据，请重试。"; }
        catch { Status = "添加搜索范围失败，请重试。"; }
    }

    private void RemoveRange(Guid id)
    {
        var s = _settings!.Load();
        var r = _search!.GetRange(id);
        if (r is null) return;
        s.Search.Ranges.RemoveAll(p => string.Equals(p, r.Path, StringComparison.OrdinalIgnoreCase));
        _settings!.Save(s);
        _search!.RemoveRange(id);
        ReloadRanges();
        Status = "已删除范围";
    }

    // ---------------- help ----------------

    private void OpenHelp()
    {
        var result = HelpLauncher.OpenManual();
        Status = result.Kind switch
        {
            HelpOpenKind.Ok => "已打开用户手册",
            HelpOpenKind.Missing => "找不到离线手册,请确认安装完整",
            HelpOpenKind.NoAssociation => "系统未关联 .html 程序",
            _ => "打开手册失败: " + result.Message,
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? AddShortcutRequested;
    public event Action<ShortcutItem?>? EditShortcutRequested;
    public event Action<ShortcutItem?>? RelocateShortcutRequested;
    public event Action<string>? CharacterChanged;
    private void OnPC([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private void OnPCFor(string n) => OnPC(n);

    /// <summary>
    /// Parameterless ctor required by XAML &lt;local:HomeViewModel x:Key="HomeVM"/&gt;.
    /// Real services are injected via Attach() once the App layer has built them.
    /// </summary>
    public HomeViewModel()
    {
        InitializeCommands();
        RefreshSearchScopes();
    }

    /// <summary>
    /// Inject the real services built by App after XAML has constructed the placeholder instance.
    /// </summary>
    public void Attach(
        SearchService search,
        ShortcutStore shortcuts,
        IAiClient ai,
        SettingsStore settings,
        IAiSecretStore? secretStore = null,
        TodoStore? todoStore = null,
        ITodoAiClient? todoAiClient = null)
    {
        _search = search;
        _shortcuts = shortcuts;
        _ai = ai;
        _settings = settings;
        _secretStore = secretStore ?? new WindowsAiSecretStore();
        var loaded = settings.Load();
        _selectedCharacter = loaded.Pet.PreferredCharacter;
        _enableWildcardSearch = loaded.Search.EnableWildcardSearch;
        _enableRegexSearch = loaded.Search.EnableRegexSearch;
        _selectedSearchScopeId = loaded.Search.LastScopeId;
        OnPCFor(nameof(SelectedCharacter));
        OnPCFor(nameof(EnableWildcardSearch));
        OnPCFor(nameof(EnableRegexSearch));
        ReloadShortcuts();
        ReloadRanges();
        ReloadAi();
        ReloadAutostart();
        RaiseCommandStates();
        if (todoStore is not null && todoAiClient is not null)
            Todo.Attach(todoStore, todoAiClient, CreateTodoAiConnection);
    }

    private TodoAiConnection? CreateTodoAiConnection()
    {
        if (_settings is null) return null;
        try
        {
            var settings = _settings.Load();
            if (!string.Equals(settings.Ai.LastStatus, "Connected", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(settings.Ai.Endpoint)
                || string.IsNullOrWhiteSpace(settings.Ai.Model))
                return null;
            var apiKey = _secretStore.Load(settings.Ai.SecretTargetName);
            return string.IsNullOrWhiteSpace(apiKey)
                ? null
                : new TodoAiConnection(settings.Ai.Endpoint, settings.Ai.Model, apiKey);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record AiConfigurationSnapshot(
    string Provider,
    string Endpoint,
    string Model,
    string ApiKey);

public sealed record PetCharacterOption(string Id, string DisplayName);
public sealed record SearchScopeOption(string Id, string DisplayName, Guid? RangeId)
{
    // The compact ComboBox template presents SelectionBoxItem directly.  A
    // readable value keeps the selected scope visible even when WPF does not
    // materialize SelectionBoxItemTemplate for an object-bound selection.
    public override string ToString() => DisplayName;
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }
    public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
    public void Execute(object? p) => _exec(p);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
