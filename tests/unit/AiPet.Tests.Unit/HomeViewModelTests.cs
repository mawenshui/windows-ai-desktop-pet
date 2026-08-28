using System;
using System.IO;
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

    private sealed class FakeAiSecretStore : IAiSecretStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _values = new();

        public string? Load(string targetName) =>
            _values.TryGetValue(targetName, out var value) ? value : null;

        public void Save(string targetName, string secret) => _values[targetName] = secret;

        public void Delete(string targetName) => _values.Remove(targetName);
    }
}
