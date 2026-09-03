using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace AiPet.AI;

public sealed record AiProviderPresetDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("providers")] IReadOnlyList<AiProviderDescriptor> Providers);

public static class AiProviderPresetStore
{
    public static IReadOnlyList<AiProviderDescriptor> Load(string path)
    {
        var document = JsonSerializer.Deserialize<AiProviderPresetDocument>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Provider 预设文件为空。");
        if (document.SchemaVersion != 1) throw new InvalidDataException("不支持的 Provider 预设 schema。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in document.Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id) || !ids.Add(provider.Id))
                throw new InvalidDataException("Provider ID 为空或重复。");
            if (!Uri.TryCreate(provider.DefaultEndpoint, UriKind.Absolute, out var endpoint)
                || (!endpoint.IsLoopback && endpoint.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(endpoint.UserInfo)
                || !string.IsNullOrEmpty(endpoint.Query))
                throw new InvalidDataException($"Provider {provider.Id} 的端点必须为无凭据、无查询参数的 HTTPS 地址；本机回环地址除外。");
        }
        return document.Providers;
    }
}

public sealed record AiDiagnosticSummary(
    string ProviderId,
    AiConnectionStatus Status,
    AiErrorCategory Category,
    long DurationMilliseconds,
    DateTimeOffset CheckedAt)
{
    public static AiDiagnosticSummary From(string providerId, AiConnectionResult result, TimeSpan duration, DateTimeOffset checkedAt) =>
        new(providerId, result.Status, result.ErrorCategory, Math.Max(0, (long)duration.TotalMilliseconds), checkedAt);
}
