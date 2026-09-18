using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AiPet.SystemIntegration;

public enum UpdateCheckState
{
    UpToDate = 0,
    UpdateAvailable = 1,
    Failed = 2,
}

public enum UpdateCheckFailureKind
{
    None = 0,
    NetworkUnavailable = 1,
    SourceUnavailable = 2,
    InvalidMetadata = 3,
}

public sealed record ReleaseUpdate(
    Version Version,
    string TagName,
    Uri ReleasePage,
    string InstallerAssetName,
    Uri InstallerAssetUrl,
    Uri ChecksumAssetUrl,
    string? InstallerSha256 = null);

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    ReleaseUpdate? Update,
    string Message,
    string RouteDisplayName = "",
    UpdateCheckFailureKind FailureKind = UpdateCheckFailureKind.None);

public sealed record UpdateDownloadResult(
    bool Success,
    string? InstallerPath,
    string Message,
    string RouteDisplayName = "");

public interface IReleaseUpdateClient
{
    Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken);

    Task<UpdateDownloadResult> DownloadInstallerAsync(
        ReleaseUpdate update,
        string destinationRoot,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads GitHub Release metadata and downloads the exact installer listed by
/// the release. HttpClient uses the Windows/system proxy by default and falls
/// back through anonymous, built-in GitHub acceleration routes. No credential
/// or user-supplied proxy address is accepted by this client.
/// </summary>
public sealed class GitHubReleaseUpdateClient : IReleaseUpdateClient
{
    public const string RepositoryOwner = "mawenshui";
    public const string RepositoryName = "windows-ai-desktop-pet";
    public const int MaximumMetadataBytes = 1024 * 1024;
    public const int MaximumChecksumBytes = 1024 * 1024;
    public const long MaximumInstallerBytes = 200L * 1024 * 1024;

    private sealed record UpdateRoute(string DisplayName, string? Template)
    {
        public Uri Resolve(Uri official) => Template is null
            ? official
            : new Uri(Template.Replace("{url}", official.AbsoluteUri, StringComparison.Ordinal));
    }

    private sealed record UpdateRouteCandidate(Uri Uri, string DisplayName, bool IsOfficial);
    private sealed record ReleaseAsset(Uri BrowserUrl, long Size, string? Sha256);

    private static readonly UpdateRoute[] MetadataRoutes =
    [
        new("GitHub 官方", null),
        new("智能加速线路", "https://gh-proxy.com/{url}"),
    ];

    private static readonly UpdateRoute[] DownloadRoutes =
    [
        new("智能加速线路", "https://gh-proxy.com/{url}"),
        new("智能加速线路", "https://ghfast.top/{url}"),
        new("智能加速线路", "https://ghproxy.net/{url}"),
        new("GitHub 官方", null),
    ];

    private static readonly Uri LatestReleaseApi = new(
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");

    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateClient(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            return;
        }

        _httpClient = new HttpClient(CreateSystemProxyHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    internal static HttpClientHandler CreateSystemProxyHandler() => new()
    {
        // HttpClient.DefaultProxy observes Windows user proxy settings and
        // the standard HTTP(S)_PROXY environment variables. The same client is
        // used for both release metadata and assets so checking and downloading
        // cannot silently take different proxy paths.
        UseProxy = true,
        Proxy = HttpClient.DefaultProxy,
        AutomaticDecompression = DecompressionMethods.GZip
            | DecompressionMethods.Deflate
            | DecompressionMethods.Brotli,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
    };

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken)
    {
        if (!TryParseStableVersion(currentVersion, out var installed))
            return new(
                UpdateCheckState.Failed,
                null,
                "当前应用版本无效，无法安全比较更新。",
                FailureKind: UpdateCheckFailureKind.InvalidMetadata);

        var failureMessage = "暂时无法检查更新。应用已自动尝试 GitHub 官方和内置加速线路，请稍后重试。";
        var failureKind = UpdateCheckFailureKind.NetworkUnavailable;
        var officialSourceUnavailable = false;
        foreach (var route in BuildCandidateRoutes(LatestReleaseApi, MetadataRoutes))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                var json = await GetBoundedBytesAsync(route.Uri, MaximumMetadataBytes, asset: false, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                return ParseLatestRelease(json, installed) with { RouteDisplayName = route.DisplayName };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                if (route.IsOfficial)
                    officialSourceUnavailable = true;
            }
            catch (InvalidDataException ex)
            {
                failureMessage = $"GitHub Release 元数据不完整或不安全，已停止更新检查（{ex.Message}）";
                failureKind = UpdateCheckFailureKind.InvalidMetadata;
            }
            catch (JsonException)
            {
                failureMessage = "GitHub Release 返回了无法识别的数据，已停止更新检查。";
                failureKind = UpdateCheckFailureKind.InvalidMetadata;
            }
            catch
            {
                // Try the next built-in/direct route. Third-party response details
                // are intentionally not surfaced in UI or logs.
            }
        }

        if (officialSourceUnavailable)
        {
            return new(
                UpdateCheckState.Failed,
                null,
                "更新源未公开或尚无正式 Release，应用无法匿名读取版本信息。请联系发布者检查 GitHub 仓库可见性。",
                "更新源无法匿名访问；这不是本机代理或加速线路故障。",
                UpdateCheckFailureKind.SourceUnavailable);
        }

        return new(
            UpdateCheckState.Failed,
            null,
            failureMessage,
            FailureKind: failureKind);
    }

    public async Task<UpdateDownloadResult> DownloadInstallerAsync(
        ReleaseUpdate update,
        string destinationRoot,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new ArgumentException("下载目录不能为空。", nameof(destinationRoot));
        var expectedInstallerName = $"windows-ai-desktop-pet-v{update.Version.ToString(3)}-setup.exe";
        var expectedTagName = "v" + update.Version.ToString(3);
        if (!string.Equals(update.TagName, expectedTagName, StringComparison.Ordinal)
            || !IsExpectedReleasePage(update.ReleasePage, expectedTagName)
            || !string.Equals(update.InstallerAssetName, expectedInstallerName, StringComparison.Ordinal)
            || !IsExpectedAssetUri(update.InstallerAssetUrl, update.TagName, expectedInstallerName)
            || !IsExpectedAssetUri(update.ChecksumAssetUrl, update.TagName, "SHA256SUMS.txt")
            || !IsSha256OrNull(update.InstallerSha256))
            return new(false, null, "Release 资产地址或文件名无效，已停止下载。");

        var checksumRoutes = BuildCandidateRoutes(update.ChecksumAssetUrl, DownloadRoutes);
        var installerRoutes = BuildCandidateRoutes(update.InstallerAssetUrl, DownloadRoutes);
        var routeCount = Math.Min(checksumRoutes.Count, installerRoutes.Count);
        var versionDirectory = Path.Combine(destinationRoot, "v" + update.Version.ToString(3));
        Directory.CreateDirectory(versionDirectory);
        var destination = Path.Combine(versionDirectory, update.InstallerAssetName);
        var temporary = destination + ".download";

        for (var route = 0; route < routeCount; route++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                var checksumBytes = await GetBoundedBytesAsync(
                    checksumRoutes[route].Uri, MaximumChecksumBytes, asset: true, cancellationToken: timeout.Token).ConfigureAwait(false);
                var expected = ReadExpectedChecksum(
                    Encoding.UTF8.GetString(checksumBytes), update.InstallerAssetName);
                if (update.InstallerSha256 is not null
                    && !string.Equals(expected, update.InstallerSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Release digest and checksum manifest do not match.");

                if (File.Exists(destination)
                    && string.Equals(await HashFileAsync(destination, timeout.Token).ConfigureAwait(false), expected, StringComparison.Ordinal))
                {
                    progress?.Report(100);
                    return new(true, destination, "更新安装器已下载并通过 SHA-256 校验。", installerRoutes[route].DisplayName);
                }

                if (File.Exists(temporary)) File.Delete(temporary);
                await DownloadFileAsync(installerRoutes[route].Uri, temporary, progress, timeout.Token)
                    .ConfigureAwait(false);
                var actual = await HashFileAsync(temporary, timeout.Token).ConfigureAwait(false);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new InvalidDataException("Downloaded installer checksum mismatch.");
                File.Move(temporary, destination, true);
                progress?.Report(100);
                return new(true, destination, "更新安装器已下载并通过 SHA-256 校验。", installerRoutes[route].DisplayName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDelete(temporary);
                throw;
            }
            catch
            {
                TryDelete(temporary);
            }
        }

        return new(false, null, "更新下载失败或校验未通过，现有版本不会改变。");
    }

    internal static UpdateCheckResult ParseLatestRelease(byte[] json, Version installed)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("tag_name", out var tagProperty)
            || tagProperty.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Release metadata is missing tag_name.");
        var tag = tagProperty.GetString() ?? string.Empty;
        if (!TryParseStableVersion(tag, out var releaseVersion))
            throw new InvalidDataException("Release tag is not a stable SemVer.");
        if (!string.Equals(tag, "v" + releaseVersion.ToString(3), StringComparison.Ordinal))
            throw new InvalidDataException("Release tag does not use the required version format.");
        if (ReadBoolean(root, "draft") || ReadBoolean(root, "prerelease"))
            throw new InvalidDataException("Latest release is not stable.");
        if (!TryReadHttpsUri(root, "html_url", out var releasePage)
            || !IsExpectedReleasePage(releasePage!, tag))
            throw new InvalidDataException("Release page URL is invalid.");
        if (releaseVersion <= installed)
            return new(UpdateCheckState.UpToDate, null, $"当前已是最新版本 {installed.ToString(3)}。");

        var installerName = $"windows-ai-desktop-pet-v{releaseVersion.ToString(3)}-setup.exe";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Release metadata is missing assets.");
        var installer = FindAsset(assets, installerName, MaximumInstallerBytes);
        var checksum = FindAsset(assets, "SHA256SUMS.txt", MaximumChecksumBytes);
        if (installer is null || checksum is null
            || !IsExpectedAssetUri(installer.BrowserUrl, tag, installerName)
            || !IsExpectedAssetUri(checksum.BrowserUrl, tag, "SHA256SUMS.txt"))
            throw new InvalidDataException("Release does not contain the required installer and checksum assets.");

        var update = new ReleaseUpdate(
            releaseVersion,
            tag,
            releasePage!,
            installerName,
            installer.BrowserUrl,
            checksum.BrowserUrl,
            installer.Sha256);
        return new(UpdateCheckState.UpdateAvailable, update, $"发现新版本 {releaseVersion.ToString(3)}。");
    }

    private static IReadOnlyList<UpdateRouteCandidate> BuildCandidateRoutes(
        Uri official,
        IReadOnlyList<UpdateRoute> routes)
    {
        var candidates = new List<UpdateRouteCandidate>(routes.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
        {
            var uri = route.Resolve(official);
            if (IsSafeHttpsUri(uri) && seen.Add(uri.AbsoluteUri))
                candidates.Add(new(uri, route.DisplayName, route.Template is null));
        }
        return candidates;
    }

    private async Task<byte[]> GetBoundedBytesAsync(
        Uri uri,
        int maximumBytes,
        bool asset,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri, asset);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && !IsAllowedFinalUri(uri, finalUri))
            throw new InvalidDataException("Response was redirected to an unsafe address.");
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maximumBytes)
            throw new InvalidDataException("Response exceeds size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new InvalidDataException("Response exceeds size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string path,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri, asset: true);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && !IsAllowedFinalUri(uri, finalUri))
            throw new InvalidDataException("Installer was redirected to an unsafe address.");
        var length = response.Content.Headers.ContentLength;
        if (length is > MaximumInstallerBytes) throw new InvalidDataException("Installer exceeds size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumInstallerBytes) throw new InvalidDataException("Installer exceeds size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (length is > 0) progress?.Report((int)Math.Clamp(total * 100 / length.Value, 0, 99));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(Uri uri, bool asset)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WindowsAiDesktopPet", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            asset ? "application/octet-stream" : "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static ReleaseAsset? FindAsset(JsonElement assets, string expectedName, long maximumBytes)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String
                || !string.Equals(name.GetString(), expectedName, StringComparison.Ordinal)
                || !asset.TryGetProperty("size", out var sizeProperty)
                || !sizeProperty.TryGetInt64(out var size)
                || size is <= 0
                || size > maximumBytes
                || !TryReadHttpsUri(asset, "browser_download_url", out var uri))
                continue;

            string? sha256 = null;
            if (asset.TryGetProperty("digest", out var digestProperty)
                && digestProperty.ValueKind is not JsonValueKind.Null)
            {
                if (digestProperty.ValueKind != JsonValueKind.String
                    || !TryParseSha256Digest(digestProperty.GetString(), out sha256))
                    throw new InvalidDataException("Release asset digest is invalid.");
            }
            return new(uri!, size, sha256);
        }
        return null;
    }

    private static bool TryReadHttpsUri(JsonElement element, string propertyName, out Uri? uri)
    {
        uri = null;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Uri.TryCreate(property.GetString(), UriKind.Absolute, out uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo);
    }

    private static bool IsExpectedReleasePage(Uri uri, string tagName) =>
        IsSafeHttpsUri(uri)
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.Equals(
            Uri.UnescapeDataString(uri.AbsolutePath),
            $"/{RepositoryOwner}/{RepositoryName}/releases/tag/{tagName}",
            StringComparison.Ordinal);

    private static bool IsExpectedAssetUri(Uri uri, string tagName, string assetName) =>
        IsSafeHttpsUri(uri)
        && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.Equals(
            Uri.UnescapeDataString(uri.AbsolutePath),
            $"/{RepositoryOwner}/{RepositoryName}/releases/download/{tagName}/{assetName}",
            StringComparison.Ordinal);

    private static bool IsAllowedFinalUri(Uri requested, Uri final) =>
        IsSafeHttpsUri(final)
        && (string.Equals(requested.Host, final.Host, StringComparison.OrdinalIgnoreCase)
            || IsOfficialGitHubDeliveryHost(final.Host));

    private static bool IsOfficialGitHubDeliveryHost(string host) =>
        host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("github-releases.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeHttpsUri(Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool TryParseSha256Digest(string? value, out string? sha256)
    {
        sha256 = null;
        const string prefix = "sha256:";
        if (value is null || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var candidate = value[prefix.Length..].ToLowerInvariant();
        if (candidate.Length != 64 || !candidate.All(Uri.IsHexDigit)) return false;
        sha256 = candidate;
        return true;
    }

    private static bool IsSha256OrNull(string? value) =>
        value is null || (value.Length == 64 && value.All(Uri.IsHexDigit));

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.True;

    private static bool TryParseStableVersion(string value, out Version version)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('v')) normalized = normalized[1..];
        if (normalized.Contains('-', StringComparison.Ordinal)
            || normalized.Contains('+', StringComparison.Ordinal)
            || !Version.TryParse(normalized, out var parsed)
            || parsed.Major < 0 || parsed.Minor < 0 || parsed.Build < 0 || parsed.Revision >= 0)
        {
            version = new Version(0, 0, 0);
            return false;
        }
        version = parsed;
        return true;
    }

    private static string ReadExpectedChecksum(string manifest, string assetName)
    {
        foreach (var line in manifest.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;
            var name = parts[1].TrimStart('*');
            if (!string.Equals(name, assetName, StringComparison.Ordinal)) continue;
            var hash = parts[0].ToLowerInvariant();
            if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash;
        }
        throw new InvalidDataException("Checksum manifest does not contain the installer.");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
