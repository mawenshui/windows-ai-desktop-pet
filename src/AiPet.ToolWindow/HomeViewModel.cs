using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
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
public sealed partial class HomeViewModel : INotifyPropertyChanged
{
    // Mutable so the XAML-resolved parameterless instance can be
    // upgraded in place via Attach() once the App layer has built the
    // real services. They are still initialised in the service ctor.
    private SearchService? _search;
    private ShortcutStore? _shortcuts;
    private IAiClient? _ai;
    private SettingsStore? _settings;
    private IAiSecretStore _secretStore = new WindowsAiSecretStore();
    private CancellationTokenSource? _aiTestCts;
    private Task? _aiTestTask;

    private CancellationTokenSource? _searchCts;
    private int _searchGeneration;
    private bool _isSearching;
    private readonly object _rangeIndexGate = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _rangeIndexCancellations = new();
    private readonly Dictionary<Guid, Task> _rangeIndexTasks = new();
    private readonly Dictionary<Guid, int> _rangeIndexProgress = new();
    private bool _searchOnboardingCompleted;
    private IReadOnlyList<SearchRangeCandidateOption>? _searchOnboardingCandidateSource;

    public TodoViewModel Todo { get; } = new();
    public string SoftwareVersion => typeof(HomeViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    public HomeViewModel(
        SearchService search,
        ShortcutStore shortcuts,
        IAiClient ai,
        SettingsStore settings,
        IAiSecretStore? secretStore = null,
        TodoStore? todoStore = null,
        ITodoAiClient? todoAiClient = null,
        IReadOnlyList<SearchRangeCandidateOption>? searchOnboardingCandidates = null,
        Func<DateTimeOffset>? todoNow = null,
        ISearchResultActions? searchResultActions = null)
    {
        _search = search;
        _shortcuts = shortcuts;
        _ai = ai;
        _settings = settings;
        _secretStore = secretStore ?? new WindowsAiSecretStore();
        _searchResultActions = searchResultActions ?? new WindowsSearchResultActions();
        var loaded = settings.Load();
        _selectedCharacter = loaded.Pet.PreferredCharacter;
        _enableWildcardSearch = loaded.Search.EnableWildcardSearch;
        _enableRegexSearch = loaded.Search.EnableRegexSearch;
        _selectedSearchScopeId = loaded.Search.LastScopeId;
        _enableContentSearch = loaded.Features.EnableContentSearch;
        _search.SetContentSearchEnabled(_enableContentSearch);
        _searchOnboardingCompleted = loaded.Search.OnboardingCompleted;
        _searchOnboardingCandidateSource = searchOnboardingCandidates;
        LoadEssentialSettings(loaded);

        InitializeCommands();
        InitializeSearchOnboardingCandidates();

        ReloadShortcuts();
        ReloadRanges();
        ReloadAi();
        ReloadAutostart();
        if (todoStore is not null && todoAiClient is not null)
            Todo.Attach(todoStore, todoAiClient, CreateTodoAiConnection, todoNow);
    }

    public ObservableCollection<SearchItem> Results { get; } = new();
    public ObservableCollection<ShortcutItem> Shortcuts { get; } = new();
    public ObservableCollection<SearchRangeRowViewModel> Ranges { get; } = new();
    public ObservableCollection<SearchRangeCandidateViewModel> SearchOnboardingCandidates { get; } = new();
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
    public bool ShowSearchOnboarding => !_searchOnboardingCompleted && !HasRanges;
    public bool HasSelectedSearchOnboardingCandidates =>
        SearchOnboardingCandidates.Any(candidate => candidate.IsSelected);
    public bool HasResults => Results.Count > 0;
    public bool HasSearchConditionsToReset =>
        HasSearchQuery
        || !string.Equals(Category, "全部", StringComparison.Ordinal)
        || !string.Equals(SelectedSearchScopeId, "all", StringComparison.Ordinal);
    public string SearchResultCountText => IsSearching
        ? "搜索中"
        : HasMoreResults
            ? $"已显示 {Results.Count} 条以上"
            : $"{Results.Count} 条";
    private bool _hasMoreResults;
    public bool HasMoreResults
    {
        get => _hasMoreResults;
        private set
        {
            if (_hasMoreResults == value) return;
            _hasMoreResults = value;
            OnPC();
            OnPCFor(nameof(SearchResultCountText));
            OnPCFor(nameof(SelectedResultSummary));
        }
    }
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
            OnPCFor(nameof(HasSearchQuery));
            OnPCFor(nameof(HasSearchConditionsToReset));
            OnPCFor(nameof(ResultEmptyMessage));
            ClearSelectedResult();
            (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ResetSearchContextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RestartSearch();
        }
    }
    public bool HasSearchQuery => !string.IsNullOrEmpty(Query);
    private string _category = "全部";
    public string Category
    {
        get => _category;
        set
        {
            if (_category == value) return;
            _category = value;
            OnPC();
            OnPCFor(nameof(HasSearchConditionsToReset));
            OnPCFor(nameof(ResultEmptyMessage));
            ClearSelectedResult();
            (ResetSearchContextCommand as RelayCommand)?.RaiseCanExecuteChanged();
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
            OnPCFor(nameof(HasSearchConditionsToReset));
            OnPCFor(nameof(ResultEmptyMessage));
            ClearSelectedResult();
            (ResetSearchContextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            SaveSearchPreferences();
            RestartSearch(immediate: true);
        }
    }

    /// <summary>
    /// Object selection used by the homepage visible choice list. Binding the selected
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
            ClearSelectedResult();
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
            ClearSelectedResult();
            SaveSearchPreferences();
            RestartSearch();
        }
    }

    public IReadOnlyList<AppearanceOption> ThemeOptions { get; } = new[]
    {
        new AppearanceOption("system", "跟随系统"), new AppearanceOption("light", "浅色"),
        new AppearanceOption("dark", "深色"), new AppearanceOption("high-contrast", "高对比度"),
    };
    private string _themePreference = "system";
    public string ThemePreference
    {
        get => _themePreference;
        set { if (_themePreference == value) return; _themePreference = value; OnPC(); SaveAppearancePreferences(); }
    }
    public AppearanceOption? SelectedTheme
    {
        get => ThemeOptions.FirstOrDefault(option => option.Id == ThemePreference);
        set { if (value is not null) ThemePreference = value.Id; }
    }
    private bool _enablePetRoaming = true;
    public bool EnablePetRoaming { get => _enablePetRoaming; set { if (_enablePetRoaming == value) return; _enablePetRoaming = value; OnPC(); SaveAppearancePreferences(); } }
    private bool _enableBubbleAnimation = true;
    public bool EnableBubbleAnimation { get => _enableBubbleAnimation; set { if (_enableBubbleAnimation == value) return; _enableBubbleAnimation = value; OnPC(); SaveAppearancePreferences(); } }
    private bool _enableFollowMotion = true;
    public bool EnableFollowMotion { get => _enableFollowMotion; set { if (_enableFollowMotion == value) return; _enableFollowMotion = value; OnPC(); SaveAppearancePreferences(); } }

    private void SaveAppearancePreferences()
    {
        if (_settings is null) return;
        var settings = _settings.Load();
        settings.Appearance.Theme = ThemePreference;
        settings.Appearance.EnablePetRoaming = EnablePetRoaming;
        settings.Appearance.EnableBubbleAnimation = EnableBubbleAnimation;
        settings.Appearance.EnableFollowMotion = EnableFollowMotion;
        _settings.Save(settings);
        AppearanceChanged?.Invoke(settings.Appearance);
        OnPCFor(nameof(SelectedTheme));
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
    private string _maintenanceStatus = "尚未执行维护操作。";
    public string MaintenanceStatus
    {
        get => _maintenanceStatus;
        private set
        {
            if (_maintenanceStatus == value) return;
            _maintenanceStatus = value;
            OnPC();
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
    private string? _selectedAiConfigurationId;
    private string _aiConfigurationName = "新配置";

    public ObservableCollection<AiConfigurationOption> SavedAiConfigurations { get; } = new();
    public bool HasSavedAiConfigurations => SavedAiConfigurations.Count > 0;

    public string? SelectedAiConfigurationId
    {
        get => _selectedAiConfigurationId;
        set
        {
            if (string.Equals(_selectedAiConfigurationId, value, StringComparison.Ordinal)) return;
            _selectedAiConfigurationId = value;
            OnPC();
            OnPCFor(nameof(SelectedAiConfiguration));
            if (!_loadingAiConfiguration && !string.IsNullOrWhiteSpace(value))
                SelectSavedAiConfiguration(value);
        }
    }

    public AiConfigurationOption? SelectedAiConfiguration
    {
        get => SavedAiConfigurations.FirstOrDefault(profile =>
            string.Equals(profile.Id, _selectedAiConfigurationId, StringComparison.Ordinal));
        set
        {
            if (value is null && _loadingAiConfiguration && !string.IsNullOrWhiteSpace(_selectedAiConfigurationId))
                return;
            SelectedAiConfigurationId = value?.Id;
        }
    }

    public string AiConfigurationName
    {
        get => _aiConfigurationName;
        set
        {
            var normalized = value ?? string.Empty;
            if (_aiConfigurationName == normalized) return;
            _aiConfigurationName = normalized;
            OnPC();
            if (!_loadingAiConfiguration)
            {
                _aiConfigurationDirty = true;
                RaiseAiStateChanged();
            }
        }
    }

    public bool IsTestingAi => _isTestingAi;
    public bool HasUnsavedAiChanges => _aiConfigurationDirty;
    public bool HasSelectedAiConfiguration => !string.IsNullOrWhiteSpace(_selectedAiConfigurationId);
    public string TestButtonLabel => IsTestingAi ? "测试中…" : "测试连接";
    public bool CanSaveAiConfig =>
        !IsTestingAi
        && _settings is not null
        && _aiConfigurationDirty
        && !string.IsNullOrWhiteSpace(AiConfigurationName)
        && _verifiedAiConfiguration == CurrentAiConfiguration();
    public string AiActionHint
    {
        get
        {
            if (IsTestingAi) return "正在验证当前配置，请稍候。";
            if (string.IsNullOrWhiteSpace(AiConfigurationName)) return "请先填写配置名称，便于以后选择。";
            if (!HasCompleteAiConfiguration) return "请完整填写服务地址、模型和 API Key。";
            if (CanSaveAiConfig) return "测试已通过，现在可以安全保存这组配置。";
            if (!_aiConfigurationDirty) return "当前加载的是已保存配置；可直接使用，也可重新测试后更新保存。";
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
    public IReadOnlyList<AiProviderDescriptor> Providers => AiProviders.Builtin.Concat(_customProviders).ToArray();
    public IReadOnlyList<AiProviderDescriptor> AiConfigurationTemplates => Providers;
    private AiProviderDescriptor? _selectedAiTemplate = AiProviders.FindById("deepseek");
    public AiProviderDescriptor? SelectedAiTemplate
    {
        get => _selectedAiTemplate;
        set
        {
            if (value is null || string.Equals(_selectedAiTemplate?.Id, value.Id, StringComparison.Ordinal)) return;
            _selectedAiTemplate = value;
            OnPC();
        }
    }
    public AiProviderDescriptor? SelectedProvider
    {
        get => Providers.FirstOrDefault(provider => provider.Id == _provider);
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
    public ICommand ClearSearchCommand { get; private set; } = null!;
    public ICommand ResetSearchContextCommand { get; private set; } = null!;
    public ICommand LoadMoreResultsCommand { get; private set; } = null!;
    public ICommand AddShortcutCommand { get; private set; } = null!;
    public ICommand EditShortcutCommand { get; private set; } = null!;
    public ICommand RelocateShortcutCommand { get; private set; } = null!;
    public ICommand MoveShortcutUpCommand { get; private set; } = null!;
    public ICommand MoveShortcutDownCommand { get; private set; } = null!;
    public ICommand RemoveShortcutCommand { get; private set; } = null!;
    public ICommand LaunchShortcutCommand { get; private set; } = null!;
    public ICommand TestConnectionCommand { get; private set; } = null!;
    public ICommand SaveAiConfigCommand { get; private set; } = null!;
    public ICommand NewAiConfigCommand { get; private set; } = null!;
    public ICommand ToggleAutostartCommand { get; private set; } = null!;
    public ICommand AddRangeCommand { get; private set; } = null!;
    public ICommand RemoveRangeCommand { get; private set; } = null!;
    public ICommand RetryRangeCommand { get; private set; } = null!;
    public ICommand CancelRangeCommand { get; private set; } = null!;
    public ICommand RefreshRangesCommand { get; private set; } = null!;
    public ICommand ConfirmSearchOnboardingCommand { get; private set; } = null!;
    public ICommand DeferSearchOnboardingCommand { get; private set; } = null!;
    public ICommand OpenHelpCommand { get; private set; } = null!;

    private void InitializeCommands()
    {
        InitializeSearchResultCommands();
        SearchCommand = new RelayCommand(_ => RestartSearch(immediate: true));
        ClearSearchCommand = new RelayCommand(_ => ClearSearch(), _ => HasSearchQuery);
        ResetSearchContextCommand = new RelayCommand(_ => ResetSearchContext(), _ => HasSearchConditionsToReset);
        LoadMoreResultsCommand = new RelayCommand(async _ => await LoadMoreResultsAsync(), _ => HasMoreResults && !IsSearching);
        AddShortcutCommand = new RelayCommand(_ => AddShortcutRequested?.Invoke(this, EventArgs.Empty));
        EditShortcutCommand = new RelayCommand(p => EditShortcutRequested?.Invoke(ShortcutById(p)), p => p is Guid && _shortcuts is not null);
        RelocateShortcutCommand = new RelayCommand(p => RelocateShortcutRequested?.Invoke(ShortcutById(p)), p => p is Guid && _shortcuts is not null);
        MoveShortcutUpCommand = new RelayCommand(p => MoveShortcut((Guid)p!, -1), p => p is Guid id && CanMoveShortcutUp(id));
        MoveShortcutDownCommand = new RelayCommand(p => MoveShortcut((Guid)p!, 1), p => p is Guid id && CanMoveShortcutDown(id));
        RemoveShortcutCommand = new RelayCommand(p => RemoveShortcut((Guid)p!), p => p is Guid && _shortcuts is not null);
        LaunchShortcutCommand = new RelayCommand(p => LaunchShortcut((Guid)p!), p => p is Guid && _shortcuts is not null);
        TestConnectionCommand = new RelayCommand(async _ => await RunAiConnectionTestAsync(), _ => CanTest);
        SaveAiConfigCommand = new RelayCommand(_ => SaveAiConfig(), _ => CanSaveAiConfig);
        NewAiConfigCommand = new RelayCommand(_ => StartNewAiConfiguration(), _ => _settings is not null && !IsTestingAi);
        ToggleAutostartCommand = new RelayCommand(async _ => await ToggleAutostartAsync(), _ => _settings is not null);
        AddRangeCommand = new RelayCommand(_ => AddRange(), _ => _search is not null && _settings is not null);
        RemoveRangeCommand = new RelayCommand(p => RemoveRange((Guid)p!), p => p is Guid && _search is not null);
        RetryRangeCommand = new RelayCommand(
            async p => await StartIndexRangeAsync((Guid)p!),
            p => p is Guid id && CanRetryRange(id));
        CancelRangeCommand = new RelayCommand(
            p => CancelRange((Guid)p!),
            p => p is Guid id && CanCancelRange(id));
        RefreshRangesCommand = new RelayCommand(_ => RefreshRanges(), _ => _search is not null);
        ConfirmSearchOnboardingCommand = new RelayCommand(
            async _ => await ConfirmSearchOnboardingAsync(),
            _ => _settings is not null && _search is not null && HasSelectedSearchOnboardingCandidates);
        DeferSearchOnboardingCommand = new RelayCommand(
            _ => DeferSearchOnboarding(),
            _ => _settings is not null && ShowSearchOnboarding);
        OpenHelpCommand = new RelayCommand(_ => OpenHelp());
        InitializeContentSearchCommands();
        InitializeEssentialCommands();
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new ICommand[]
        {
            SearchCommand, ClearSearchCommand, ResetSearchContextCommand, AddShortcutCommand, EditShortcutCommand,
            RelocateShortcutCommand, MoveShortcutUpCommand, MoveShortcutDownCommand,
            RemoveShortcutCommand, LaunchShortcutCommand, TestConnectionCommand,
            SaveAiConfigCommand, NewAiConfigCommand, ToggleAutostartCommand, AddRangeCommand,
            RemoveRangeCommand, RetryRangeCommand, CancelRangeCommand, RefreshRangesCommand,
            ConfirmSearchOnboardingCommand, DeferSearchOnboardingCommand, OpenHelpCommand,
            SaveGlobalHotkeysCommand, SaveAutomaticBackupSettingsCommand,
            CreateAutomaticBackupNowCommand, OpenAutomaticBackupDirectoryCommand,
            SaveUpdateSettingsCommand, CheckForUpdatesCommand, DownloadUpdateCommand,
            RebuildContentIndexCommand, DisableContentSearchCommand,
        })
        {
            (command as RelayCommand)?.RaiseCanExecuteChanged();
        }
        RaiseSearchResultCommandStates();
    }

    // ---------------- search ----------------

    private async void RestartSearch(bool immediate = false)
    {
        // Text input is debounced, while explicit filter/scope changes apply
        // immediately so the visible choice control never appears inert.
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
            ClearSelectedResult();
            HasMoreResults = false;
            RaiseSearchResultCollectionState();
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
            const int pageSize = 50;
            var options = new SearchQueryOptions(
                enableWildcard,
                enableRegex,
                scope,
                Limit: pageSize + 1, Field: SelectedSearchField.Field, UseRecentHistory: UseRecentSearchHistory,
                EnableContentSearch: EnableContentSearch);
            var page = await Task.Run(() => _search.SearchPage(query, kind, options, ct), ct);
            var rows = page.Items;
            if (!IsCurrentSearch(generation, ct)) return;
            var selectedRangeId = SelectedResult?.RangeId;
            var selectedPath = SelectedResult?.FullPath;
            _searchPageRevision = page.Revision;
            Results.Clear();
            foreach (var r in rows.Take(pageSize)) Results.Add(r);
            RestoreSelectedResult(selectedRangeId, selectedPath);
            HasMoreResults = rows.Count > pageSize;
            RaiseSearchResultCollectionState();
            Status = HasMoreResults ? $"已显示 {Results.Count} 条，可继续加载" : $"命中 {Results.Count} 条";
        }
        catch (OperationCanceledException) { }
        catch (SearchQueryException ex)
        {
            if (!IsCurrentSearch(generation, ct)) return;
            Status = ex.Message;
            Results.Clear();
            ClearSelectedResult();
            HasMoreResults = false;
            RaiseSearchResultCollectionState();
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

    private async Task LoadMoreResultsAsync()
    {
        if (_search is null || !HasMoreResults) return;
        var generation = Volatile.Read(ref _searchGeneration);
        var ct = _searchCts?.Token ?? CancellationToken.None;
        SetSearching(true);
        try
        {
            const int pageSize = 50;
            var category = Category;
            var scope = SearchScopes.FirstOrDefault(x => x.Id == SelectedSearchScopeId)?.RangeId;
            var kind = category == "全部" ? (SearchItemKind?)null : MapCategory(category);
            var query = Query;
            var options = new SearchQueryOptions(EnableWildcardSearch, EnableRegexSearch, scope, pageSize + 1, Results.Count, SelectedSearchField.Field, UseRecentSearchHistory, EnableContentSearch);
            var page = await Task.Run(() => _search.SearchPage(query, kind, options, ct), ct);
            if (!IsCurrentSearch(generation, ct)) return;
            if (page.Revision != _searchPageRevision) { RestartSearch(immediate: true); Status = "索引已更新，正在刷新结果。"; return; }
            var rows = page.Items;
            foreach (var row in rows.Take(pageSize)) Results.Add(row);
            HasMoreResults = rows.Count > pageSize;
            RaiseSearchResultCollectionState();
            Status = HasMoreResults ? $"已显示 {Results.Count} 条，可继续加载" : $"已显示全部 {Results.Count} 条";
        }
        catch (OperationCanceledException) { }
        catch (SearchQueryException ex) { if (IsCurrentSearch(generation, ct)) Status = ex.Message; }
        catch { if (IsCurrentSearch(generation, ct)) Status = "加载失败，请重新搜索。"; }
        finally { if (generation == Volatile.Read(ref _searchGeneration)) SetSearching(false); (LoadMoreResultsCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    private bool IsCurrentSearch(int generation, CancellationToken ct) =>
        !ct.IsCancellationRequested && generation == Volatile.Read(ref _searchGeneration);

    private void SetSearching(bool value)
    {
        if (_isSearching == value) return;
        _isSearching = value;
        OnPCFor(nameof(IsSearching));
        OnPCFor(nameof(SearchResultCountText));
        OnPCFor(nameof(ResultEmptyMessage));
        (LoadMoreResultsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ResetSearchContext()
    {
        if (!HasSearchConditionsToReset) return;
        _searchCts?.Cancel();
        _query = string.Empty;
        _category = "全部";
        _selectedSearchScopeId = "all";
        OnPCFor(nameof(Query));
        OnPCFor(nameof(HasSearchQuery));
        OnPCFor(nameof(Category));
        OnPCFor(nameof(SelectedSearchScopeId));
        OnPCFor(nameof(SelectedSearchScope));
        OnPCFor(nameof(HasSearchConditionsToReset));
        OnPCFor(nameof(ResultEmptyMessage));
        ClearSelectedResult();
        (ClearSearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ResetSearchContextCommand as RelayCommand)?.RaiseCanExecuteChanged();
        SaveSearchPreferences();
        RestartSearch(immediate: true);
    }

    private void RaiseSearchResultCollectionState()
    {
        OnPCFor(nameof(HasResults));
        OnPCFor(nameof(ResultEmptyMessage));
        OnPCFor(nameof(SearchResultCountText));
        OnPCFor(nameof(SelectedResultSummary));
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
        SelectedResult = item;
        OpenSelectedResultCommand.Execute(null);
    }

    // ---------------- shortcuts ----------------

    private void ReloadShortcuts()
    {
        Shortcuts.Clear();
        foreach (var s in _shortcuts!.Load()) Shortcuts.Add(s);
        OnPCFor(nameof(HasShortcuts));
        OnPCFor(nameof(IsSelectedResultInShortcuts));
        OnPCFor(nameof(SelectedResultShortcutLabel));
        RaiseSearchResultCommandStates();
        (MoveShortcutUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (MoveShortcutDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ClearSearch()
    {
        if (!HasSearchQuery) return;
        Query = string.Empty;
        RestartSearch(immediate: true);
    }

    public bool HasShortcutTarget(string path) =>
        _shortcuts is not null && _shortcuts.ContainsTarget(path);

    public ShortcutManagerWindow? CreateShortcutManager(System.Windows.Window owner) => _shortcuts is null ? null : new(owner, _shortcuts, RelocateShortcut, ReloadShortcuts);

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
                Group = item.Group,
                Pinned = item.Pinned,
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
                Group = item.Group,
                Pinned = item.Pinned,
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
            RefreshSavedAiConfigurations(s.Ai);
            var active = FindAiProfile(s.Ai, s.Ai.ActiveProfileId);
            _selectedAiConfigurationId = active?.Id;
            OnPCFor(nameof(SelectedAiConfigurationId));
            OnPCFor(nameof(SelectedAiConfiguration));
            AiConfigurationName = active?.DisplayName ?? "新配置";
            Provider = string.IsNullOrEmpty(active?.ProviderId ?? s.Ai.ProviderId)
                ? "deepseek"
                : active?.ProviderId ?? s.Ai.ProviderId;
            Endpoint = active?.Endpoint ?? s.Ai.Endpoint ?? string.Empty;
            Model = active?.Model ?? s.Ai.Model ?? string.Empty;
            ApplyProviderDefaults(overwriteExisting: false);
            try
            {
                var secretTarget = active?.SecretTargetName ?? s.Ai.SecretTargetName;
                ApiKey = string.IsNullOrWhiteSpace(secretTarget)
                    ? string.Empty
                    : _secretStore.Load(secretTarget) ?? string.Empty;
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
        var selectedProfile = FindAiProfile(s.Ai, _selectedAiConfigurationId);
        AiStatus = (selectedProfile?.LastStatus ?? s.Ai.LastStatus) switch
        {
            "Connected" => "连接正常",
            "Failed" => "连接异常",
            _ => "未验证",
        };
        AiStatusDetail = (selectedProfile?.LastVerifiedAt ?? s.Ai.LastVerifiedAt) is { } t
            ? $"上次验证: {t.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "尚未执行连接测试";
        RaiseAiStateChanged();
    }

    private void RefreshSavedAiConfigurations(AiSettings settings)
    {
        SavedAiConfigurations.Clear();
        foreach (var profile in settings.Profiles)
            SavedAiConfigurations.Add(new AiConfigurationOption(profile.Id, profile.DisplayName));
        OnPCFor(nameof(HasSavedAiConfigurations));
        OnPCFor(nameof(SelectedAiConfiguration));
    }

    private void SelectSavedAiConfiguration(string profileId)
    {
        if (_settings is null) return;
        try
        {
            var settings = _settings.Load();
            var profile = FindAiProfile(settings.Ai, profileId);
            if (profile is null)
            {
                ReloadAi();
                return;
            }

            _loadingAiConfiguration = true;
            try
            {
                AiConfigurationName = profile.DisplayName;
                Provider = profile.ProviderId;
                Endpoint = profile.Endpoint;
                Model = profile.Model;
                ApiKey = string.IsNullOrWhiteSpace(profile.SecretTargetName)
                    ? string.Empty
                    : _secretStore.Load(profile.SecretTargetName) ?? string.Empty;
            }
            finally
            {
                _loadingAiConfiguration = false;
            }

            settings.Ai.ActiveProfileId = profile.Id;
            CopyProfileToActiveAiSettings(profile, settings.Ai);
            _settings.Save(settings);
            _aiConfigurationDirty = false;
            _verifiedAiConfiguration = null;
            _verifiedAiAt = null;
            AiStatus = profile.LastStatus == "Connected" ? "连接正常" : "未验证";
            AiStatusDetail = profile.LastVerifiedAt is { } verifiedAt
                ? $"已切换到“{profile.DisplayName}”；上次验证: {verifiedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : $"已切换到“{profile.DisplayName}”；尚未执行连接测试。";
            Status = $"已启用 AI 配置：{profile.DisplayName}";
        }
        catch
        {
            AiStatus = "加载失败";
            AiStatusDetail = "无法加载已保存配置，请检查当前账户的凭据状态后重试。";
        }
        RaiseAiStateChanged();
    }

    private void StartNewAiConfiguration()
    {
        var template = SelectedAiTemplate ?? AiProviders.FindById("deepseek")!;
        _loadingAiConfiguration = true;
        try
        {
            _selectedAiConfigurationId = null;
            OnPCFor(nameof(SelectedAiConfigurationId));
            OnPCFor(nameof(SelectedAiConfiguration));
            var templateName = template.DisplayName
                .Replace("（默认）", string.Empty, StringComparison.Ordinal)
                .Replace("(默认)", string.Empty, StringComparison.Ordinal)
                .Trim();
            AiConfigurationName = $"{templateName} 配置";
            Provider = template.Id;
            Endpoint = template.DefaultEndpoint;
            Model = template.DefaultModel;
            ApiKey = string.Empty;
        }
        finally
        {
            _loadingAiConfiguration = false;
        }
        _aiConfigurationDirty = true;
        _verifiedAiConfiguration = null;
        _verifiedAiAt = null;
        AiStatus = "待测试";
        AiStatusDetail = $"已从“{template.DisplayName}”模板创建；可修改任意字段，测试通过后才能保存。";
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
        try
        {
            var s = _settings.Load();
            var existing = FindAiProfile(s.Ai, _selectedAiConfigurationId);
            var normalizedName = AiConfigurationName.Trim();
            if (s.Ai.Profiles.Any(profile =>
                    !string.Equals(profile.Id, existing?.Id, StringComparison.Ordinal)
                    && string.Equals(profile.DisplayName.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                AiStatus = "无法保存";
                AiStatusDetail = "已有同名配置，请换一个名称后再保存。";
                Status = "AI 配置名称重复";
                RaiseAiStateChanged();
                return;
            }
            var profileId = existing?.Id ?? Guid.NewGuid().ToString("N");
            var targetName = string.IsNullOrWhiteSpace(existing?.SecretTargetName)
                ? $"WindowsAiDesktopPet:AI:profile:{profileId}"
                : existing.SecretTargetName;
            if (string.IsNullOrEmpty(ApiKey)) _secretStore.Delete(targetName);
            else _secretStore.Save(targetName, ApiKey);

            var profile = existing ?? new AiConfigurationProfile { Id = profileId };
            profile.DisplayName = normalizedName;
            profile.ProviderId = Provider;
            profile.Endpoint = Endpoint.Trim();
            profile.Model = Model.Trim();
            profile.SecretTargetName = targetName;
            profile.LastStatus = "Connected";
            profile.LastVerifiedAt = _verifiedAiAt ?? DateTimeOffset.UtcNow;
            if (existing is null) s.Ai.Profiles.Add(profile);
            s.Ai.ActiveProfileId = profile.Id;
            CopyProfileToActiveAiSettings(profile, s.Ai);
            _settings.Save(s);

            _loadingAiConfiguration = true;
            _selectedAiConfigurationId = profile.Id;
            OnPCFor(nameof(SelectedAiConfigurationId));
            OnPCFor(nameof(SelectedAiConfiguration));
            RefreshSavedAiConfigurations(s.Ai);
            _loadingAiConfiguration = false;
            _aiConfigurationDirty = false;
            AiStatus = "已保存";
            AiStatusDetail = $"“{profile.DisplayName}”已保存并设为当前使用配置；凭据仍存放在 Windows 安全存储中。";
            Status = "AI 配置已保存";
        }
        catch
        {
            AiStatus = "保存失败";
            AiStatusDetail = "配置保存失败，输入内容仍保留；请检查账户权限后重试。";
            Status = "AI 配置保存失败，请重试";
        }
        RaiseAiStateChanged();
    }

    public void DiscardAiConfigurationChanges() => ReloadAi();

    public bool DeleteSelectedAiConfiguration()
    {
        if (_settings is null) return false;
        try
        {
            var settings = _settings.Load();
            var profile = FindAiProfile(settings.Ai, _selectedAiConfigurationId);
            if (profile is null)
            {
                StartNewAiConfiguration();
                ApiKey = string.Empty;
                _aiConfigurationDirty = false;
                AiStatus = "未配置";
                AiStatusDetail = "未保存的连接信息已清除。";
                Status = "已清除未保存的 AI 配置";
                RaiseAiStateChanged();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(profile.SecretTargetName))
                _secretStore.Delete(profile.SecretTargetName);
            ApiKey = string.Empty;
            settings.Ai.Profiles.RemoveAll(item =>
                string.Equals(item.Id, profile.Id, StringComparison.Ordinal));

            var next = settings.Ai.Profiles.FirstOrDefault();
            settings.Ai.ActiveProfileId = next?.Id;
            if (next is not null)
            {
                CopyProfileToActiveAiSettings(next, settings.Ai);
            }
            else
            {
                ResetActiveAiSettings(settings.Ai);
            }
            _settings.Save(settings);
            ReloadAi();
            Status = next is null
                ? "AI 配置与凭据已清除"
                : $"已删除“{profile.DisplayName}”，当前切换到“{next.DisplayName}”。";
            return true;
        }
        catch
        {
            ApiKey = string.Empty;
            _verifiedAiConfiguration = null;
            _verifiedAiAt = null;
            AiStatus = "清除失败";
            AiStatusDetail = "内存中的 Key 已清除；凭据或配置清理未全部完成，请重试。";
            Status = "AI 配置清除未完成";
            RaiseAiStateChanged();
            return false;
        }
    }

    private async Task RunAiConnectionTestAsync()
    {
        var task = TestConnectionAsync();
        _aiTestTask = task;
        try { await task; }
        finally
        {
            if (ReferenceEquals(_aiTestTask, task)) _aiTestTask = null;
        }
    }

    private async Task TestConnectionAsync(bool generation = false)
    {
        if (!CanTest || _ai is null) return;
        var testedConfiguration = CurrentAiConfiguration();
        _isTestingAi = true;
        _aiUserCancelled = false;
        _verifiedAiConfiguration = null;
        AiStatus = "测试中…";
        AiStatusDetail = "正在验证服务地址、模型和凭据。";
        RaiseAiStateChanged();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _aiTestCts = timeout;
        try
        {
            var r = generation && _ai is IAiCapabilityClient capability
                ? await capability.VerifyGenerationAsync(testedConfiguration.Endpoint, testedConfiguration.Model, testedConfiguration.ApiKey, timeout.Token)
                : await _ai.TestConnectionAsync(
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
                // A fresh verification timestamp is persistable state even
                // when the loaded profile fields were not edited.
                _aiConfigurationDirty = true;
                AiStatus = "连接正常";
                AiStatusDetail = $"{r.CapabilitySummary}\n耗时 {r.LatencyMs} ms；可以保存配置。";
                Status = $"AI 连接正常 ({r.LatencyMs} ms)";
            }
            else
            {
                AiStatus = "连接异常";
                AiStatusDetail = r.CapabilitySummary + "\n" + (r.Suggestion ?? r.LocalizedMessage ?? "未知错误");
                Status = "AI 连接异常: " + (r.LocalizedMessage ?? "未知错误");
            }
        }
        catch (OperationCanceledException)
        {
            AiStatus = "连接异常";
            AiStatusDetail = _aiUserCancelled ? "测试已取消，输入保留，未保存验证结果。" : "连接测试超时，请检查网络与服务状态。";
            Status = _aiUserCancelled ? "AI 测试已取消" : "AI 连接测试超时";
        }
        catch
        {
            AiStatus = "连接异常";
            AiStatusDetail = "连接测试失败，输入内容已保留；请检查网络、服务地址和本机代理设置。";
            Status = "AI 连接测试失败";
        }
        finally
        {
            if (ReferenceEquals(_aiTestCts, timeout)) _aiTestCts = null;
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
        OnPCFor(nameof(CanVerifyGeneration));
        OnPCFor(nameof(CanSaveAiConfig));
        OnPCFor(nameof(HasUnsavedAiChanges));
        OnPCFor(nameof(HasSelectedAiConfiguration));
        OnPCFor(nameof(TestButtonLabel));
        OnPCFor(nameof(AiActionHint));
        (TestConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveAiConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NewAiConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private static AiConfigurationProfile? FindAiProfile(AiSettings settings, string? profileId) =>
        string.IsNullOrWhiteSpace(profileId)
            ? null
            : settings.Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, profileId, StringComparison.Ordinal));

    private static void CopyProfileToActiveAiSettings(AiConfigurationProfile profile, AiSettings settings)
    {
        settings.ProviderId = profile.ProviderId;
        settings.Endpoint = profile.Endpoint;
        settings.Model = profile.Model;
        settings.SecretTargetName = profile.SecretTargetName;
        settings.LastStatus = profile.LastStatus;
        settings.LastVerifiedAt = profile.LastVerifiedAt;
    }

    private static void ResetActiveAiSettings(AiSettings settings)
    {
        settings.ActiveProfileId = null;
        settings.ProviderId = "deepseek";
        settings.Endpoint = null;
        settings.Model = null;
        settings.SecretTargetName = string.Empty;
        settings.LastStatus = "Untested";
        settings.LastVerifiedAt = null;
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
        Dictionary<Guid, int> progress;
        lock (_rangeIndexGate) progress = new Dictionary<Guid, int>(_rangeIndexProgress);
        Ranges.Clear();
        var s = _search!.ListRanges();
        foreach (var r in s)
        {
            if (!savedPaths.Contains(r.Path)) continue;
            Ranges.Add(new SearchRangeRowViewModel(
                SearchRangeSummary.From(r, _search!.CountItems(r.Id)),
                progress.GetValueOrDefault(r.Id)));
        }
        OnPCFor(nameof(HasRanges));
        OnPCFor(nameof(ShowSearchOnboarding));
        RefreshSearchScopes();
        OnPCFor(nameof(ContentSearchStatus));
        RaiseContentSearchCommandStates();
        RaiseRangeCommandStates();
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
        // the effective query even though the selection setter is not called.
        // Re-run the current non-empty query so results never belong to the
        // previous scope.
        if (selectionChanged) ClearSelectedResult();
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
        _searchOnboardingCompleted = s.Search.OnboardingCompleted;
        if (s.Search.Ranges.Count == 0)
            Status = ShowSearchOnboarding
                ? "请选择允许搜索的常用文件夹；确认前不会读取其中内容。"
                : "尚未授权文件夹；仍可搜索已发现的应用。";
        var indexedRanges = _search!.ListRanges();
        var indexedPaths = indexedRanges.Select(range => range.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in s.Search.Ranges)
        {
            if (!indexedPaths.Contains(path)) _search.AddRange(path);
            var added = _search.ListRanges().First(range =>
                string.Equals(range.Path, path, StringComparison.OrdinalIgnoreCase));
            if (!System.IO.Directory.Exists(path))
            {
                _search.MarkState(added.Id, SearchRangeState.PathUnavailable, "path_unavailable");
                continue;
            }
            if (added.State == SearchRangeState.NotConfigured)
                _ = StartIndexRangeAsync(added.Id);
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
            if (added is not null) _ = StartIndexRangeAsync(added.Id);
            RefreshRanges();
            Status = "搜索范围已添加，正在建立索引。";
        }
        catch (UnauthorizedAccessException) { Status = "无法添加该文件夹，请检查访问权限。"; }
        catch (System.IO.IOException) { Status = "搜索范围保存失败，已保留原有数据，请重试。"; }
        catch { Status = "添加搜索范围失败，请重试。"; }
    }

    private void RemoveRange(Guid id)
    {
        try
        {
            CancelRange(id, announce: false);
            var s = _settings!.Load();
            var r = _search!.GetRange(id);
            if (r is null) return;
            s.Search.Ranges.RemoveAll(p => string.Equals(p, r.Path, StringComparison.OrdinalIgnoreCase));
            _settings!.Save(s);
            _search!.RemoveRange(id);
            RefreshRanges();
            Status = "已移除搜索范围及其索引。";
        }
        catch (UnauthorizedAccessException) { Status = "无法移除该范围，请检查当前账户权限。"; }
        catch (System.IO.IOException) { Status = "范围移除失败，原有索引仍保留，请重试。"; }
        catch { Status = "范围移除失败，请重试。"; }
    }

    public async Task ConfirmSearchOnboardingAsync()
    {
        if (_settings is null || _search is null) return;
        var selectedPaths = SearchOnboardingCandidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedPaths.Count == 0)
        {
            Status = "请至少选择一个文件夹，或选择“稍后设置”。";
            return;
        }

        var availablePaths = selectedPaths.Where(System.IO.Directory.Exists).ToList();
        var unavailableCount = selectedPaths.Count - availablePaths.Count;
        if (availablePaths.Count == 0)
        {
            Status = "候选文件夹当前不可用；你可以稍后手动添加其他文件夹。";
            return;
        }

        var settings = _settings.Load();
        foreach (var path in availablePaths)
        {
            if (!settings.Search.Ranges.Contains(path, StringComparer.OrdinalIgnoreCase))
                settings.Search.Ranges.Add(path);
        }
        settings.Search.OnboardingCompleted = true;
        _settings.Save(settings);
        _searchOnboardingCompleted = true;
        OnPCFor(nameof(ShowSearchOnboarding));

        var tasks = new List<Task>();
        foreach (var path in availablePaths)
        {
            _search.AddRange(path);
            var range = _search.ListRanges().First(item =>
                string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
            tasks.Add(StartIndexRangeAsync(range.Id));
        }
        RefreshRanges();
        Status = unavailableCount == 0
            ? "已授权所选文件夹，正在建立可取消的索引。"
            : $"已授权 {availablePaths.Count} 个文件夹；{unavailableCount} 个候选当前不可用。";
        await Task.WhenAll(tasks);
    }

    private void DeferSearchOnboarding()
    {
        if (_settings is null) return;
        var settings = _settings.Load();
        settings.Search.OnboardingCompleted = true;
        _settings.Save(settings);
        _searchOnboardingCompleted = true;
        OnPCFor(nameof(ShowSearchOnboarding));
        (DeferSearchOnboardingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        Status = "已稍后设置；当前只搜索应用，可随时在设置中添加文件夹。";
    }

    private async Task StartIndexRangeAsync(Guid id)
    {
        if (_search is null) return;
        Task task;
        var created = false;
        lock (_rangeIndexGate)
        {
            if (_rangeIndexTasks.TryGetValue(id, out var running) && !running.IsCompleted)
            {
                task = running;
            }
            else
            {
                var cancellation = new CancellationTokenSource();
                _rangeIndexCancellations[id] = cancellation;
                _rangeIndexProgress[id] = 0;
                task = IndexRangeCoreAsync(id, cancellation);
                _rangeIndexTasks[id] = task;
                created = true;
            }
        }
        if (created) RefreshRanges();
        await task;
    }

    private async Task IndexRangeCoreAsync(Guid id, CancellationTokenSource cancellation)
    {
        try
        {
            var progress = new Progress<int>(count =>
            {
                lock (_rangeIndexGate) _rangeIndexProgress[id] = count;
                Ranges.FirstOrDefault(range => range.Id == id)?.UpdateProgress(count);
            });
            await _search!.IndexRangeAsync(id, progress, ct: cancellation.Token);
            Status = "搜索范围已可用。";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消建立索引；上一份可用结果仍保留。";
        }
        catch (System.IO.DirectoryNotFoundException)
        {
            Status = "文件夹路径已失效；请重新连接后重试或移除范围。";
        }
        catch (UnauthorizedAccessException)
        {
            Status = "当前账户无法读取该文件夹；请检查权限后重试。";
        }
        catch (System.IO.IOException)
        {
            Status = "索引暂时无法写入；上一份可用结果仍保留。";
        }
        catch
        {
            Status = "建立索引失败；上一份可用结果仍保留，请重试。";
        }
        finally
        {
            lock (_rangeIndexGate)
            {
                _rangeIndexCancellations.Remove(id);
                _rangeIndexTasks.Remove(id);
            }
            cancellation.Dispose();
            RefreshRanges();
        }
    }

    private bool CanCancelRange(Guid id)
    {
        lock (_rangeIndexGate)
            return _rangeIndexCancellations.TryGetValue(id, out var cancellation)
                && !cancellation.IsCancellationRequested;
    }

    private bool CanRetryRange(Guid id)
    {
        if (_search?.GetRange(id) is not { } range) return false;
        return range.State != SearchRangeState.Preparing && !CanCancelRange(id);
    }

    private void CancelRange(Guid id, bool announce = true)
    {
        lock (_rangeIndexGate)
        {
            if (_rangeIndexCancellations.TryGetValue(id, out var cancellation))
                cancellation.Cancel();
        }
        if (announce) Status = "正在取消索引…";
        RaiseRangeCommandStates();
    }

    private void RaiseRangeCommandStates()
    {
        (RetryRangeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CancelRangeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ConfirmSearchOnboardingCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeferSearchOnboardingCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void InitializeSearchOnboardingCandidates()
    {
        foreach (var existing in SearchOnboardingCandidates)
            existing.PropertyChanged -= OnSearchOnboardingCandidateChanged;
        SearchOnboardingCandidates.Clear();

        var candidates = _searchOnboardingCandidateSource ?? new[]
        {
            new SearchRangeCandidateOption("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new SearchRangeCandidateOption("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new SearchRangeCandidateOption("下载", System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads")),
        };
        foreach (var candidate in candidates
                     .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Path))
                     .DistinctBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SearchRangeCandidateViewModel(candidate.DisplayName, candidate.Path, isSelected: true);
            row.PropertyChanged += OnSearchOnboardingCandidateChanged;
            SearchOnboardingCandidates.Add(row);
        }
        OnPCFor(nameof(HasSelectedSearchOnboardingCandidates));
        OnPCFor(nameof(ShowSearchOnboarding));
        RaiseRangeCommandStates();
    }

    private void OnSearchOnboardingCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SearchRangeCandidateViewModel.IsSelected)) return;
        OnPCFor(nameof(HasSelectedSearchOnboardingCandidates));
        RaiseRangeCommandStates();
    }

    public void CancelBackgroundWork()
    {
        _searchCts?.Cancel();
        _aiTestCts?.Cancel();
        CancelUpdateWork();
        Todo.CancelBackgroundWork();
        lock (_rangeIndexGate)
            foreach (var cancellation in _rangeIndexCancellations.Values)
                cancellation.Cancel();
    }

    public async Task WaitForBackgroundWorkAsync(TimeSpan timeout)
    {
        var tasks = new List<Task>();
        lock (_rangeIndexGate) tasks.AddRange(_rangeIndexTasks.Values);
        if (_aiTestTask is { IsCompleted: false } aiTestTask) tasks.Add(aiTestTask);
        AddUpdateBackgroundTasks(tasks);
        tasks.Add(Todo.WaitForBackgroundWorkAsync(timeout));
        if (tasks.Count == 0) return;
        await Task.WhenAny(Task.WhenAll(tasks), Task.Delay(timeout));
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
    public event Action<AppearanceSettings>? AppearanceChanged;
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
        InitializeSearchOnboardingCandidates();
        RefreshSearchScopes();
    }

    public event EventHandler? MaintenanceExitRequested;
    public DataMaintenanceService Maintenance
    {
        get
        {
            var appDataDirectory = _settings?.AppDataDir
                ?? throw new InvalidOperationException("应用服务尚未就绪。");
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = string.IsNullOrWhiteSpace(localApplicationData)
                ? Path.Combine(appDataDirectory, "logs")
                : Path.GetDirectoryName(ApplicationDataPaths.GetDiagnosticLogPath(localApplicationData));
            return new DataMaintenanceService(appDataDirectory, logDirectory);
        }
    }

    public string BackupLocalData(string destination, DataModule modules = DataModule.AllNonSecret & ~DataModule.SearchIndex)
    {
        if (_settings is null) throw new InvalidOperationException("应用服务尚未就绪。");
        var path = Maintenance.Backup(destination, modules);
        SetMaintenanceStatus("本地数据备份完成（不含 API Key）。");
        return path;
    }

    public DataMaintenanceResult RestoreLocalData(string source, DataModule modules = DataModule.Settings | DataModule.Layout | DataModule.Todos | DataModule.Shortcuts | DataModule.IconCache)
    {
        if (_settings is null) throw new InvalidOperationException("应用服务尚未就绪。");
        Maintenance.QueueRestore(source, modules);
        SetMaintenanceStatus("备份已验证；退出后请重新启动应用完成恢复。");
        MaintenanceExitRequested?.Invoke(this, EventArgs.Empty);
        return new(Array.Empty<DataModule>(), Array.Empty<string>());
    }

    public DataMaintenanceResult ResetLocalCaches()
    {
        if (_settings is null) throw new InvalidOperationException("应用服务尚未就绪。");
        _shortcuts?.CleanupUnreferencedIcons();
        Maintenance.QueueReset(DataModule.SearchIndex | DataModule.Logs);
        SetMaintenanceStatus("无引用图标已清理；重启后重建索引并清理日志。");
        MaintenanceExitRequested?.Invoke(this, EventArgs.Empty);
        return new(Array.Empty<DataModule>(), Array.Empty<string>());
    }

    public void ExportDiagnostics(string destination)
    {
        if (_settings is null) throw new InvalidOperationException("应用服务尚未就绪。");
        var loaded = _settings.Load();
        DiagnosticExporter.Export(destination, new DiagnosticSnapshot(
            typeof(HomeViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            Environment.OSVersion.VersionString,
            Environment.Version.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ThemeManager.Resolve(loaded.Appearance.Theme),
            Ranges.Count,
            Shortcuts.Count,
            Todo.TotalPendingCount,
            loaded.Ai.ProviderId,
            loaded.Ai.LastStatus,
            Ranges.Where(range => !string.IsNullOrWhiteSpace(range.LastError)).Select(range => range.LastError!).Distinct().ToArray()));
        SetMaintenanceStatus("已导出白名单诊断；不含 Key、查询词、完整路径、待办内容或 AI 原文。");
    }

    internal void ReportMaintenanceFailure(string stableMessage) => SetMaintenanceStatus(stableMessage);

    private void SetMaintenanceStatus(string message)
    {
        MaintenanceStatus = message;
        Status = message;
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
        ITodoAiClient? todoAiClient = null,
        Func<DateTimeOffset>? todoNow = null)
    {
        _search = search;
        _shortcuts = shortcuts;
        _ai = ai;
        _settings = settings;
        LoadCustomProviderPresets();
        _secretStore = secretStore ?? new WindowsAiSecretStore();
        var loaded = settings.Load();
        LoadEssentialSettings(loaded);
        _selectedCharacter = loaded.Pet.PreferredCharacter;
        _enableWildcardSearch = loaded.Search.EnableWildcardSearch;
        _enableRegexSearch = loaded.Search.EnableRegexSearch;
        _enableContentSearch = loaded.Features.EnableContentSearch;
        _search.SetContentSearchEnabled(_enableContentSearch);
        _selectedSearchField = SearchFields.First(field => field.Id == (loaded.Search.QueryField == "path" ? "path" : "name"));
        _useRecentSearchHistory = loaded.Search.UseRecentHistory;
        OnPCFor(nameof(SelectedSearchField)); OnPCFor(nameof(SearchHistoryLabel));
        _themePreference = loaded.Appearance.Theme;
        _enablePetRoaming = loaded.Appearance.EnablePetRoaming;
        _enableBubbleAnimation = loaded.Appearance.EnableBubbleAnimation;
        _enableFollowMotion = loaded.Appearance.EnableFollowMotion;
        _selectedSearchScopeId = loaded.Search.LastScopeId;
        _searchOnboardingCompleted = loaded.Search.OnboardingCompleted;
        OnPCFor(nameof(SelectedCharacter));
        OnPCFor(nameof(EnableWildcardSearch));
        OnPCFor(nameof(EnableRegexSearch));
        OnPCFor(nameof(EnableContentSearch));
        OnPCFor(nameof(ContentSearchStatus));
        OnPCFor(nameof(SearchPrivacyNotice));
        OnPCFor(nameof(ThemePreference));
        OnPCFor(nameof(SelectedTheme));
        OnPCFor(nameof(EnablePetRoaming));
        OnPCFor(nameof(EnableBubbleAnimation));
        OnPCFor(nameof(EnableFollowMotion));
        InitializeSearchOnboardingCandidates();
        ReloadShortcuts();
        ReloadRanges();
        ReloadAi();
        ReloadAutostart();
        RaiseCommandStates();
        if (todoStore is not null && todoAiClient is not null)
            Todo.Attach(todoStore, todoAiClient, CreateTodoAiConnection, todoNow);
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
public sealed record AiConfigurationOption(string Id, string DisplayName);
public sealed record SearchRangeCandidateOption(string DisplayName, string Path);
public sealed record AppearanceOption(string Id, string DisplayName);
public sealed record SearchScopeOption(string Id, string DisplayName, Guid? RangeId)
{
    // Visible selection controls and UI Automation may use ToString as the item
    // name. Keep it concise while preserving the full option as the object.
    public override string ToString() => DisplayName;
}

public sealed class SearchRangeRowViewModel : INotifyPropertyChanged
{
    private int _progressItems;

    public SearchRangeRowViewModel(SearchRangeSummary summary, int progressItems)
    {
        Id = summary.Id;
        Path = summary.Path;
        State = summary.State;
        Items = summary.Items;
        LastError = summary.LastError;
        LastIndexedAt = summary.LastIndexedAt;
        _progressItems = progressItems;
    }

    public Guid Id { get; }
    public string Path { get; }
    public SearchRangeState State { get; }
    public int Items { get; }
    public string? LastError { get; }
    public DateTimeOffset? LastIndexedAt { get; }
    public bool IsPreparing => State == SearchRangeState.Preparing;
    public bool CanRetry => !IsPreparing;
    public string PrimaryActionLabel => State == SearchRangeState.Ready ? "重新索引" : "重试";
    public string StatusText => State switch
    {
        SearchRangeState.NotConfigured => "等待建立索引",
        SearchRangeState.Preparing => $"准备中 · 已发现 {_progressItems:N0} 项",
        SearchRangeState.Ready => $"可用 · {Items:N0} 项",
        SearchRangeState.Cancelled => $"已取消 · 保留上次 {Items:N0} 项",
        SearchRangeState.PathUnavailable => $"路径失效 · 保留上次 {Items:N0} 项",
        SearchRangeState.Failed when LastError == "access_denied" => $"无读取权限 · 保留上次 {Items:N0} 项",
        SearchRangeState.Failed when LastError == "io_error" => $"索引写入失败 · 保留上次 {Items:N0} 项",
        SearchRangeState.Failed => $"建立索引失败 · 保留上次 {Items:N0} 项",
        _ => "状态未知",
    };
    public string LastUpdatedText => LastIndexedAt is null
        ? "尚无成功索引"
        : $"上次成功：{LastIndexedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";

    public void UpdateProgress(int count)
    {
        if (_progressItems == count) return;
        _progressItems = count;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SearchRangeCandidateViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public SearchRangeCandidateViewModel(string displayName, string path, bool isSelected)
    {
        DisplayName = displayName;
        Path = path;
        _isSelected = isSelected;
    }

    public string DisplayName { get; }
    public string Path { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
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
