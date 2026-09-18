using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task Latest_stable_release_with_required_assets_is_offered_from_official_route()
    {
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.20.0")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal(new Version(0, 20, 0), result.Update?.Version);
        Assert.Equal("windows-ai-desktop-pet-v0.20.0-setup.exe", result.Update?.InstallerAssetName);
        Assert.Equal("GitHub 官方", result.RouteDisplayName);
        Assert.Single(handler.Requests);
        Assert.Equal("api.github.com", handler.Requests[0].Host);
        Assert.All(handler.AuthorizationParameters, Assert.Null);
    }

    [Fact]
    public async Task Same_or_older_release_is_not_offered()
    {
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.19.0", includeAssets: false)));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpToDate, result.State);
        Assert.Null(result.Update);
        Assert.Equal("GitHub 官方", result.RouteDisplayName);
    }

    [Fact]
    public async Task Same_version_draft_is_rejected_instead_of_clearing_an_available_update()
    {
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.19.0", includeAssets: false, draft: true)));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Null(result.Update);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Metadata_falls_back_to_builtin_api_accelerator_after_official_failure()
    {
        var handler = new RouteHandler(request => request.RequestUri!.Host == "api.github.com"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : JsonResponse(ReleaseJson("0.20.0")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.UpdateAvailable, result.State);
        Assert.Equal("智能加速线路", result.RouteDisplayName);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("api.github.com", handler.Requests[0].Host);
        Assert.Equal("gh-proxy.com", handler.Requests[1].Host);
        Assert.Contains("https://api.github.com/", handler.Requests[1].AbsoluteUri, StringComparison.Ordinal);
        Assert.All(handler.AuthorizationParameters, Assert.Null);
    }

    [Fact]
    public async Task Metadata_failure_reports_after_official_and_builtin_routes_are_exhausted()
    {
        var handler = new RouteHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Null(result.Update);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("自动尝试", result.Message, StringComparison.Ordinal);
        Assert.Equal(UpdateCheckFailureKind.NetworkUnavailable, result.FailureKind);
    }

    [Fact]
    public async Task Anonymous_404_is_reported_as_an_unavailable_release_source_not_a_route_failure()
    {
        var handler = new RouteHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Null(result.Update);
        Assert.Equal(UpdateCheckFailureKind.SourceUnavailable, result.FailureKind);
        Assert.Contains("未公开", result.Message, StringComparison.Ordinal);
        Assert.Contains("不是本机代理", result.RouteDisplayName, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Default_update_handler_uses_the_windows_and_environment_proxy_chain()
    {
        var factory = typeof(GitHubReleaseUpdateClient).GetMethod(
            "CreateSystemProxyHandler",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(factory);
        using var handler = Assert.IsType<HttpClientHandler>(factory.Invoke(null, null));

        Assert.True(handler.UseProxy);
        Assert.Same(HttpClient.DefaultProxy, handler.Proxy);
        Assert.True(handler.AllowAutoRedirect);
        Assert.InRange(handler.MaxAutomaticRedirections, 1, 5);
    }

    [Fact]
    public async Task Metadata_digest_is_normalized_and_carried_to_download()
    {
        var digest = new string('a', 64);
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson("0.20.0", digest: "sha256:" + digest)));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(digest, result.Update?.InstallerSha256);
    }

    [Theory]
    [InlineData("https://example.test/mawenshui/windows-ai-desktop-pet/releases/tag/v0.20.0", null, 100L, null)]
    [InlineData(null, "https://example.test/windows-ai-desktop-pet-v0.20.0-setup.exe", 100L, null)]
    [InlineData(null, null, 0L, null)]
    [InlineData(null, null, 209715201L, null)]
    [InlineData(null, null, 100L, "sha256:not-a-digest")]
    public async Task Unsafe_or_incomplete_release_metadata_is_rejected(
        string? releasePage,
        string? installerUrl,
        long installerSize,
        string? digest)
    {
        var handler = new RouteHandler(_ => JsonResponse(ReleaseJson(
            "0.20.0",
            releasePage: releasePage,
            installerUrl: installerUrl,
            installerSize: installerSize,
            digest: digest)));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task Redirect_to_unknown_host_is_rejected_for_every_metadata_route()
    {
        var handler = new RouteHandler(_ =>
        {
            var response = JsonResponse(ReleaseJson("0.20.0"));
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://downloads.example.test/release.json");
            return response;
        });
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.19.0", CancellationToken.None);

        Assert.Equal(UpdateCheckState.Failed, result.State);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Installer_is_published_only_after_release_checksum_matches()
    {
        var installer = Encoding.UTF8.GetBytes("anonymous installer fixture");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var handler = new RouteHandler(request => IsChecksumRequest(request)
            ? TextResponse($"{hash}  windows-ai-desktop-pet-v0.20.0-setup.exe\n")
            : BytesResponse(installer));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));
        var update = BuildUpdate("0.20.0", hash);

        var result = await client.DownloadInstallerAsync(
            update, _root, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("智能加速线路", result.RouteDisplayName);
        Assert.NotNull(result.InstallerPath);
        Assert.Equal(installer, File.ReadAllBytes(result.InstallerPath!));
        Assert.False(File.Exists(result.InstallerPath + ".download"));
        Assert.Equal("gh-proxy.com", handler.Requests[0].Host);
        Assert.All(handler.AuthorizationParameters, Assert.Null);
    }

    [Fact]
    public async Task Download_falls_back_through_builtin_static_routes_without_user_configuration()
    {
        var installer = Encoding.UTF8.GetBytes("fallback installer fixture");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var handler = new RouteHandler(request => request.RequestUri!.Host == "gh-proxy.com"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : IsChecksumRequest(request)
                ? TextResponse($"{hash}  windows-ai-desktop-pet-v0.20.0-setup.exe\n")
                : BytesResponse(installer));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.DownloadInstallerAsync(
            BuildUpdate("0.20.0", hash), _root, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("gh-proxy.com", handler.Requests[0].Host);
        Assert.Equal("ghfast.top", handler.Requests[1].Host);
        Assert.Equal("ghfast.top", handler.Requests[2].Host);
        Assert.All(handler.AuthorizationParameters, Assert.Null);
    }

    [Fact]
    public async Task Download_reaches_official_route_after_all_builtin_routes_fail()
    {
        var installer = Encoding.UTF8.GetBytes("official fallback fixture");
        var hash = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var handler = new RouteHandler(request => request.RequestUri!.Host != "github.com"
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : IsChecksumRequest(request)
                ? TextResponse($"{hash}  windows-ai-desktop-pet-v0.20.0-setup.exe\n")
                : BytesResponse(installer));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.DownloadInstallerAsync(
            BuildUpdate("0.20.0", hash), _root, null, CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("GitHub 官方", result.RouteDisplayName);
        Assert.Equal(
            ["gh-proxy.com", "ghfast.top", "ghproxy.net", "github.com", "github.com"],
            handler.Requests.Select(uri => uri.Host));
    }

    [Fact]
    public async Task Download_rejects_noncanonical_release_before_network_access()
    {
        var handler = new RouteHandler(_ => throw new InvalidOperationException("network must not be used"));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));
        var update = BuildUpdate("0.20.0") with
        {
            InstallerAssetUrl = new Uri("https://downloads.example.test/setup.exe"),
        };

        var result = await client.DownloadInstallerAsync(update, _root, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Digest_and_checksum_disagreement_stops_every_route_before_installer_download()
    {
        var manifestHash = new string('a', 64);
        var metadataHash = new string('b', 64);
        var handler = new RouteHandler(_ =>
            TextResponse($"{manifestHash}  windows-ai-desktop-pet-v0.20.0-setup.exe\n"));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));

        var result = await client.DownloadInstallerAsync(
            BuildUpdate("0.20.0", metadataHash), _root, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains("SHA256SUMS.txt", request.AbsoluteUri, StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(
            _root, "v0.20.0", "windows-ai-desktop-pet-v0.20.0-setup.exe")));
    }

    [Fact]
    public async Task Checksum_mismatch_removes_partial_download_and_preserves_current_version()
    {
        var handler = new RouteHandler(request => IsChecksumRequest(request)
            ? TextResponse($"{new string('0', 64)}  windows-ai-desktop-pet-v0.20.0-setup.exe\n")
            : BytesResponse(Encoding.UTF8.GetBytes("tampered")));
        var client = new GitHubReleaseUpdateClient(new HttpClient(handler));
        var update = BuildUpdate("0.20.0");

        var result = await client.DownloadInstallerAsync(
            update, _root, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(File.Exists(Path.Combine(
            _root, "v0.20.0", "windows-ai-desktop-pet-v0.20.0-setup.exe")));
        Assert.False(File.Exists(Path.Combine(
            _root, "v0.20.0", "windows-ai-desktop-pet-v0.20.0-setup.exe.download")));
    }

    [Fact]
    public async Task View_model_starts_exactly_one_initial_check_and_persists_only_simple_settings()
    {
        var settings = new SettingsStore(Path.Combine(_root, "vm"));
        settings.Save(new AppSettings
        {
            Updates = new UpdateSettings { AccelerationTemplate = "https://legacy.example.test/{url}" },
        });
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
        Assert.True(vm.HasUnsavedUpdateSettings);
        Assert.Equal("保存更新设置（有修改）", vm.UpdateSettingsSaveLabel);
        vm.SaveUpdateSettingsCommand.Execute(null);
        var loaded = settings.Load();
        Assert.True(loaded.Updates.PeriodicEnabled, vm.UpdateStatus);
        Assert.Equal(6, loaded.Updates.IntervalHours);
        Assert.Equal(string.Empty, loaded.Updates.AccelerationTemplate);
        Assert.False(vm.HasUnsavedUpdateSettings);
        Assert.Equal("已保存", vm.UpdateSettingsSaveLabel);
        vm.CancelBackgroundWork();
    }

    [Fact]
    public async Task View_model_reports_route_and_keeps_available_update_after_failed_check()
    {
        var settings = new SettingsStore(Path.Combine(_root, "recovery-vm"));
        settings.Save(new AppSettings());
        using var search = new SearchService(settings.IndexPath, appProvider: () => []);
        var secrets = new InMemorySecretStore();
        secrets.Save("WindowsAiDesktopPet:Updates:GitHub", "legacy-token-that-must-not-be-read");
        var vm = new HomeViewModel(
            search,
            new ShortcutStore(settings.AppDataDir),
            new OpenAiCompatibleClient(),
            settings,
            secrets);
        var fake = new FakeUpdateClient();
        fake.Results.Enqueue(new UpdateCheckResult(
            UpdateCheckState.UpdateAvailable, BuildUpdate("0.20.0"), "发现新版本。", "智能加速线路"));
        fake.Results.Enqueue(new UpdateCheckResult(
            UpdateCheckState.Failed,
            null,
            "更新源未公开。",
            FailureKind: UpdateCheckFailureKind.SourceUnavailable));
        fake.Results.Enqueue(new UpdateCheckResult(
            UpdateCheckState.UpToDate, null, "当前已是最新版本。", "GitHub 官方"));
        vm.SetUpdateClient(fake);

        await vm.CheckForUpdatesAsync(automatic: false);
        Assert.True(vm.HasAvailableUpdate);
        Assert.False(vm.HasNoAvailableUpdate);
        Assert.Contains("智能加速线路", vm.UpdateRouteStatus, StringComparison.Ordinal);

        await vm.CheckForUpdatesAsync(automatic: false);
        Assert.True(vm.HasAvailableUpdate);
        Assert.Contains("无法匿名访问", vm.UpdateRouteStatus, StringComparison.Ordinal);

        await vm.CheckForUpdatesAsync(automatic: false);
        Assert.False(vm.HasAvailableUpdate);
        Assert.True(vm.HasNoAvailableUpdate);
        Assert.Contains("GitHub 官方", vm.UpdateRouteStatus, StringComparison.Ordinal);
        Assert.Equal("legacy-token-that-must-not-be-read", secrets.Load("WindowsAiDesktopPet:Updates:GitHub"));
        vm.CancelBackgroundWork();
    }

    private static bool IsChecksumRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsoluteUri.Contains("SHA256SUMS.txt", StringComparison.Ordinal);

    private static ReleaseUpdate BuildUpdate(string version, string? digest = null)
    {
        var parsed = Version.Parse(version);
        var tag = "v" + version;
        var root = $"https://github.com/mawenshui/windows-ai-desktop-pet/releases/download/{tag}";
        return new(
            parsed,
            tag,
            new Uri($"https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/{tag}"),
            $"windows-ai-desktop-pet-v{version}-setup.exe",
            new Uri($"{root}/windows-ai-desktop-pet-v{version}-setup.exe"),
            new Uri($"{root}/SHA256SUMS.txt"),
            digest);
    }

    private static string ReleaseJson(
        string version,
        bool includeAssets = true,
        string? releasePage = null,
        string? installerUrl = null,
        long installerSize = 100,
        string? digest = null,
        bool draft = false)
    {
        var tag = "v" + version;
        var downloadRoot = $"https://github.com/mawenshui/windows-ai-desktop-pet/releases/download/{tag}";
        object[] assets = includeAssets
            ?
            [
                new
                {
                    name = $"windows-ai-desktop-pet-v{version}-setup.exe",
                    browser_download_url = installerUrl ?? $"{downloadRoot}/windows-ai-desktop-pet-v{version}-setup.exe",
                    size = installerSize,
                    digest,
                },
                new
                {
                    name = "SHA256SUMS.txt",
                    browser_download_url = $"{downloadRoot}/SHA256SUMS.txt",
                    size = 200L,
                    digest = (string?)null,
                },
            ]
            : [];
        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            html_url = releasePage ?? $"https://github.com/mawenshui/windows-ai-desktop-pet/releases/tag/{tag}",
            draft,
            prerelease = false,
            assets,
        });
    }

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
            var response = route(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
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

        public Task<UpdateCheckResult> CheckAsync(
            string currentVersion,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CheckCount);
            Checked.TrySetResult();
            return Task.FromResult(Results.Count > 0
                ? Results.Dequeue()
                : new UpdateCheckResult(UpdateCheckState.UpToDate, null, "当前已是最新版本。", "GitHub 官方"));
        }

        public Task<UpdateDownloadResult> DownloadInstallerAsync(
            ReleaseUpdate update,
            string destinationRoot,
            IProgress<int>? progress,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
