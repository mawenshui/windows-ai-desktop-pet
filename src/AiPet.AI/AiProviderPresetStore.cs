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
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("预设最大 1 MB。");
        var document = JsonSerializer.Deserialize<AiProviderPresetDocument>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Provider 预设文件为空。");
        if (document.SchemaVersion != 1 || document.Providers is null) throw new InvalidDataException("不支持的 Provider 预设 schema 或数量。");
        Validate(document.Providers);
        return document.Providers;
    }

    public static void Save(string path, IReadOnlyList<AiProviderDescriptor> providers)
    {
        Validate(providers);
        var text = JsonSerializer.Serialize(
            new AiProviderPresetDocument(1, providers),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
        AiPet.Storage.RecoverableAtomicFile.WriteAllText(path, text);
    }

    public static void Validate(IReadOnlyList<AiProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Count > 32) throw new InvalidDataException("不支持的 Provider 预设 schema 或数量。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.Id) || provider.Id.Length > 100 || !ids.Add(provider.Id))
                throw new InvalidDataException("Provider ID 为空或重复。");
            if (string.IsNullOrWhiteSpace(provider.DisplayName) || provider.DisplayName.Length > 100
                || provider.InfoUrl is null || provider.InfoUrl.Length > 2048
                || provider.DefaultEndpoint is null
                || provider.DefaultModel is null || provider.DefaultModel.Length > 200
                || provider.Notes is null || provider.Notes.Length > 2000
                || provider.ValidationScope is null || provider.ValidationScope.Length > 500
                || provider.SuggestedModels is { Count: > 100 }
                || provider.SuggestedModels?.Any(model => string.IsNullOrWhiteSpace(model) || model.Length > 200) == true)
                throw new InvalidDataException($"Provider {provider.Id} 的显示字段无效或超限。");
            if (!Uri.TryCreate(provider.DefaultEndpoint, UriKind.Absolute, out var endpoint)
                || (!endpoint.IsLoopback && endpoint.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(endpoint.UserInfo)
                || !string.IsNullOrEmpty(endpoint.Query) || !OpenAiCompatibleClient.IsValidEndpoint(provider.DefaultEndpoint))
                throw new InvalidDataException($"Provider {provider.Id} 的端点必须为无凭据、无查询参数的 HTTPS 地址；本机回环地址除外。");
        }
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
