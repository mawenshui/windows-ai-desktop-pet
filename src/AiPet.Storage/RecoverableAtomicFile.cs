using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace AiPet.Storage;

/// <summary>
/// Writes a text file through a unique temporary file and preserves malformed
/// legacy directory targets instead of deleting them. Older preview builds
/// could accidentally create <c>settings.json</c> / <c>shortcuts.json</c> as
/// directories; keeping the directory beside the repaired file makes the
/// migration recoverable.
/// </summary>
public static class RecoverableAtomicFile
{
    private static readonly ConcurrentDictionary<string, object> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var fullPath = Path.GetFullPath(path);
        var gate = Gates.GetOrAdd(fullPath, static _ => new object());
        lock (gate)
        {
            var parent = Path.GetDirectoryName(fullPath)
                ?? throw new IOException("配置文件缺少父目录。");
            Directory.CreateDirectory(parent);
            PreserveInvalidDirectory(fullPath);
            if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                try { using var existing = JsonDocument.Parse(File.ReadAllText(fullPath)); }
                catch (JsonException ex) { throw new InvalidDataException("现有 JSON 无法读取，请先恢复备份；原文件保留。", ex); }
            }

            var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, fullPath, fullPath + ".previous");
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(fullPath, fullPath + ".previous", true);
                        File.Move(temporaryPath, fullPath, overwrite: true);
                    }
                    catch (IOException)
                    {
                        // Some redirected/network-backed profile folders do not
                        // support ReplaceFile even though an overwrite move works.
                        File.Copy(fullPath, fullPath + ".previous", true);
                        File.Move(temporaryPath, fullPath, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, fullPath);
                }
            }
            finally
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch { /* A stale unique temp is safe and can be retried later. */ }
            }
        }
    }

    private static void PreserveInvalidDirectory(string targetPath)
    {
        if (!Directory.Exists(targetPath)) return;

        var backupPath = targetPath + ".invalid-directory-backup";
        for (var suffix = 2; Directory.Exists(backupPath) || File.Exists(backupPath); suffix++)
            backupPath = targetPath + $".invalid-directory-backup-{suffix}";

        Directory.Move(targetPath, backupPath);
    }
}
