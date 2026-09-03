using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiPet.AI;
using AiPet.Search;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class HomeViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aipet-vm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SearchService _search;

    public HomeViewModelTests()
    {
        Directory.CreateDirectory(_root);
        _search = new SearchService(Path.Combine(_root, "index.db"), appProvider: () => []);
    }

    public void Dispose()
    {
        _search.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Character_selection_applies_immediately_and_is_persisted()
    {
        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "shortcuts")),
            new OpenAiCompatibleClient(),
            settings);
        string? applied = null;
        vm.CharacterChanged += value => applied = value;

        vm.SelectedCharacter = "monster";

        Assert.Equal("monster", applied);
        Assert.Equal("monster", settings.Load().Pet.PreferredCharacter);
    }

    [Fact]
    public void Built_in_provider_selection_replaces_endpoint_and_model_with_preset()
    {
        var vm = CreateViewModel();
        vm.Endpoint = "https://example.invalid";
        vm.Model = "old-model";

        vm.Provider = "qwen";

        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", vm.Endpoint);
        Assert.Equal("qwen-plus", vm.Model);
    }

    [Fact]
    public void Default_provider_populates_empty_configuration_on_first_load()
    {
        var vm = CreateViewModel();

        Assert.Equal("https://api.deepseek.com", vm.Endpoint);
        Assert.Equal("deepseek-chat", vm.Model);
    }

    [Fact]
    public async Task Attach_keeps_bound_commands_and_refreshes_save_state_after_ai_test()
    {
        var settings = new SettingsStore(Path.Combine(_root, "attached-ai-settings"));
        var shortcuts = new ShortcutStore(Path.Combine(_root, "attached-ai-shortcuts"));
        var secrets = new FakeAiSecretStore();
        var vm = new HomeViewModel();
        var boundTestCommand = vm.TestConnectionCommand;
        var boundSaveCommand = vm.SaveAiConfigCommand;

        vm.Attach(
            _search,
            shortcuts,
            new FakeAiClient(AiConnectionResult.Connected(19)),
            settings,
            secrets);

        Assert.Same(boundTestCommand, vm.TestConnectionCommand);
        Assert.Same(boundSaveCommand, vm.SaveAiConfigCommand);

        vm.ApiKey = "attached-test-key";
        boundTestCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);

        Assert.True(boundSaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task Ai_configuration_must_pass_test_before_save_and_edits_invalidate_success()
    {
        var ai = new FakeAiClient(AiConnectionResult.Connected(27));
        var vm = CreateViewModel(ai, new FakeAiSecretStore());
        vm.ApiKey = "test-key-123456";

        Assert.True(vm.TestConnectionCommand.CanExecute(null));
        Assert.False(vm.SaveAiConfigCommand.CanExecute(null));

        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);

        Assert.True(vm.SaveAiConfigCommand.CanExecute(null));
        vm.Model = "deepseek-reasoner";

        Assert.False(vm.CanSaveAiConfig);
        Assert.False(vm.SaveAiConfigCommand.CanExecute(null));
        Assert.Equal("deepseek-reasoner", vm.Model);
        Assert.Equal("待测试", vm.AiStatus);
    }

    [Fact]
    public async Task Failed_ai_test_preserves_input_and_does_not_unlock_or_persist_save()
    {
        var settings = new SettingsStore(Path.Combine(_root, "failed-ai-settings"));
        var ai = new FakeAiClient(AiConnectionResult.Failed(
            AiErrorCategory.AuthFailed,
            "API Key 无效",
            "请核对后重试"));
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "failed-ai-shortcuts")),
            ai,
            settings,
            new FakeAiSecretStore());
        vm.Endpoint = "https://api.example.test/v1";
        vm.Model = "test-model";
        vm.ApiKey = "invalid-key-kept";

        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsTestingAi && vm.AiStatus == "连接异常");

        Assert.Equal("https://api.example.test/v1", vm.Endpoint);
        Assert.Equal("test-model", vm.Model);
        Assert.Equal("invalid-key-kept", vm.ApiKey);
        Assert.False(vm.CanSaveAiConfig);
        Assert.False(vm.SaveAiConfigCommand.CanExecute(null));
        Assert.Equal("Untested", settings.Load().Ai.LastStatus);
        Assert.Null(settings.Load().Ai.Endpoint);
    }

    [Fact]
    public async Task Successful_ai_test_enables_one_save_and_persists_exact_configuration()
    {
        var settings = new SettingsStore(Path.Combine(_root, "saved-ai-settings"));
        var secrets = new FakeAiSecretStore();
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "saved-ai-shortcuts")),
            new FakeAiClient(AiConnectionResult.Connected(31)),
            settings,
            secrets);
        vm.Provider = "custom";
        vm.Endpoint = "https://ai.example.test/v1";
        vm.Model = "pet-chat";
        vm.ApiKey = "secret-key-123456";

        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);
        vm.SaveAiConfigCommand.Execute(null);

        var saved = settings.Load();
        Assert.Equal("custom", saved.Ai.ProviderId);
        Assert.Equal("https://ai.example.test/v1", saved.Ai.Endpoint);
        Assert.Equal("pet-chat", saved.Ai.Model);
        Assert.Equal("Connected", saved.Ai.LastStatus);
        Assert.NotNull(saved.Ai.LastVerifiedAt);
        Assert.Equal("secret-key-123456", secrets.Load(saved.Ai.SecretTargetName));
        Assert.False(vm.CanSaveAiConfig);
        Assert.False(vm.SaveAiConfigCommand.CanExecute(null));
        Assert.Equal("已保存", vm.AiStatus);
    }

    [Fact]
    public async Task Retesting_loaded_ai_profile_unlocks_save_and_keeps_profile_selectable()
    {
        var settings = new SettingsStore(Path.Combine(_root, "retest-saved-ai-settings"));
        var secrets = new FakeAiSecretStore();
        var first = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "retest-saved-ai-shortcuts-1")),
            new FakeAiClient(AiConnectionResult.Connected(11)),
            settings,
            secrets);
        first.AiConfigurationName = "工作 AI";
        first.Provider = "custom";
        first.Endpoint = "https://work.example.test/v1";
        first.Model = "work-model";
        first.ApiKey = "work-secret";
        first.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => first.CanSaveAiConfig);
        first.SaveAiConfigCommand.Execute(null);

        var second = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "retest-saved-ai-shortcuts-2")),
            new FakeAiClient(AiConnectionResult.Connected(13)),
            settings,
            secrets);

        Assert.Single(second.SavedAiConfigurations);
        Assert.Equal("工作 AI", second.SelectedAiConfiguration?.DisplayName);
        Assert.False(second.CanSaveAiConfig);

        second.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => second.CanSaveAiConfig);

        Assert.True(second.SaveAiConfigCommand.CanExecute(null));
        second.SaveAiConfigCommand.Execute(null);
        Assert.Single(settings.Load().Ai.Profiles);
        Assert.Equal("工作 AI", settings.Load().Ai.Profiles[0].DisplayName);
    }

    [Fact]
    public async Task Multiple_ai_profiles_persist_and_switch_active_connection()
    {
        var settings = new SettingsStore(Path.Combine(_root, "multiple-ai-settings"));
        var secrets = new FakeAiSecretStore();
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "multiple-ai-shortcuts")),
            new FakeAiClient(AiConnectionResult.Connected(9)),
            settings,
            secrets);

        vm.AiConfigurationName = "DeepSeek 工作";
        vm.ApiKey = "deepseek-secret";
        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);
        vm.SaveAiConfigCommand.Execute(null);

        vm.NewAiConfigCommand.Execute(null);
        vm.AiConfigurationName = "Qwen 个人";
        vm.Provider = "qwen";
        vm.ApiKey = "qwen-secret";
        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);
        vm.SaveAiConfigCommand.Execute(null);

        var saved = settings.Load();
        Assert.Equal(2, saved.Ai.Profiles.Count);
        Assert.Equal(saved.Ai.Profiles[1].Id, saved.Ai.ActiveProfileId);
        Assert.Equal("qwen", saved.Ai.ProviderId);

        vm.SelectedAiConfigurationId = saved.Ai.Profiles[0].Id;

        Assert.Equal("deepseek", vm.Provider);
        Assert.Equal("DeepSeek 工作", vm.AiConfigurationName);
        Assert.Equal("deepseek-secret", vm.ApiKey);
        Assert.Equal(saved.Ai.Profiles[0].Id, settings.Load().Ai.ActiveProfileId);
    }

    [Fact]
    public async Task Deleting_ai_profile_removes_credential_non_sensitive_state_and_memory_key()
    {
        var settings = new SettingsStore(Path.Combine(_root, "delete-ai-settings"));
        var secrets = new FakeAiSecretStore();
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "delete-ai-shortcuts")),
            new FakeAiClient(AiConnectionResult.Connected(8)),
            settings,
            secrets);
        vm.AiConfigurationName = "待删除配置";
        vm.ApiKey = "delete-me-secret";
        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);
        vm.SaveAiConfigCommand.Execute(null);
        var targetName = Assert.Single(settings.Load().Ai.Profiles).SecretTargetName;

        Assert.True(vm.DeleteSelectedAiConfiguration());

        var cleared = settings.Load();
        Assert.Empty(cleared.Ai.Profiles);
        Assert.Null(cleared.Ai.ActiveProfileId);
        Assert.Equal("Untested", cleared.Ai.LastStatus);
        Assert.Null(cleared.Ai.Endpoint);
        Assert.Null(secrets.Load(targetName));
        Assert.Empty(vm.ApiKey);
        Assert.False(vm.HasSelectedAiConfiguration);
    }

    [Fact]
    public async Task Duplicate_ai_profile_name_is_rejected_without_overwriting_saved_profile()
    {
        var settings = new SettingsStore(Path.Combine(_root, "duplicate-ai-settings"));
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "duplicate-ai-shortcuts")),
            new FakeAiClient(AiConnectionResult.Connected(8)),
            settings,
            new FakeAiSecretStore());
        vm.AiConfigurationName = "工作配置";
        vm.ApiKey = "first-secret";
        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);
        vm.SaveAiConfigCommand.Execute(null);

        vm.NewAiConfigCommand.Execute(null);
        vm.AiConfigurationName = "  工作配置  ";
        vm.ApiKey = "second-secret";
        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => vm.CanSaveAiConfig);
        vm.SaveAiConfigCommand.Execute(null);

        Assert.Single(settings.Load().Ai.Profiles);
        Assert.Equal("无法保存", vm.AiStatus);
        Assert.True(vm.HasUnsavedAiChanges);
    }

    [Fact]
    public async Task Unexpected_ai_exception_is_standardized_without_exposing_raw_message()
    {
        var vm = CreateViewModel(new ThrowingAiClient(), new FakeAiSecretStore());
        vm.ApiKey = "secret-not-logged";

        vm.TestConnectionCommand.Execute(null);
        await WaitUntilAsync(() => !vm.IsTestingAi && vm.AiStatus == "连接异常");

        Assert.DoesNotContain("sensitive-response-body", vm.AiStatusDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-not-logged", vm.AiStatusDetail, StringComparison.Ordinal);
        Assert.Contains("输入内容已保留", vm.AiStatusDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Category_dropdown_immediately_requeries_with_kind_filter()
    {
        var folder = Path.Combine(_root, "search-items");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "sample-note.txt"), "text");
        File.WriteAllBytes(Path.Combine(folder, "sample-image.png"), [0x89, 0x50]);
        _search.AddRange(folder);
        var range = Assert.Single(_search.ListRanges());
        await _search.IndexRangeAsync(range.Id);
        var vm = CreateViewModel();
        vm.Query = "sample";
        await WaitUntilAsync(() => vm.Results.Count == 2);

        vm.Category = "图片";

        await WaitUntilAsync(() => vm.Results.Count == 1);
        Assert.All(vm.Results, row => Assert.Equal(SearchItemKind.Image, row.Kind));
        Assert.Equal("命中 1 条", vm.Status);
    }

    [Fact]
    public async Task First_run_search_candidates_do_not_scan_until_user_confirms()
    {
        var candidateRoot = Path.Combine(_root, "onboarding-candidate");
        Directory.CreateDirectory(candidateRoot);
        File.WriteAllText(Path.Combine(candidateRoot, "confirmed-only.txt"), "fixture");
        var scannerCalls = 0;
        IEnumerable<SearchItemRow> Scan(SearchRange range)
        {
            Interlocked.Increment(ref scannerCalls);
            foreach (var file in Directory.EnumerateFiles(range.Path))
            {
                var info = new FileInfo(file);
                yield return new SearchItemRow(
                    range.Id,
                    info.Name,
                    info.FullName,
                    info.Name,
                    info.Extension,
                    SearchItemKind.Document,
                    info.Length,
                    info.LastWriteTimeUtc);
            }
        }

        using var search = new SearchService(
            Path.Combine(_root, "onboarding-index.db"),
            Scan,
            appProvider: () => []);
        var settings = new SettingsStore(Path.Combine(_root, "onboarding-settings"));
        var vm = new HomeViewModel(
            search,
            new ShortcutStore(Path.Combine(_root, "onboarding-shortcuts")),
            new OpenAiCompatibleClient(),
            settings,
            searchOnboardingCandidates:
            [
                new SearchRangeCandidateOption("测试目录", candidateRoot),
            ]);

        Assert.True(vm.ShowSearchOnboarding);
        Assert.Equal(0, Volatile.Read(ref scannerCalls));
        Assert.Empty(settings.Load().Search.Ranges);

        await vm.ConfirmSearchOnboardingAsync();

        Assert.Equal(1, Volatile.Read(ref scannerCalls));
        Assert.False(vm.ShowSearchOnboarding);
        Assert.Equal(candidateRoot, Assert.Single(settings.Load().Search.Ranges));
        Assert.Equal(SearchRangeState.Ready, Assert.Single(search.ListRanges()).State);
        Assert.Equal("confirmed-only.txt", Assert.Single(search.Search("confirmed-only", null)).Name);
    }

    private HomeViewModel CreateViewModel(
        IAiClient? ai = null,
        IAiSecretStore? secretStore = null) => new(
            _search,
            new ShortcutStore(Path.Combine(_root, "shortcuts-" + Guid.NewGuid().ToString("N"))),
            ai ?? new OpenAiCompatibleClient(),
            new SettingsStore(Path.Combine(_root, "settings-" + Guid.NewGuid().ToString("N"))),
            secretStore);

    private static async Task WaitUntilAsync(System.Func<bool> predicate)
    {
        for (var i = 0; i < 40 && !predicate(); i++)
            await Task.Delay(50);
        Assert.True(predicate());
    }

    private sealed class FakeAiClient(AiConnectionResult result) : IAiClient
    {
        public Task<AiConnectionResult> TestConnectionAsync(
            string endpoint,
            string model,
            string apiKey,
            CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class ThrowingAiClient : IAiClient
    {
        public Task<AiConnectionResult> TestConnectionAsync(
            string endpoint,
            string model,
            string apiKey,
            CancellationToken ct) => throw new IOException("sensitive-response-body");
    }

    private sealed class FakeAiSecretStore : IAiSecretStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values = new();

        public string? Load(string targetName) =>
            _values.TryGetValue(targetName, out var value) ? value : null;

        public void Save(string targetName, string secret) => _values[targetName] = secret;

        public void Delete(string targetName) => _values.Remove(targetName);
    }
}
