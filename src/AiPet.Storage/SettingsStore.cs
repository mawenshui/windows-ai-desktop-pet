using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiPet.Storage;

public sealed class AppSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 6;

    [JsonPropertyName("pet")]
    public PetSettings Pet { get; set; } = new();

    [JsonPropertyName("toolWindow")]
    public ToolWindowSettings ToolWindow { get; set; } = new();

    [JsonPropertyName("search")]
    public SearchSettings Search { get; set; } = new();

    [JsonPropertyName("ai")]
    public AiSettings Ai { get; set; } = new();

    [JsonPropertyName("autostart")]
    public AutostartSettings Autostart { get; set; } = new();

    [JsonPropertyName("appearance")]
    public AppearanceSettings Appearance { get; set; } = new();

    [JsonPropertyName("focus")]
    public FocusSettings Focus { get; set; } = new();

    [JsonPropertyName("features")]
    public FeatureSettings Features { get; set; } = new();

    [JsonPropertyName("hotkeys")]
    public HotkeySettings Hotkeys { get; set; } = new();

    [JsonPropertyName("backup")]
    public BackupSettings Backup { get; set; } = new();

    [JsonPropertyName("updates")]
    public UpdateSettings Updates { get; set; } = new();
}

public sealed class HotkeySettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("searchGesture")]
    public string SearchGesture { get; set; } = "Ctrl+Alt+Space";

    [JsonPropertyName("quickTodoGesture")]
    public string QuickTodoGesture { get; set; } = "Ctrl+Alt+T";
}

public sealed class BackupSettings
{
    [JsonPropertyName("automaticEnabled")]
    public bool AutomaticEnabled { get; set; } = true;

    [JsonPropertyName("retentionCount")]
    public int RetentionCount { get; set; } = AutomaticBackupService.DefaultRetentionCount;
}

public sealed class UpdateSettings
{
    [JsonPropertyName("periodicEnabled")]
    public bool PeriodicEnabled { get; set; }

    [JsonPropertyName("intervalHours")]
    public int IntervalHours { get; set; } = 24;

    [JsonPropertyName("accelerationTemplate")]
    // Retained for schema-5 compatibility. Since 0.20.0 update routes are
    // built in and this legacy user-supplied value is never used.
    public string AccelerationTemplate { get; set; } = string.Empty;
}

public sealed class AppearanceSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";
    [JsonPropertyName("enablePetRoaming")]
    public bool EnablePetRoaming { get; set; } = true;
    [JsonPropertyName("enableBubbleAnimation")]
    public bool EnableBubbleAnimation { get; set; } = true;
    [JsonPropertyName("enableFollowMotion")]
    public bool EnableFollowMotion { get; set; } = true;
    [JsonPropertyName("hidePetDuringFullscreen")]
    public bool HidePetDuringFullscreen { get; set; }
}

public sealed class FocusSettings
{
    [JsonPropertyName("clickThroughPet")]
    public bool ClickThroughPet { get; set; }
}

public sealed class FeatureSettings
{
    [JsonPropertyName("enableContentSearch")]
    public bool EnableContentSearch { get; set; }
    [JsonPropertyName("enableCustomProviderPresets")]
    public bool EnableCustomProviderPresets { get; set; }
    [JsonPropertyName("enableOnlineHelpFallback")]
    public bool EnableOnlineHelpFallback { get; set; }
}

public sealed class PetSettings
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "rgs-8dir";

    [JsonPropertyName("preferredCharacter")]
    public string PreferredCharacter { get; set; } = "hero";
}

public sealed class ToolWindowSettings
{
    [JsonPropertyName("width")]
    public double Width { get; set; } = 440;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 536;

    [JsonPropertyName("stayOpen")]
    public bool StayOpen { get; set; }

    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; } = true;
}

public sealed class SearchSettings
{
    [JsonPropertyName("queryField")]
    public string QueryField { get; set; } = "name";
    [JsonPropertyName("useRecentHistory")]
    public bool UseRecentHistory { get; set; }
    [JsonPropertyName("ranges")]
    public List<string> Ranges { get; set; } = new();
    [JsonPropertyName("onboardingCompleted")]
    public bool OnboardingCompleted { get; set; }
    [JsonPropertyName("lastCategory")]
    public string LastCategory { get; set; } = "all";
    [JsonPropertyName("enableWildcardSearch")]
    public bool EnableWildcardSearch { get; set; }
    [JsonPropertyName("enableRegexSearch")]
    public bool EnableRegexSearch { get; set; }
    [JsonPropertyName("lastScopeId")]
    public string LastScopeId { get; set; } = "all";
}

