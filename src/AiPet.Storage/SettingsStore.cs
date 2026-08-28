using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiPet.Storage;

public sealed class AppSettings
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

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
    [JsonPropertyName("ranges")]
    public List<string> Ranges { get; set; } = new();
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
            return s ?? Defaults();
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
        var text = JsonSerializer.Serialize(settings, Options);
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

    public static AppSettings Defaults() => new();
}

public sealed class WindowLayout
{
    [JsonPropertyName("petX")] public int PetX { get; set; }
    [JsonPropertyName("petY")] public int PetY { get; set; }
    [JsonPropertyName("toolX")] public int? ToolX { get; set; }
    [JsonPropertyName("toolY")] public int? ToolY { get; set; }
}
