using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiPet.Shortcuts;

public enum ShortcutKind
{
    File = 0,
    Application = 1,
    Url = 2,
    Folder = 3,
}

public sealed class ShortcutItem
{
    [JsonPropertyName("id")]            public Guid Id { get; set; } = Guid.NewGuid();
    [JsonPropertyName("kind")]          public ShortcutKind Kind { get; set; }
    [JsonPropertyName("targetPath")]    public string TargetPath { get; set; } = "";
    [JsonPropertyName("displayName")]   public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")]   public string? Description { get; set; }
    [JsonPropertyName("iconPath")]      public string? IconPath { get; set; }
    [JsonPropertyName("order")]         public int Order { get; set; }
    [JsonPropertyName("createdAt")]     public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("updatedAt")]     public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool ExistsNow
    {
        get
        {
            if (Kind == ShortcutKind.Url) return !string.IsNullOrEmpty(TargetPath);
            if (Kind == ShortcutKind.Folder) return Directory.Exists(TargetPath);
            return File.Exists(TargetPath);
        }
    }
}

public sealed class ShortcutsFile
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("items")]         public List<ShortcutItem> Items { get; set; } = new();
}

public enum ShortcutLaunchResult
{
    Success = 0,
    TargetMissing = 1,
    NoAssociation = 2,
    PermissionDenied = 3,
    OtherError = 4,
}

public sealed class ShortcutLaunchOutcome
{
    public ShortcutLaunchResult Result { get; init; }
    public string? Message { get; init; }
    public static ShortcutLaunchOutcome Ok() => new() { Result = ShortcutLaunchResult.Success };
    public static ShortcutLaunchOutcome Missing() => new() { Result = ShortcutLaunchResult.TargetMissing, Message = "目标不存在" };
    public static ShortcutLaunchOutcome NoAssoc() => new() { Result = ShortcutLaunchResult.NoAssociation, Message = "无默认关联程序" };
    public static ShortcutLaunchOutcome Denied() => new() { Result = ShortcutLaunchResult.PermissionDenied, Message = "无权限启动" };
    public static ShortcutLaunchOutcome Other(string msg) => new() { Result = ShortcutLaunchResult.OtherError, Message = msg };
}