public sealed class AiSettings
{
    [JsonPropertyName("requireExplicitActivation")]
    public bool RequireExplicitActivation { get; set; }
    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "deepseek";
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }
    [JsonPropertyName("model")]
    public string? Model { get; set; }
    [JsonPropertyName("secretTargetName")]
    public string SecretTargetName { get; set; } = "WindowsAiDesktopPet:AI:deepseek";
    [JsonPropertyName("lastStatus")]
    public string LastStatus { get; set; } = "Untested";
    [JsonPropertyName("lastVerifiedAt")]
    public DateTimeOffset? LastVerifiedAt { get; set; }

    [JsonPropertyName("activeProfileId")]
    public string? ActiveProfileId { get; set; }

    [JsonPropertyName("profiles")]
    public List<AiConfigurationProfile> Profiles { get; set; } = new();
}

public sealed class AiConfigurationProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "AI 配置";

    [JsonPropertyName("providerId")]
    public string ProviderId { get; set; } = "deepseek";

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("secretTargetName")]
    public string SecretTargetName { get; set; } = string.Empty;

    [JsonPropertyName("lastStatus")]
    public string LastStatus { get; set; } = "Untested";

    [JsonPropertyName("lastVerifiedAt")]
    public DateTimeOffset? LastVerifiedAt { get; set; }
}

