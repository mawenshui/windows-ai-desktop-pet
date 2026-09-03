using System.IO.Compression;
using System.Text.Json;

namespace AiPet.Storage;

[Flags]
public enum DataModule
{
    None = 0,
    Settings = 1,
    Layout = 2,
    Todos = 4,
    Shortcuts = 8,
    SearchIndex = 16,
    IconCache = 32,
    Logs = 64,
    AllNonSecret = Settings | Layout | Todos | Shortcuts | SearchIndex | IconCache | Logs,
}

public sealed record DataModulePreview(DataModule Module, string DisplayName, bool Exists, long Bytes);
public sealed record DataMaintenanceResult(IReadOnlyList<DataModule> Completed, IReadOnlyList<string> Errors);

/// <summary>Backs up and maintains local modules without ever reading credentials.</summary>
public sealed class DataMaintenanceService
{
    private static readonly IReadOnlyDictionary<DataModule, string> ModulePaths =
        new Dictionary<DataModule, string>
        {
            [DataModule.Settings] = "settings.json",
            [DataModule.Layout] = "layout.json",
            [DataModule.Todos] = "todos.json",
            [DataModule.Shortcuts] = "shortcuts.json",
            [DataModule.SearchIndex] = "index.db",
            [DataModule.IconCache] = "icons",
            [DataModule.Logs] = "logs",
        };

    private readonly string _root;
    public DataMaintenanceService(string root) => _root = Path.GetFullPath(root);

    public IReadOnlyList<DataModulePreview> Preview() => ModulePaths.Select(pair =>
    {
        var path = Path.Combine(_root, pair.Value);
        var exists = File.Exists(path) || Directory.Exists(path);
        long bytes = File.Exists(path) ? new FileInfo(path).Length :
            Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;
        return new DataModulePreview(pair.Key, DisplayName(pair.Key), exists, bytes);
    }).ToArray();

    public string Backup(string destinationZip, DataModule modules = DataModule.AllNonSecret)
    {
        var destination = Path.GetFullPath(destinationZip);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (var pair in ModulePaths.Where(pair => modules.HasFlag(pair.Key)))
            {
                var source = Path.Combine(_root, pair.Value);
                if (File.Exists(source)) archive.CreateEntryFromFile(source, pair.Value, CompressionLevel.Optimal);
                else if (Directory.Exists(source))
                    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                        archive.CreateEntryFromFile(file, Path.GetRelativePath(_root, file), CompressionLevel.Optimal);
            }
            var manifest = archive.CreateEntry("backup-manifest.json");
            using var writer = new StreamWriter(manifest.Open());
            writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, createdAt = DateTimeOffset.UtcNow, modules = modules.ToString(), containsCredentials = false }));
        }
        File.Move(temporary, destination, true);
        return destination;
    }

    public DataMaintenanceResult Restore(string backupZip, DataModule modules)
    {
        var completed = new List<DataModule>();
        var errors = new List<string>();
        using var archive = ZipFile.OpenRead(backupZip);
        foreach (var pair in ModulePaths.Where(pair => modules.HasFlag(pair.Key)))
        {
            try
            {
                var entries = archive.Entries.Where(entry =>
                    string.Equals(entry.FullName, pair.Value, StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.StartsWith(pair.Value + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (entries.Length == 0) continue;
                foreach (var entry in entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(_root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("备份包含越界路径。");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, true);
                }
                completed.Add(pair.Key);
            }
            catch (Exception ex) { errors.Add($"{DisplayName(pair.Key)}: {ex.GetType().Name}"); }
        }
        return new DataMaintenanceResult(completed, errors);
    }

    public DataMaintenanceResult Reset(DataModule modules)
    {
        var completed = new List<DataModule>();
        var errors = new List<string>();
        foreach (var pair in ModulePaths.Where(pair => modules.HasFlag(pair.Key)))
        {
            try
            {
                var path = Path.Combine(_root, pair.Value);
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, true);
                completed.Add(pair.Key);
            }
            catch (Exception ex) { errors.Add($"{DisplayName(pair.Key)}: {ex.GetType().Name}"); }
        }
        return new DataMaintenanceResult(completed, errors);
    }

    private static string DisplayName(DataModule module) => module switch
    {
        DataModule.Settings => "应用设置", DataModule.Layout => "窗口位置", DataModule.Todos => "待办与提醒",
        DataModule.Shortcuts => "快捷入口", DataModule.SearchIndex => "搜索索引", DataModule.IconCache => "图标缓存",
        DataModule.Logs => "诊断日志", _ => module.ToString(),
    };
}
