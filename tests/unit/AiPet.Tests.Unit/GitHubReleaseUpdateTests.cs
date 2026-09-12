using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using AiPet.AI;
using AiPet.Search;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.SystemIntegration;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class GitHubReleaseUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aipet-updates-" + Guid.NewGuid().ToString("N"));

    public GitHubReleaseUpdateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Latest_stable_release_with_required_assets_is_offered()
    {
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.16.0")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.15.0", null, null, CancellationToken.None);

        Assert.True(result.State == UpdateCheckState.UpdateAvailable, result.Message);
        Assert.Equal(new Version(0, 16, 0), result.Update?.Version);
        Assert.Equal("windows-ai-desktop-pet-v0.16.0-setup.exe", result.Update?.InstallerAssetName);
        Assert.Single(handler.Requests);
        Assert.Equal("api.github.com", handler.Requests[0].Host);
    }

    [Fact]
    public async Task Same_or_older_release_is_not_offered()
    {
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.15.0", includeAssets: false)));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.15.0", null, null, CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpToDate, result.State);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Trusted_acceleration_route_is_tried_before_official_github()
    {
        var handler = new RouteHandler(request => request.RequestUri!.Host == "mirror.example.test"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : JsonResponse(ReleaseJson("0.16.0")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync(
            "0.15.0", "https://mirror.example.test/{url}", null, CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("mirror.example.test", handler.Requests[0].Host);
        Assert.Equal("api.github.com", handler.Requests[1].Host);
    }

    [Theory]
    [InlineData("http://mirror.example.test/{url}")]
    [InlineData("https://user:password@mirror.example.test/{url}")]
    [InlineData("https://mirror.example.test/no-placeholder")]
    [InlineData("https://mirror.example.test/{url}/{url}")]
    public async Task Unsafe_acceleration_template_is_rejected_before_network_access(string template)
    {
        var handler = new RouteHandler(_ => throw new InvalidOperationException("network must not be used"));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.15.0", template, null, CancellationToken.None);

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Access_token_uses_only_official_github_and_is_sent_as_bearer()
    {
        const string token = "github_pat_readonly_example_123456";
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.16.0")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync(
            "0.15.0", "http://mirror.example.test/{url}", token, CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Single(handler.Requests);
        Assert.Equal("api.github.com", handler.Requests[0].Host);
        Assert.Equal(token, handler.AuthorizationParameters[0]);
    }

    [Fact]
    public async Task Installer_is_published_only_after_release_checksum_matches()
    {
        var installer = Encoding.UTF8.GetBytes("anonymous installer fixture");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/2", StringComparison.Ordinal)
            ? TextResponse($"{hash}  windows-ai-desktop-pet-v0.16.0-setup.exe\n")
            : BytesResponse(installer));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));
        var update = BuildUpdate("0.16.0");

        var result = await client.DownloadInstallerAsync(
            update, _root, null, null, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.InstallerPath);
        Assert.Equal(installer, File.ReadAllBytes(result.InstallerPath!));
        Assert.False(File.Exists(result.InstallerPath + ".download"));
    }

    [Fact]
    public async Task Checksum_mismatch_removes_partial_download_and_preserves_current_version()
    {
        var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/2", StringComparison.Ordinal)
            ? TextResponse($"{new string('0', 64)}  windows-ai-desktop-pet-v0.16.0-setup.exe\n")
            : BytesResponse(Encoding.UTF8.GetBytes("tampered")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));
        var update = BuildUpdate("0.16.0");

        var result = await client.DownloadInstallerAsync(
            update, _root, null, null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(
            _root, "v0.16.0", "windows-ai-desktop-pet-v0.16.0-setup.exe")));
        Assert.False(File.Exists(Path.Combine(
            _root, "v0.16.0", "windows-ai-desktop-pet-v0.16.0-setup.exe.download")));
    }

    [Fact]
    public async Task View_model_starts_exactly_one_initial_check_and_persists_periodic_settings()
    {
        var settings = new SettingsStore(Path.Combine(_root, "vm"));
        settings.Save(new AppSettings());
        using var search = new SearchService(settings.IndexPath, appProvider: () => []);
        var vm = new HomeViewModel(
            search,
            new ShortcutStore(settings.AppDataDir),
            new OpenAiCompatibleClient(),
            settings,
            new InMemorySecretStore());
        var fake = new FakeUpdateClient();
        vm.SetUpdateClient(fake);

        vm.StartUpdateChecks();
        vm.StartUpdateChecks();
        await fake.Checked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        Assert.Equal(1, fake.CheckCount);
        Assert.False(vm.HasUnsavedUpdateSettings);
        Assert.False(vm.SaveUpdateSettingsCommand.CanExecute(null));
        vm.PeriodicUpdateChecksEnabled = true;
        vm.UpdateIntervalHoursText = "6";
        vm.UpdateAccelerationTemplate = "https://mirror.example.test/{url}";
        Assert.True(vm.HasUnsavedUpdateSettings);
        Assert.Equal("保存更新设置（有修改）", vm.UpdateSettingsSaveLabel);
        Assert.True(vm.SaveUpdateSettingsCommand.CanExecute(null));
        vm.SaveUpdateSettingsCommand.Execute(null);
        var loaded = settings.Load();
        Assert.True(loaded.Updates.PeriodicEnabled, vm.UpdateStatus);
        Assert.Equal(6, loaded.Updates.IntervalHours);
        Assert.Equal("https://mirror.example.test/{url}", loaded.Updates.AccelerationTemplate);
        Assert.False(vm.HasUnsavedUpdateSettings);
        Assert.Equal("已保存", vm.UpdateSettingsSaveLabel);
        Assert.False(vm.SaveUpdateSettingsCommand.CanExecute(null));
        vm.CancelBackgroundWork();
    }

    [Fact]
    public void View_model_stores_update_token_only_in_credential_store()
    {
        var settings = new SettingsStore(Path.Combine(_root, "token-vm"));
        settings.Save(new AppSettings());
        using var search = new SearchService(settings.IndexPath, appProvider: () => []);
        var secrets = new InMemorySecretStore();
        var vm = new HomeViewModel(
            search,
            new ShortcutStore(settings.AppDataDir),
            new OpenAiCompatibleClient(),
            settings,
            secrets);

        vm.UpdateAccessTokenInput = "github_pat_readonly_example_123456";
        vm.SaveUpdateAccessTokenCommand.Execute(null);

        Assert.True(vm.HasStoredUpdateAccessToken);
        Assert.Equal(string.Empty, vm.UpdateAccessTokenInput);
        Assert.Equal("github_pat_readonly_example_123456", secrets.Load("WindowsAiDesktopPet:Updates:GitHub"));
        Assert.DoesNotContain("github_pat", File.ReadAllText(settings.SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task View_model_uses_only_saved_token_and_keeps_available_update_after_failed_check()
    {
        var settings = new SettingsStore(Path.Combine(_root, "recovery-vm"));
        settings.Save(new AppSettings());
        using var search = new SearchService(settings.IndexPath, appProvider: () => []);
        var secrets = new InMemorySecretStore();
        var vm = new HomeViewModel(
            search,
            new ShortcutStore(settings.AppDataDir),
            new OpenAiCompatibleClient(),
            settings,
            secrets);
        var fake = new FakeUpdateClient();
        fake.Results.Enqueue(new UpdateCheckResult(
            UpdateCheckState.UpdateAvailable, BuildUpdate("0.16.1"), "发现新版本。"));
        fake.Results.Enqueue(new UpdateCheckResult(
            UpdateCheckState.Failed, null, "网络暂时不可用。"));
        fake.Results.Enqueue(new UpdateCheckResult(
            UpdateCheckState.UpToDate, null, "当前已是最新版本。"));
        vm.SetUpdateClient(fake);
        vm.UpdateAccessTokenInput = "github_pat_unsaved_example_123456";

        await vm.CheckForUpdatesAsync(automatic: false);
        Assert.Null(fake.AccessTokens[0]);
        Assert.True(vm.HasAvailableUpdate);

        await vm.CheckForUpdatesAsync(automatic: false);
        Assert.Null(fake.AccessTokens[1]);
        Assert.True(vm.HasAvailableUpdate);

        vm.SaveUpdateAccessTokenCommand.Execute(null);
        await vm.CheckForUpdatesAsync(automatic: false);
        Assert.Equal("github_pat_unsaved_example_123456", fake.AccessTokens[2]);
        Assert.False(vm.HasAvailableUpdate);
        vm.CancelBackgroundWork();
    }

    private static ReleaseUpdate BuildUpdate(string version)
    {
        var parsed = Version.Parse(version);
        return new(
            parsed,
            "v" + version,
            new Uri($"https://github.com/example/repo/releases/tag/v{version}"),
            $"windows-ai-desktop-pet-v{version}-setup.exe",
            new Uri("https://api.github.com/repos/example/repo/releases/assets/1"),
            new Uri("https://api.github.com/repos/example/repo/releases/assets/2"));
    }

    private static string ReleaseJson(string version, bool includeAssets = true) => $$"""
        {
          "tag_name": "v{{version}}",
          "html_url": "https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/v{{version}}",
          "draft": false,
          "prerelease": false,
          "assets": {{(includeAssets ? $$"""
            [
              {"name":"windows-ai-desktop-pet-v{{version}}-setup.exe","url":"https://api.github.com/repos/mawenshui/windows-ai-desktop-pet/releases/assets/1"},
              {"name":"SHA256SUMS.txt","url":"https://api.github.com/repos/mawenshui/windows-ai-desktop-pet/releases/assets/2"}
            ]
            """ : "[]")}}
        }
        """;

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TextResponse(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage BytesResponse(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();
        public List<string?> AuthorizationParameters { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(route(request));
        }
    }

    private sealed class InMemorySecretStore : IAiSecretStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public string? Load(string targetName) => _values.GetValueOrDefault(targetName);
        public void Save(string targetName, string secret) => _values[targetName] = secret;
        public void Delete(string targetName) => _values.Remove(targetName);
    }

    private sealed class FakeUpdateClient : IReleaseUpdateClient
    {
        public int CheckCount;
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<UpdateCheckResult> Results { get; } = new();
        public List<string?> AccessTokens { get; } = new();

        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            string? accelerationTemplate,
            string? accessToken,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CheckCount);
            AccessTokens.Add(accessToken);
            Checked.TrySetResult();
            return Task.FromResult(Results.Count > 0
                ? Results.Dequeue()
                : new UpdateCheckResult(UpdateCheckState.UpToDate, null, "当前已是最新版本。"));
        }

        public Task<UpdateDownloadResult> DownloadInstallerAsync(
            ReleaseUpdate update,
            string destinationRoot,
            string? accelerationTemplate,
            string? accessToken,
            IProgress<int>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