public sealed class AutostartSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
}

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON under
/// <c>%APPDATA%\WindowsAiDesktopPet\settings.json</c>. A corrupted or missing
/// file is treated as the defaults — the previous good file is never
/// overwritten with a corrupted one (PRD §1.7 DATA-01).
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string AppDataDir { get; }
    public string SettingsPath { get; }
    public string LayoutPath { get; }
    public string IndexPath { get; }

    public SettingsStore(string? overrideRoot = null)
    {
        AppDataDir = overrideRoot
            ?? Environment.GetEnvironmentVariable("AIPET_APP_DATA_ROOT")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowsAiDesktopPet");
        SettingsPath = Path.Combine(AppDataDir, "settings.json");
        LayoutPath = Path.Combine(AppDataDir, "layout.json");
        IndexPath = Path.Combine(AppDataDir, "index.db");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return Defaults();
            var text = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(text, Options);
            if (s is null) return Defaults();
            if (s.SchemaVersion < 6)
            {
                if (s.SchemaVersion < 3)
                    RecoverableAtomicFile.WriteAllText(SettingsPath + ".pre-v3.bak", text);
                if (s.SchemaVersion < 4)
                    RecoverableAtomicFile.WriteAllText(SettingsPath + ".pre-v4.bak", text);
                if (s.SchemaVersion < 5)
                    RecoverableAtomicFile.WriteAllText(SettingsPath + ".pre-v5.bak", text);
                RecoverableAtomicFile.WriteAllText(SettingsPath + ".pre-v6.bak", text);
                s = Normalize(s);
                Save(s);
            }
            return Normalize(s);
        }
        catch
        {
            // PRD DATA-01: a corrupted file must not destroy the previous
            // good config. Fall back to defaults for this session and leave
            // the existing file untouched so the user can recover it.
            return Defaults();
        }
    }

    public void Save(AppSettings settings)
    {
        if (File.Exists(SettingsPath)) DataMaintenanceService.ValidateJson("settings.json", File.ReadAllBytes(SettingsPath));
        var text = JsonSerializer.Serialize(Normalize(settings), Options);
        RecoverableAtomicFile.WriteAllText(SettingsPath, text);
    }

    public WindowLayout? TryLoadLayout()
    {
        try
        {
            if (!File.Exists(LayoutPath)) return null;
            var text = File.ReadAllText(LayoutPath);
            return JsonSerializer.Deserialize<WindowLayout>(text, Options);
        }
        catch
        {
            return null;
        }
    }

    public void SaveLayout(WindowLayout layout)
    {
        var text = JsonSerializer.Serialize(layout, Options);
        RecoverableAtomicFile.WriteAllText(LayoutPath, text);
    }

    public static AppSettings Defaults() => Normalize(new AppSettings());

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = 6;
        settings.Pet ??= new PetSettings();
        settings.ToolWindow ??= new ToolWindowSettings();
        settings.Autostart ??= new AutostartSettings();
        settings.Appearance ??= new AppearanceSettings();
        settings.Focus ??= new FocusSettings();
        settings.Features ??= new FeatureSettings();
        settings.Hotkeys ??= new HotkeySettings();
        settings.Backup ??= new BackupSettings();
        settings.Updates ??= new UpdateSettings();
        settings.Hotkeys.SearchGesture = NormalizeGestureText(settings.Hotkeys.SearchGesture, "Ctrl+Alt+Space");
        settings.Hotkeys.QuickTodoGesture = NormalizeGestureText(settings.Hotkeys.QuickTodoGesture, "Ctrl+Alt+T");
        settings.Backup.RetentionCount = Math.Clamp(settings.Backup.RetentionCount, 1, 30);
        settings.Updates.IntervalHours = Math.Clamp(settings.Updates.IntervalHours, 1, 168);
        settings.Updates.AccelerationTemplate = string.Empty;
        if (settings.Appearance.Theme is not ("system" or "light" or "dark" or "high-contrast"))
            settings.Appearance.Theme = "system";
        settings.Search ??= new SearchSettings();
        settings.Search.Ranges ??= new List<string>();
        if (settings.Search.Ranges.Count > 0)
            settings.Search.OnboardingCompleted = true;
        settings.Ai ??= new AiSettings();
        settings.Ai.Profiles ??= new List<AiConfigurationProfile>();

        // Schema v1 stored only one AI configuration. Expose it as a stable
        // profile in memory so existing users can select and update it without
        // re-entering any non-sensitive fields or moving the credential.
        if (settings.Ai.Profiles.Count == 0
            && (!string.IsNullOrWhiteSpace(settings.Ai.Endpoint)
                || !string.IsNullOrWhiteSpace(settings.Ai.Model)
                || !string.Equals(settings.Ai.LastStatus, "Untested", StringComparison.Ordinal)))
        {
            settings.Ai.Profiles.Add(new AiConfigurationProfile
            {
                Id = "legacy",
                DisplayName = BuildLegacyProfileName(settings.Ai.ProviderId, settings.Ai.Model),
                ProviderId = string.IsNullOrWhiteSpace(settings.Ai.ProviderId) ? "deepseek" : settings.Ai.ProviderId,
                Endpoint = settings.Ai.Endpoint ?? string.Empty,
                Model = settings.Ai.Model ?? string.Empty,
                SecretTargetName = settings.Ai.SecretTargetName,
                LastStatus = settings.Ai.LastStatus,
                LastVerifiedAt = settings.Ai.LastVerifiedAt,
            });
        }

        settings.Ai.Profiles.RemoveAll(profile => profile is null || string.IsNullOrWhiteSpace(profile.Id));
        var duplicateIds = new HashSet<string>(StringComparer.Ordinal);
        settings.Ai.Profiles.RemoveAll(profile => !duplicateIds.Add(profile.Id));

        var active = settings.Ai.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, settings.Ai.ActiveProfileId, StringComparison.Ordinal));
        if (!settings.Ai.RequireExplicitActivation) active ??= settings.Ai.Profiles.FirstOrDefault();
        settings.Ai.ActiveProfileId = active?.Id;
        if (active is not null) CopyProfileToActiveSettings(active, settings.Ai);

        return settings;
    }

    private static string NormalizeGestureText(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 80 ? fallback : normalized;
    }

    private static void CopyProfileToActiveSettings(AiConfigurationProfile profile, AiSettings settings)
    {
        settings.ProviderId = profile.ProviderId;
        settings.Endpoint = profile.Endpoint;
        settings.Model = profile.Model;
        settings.SecretTargetName = profile.SecretTargetName;
        settings.LastStatus = profile.LastStatus;
        settings.LastVerifiedAt = profile.LastVerifiedAt;
    }

    private static string BuildLegacyProfileName(string providerId, string? model)
    {
        var provider = string.IsNullOrWhiteSpace(providerId) ? "AI" : providerId;
        return string.IsNullOrWhiteSpace(model) ? provider : $"{provider} · {model}";
    }
}

public sealed class WindowLayout
{
    [JsonPropertyName("petX")] public int PetX { get; set; }
    [JsonPropertyName("petY")] public int PetY { get; set; }
    [JsonPropertyName("toolX")] public int? ToolX { get; set; }
    [JsonPropertyName("toolY")] public int? ToolY { get; set; }
}
