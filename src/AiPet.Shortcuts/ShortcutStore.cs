using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiPet.Storage;

namespace AiPet.Shortcuts;

/// <summary>
/// Persists <see cref="ShortcutItem"/>s to <c>shortcuts.json</c> under
/// <c>%APPDATA%\WindowsAiDesktopPet\</c> and exposes launch semantics
/// aligned with PRD §3.2 (QCK-05). Corrupted files fall back to an
/// empty list (no destructive overwrite).
/// </summary>
public sealed partial class ShortcutStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public string AppDataDir { get; }
    public string ShortcutsPath { get; }
    public string IconsDir    { get; }

    public ShortcutStore(string? overrideRoot = null)
    {
        AppDataDir = overrideRoot
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WindowsAiDesktopPet");
        ShortcutsPath = Path.Combine(AppDataDir, "shortcuts.json");
        IconsDir = Path.Combine(AppDataDir, "icons");
    }

    public IReadOnlyList<ShortcutItem> Load()
    {
        try
        {
            if (!File.Exists(ShortcutsPath)) return Array.Empty<ShortcutItem>();
            var text = File.ReadAllText(ShortcutsPath);
            var f = JsonSerializer.Deserialize<ShortcutsFile>(text, Options);
            if (f is null) return Array.Empty<ShortcutItem>();
            return f.Items.OrderByDescending(i => i.Pinned).ThenBy(i => i.Order).ThenBy(i => i.CreatedAt).ToList();
        }
        catch
        {
            return Array.Empty<ShortcutItem>();
        }
    }

    public void Save(IEnumerable<ShortcutItem> items)
    {
        if (File.Exists(ShortcutsPath)) DataMaintenanceService.ValidateJson("shortcuts.json", File.ReadAllBytes(ShortcutsPath));
        var list = items.ToList();
        if (list.Any(item => item.Id == Guid.Empty || item.Group?.Length > 40) || list.Select(item => item.Id).Distinct().Count() != list.Count)
            throw new InvalidOperationException("快捷入口 ID 或分组无效。");
        var f = new ShortcutsFile { Items = list };
        RecoverableAtomicFile.WriteAllText(
            ShortcutsPath,
            JsonSerializer.Serialize(f, Options));
    }

    public bool ContainsTarget(string targetPath) =>
        Load().Any(item => TargetsMatch(item.TargetPath, targetPath));

    public ShortcutItem Add(ShortcutItem item, bool allowDuplicate = false)
    {
        var all = Load().ToList();
        // PRD QCK-02: the UI may continue only after explicit confirmation.
        if (!allowDuplicate && all.Any(i => TargetsMatch(i.TargetPath, item.TargetPath)))
            throw new InvalidOperationException("该目标已存在快捷项");
        item.CreatedAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = item.CreatedAt;
        item.Order = all.Count == 0 ? 0 : all.Max(i => i.Order) + 1;
        all.Add(item);
        Save(all);
        return item;
    }

    public ShortcutBatchReport AddMany(IEnumerable<ShortcutItem> candidates)
    {
        var added = 0;
        var duplicates = 0;
        var invalid = 0;
        var failures = 0;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.TargetPath) || !candidate.ExistsNow) { invalid++; continue; }
            try { Add(candidate); added++; }
            catch (InvalidOperationException) { duplicates++; }
            catch { failures++; }
        }
        return new ShortcutBatchReport(added, duplicates, invalid, failures);
    }

    private static bool TargetsMatch(string left, string right)
    {
        static string Normalize(string path)
        {
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch { return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    public void Update(ShortcutItem item)
    {
        var all = Load().ToList();
        var idx = all.FindIndex(i => i.Id == item.Id);
        if (idx < 0) throw new InvalidOperationException("快捷项不存在");
        item.UpdatedAt = DateTimeOffset.UtcNow;
        all[idx] = item;
        Save(all);
    }

    public void Remove(Guid id)
    {
        var all = Load().ToList();
        all.RemoveAll(i => i.Id == id);
        Save(all);
    }

    public void Reorder(IEnumerable<Guid> orderedIds)
    {
        var all = Load().ToDictionary(i => i.Id);
        var list = new List<ShortcutItem>();
        var placed = new HashSet<Guid>();
        var order = 0;
        foreach (var id in orderedIds)
        {
            if (!placed.Contains(id) && all.TryGetValue(id, out var it))
            {
                it.Order = order++;
                list.Add(it);
                placed.Add(id);
            }
        }
        foreach (var item in all.Values.Where(item => !placed.Contains(item.Id)).OrderBy(item => item.Order).ThenBy(item => item.CreatedAt))
        {
            item.Order = order++;
            list.Add(item);
        }
        Save(list);
    }

    public int CleanupUnreferencedIcons()
    {
        if (!Directory.Exists(IconsDir)) return 0;
        var referenced = Load().Concat(_removedForUndo).Select(item => item.IconPath).Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(IconsDir))
        {
            if (referenced.Contains(Path.GetFullPath(file))) continue;
            try { File.Delete(file); deleted++; } catch { }
        }
        return deleted;
    }

    public ShortcutLaunchOutcome Launch(ShortcutItem item)
    {
        if (!item.ExistsNow) return ShortcutLaunchOutcome.Missing();
        try
        {
            if (item.Kind == ShortcutKind.Url)
            {
                Process.Start(new ProcessStartInfo(item.TargetPath) { UseShellExecute = true });
                return ShortcutLaunchOutcome.Ok();
            }
            // For files and folders, prefer ShellExecute so Windows can use
            // the user's default association (Explorer for a folder and the
            // registered handler for a document or application shortcut).
            Process.Start(new ProcessStartInfo(item.TargetPath) { UseShellExecute = true });
            return ShortcutLaunchOutcome.Ok();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1155) // no app associated
        {
            return ShortcutLaunchOutcome.NoAssoc();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 0x522)
        {
            return ShortcutLaunchOutcome.Denied();
        }
        catch (Exception ex)
        {
            return ShortcutLaunchOutcome.Other(ex.Message);
        }
    }

    public string? CopyIcon(string sourceIconPath)
    {
        if (!File.Exists(sourceIconPath)) return null;
        Directory.CreateDirectory(IconsDir);
        var ext = Path.GetExtension(sourceIconPath);
        if (string.IsNullOrEmpty(ext)) ext = ".ico";
        var dest = Path.Combine(IconsDir, Guid.NewGuid().ToString("N") + ext);
        File.Copy(sourceIconPath, dest, true);
        return dest;
    }
}

public sealed record ShortcutBatchReport(int Added, int Duplicates, int Invalid, int Failed);
