using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiPet.Storage;

[Flags]
public enum DataModule
{
    None = 0, Settings = 1, Layout = 2, Todos = 4, Shortcuts = 8,
    SearchIndex = 16, IconCache = 32, Logs = 64, Notifications = 128, ProviderPresets = 256,
    Journal = 512, FocusSessions = 1024,
    AllNonSecret = Settings | Layout | Todos | Shortcuts | SearchIndex | IconCache | Logs | Notifications | ProviderPresets | Journal | FocusSessions,
}
public sealed record DataModulePreview(DataModule Module, string DisplayName, bool Exists, long Bytes);
public sealed record RestorePreview(DataModule Module, string DisplayName, long CurrentBytes, long IncomingBytes, int FileCount);
public sealed record DataMaintenanceResult(IReadOnlyList<DataModule> Completed, IReadOnlyList<string> Errors);

/// <summary>Validated transactions. Restore/Reset require an offline host. Queue methods
/// are safe during normal operation; App applies them before creating stores or workers.</summary>
public sealed partial class DataMaintenanceService
{
    private static readonly IReadOnlyDictionary<DataModule, string> ModulePaths = new Dictionary<DataModule, string>
    {
        [DataModule.Settings]="settings.json", [DataModule.Layout]="layout.json",
        [DataModule.Todos]="todos.json", [DataModule.Shortcuts]="shortcuts.json",
        [DataModule.SearchIndex]="index.db", [DataModule.IconCache]="icons", [DataModule.Logs]="logs",
        [DataModule.Notifications]="notifications.json",
        [DataModule.ProviderPresets]="provider-presets.json",
        [DataModule.Journal]="journal.json",
        [DataModule.FocusSessions]="focus-sessions.json",
    };
    public const long MaximumBackupBytes = 512L * 1024 * 1024;
    private const long MaximumEntryBytes = 64L * 1024 * 1024;
    private readonly string _root;
    private readonly string _logs;
    private readonly Action<DataModule>? _afterApply;
    public DataMaintenanceService(string root, string? logDirectory = null, Action<DataModule>? afterApply = null)
    {
        _root = Path.GetFullPath(root);
        _logs = Path.GetFullPath(logDirectory ?? Path.Combine(_root, "logs"));
        _afterApply = afterApply;
    }
    private string WorkRoot => Path.Combine(_root, ".maintenance");
    private string Target(DataModule module) => module == DataModule.Logs ? _logs : Path.Combine(_root, ModulePaths[module]);
    private static IEnumerable<string> Files(string path)
    {
        if (File.Exists(path)) { CheckNoLinks(path); return new[] { path }; }
        if (!Directory.Exists(path)) return Array.Empty<string>();
        CheckNoLinks(path);
        return Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories=true, IgnoreInaccessible=false, AttributesToSkip=FileAttributes.ReparsePoint }).ToArray();
    }
    private static void CheckNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("维护路径不能经过链接或重解析点。");
    }
    public IReadOnlyList<DataModulePreview> Preview() => ModulePaths.Select(pair => new DataModulePreview(pair.Key,
        DisplayName(pair.Key), File.Exists(Target(pair.Key)) || Directory.Exists(Target(pair.Key)),
        Files(Target(pair.Key)).Sum(file => new FileInfo(file).Length))).ToArray();

    public string Backup(string destinationZip, DataModule modules = DataModule.AllNonSecret & ~DataModule.SearchIndex)
    {
        // SQLite is derived data and may have an active WAL. It is always rebuilt, never copied live.
        modules = WithDependencies(modules);
        var destination = Path.GetFullPath(destinationZip);
        CheckNoLinks(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var hashes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                long total = 0;
                foreach (var pair in ModulePaths.Where(pair => modules.HasFlag(pair.Key)))
                foreach (var file in Files(Target(pair.Key)))
                {
                    CheckNoLinks(file);
                    var length = new FileInfo(file).Length;
                    total += length;
                    if (length > MaximumEntryBytes || total > MaximumBackupBytes || hashes.Count >= 10000)
                        throw new InvalidDataException("备份超过容量或文件数量上限。");
                    var name = Directory.Exists(Target(pair.Key)) ? pair.Value + "/" + Path.GetRelativePath(Target(pair.Key), file).Replace('\\','/') : pair.Value;
                    if (Path.GetFullPath(file).Equals(destination, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份不能写入所选模块内部。");
                    var bytes = File.ReadAllBytes(file);
                    if (bytes.LongLength > MaximumEntryBytes) throw new InvalidDataException("备份过程中模块容量已变化。");
                    if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !name.Contains('/')) ValidateJson(name, bytes);
                    var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                    using (var output = entry.Open()) output.Write(bytes);
                    hashes[name] = Convert.ToHexString(SHA256.HashData(bytes));
                }
                using var writer = new StreamWriter(archive.CreateEntry("backup-manifest.json").Open());
                writer.Write(JsonSerializer.Serialize(new { schemaVersion=2, createdAt=DateTimeOffset.UtcNow, modules=modules.ToString(), containsCredentials=false, files=hashes }));
            }
            File.Move(temporary, destination, true);
            return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Dictionary<string, byte[]> ReadArchive(string source)
    {
        if (new FileInfo(source).Length > MaximumBackupBytes) throw new InvalidDataException("备份文件过大。");
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Count > 10001) throw new InvalidDataException("备份文件数量过多。");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\','/');
            var parts = name.Split('/');
            if (parts.Any(part => part is "." or ".." || part.Contains(':') || part.EndsWith(' ') || part.EndsWith('.')) || name.StartsWith('/'))
                throw new InvalidDataException("备份包含越界或无效路径。");
            if (name.EndsWith('/')) continue;
            if (name != "backup-manifest.json" && !ModulePaths.Values.Any(path => name.Equals(path, StringComparison.OrdinalIgnoreCase) || (path is "icons" or "logs" && name.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("备份包含未知模块。");
            total += entry.Length;
            if (entry.Length > MaximumEntryBytes || total > MaximumBackupBytes || files.ContainsKey(name)) throw new InvalidDataException("备份容量、数量或重复路径无效。");
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                if (memory.Length + count > entry.Length || memory.Length + count > MaximumEntryBytes) throw new InvalidDataException("解压容量与清单不符。");
                memory.Write(buffer,0,count);
            }
            if (memory.Length != entry.Length) throw new InvalidDataException("备份内容不完整。");
            files.Add(name, memory.ToArray());
        }
        if (!files.TryGetValue("backup-manifest.json", out var manifestBytes)) throw new InvalidDataException("缺少备份清单。");
        using var manifest = JsonDocument.Parse(manifestBytes);
        var version = manifest.RootElement.GetProperty("schemaVersion").GetInt32();
        if (version is < 1 or > 2 || manifest.RootElement.GetProperty("containsCredentials").GetBoolean()) throw new InvalidDataException("不支持的备份格式。");
        foreach (var (name, bytes) in files.Where(pair => pair.Key != "backup-manifest.json"))
        {
            if (version == 2 && (!manifest.RootElement.GetProperty("files").TryGetProperty(name, out var expected) ||
                !string.Equals(expected.GetString(), Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("备份校验和不匹配。");
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !name.Contains('/')) ValidateJson(name, bytes);
            if (name.StartsWith("icons/", StringComparison.OrdinalIgnoreCase) && !new[] { ".png", ".ico", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(name).ToLowerInvariant()))
                throw new InvalidDataException("图标格式不支持。");
        }
        return files;
    }
    public static void ValidateJson(string name, byte[] bytes)
    {
        if (name == "journal.json" && bytes.LongLength > 32L * 1024 * 1024)
            throw new InvalidDataException("复盘数据文件超过 32 MiB 上限。");
        if (name == "focus-sessions.json" && bytes.LongLength > 16L * 1024 * 1024)
            throw new InvalidDataException("专注记录超过 16 MiB 上限。");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth=32 });
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("模块必须是 JSON 对象。");
        if (name != "layout.json")
        {
            var max = name is "shortcuts.json" or "notifications.json" or "provider-presets.json" or "journal.json" or "focus-sessions.json" ? 1 : name == "settings.json" ? 6 : name == "todos.json" ? 4 : 3;
            if (!json.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() < 1 || schema.GetInt32() > max) throw new InvalidDataException("模块 schema 不受支持。");
            if (name is "todos.json" or "shortcuts.json" && (!json.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array))
                throw new InvalidDataException("事项列表无效。");
        }
        ValidateModuleShape(name, json.RootElement);
        RejectSecrets(json.RootElement);
    }
    private static void RejectSecrets(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (new[] { "apikey", "password", "token", "secret" }.Contains(property.Name.ToLowerInvariant())) throw new InvalidDataException("备份不能包含凭据字段。");
                RejectSecrets(property.Value);
            }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectSecrets(child);
    }
    private static bool Belongs(string name, DataModule module) => name.Equals(ModulePaths[module], StringComparison.OrdinalIgnoreCase) ||
        (module is DataModule.IconCache or DataModule.Logs && name.StartsWith(ModulePaths[module] + "/", StringComparison.OrdinalIgnoreCase));
    public IReadOnlyList<RestorePreview> PreviewRestore(string source)
    {
        var files = ReadArchive(source);
        return ModulePaths.Where(pair => pair.Key != DataModule.SearchIndex).Select(pair =>
        {
            var incoming = files.Where(file => Belongs(file.Key,pair.Key)).ToArray();
            return new RestorePreview(pair.Key, DisplayName(pair.Key), Files(Target(pair.Key)).Sum(file => new FileInfo(file).Length), incoming.Sum(file => (long)file.Value.Length), incoming.Length);
        }).ToArray();
    }
    public void QueueRestore(string source, DataModule modules)
    {
        _ = ReadArchive(source);
        modules = WithDependencies(modules);
        Directory.CreateDirectory(WorkRoot);
        if (File.Exists(Path.Combine(WorkRoot,"pending.json"))) throw new InvalidOperationException("已有待执行维护，请先重启应用。");
        var copy = Path.Combine(WorkRoot, "pending.zip");
        File.Copy(source, copy, true);
        RecoverableAtomicFile.WriteAllText(Path.Combine(WorkRoot,"pending.json"), JsonSerializer.Serialize(new { action="restore", modules, sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(copy))) }));
    }
    public void QueueReset(DataModule modules)
    {
        if ((modules & ~(DataModule.SearchIndex | DataModule.Logs)) != 0) throw new InvalidOperationException("运行中只允许排队重建索引和清理日志。");
        Directory.CreateDirectory(WorkRoot);
        if (File.Exists(Path.Combine(WorkRoot,"pending.json"))) throw new InvalidOperationException("已有待执行维护。");
        RecoverableAtomicFile.WriteAllText(Path.Combine(WorkRoot,"pending.json"), JsonSerializer.Serialize(new { action="reset", modules }));
    }
    public DataMaintenanceResult ApplyPending()
    {
        RecoverInterrupted();
        var pending = Path.Combine(WorkRoot,"pending.json");
        if (!File.Exists(pending)) return new(Array.Empty<DataModule>(),Array.Empty<string>());
        using var json = JsonDocument.Parse(File.ReadAllText(pending));
        var modules = (DataModule)json.RootElement.GetProperty("modules").GetInt32();
        DataMaintenanceResult result;
        if (json.RootElement.GetProperty("action").GetString() == "restore")
        {
            var zip = Path.Combine(WorkRoot,"pending.zip");
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))) != json.RootElement.GetProperty("sha256").GetString()) throw new InvalidDataException("待恢复备份已改变。");
            result = Restore(zip, modules);
        }
        else result = Reset(modules);
        File.Move(pending, Path.Combine(WorkRoot,"last-operation.json"), true);
        return result;
    }
    public DataMaintenanceResult Restore(string source, DataModule modules)
    {
        var files = ReadArchive(source); // Complete validation before touching live data.
        modules = WithDependencies(modules);
        if (modules.HasFlag(DataModule.Todos) && files.ContainsKey("todos.json") && !files.ContainsKey("notifications.json"))
        {
            using var todos = JsonDocument.Parse(files["todos.json"]);
            if (todos.RootElement.GetProperty("items").EnumerateArray().Any(item => item.TryGetProperty("reminderState", out var state) && (state.ToString()=="Queued" || state.ToString()=="6")))
                throw new InvalidDataException("排队中的提醒缺少配套队列，不能恢复。");
            files["notifications.json"] = System.Text.Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"entries\":[]}");
        }
        var selected = ModulePaths.Keys.Where(module => modules.HasFlag(module) && files.Keys.Any(name => Belongs(name,module))).ToArray();
        var transaction = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        var snapshots = selected.Select(module => new Snapshot(module, File.Exists(Target(module)) || Directory.Exists(Target(module)))).ToArray();
        foreach (var snapshot in snapshots) CopyTree(Target(snapshot.Module), Path.Combine(transaction,"before",ModulePaths[snapshot.Module]));
        var journal = Path.Combine(transaction,"journal.json");
        RecoverableAtomicFile.WriteAllText(journal, JsonSerializer.Serialize(new Journal("applying",snapshots)));
        try
        {
            foreach (var module in selected)
            {
                var incoming = Path.Combine(transaction,"incoming",ModulePaths[module]);
                foreach (var (name, bytes) in files.Where(pair => Belongs(pair.Key,module)))
                {
                    var destination = Path.Combine(transaction,"incoming",name.Replace('/',Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination,bytes);
                }
                if (module == DataModule.Shortcuts) RebaseIcons(incoming, files);
                CheckNoLinks(Target(module));
                DeleteTree(Target(module));
                CopyTree(incoming, Target(module));
                _afterApply?.Invoke(module);
            }
            // Restored settings can revoke authorizations: discard old derived metadata.
            if (selected.Contains(DataModule.Settings) && Reset(DataModule.SearchIndex).Errors.Count > 0)
                throw new IOException("旧授权索引无法清除。");
            RecoverableAtomicFile.WriteAllText(journal, JsonSerializer.Serialize(new Journal("committed",snapshots)));
            return new(selected, Array.Empty<string>());
        }
        catch
        {
            Rollback(transaction, snapshots);
            RecoverableAtomicFile.WriteAllText(journal, JsonSerializer.Serialize(new Journal("rolled-back",snapshots)));
            return new(Array.Empty<DataModule>(), new[] { "restore_failed_rolled_back" });
        }
    }
    private void RebaseIcons(string shortcuts, Dictionary<string,byte[]> files)
    {
        var document = JsonNode.Parse(File.ReadAllText(shortcuts))!;
        foreach (var item in document["items"]!.AsArray())
        {
            var icon = item?["iconPath"]?.GetValue<string>();
            if (string.IsNullOrEmpty(icon)) continue;
            var filename = Path.GetFileName(icon.Replace('\\','/'));
            item!["iconPath"] = files.ContainsKey("icons/" + filename) ? Path.Combine(_root,"icons",filename) : null;
        }
        File.WriteAllText(shortcuts, document.ToJsonString());
    }
    public void RecoverInterrupted()
    {
        if (!Directory.Exists(WorkRoot)) return;
        CheckNoLinks(WorkRoot);
        foreach (var directory in Directory.EnumerateDirectories(WorkRoot))
        {
            var path = Path.Combine(directory,"journal.json");
            if (!File.Exists(path)) continue;
            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path)) ?? throw new InvalidDataException("恢复日志无效。");
            if (journal.Phase != "applying") continue;
            Rollback(directory,journal.Snapshots);
            RecoverableAtomicFile.WriteAllText(path, JsonSerializer.Serialize(journal with { Phase="rolled-back" }));
        }
    }
    private void Rollback(string directory, Snapshot[] snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            DeleteTree(Target(snapshot.Module));
            if (snapshot.Existed) CopyTree(Path.Combine(directory,"before",ModulePaths[snapshot.Module]),Target(snapshot.Module));
        }
    }
    private static void CopyTree(string source, string destination)
    {
        CheckNoLinks(source); CheckNoLinks(destination);
        if (File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source,destination,true); }
        else if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Files(source)) { var target=Path.Combine(destination,Path.GetRelativePath(source,file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file,target,true); }
        }
    }
    private static void DeleteTree(string path)
    {
        CheckNoLinks(path);
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path,true);
    }
    public DataMaintenanceResult Reset(DataModule modules)
    {
        var completed=new List<DataModule>(); var errors=new List<string>();
        foreach (var module in ModulePaths.Keys.Where(module => modules.HasFlag(module)))
        {
            try
            {
                DeleteTree(Target(module));
                if (module == DataModule.SearchIndex) foreach (var suffix in new[] { "-wal","-shm" }) DeleteTree(Target(module)+suffix);
                completed.Add(module);
            }
            catch { errors.Add("reset_failed_" + module); }
        }
        return new(completed,errors);
    }
    private sealed record Snapshot(DataModule Module, bool Existed);
    private sealed record Journal(string Phase, Snapshot[] Snapshots);
    private static string DisplayName(DataModule module) => module switch
    {
        DataModule.Settings=>"应用设置", DataModule.Layout=>"窗口位置", DataModule.Todos=>"待办与提醒",
        DataModule.Shortcuts=>"快捷入口", DataModule.SearchIndex=>"搜索索引", DataModule.IconCache=>"自定义图标", DataModule.Logs=>"诊断日志", DataModule.Notifications=>"提醒队列与历史", DataModule.ProviderPresets=>"AI 服务预设", DataModule.Journal=>"每日复盘", DataModule.FocusSessions=>"专注记录", _=>module.ToString(),
    };
    private static DataModule WithDependencies(DataModule modules)
    {
        if ((modules & ~DataModule.AllNonSecret) != 0) throw new InvalidDataException("未知维护模块。");
        modules &= ~DataModule.SearchIndex;
        if (modules.HasFlag(DataModule.Shortcuts)) modules |= DataModule.IconCache;
        if ((modules & (DataModule.Todos | DataModule.Notifications)) != 0) modules |= DataModule.Todos | DataModule.Notifications;
        return modules;
    }
}
