using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace AiPet.AI;

public enum AiErrorCategory
{
    None = 0,
    MissingField,
    AuthFailed,
    Forbidden,
    RateLimited,
    BadEndpoint,
    NetworkUnreachable,
    Timeout,
    Unknown,
}

public enum AiConnectionStatus
{
    Connected = 0,
    Failed = 1,
}

/// <summary>
/// Result of a "Test connection" call. UI maps <see cref="Status"/>
/// to "连接正常" / "连接异常" and <see cref="ErrorCategory"/> +
/// <see cref="Suggestion"/> to the standardized error table.
/// </summary>
public sealed record AiConnectionResult(
    AiConnectionStatus Status,
    AiErrorCategory ErrorCategory,
    string? LocalizedMessage,
    string? Suggestion,
    DateTimeOffset TestedAt,
    int LatencyMs)
{
    public static AiConnectionResult Connected(int latencyMs) =>
        new(AiConnectionStatus.Connected, AiErrorCategory.None, null, null, DateTimeOffset.UtcNow, latencyMs);

    public static AiConnectionResult Failed(AiErrorCategory cat, string msg, string suggestion) =>
        new(AiConnectionStatus.Failed, cat, msg, suggestion, DateTimeOffset.UtcNow, 0);
}

public interface IAiClient
{
    Task<AiConnectionResult> TestConnectionAsync(
        string endpoint, string model, string apiKey, CancellationToken ct);
}

public sealed class OpenAiCompatibleClient : IAiClient
{
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<AiConnectionResult> TestConnectionAsync(
        string endpoint, string model, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return AiConnectionResult.Failed(AiErrorCategory.MissingField, "服务地址格式错误", "请填写以 http(s):// 开头的服务地址");
        if (string.IsNullOrWhiteSpace(model))
            return AiConnectionResult.Failed(AiErrorCategory.MissingField, "模型名称为空", "请填写模型名称");
        if (string.IsNullOrEmpty(apiKey) || apiKey.Trim().Length < 8)
            return AiConnectionResult.Failed(AiErrorCategory.MissingField, "API Key 为空或过短", "请填写有效的 API Key");
        if (apiKey.Any(char.IsWhiteSpace))
            return AiConnectionResult.Failed(AiErrorCategory.MissingField, "API Key 含空白字符", "请检查 API Key 是否完整");

        var url = BuildModelsEndpoint(endpoint);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            sw.Stop();
            if ((int)resp.StatusCode == 200) return AiConnectionResult.Connected((int)sw.ElapsedMilliseconds);
            if ((int)resp.StatusCode == 401) return AiConnectionResult.Failed(AiErrorCategory.AuthFailed, "API Key 无效或已过期", "请核对后重试");
            if ((int)resp.StatusCode == 403) return AiConnectionResult.Failed(AiErrorCategory.Forbidden, "当前账户无访问权限", "请在服务方检查账户权限");
            if ((int)resp.StatusCode == 429) return AiConnectionResult.Failed(AiErrorCategory.RateLimited, "触发频率限制", "请稍后重试");
            if ((int)resp.StatusCode is 400 or 404) return AiConnectionResult.Failed(AiErrorCategory.BadEndpoint, "服务地址或模型不存在", "请检查服务地址和模型名拼写");
            return AiConnectionResult.Failed(AiErrorCategory.Unknown, $"服务返回未分类错误 ({resp.StatusCode})", "请稍后重试或查看帮助");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return AiConnectionResult.Failed(AiErrorCategory.Timeout, "请求超时", "请稍后重试或检查服务状态");
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException se)
        {
            return AiConnectionResult.Failed(
                se.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound
                    ? AiErrorCategory.NetworkUnreachable
                    : AiErrorCategory.NetworkUnreachable,
                "无法连接服务地址", "请检查网络与服务状态");
        }
        catch (HttpRequestException)
        {
            return AiConnectionResult.Failed(AiErrorCategory.NetworkUnreachable, "无法连接服务地址", "请检查网络与服务状态");
        }
        catch (Exception ex)
        {
            return AiConnectionResult.Failed(AiErrorCategory.Unknown, $"未分类错误: {ex.GetType().Name}", "请稍后重试");
        }
    }

    public static string BuildModelsEndpoint(string endpoint)
    {
        var normalizedEndpoint = endpoint.TrimEnd('/');
        return Regex.IsMatch(normalizedEndpoint, @"/v\d+$", RegexOptions.IgnoreCase)
            ? normalizedEndpoint + "/models"
            : normalizedEndpoint + "/v1/models";
    }
}
