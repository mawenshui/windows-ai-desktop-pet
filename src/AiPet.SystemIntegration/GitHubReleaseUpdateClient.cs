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

public sealed record ReleaseUpdate(
    Version Version,
    string TagName,
    Uri ReleasePage,
    string InstallerAssetName,
    Uri InstallerAssetUrl,
    Uri ChecksumAssetUrl);

public sealed record UpdateCheckResult(
    UpdateCheckState State,
    ReleaseUpdate? Update,
    string Message);

public sealed record UpdateDownloadResult(
    bool Success,
    string? InstallerPath,
    string Message);

public interface IReleaseUpdateClient
{
    Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        string? accelerationTemplate,
        string? accessToken,
        CancellationToken cancellationToken);

    Task<UpdateDownloadResult> DownloadInstallerAsync(
        ReleaseUpdate update,
        string destinationRoot,
        string? accelerationTemplate,
        string? accessToken,
        IProgress<int>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads GitHub Release metadata and downloads the exact installer listed by
/// the release. HttpClient uses the Windows/system proxy by default. Private
/// repositories may use a token supplied by the caller.
/// An optional HTTPS template such as https://trusted.example/{url} may be
/// supplied for networks that need a user-selected GitHub accelerator.
/// </summary>
public sealed class GitHubReleaseUpdateClient : IReleaseUpdateClient
{
    public const string RepositoryOwner = "mawenshui";
    public const string RepositoryName = "windows-ai-desktop-pet";
    public const int MaximumMetadataBytes = 1024 * 1024;
    public const int MaximumChecksumBytes = 1024 * 1024;
    public const long MaximumInstallerBytes = 200L * 1024 * 1024;

    private static readonly Uri LatestReleaseApi = new(
        $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");

    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        string? accelerationTemplate,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        if (!TryParseStableVersion(currentVersion, out var installed))
            return new(UpdateCheckState.Failed, null, "当前应用版本无效，无法安全比较更新。");
        if (!TryNormalizeAccessToken(accessToken, out var token))
            return new(UpdateCheckState.Failed, null, "GitHub 访问令牌格式无效。");
        string? template = null;
        if (token is null
            && !TryNormalizeAccelerationTemplate(accelerationTemplate, out template, out var templateError))
            return new(UpdateCheckState.Failed, null, templateError!);

        var failureMessage = "暂时无法连接 GitHub Release。请检查网络、系统代理或更新加速地址；私有仓库需保存只读访问令牌。";
        foreach (var uri in BuildCandidateUris(LatestReleaseApi, token is null ? template : null))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var json = await GetBoundedBytesAsync(uri, MaximumMetadataBytes, token, asset: false, cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                return ParseLatestRelease(json, installed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InvalidDataException ex)
            {
                failureMessage = $"GitHub Release 元数据不完整或不安全，已停止更新检查（{ex.Message}）";
            }
            catch (JsonException)
            {
                failureMessage = "GitHub Release 返回了无法识别的数据，已停止更新检查。";
            }
            catch
            {
                // Try the next user-configured/direct route. Error details are
                // intentionally not surfaced because proxy responses may contain secrets.
            }
        }

        return new(
            UpdateCheckState.Failed,
            null,
            failureMessage);
    }

    public async Task<UpdateDownloadResult> DownloadInstallerAsync(
        ReleaseUpdate update,
        string destinationRoot,
        string? accelerationTemplate,
        string? accessToken,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new ArgumentException("下载目录不能为空。", nameof(destinationRoot));
        var expectedInstallerName = $"windows-ai-desktop-pet-v{update.Version.ToString(3)}-setup.exe";
        if (!string.Equals(update.InstallerAssetName, expectedInstallerName, StringComparison.Ordinal)
            || !IsSafeHttpsUri(update.InstallerAssetUrl)
            || !IsSafeHttpsUri(update.ChecksumAssetUrl))
            return new(false, null, "Release 资产地址或文件名无效，已停止下载。");
        if (!TryNormalizeAccessToken(accessToken, out var token))
            return new(false, null, "GitHub 访问令牌格式无效。");
        string? template = null;
        if (token is null
            && !TryNormalizeAccelerationTemplate(accelerationTemplate, out template, out var templateError))
            return new(false, null, templateError!);

        var checksumRoutes = BuildCandidateUris(update.ChecksumAssetUrl, token is null ? template : null);
        var installerRoutes = BuildCandidateUris(update.InstallerAssetUrl, token is null ? template : null);
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
                    checksumRoutes[route], MaximumChecksumBytes, token, asset: true, cancellationToken: timeout.Token).ConfigureAwait(false);
                var expected = ReadExpectedChecksum(
                    Encoding.UTF8.GetString(checksumBytes), update.InstallerAssetName);

                if (File.Exists(destination)
                    && string.Equals(await HashFileAsync(destination, timeout.Token).ConfigureAwait(false), expected, StringComparison.Ordinal))
                {
                    progress?.Report(100);
                    return new(true, destination, "更新安装器已下载并通过 SHA-256 校验。");
                }

                if (File.Exists(temporary)) File.Delete(temporary);
                await DownloadFileAsync(installerRoutes[route], temporary, token, progress, timeout.Token)
                    .ConfigureAwait(false);
                var actual = await HashFileAsync(temporary, timeout.Token).ConfigureAwait(false);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    throw new InvalidDataException("Downloaded installer checksum mismatch.");
                File.Move(temporary, destination, true);
                progress?.Report(100);
                return new(true, destination, "更新安装器已下载并通过 SHA-256 校验。");
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

    public static bool TryNormalizeAccelerationTemplate(
        string? value,
        out string? normalized,
        out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var candidate = value.Trim();
        if (candidate.Length > 500 || candidate.Count('{') != 1 || candidate.Count('}') != 1
            || !candidate.Contains("{url}", StringComparison.Ordinal))
        {
            error = "更新加速地址必须包含且只包含一个 {url} 占位符。";
            return false;
        }

        var probeText = candidate.Replace("{url}", LatestReleaseApi.AbsoluteUri, StringComparison.Ordinal);
        if (!Uri.TryCreate(probeText, UriKind.Absolute, out var probe)
            || probe.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(probe.UserInfo))
        {
            error = "更新加速地址必须是无账号信息的 HTTPS 地址。";
            return false;
        }

        normalized = candidate;
        return true;
    }

    public static bool TryNormalizeAccessToken(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var candidate = value.Trim();
        if (candidate.Length is < 20 or > 512 || candidate.Any(char.IsWhiteSpace)) return false;
        normalized = candidate;
        return true;
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
        if (releaseVersion <= installed)
            return new(UpdateCheckState.UpToDate, null, $"当前已是最新版本 {installed.ToString(3)}。");

        if (ReadBoolean(root, "draft") || ReadBoolean(root, "prerelease"))
            throw new InvalidDataException("Latest release is not stable.");
        if (!TryReadHttpsUri(root, "html_url", out var releasePage))
            throw new InvalidDataException("Release page URL is invalid.");

        var installerName = $"windows-ai-desktop-pet-v{releaseVersion.ToString(3)}-setup.exe";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Release metadata is missing assets.");
        var installerUrl = FindAssetUrl(assets, installerName);
        var checksumUrl = FindAssetUrl(assets, "SHA256SUMS.txt");
        if (installerUrl is null || checksumUrl is null)
            throw new InvalidDataException("Release does not contain the required installer and checksum assets.");

        var update = new ReleaseUpdate(
            releaseVersion,
            tag,
            releasePage!,
            installerName,
            installerUrl,
            checksumUrl);
        return new(UpdateCheckState.UpdateAvailable, update, $"发现新版本 {releaseVersion.ToString(3)}。");
    }

    private static IReadOnlyList<Uri> BuildCandidateUris(Uri official, string? template)
    {
        var routes = new List<Uri>(2);
        if (!string.IsNullOrWhiteSpace(template))
        {
            var accelerated = template.Replace("{url}", official.AbsoluteUri, StringComparison.Ordinal);
            if (Uri.TryCreate(accelerated, UriKind.Absolute, out var acceleratedUri)) routes.Add(acceleratedUri);
        }
        routes.Add(official);
        return routes.Distinct().ToArray();
    }

    private async Task<byte[]> GetBoundedBytesAsync(
        Uri uri,
        int maximumBytes,
        string? accessToken,
        bool asset,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri, accessToken, asset);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && !IsSafeHttpsUri(finalUri))
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
        string? accessToken,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri, accessToken, asset: true);
        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && !IsSafeHttpsUri(finalUri))
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

    private static HttpRequestMessage CreateRequest(Uri uri, string? accessToken, bool asset)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WindowsAiDesktopPet", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            asset ? "application/octet-stream" : "application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static Uri? FindAssetUrl(JsonElement assets, string expectedName)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String
                || !string.Equals(name.GetString(), expectedName, StringComparison.Ordinal)
                || !TryReadHttpsUri(asset, "url", out var uri))
                continue;
            return uri;
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

    private static bool IsSafeHttpsUri(Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

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
