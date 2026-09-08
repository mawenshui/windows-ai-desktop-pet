using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiPet.AI;

public sealed record CompleteAiConfiguration(
    string Id,
    string Name,
    string ProviderId,
    string Endpoint,
    string Model,
    string ApiKey,
    string LastStatus,
    DateTimeOffset? LastVerifiedAt);

public sealed record CompleteAiConfigurationDocument(
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset ExportedAtUtc,
    bool RequireExplicitActivation,
    bool EnableCustomProviderPresets,
    string ActiveConfigurationId,
    IReadOnlyList<CompleteAiConfiguration> Configurations,
    IReadOnlyList<AiProviderDescriptor> CustomProviders);

public sealed record CompleteAiConfigurationPreview(
    int ConfigurationCount,
    int ConfigurationWithKeyCount,
    int CustomProviderCount,
    string ActiveConfigurationName,
    string SourceVersion,
    DateTimeOffset ExportedAtUtc);

internal sealed record EncryptedAiConfigurationEnvelope(
    string Kind,
    int SchemaVersion,
    string Kdf,
    int Iterations,
    string Cipher,
    byte[] Salt,
    byte[] Nonce,
    byte[] Tag,
    byte[] Ciphertext);

/// <summary>
/// Password-encrypted, authenticated transport for all AI profiles, keys and
/// custom provider presets. The envelope never contains a plaintext key or
/// credential target name.
/// </summary>
public static class EncryptedAiConfigurationBundle
{
    public const string FileExtension = ".aipet-ai-config";
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    public const int MinimumPasswordLength = 10;

    private const string Kind = "WindowsAiDesktopPet.AiConfigurationBundle";
    private const int EnvelopeSchemaVersion = 1;
    private const int DocumentSchemaVersion = 1;
    private const int Iterations = 310_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes($"{Kind}.v{EnvelopeSchemaVersion}");
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static string Encrypt(CompleteAiConfigurationDocument document, string password)
    {
        ValidatePassword(password);
        ValidateDocument(document);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, Options);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            return JsonSerializer.Serialize(new EncryptedAiConfigurationEnvelope(
                Kind,
                EnvelopeSchemaVersion,
                "PBKDF2-HMAC-SHA256",
                Iterations,
                "AES-256-GCM",
                salt,
                nonce,
                tag,
                ciphertext), Options);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static CompleteAiConfigurationDocument Decrypt(string envelopeJson, string password)
    {
        ArgumentNullException.ThrowIfNull(envelopeJson);
        ValidatePassword(password);
        if (Encoding.UTF8.GetByteCount(envelopeJson) > MaximumFileBytes)
            throw new InvalidDataException("完整 AI 配置包最大 4 MB。");

        EncryptedAiConfigurationEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EncryptedAiConfigurationEnvelope>(envelopeJson, Options)
                ?? throw new InvalidDataException("完整 AI 配置包为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("完整 AI 配置包格式无效。", ex);
        }

        ValidateEnvelope(envelope);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, envelope.Salt, envelope.Iterations, HashAlgorithmName.SHA256, KeySize);
        var plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            try
            {
                aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, AssociatedData);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException("迁移口令错误或完整 AI 配置包已损坏。", ex);
            }

            try
            {
                var document = JsonSerializer.Deserialize<CompleteAiConfigurationDocument>(plaintext, Options)
                    ?? throw new InvalidDataException("完整 AI 配置包内容为空。");
                ValidateDocument(document);
                return document;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("完整 AI 配置包内容无效。", ex);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static CompleteAiConfigurationPreview Preview(CompleteAiConfigurationDocument document)
    {
        ValidateDocument(document);
        var active = document.Configurations.Single(configuration =>
            string.Equals(configuration.Id, document.ActiveConfigurationId, StringComparison.Ordinal));
        return new CompleteAiConfigurationPreview(
            document.Configurations.Count,
            document.Configurations.Count(configuration => !string.IsNullOrEmpty(configuration.ApiKey)),
            document.CustomProviders.Count,
            active.Name,
            document.AppVersion,
            document.ExportedAtUtc);
    }

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length is < MinimumPasswordLength or > 256)
            throw new ArgumentException($"迁移口令需为 {MinimumPasswordLength}～256 个字符。", nameof(password));
    }

    private static void ValidateEnvelope(EncryptedAiConfigurationEnvelope envelope)
    {
        if (!string.Equals(envelope.Kind, Kind, StringComparison.Ordinal)
            || envelope.SchemaVersion != EnvelopeSchemaVersion
            || !string.Equals(envelope.Kdf, "PBKDF2-HMAC-SHA256", StringComparison.Ordinal)
            || envelope.Iterations != Iterations
            || !string.Equals(envelope.Cipher, "AES-256-GCM", StringComparison.Ordinal)
            || envelope.Salt is not { Length: SaltSize }
            || envelope.Nonce is not { Length: NonceSize }
            || envelope.Tag is not { Length: TagSize }
            || envelope.Ciphertext is not { Length: > 0 }
            || envelope.Ciphertext.Length > MaximumFileBytes)
            throw new InvalidDataException("不支持或损坏的完整 AI 配置包。");
    }

    private static void ValidateDocument(CompleteAiConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != DocumentSchemaVersion
            || string.IsNullOrWhiteSpace(document.AppVersion)
            || document.AppVersion.Length > 32
            || document.Configurations is null
            || document.Configurations.Count is < 1 or > 100
            || document.CustomProviders is null
            || document.CustomProviders.Count > 32)
            throw new InvalidDataException("完整 AI 配置内容或数量无效。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configuration in document.Configurations)
        {
            if (configuration is null
                || string.IsNullOrWhiteSpace(configuration.Id) || configuration.Id.Length > 100 || !ids.Add(configuration.Id)
                || string.IsNullOrWhiteSpace(configuration.Name) || configuration.Name.Length > 100 || !names.Add(configuration.Name.Trim())
                || string.IsNullOrWhiteSpace(configuration.ProviderId) || configuration.ProviderId.Length > 100
                || string.IsNullOrWhiteSpace(configuration.Endpoint)
                || string.IsNullOrWhiteSpace(configuration.Model) || configuration.Model.Length > 200
                || configuration.ApiKey is null || Encoding.UTF8.GetByteCount(configuration.ApiKey) > 2560
                || configuration.LastStatus is not ("Untested" or "Connected" or "Failed")
                || !OpenAiCompatibleClient.IsValidEndpoint(configuration.Endpoint))
                throw new InvalidDataException("完整 AI 配置包含无效或重复的配置项。");
        }

        if (!ids.Contains(document.ActiveConfigurationId))
            throw new InvalidDataException("完整 AI 配置包没有有效的当前配置。");

        AiProviderPresetStore.Validate(document.CustomProviders);
        foreach (var provider in document.CustomProviders)
        {
            if (AiProviders.Builtin.Any(builtin => string.Equals(builtin.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("完整 AI 配置包包含无效或重复的自定义 Provider 预设。");
        }
    }
}
