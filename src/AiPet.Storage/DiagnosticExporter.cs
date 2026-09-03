using System.Text.Json;

namespace AiPet.Storage;

public sealed record DiagnosticSnapshot(
    string AppVersion,
    string OsVersion,
    string RuntimeVersion,
    string Architecture,
    string Theme,
    int SearchRangeCount,
    int ShortcutCount,
    int PendingTodoCount,
    string AiProviderId,
    string AiConnectionStatus,
    IReadOnlyList<string> StableErrorCodes);

public static class DiagnosticExporter
{
    public static readonly IReadOnlyList<string> ExportedFields = typeof(DiagnosticSnapshot)
        .GetProperties().Select(property => property.Name).ToArray();

    public static void Export(string path, DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        RecoverableAtomicFile.WriteAllText(path, json);
    }
}
