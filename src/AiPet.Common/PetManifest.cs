using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiPet.Common;

/// <summary>
/// Lightweight DTO for <c>pet.json</c>. We do not bind to a strict schema;
/// unknown fields are ignored so a future version of the manifest can add
/// fields without breaking the loader.
/// </summary>
public sealed class PetManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("source")]
    public PetSource? Source { get; set; }

    [JsonPropertyName("frameInventory")]
    public PetFrameInventory? FrameInventory { get; set; }

    [JsonPropertyName("directionModel")]
    public PetDirectionModel? DirectionModel { get; set; }

    [JsonPropertyName("stateMachine")]
    public PetStateMachine? StateMachine { get; set; }

    [JsonPropertyName("render")]
    public PetRender? Render { get; set; }
}

public sealed class PetSource
{
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("zipSha256")]
    public string? ZipSha256 { get; set; }
}

public sealed class PetFrameInventory
{
    [JsonPropertyName("characters")]
    public List<string> Characters { get; set; } = new();

    [JsonPropertyName("preferred")]
    public string? Preferred { get; set; }

    [JsonPropertyName("actionsPerCharacter")]
    public List<string> ActionsPerCharacter { get; set; } = new();

    [JsonPropertyName("directions")]
    public List<string> Directions { get; set; } = new();

    [JsonPropertyName("framesPerAction")]
    public Dictionary<string, int> FramesPerAction { get; set; } = new();

    [JsonPropertyName("filePathPattern")]
    public string? FilePathPattern { get; set; }

    [JsonPropertyName("deathFilePathPattern")]
    public string? DeathFilePathPattern { get; set; }

    /// <summary>
    /// Total single-frame PNGs across all characters + the global death
    /// FX. Mirrors the value written by the manifest organizer; we keep
    /// a derived default for tests / minimal manifests.
    /// </summary>
    [JsonPropertyName("totalFrames")]
    public int TotalFrames
    {
        get
        {
            if (FramesPerAction is null || FramesPerAction.Count == 0) return 0;
            int per = 0;
            foreach (var v in FramesPerAction.Values) per += v;
            int perChar = per * Math.Max(1, Directions.Count);
            return Characters.Count * perChar + (GlobalActions?.Sum(g => g.Frames) ?? 0);
        }
    }

    [JsonPropertyName("globalActions")]
    public List<GlobalAction> GlobalActions { get; set; } = new();
}

public sealed class GlobalAction
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frames")] public int Frames { get; set; }
    [JsonPropertyName("direction")] public string? Direction { get; set; }
}

public sealed class PetDirectionModel
{
    [JsonPropertyName("authored")]
    public List<string> Authored { get; set; } = new();

    [JsonPropertyName("columnOrder8Dir")]
    public List<string> ColumnOrder8Dir { get; set; } = new();
}

public sealed class PetStateMachine
{
    [JsonPropertyName("default")]
    public PetStateTransition? Default { get; set; }

    [JsonPropertyName("transitions")]
    public List<PetStateTransition> Transitions { get; set; } = new();
}

public sealed class PetStateTransition
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("to")]
    public string? To { get; set; }

    [JsonPropertyName("param")]
    public string? Param { get; set; }
}

public sealed class PetRender
{
    [JsonPropertyName("defaultScale")]
    public double DefaultScale { get; set; } = 1.0;

    [JsonPropertyName("anchor")]
    public string? Anchor { get; set; }

    [JsonPropertyName("fps")]
    public int Fps { get; set; } = 12;
}

/// <summary>
/// Loads and validates a <c>pet.json</c> manifest. Pure I/O, no WPF types
/// — unit-testable in <c>AiPet.Tests.Unit</c>.
/// </summary>
public static class PetManifestLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static PetManifest Load(string petJsonPath)
    {
        if (!File.Exists(petJsonPath))
            throw new FileNotFoundException($"pet.json not found: {petJsonPath}", petJsonPath);
        var text = File.ReadAllText(petJsonPath);
        var manifest = JsonSerializer.Deserialize<PetManifest>(text, Options)
            ?? throw new InvalidDataException($"pet.json is empty or invalid: {petJsonPath}");
        return manifest;
    }

    /// <summary>
    /// Resolve a frame path under the package's <c>frames/</c> root, based on
    /// the manifest's <c>filePathPattern</c>. Pattern format:
    /// <c>frames/&lt;character&gt;/&lt;action&gt;_&lt;direction&gt;_&lt;NN&gt;.png</c>.
    /// </summary>
    public static string ResolveFramePath(
        string framesRoot,
        PetManifest manifest,
        string character,
        string action,
        Direction8 direction,
        int frameIndex)
    {
        if (manifest.FrameInventory is null)
            throw new InvalidDataException("pet.json has no frameInventory");

        // Map requested direction to authored direction for the file lookup.
        var authored = DirectionMapper.AuthoredMirror(direction);
        var dirToken = AuthoredDirectionToken(authored);

        var pattern = manifest.FrameInventory.FilePathPattern
            ?? "frames/<character>/<action>_<direction>_<NN>.png";

        // RGS resources use 1-based file names (idle_down_01.png, ...).
        // The frameIndex passed in is 0-based; convert at the boundary.
        var nn = (frameIndex + 1).ToString("D2");
        var rel = pattern
            .Replace("<character>", character, StringComparison.Ordinal)
            .Replace("<action>", action, StringComparison.Ordinal)
            .Replace("<direction>", dirToken, StringComparison.Ordinal)
            .Replace("<NN>", nn, StringComparison.Ordinal);

        return Path.Combine(framesRoot, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string ResolveDeathFramePath(string framesRoot, PetManifest manifest, int frameIndex)
    {
        if (manifest.FrameInventory is null)
            throw new InvalidDataException("pet.json has no frameInventory");
        var pattern = manifest.FrameInventory.DeathFilePathPattern
            ?? "frames/_global/death_<NN>.png";
        // Same 0-based -> 1-based mapping as the per-character frames.
        var nn = (frameIndex + 1).ToString("D2");
        var rel = pattern
            .Replace("<NN>", nn, StringComparison.Ordinal);
        return Path.Combine(framesRoot, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string AuthoredDirectionToken(Direction8 direction) => direction switch
    {
        Direction8.Down        => "down",
        Direction8.DownRight   => "down_right",
        Direction8.Right       => "right",
        Direction8.UpRight     => "up_right",
        Direction8.Up          => "up",
        _ => throw new ArgumentException($"Not an authored direction: {direction}", nameof(direction)),
    };
}
