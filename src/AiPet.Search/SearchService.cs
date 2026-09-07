using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AiPet.Search;

/// <summary>
/// High-level facade. Owns the SQLite index and the indexing coroutine.
/// UI talks to this class only; index internals stay private.
/// </summary>
public sealed partial class SearchService : IDisposable
{
    private readonly SearchIndex _index;
    private readonly object _indexGate = new();
    private readonly Func<SearchRange, IEnumerable<SearchItemRow>> _scan;
    private readonly Func<IReadOnlyList<ApplicationEntry>> _appEntries;
    private readonly Dictionary<Guid, SearchIndexWatcher> _watchers = new();

    public SearchService(
        string dbPath,
        Func<SearchRange, IEnumerable<SearchItemRow>>? directoryScanner = null,
        Func<IReadOnlyList<ApplicationEntry>>? appProvider = null)
    {
        _index = new SearchIndex(dbPath);
        _scan = directoryScanner ?? DefaultDirectoryScanner;
        _appEntries = appProvider ?? DefaultApplicationProvider;
        foreach (var range in _index.ListRanges()) EnsureWatcher(range);
    }

    public IReadOnlyList<SearchRange> ListRanges() { lock (_indexGate) return _index.ListRanges(); }
    public SearchRange? GetRange(Guid id) { lock (_indexGate) return _index.GetRange(id); }
    public int CountItems(Guid id) { lock (_indexGate) return _index.CountItemsInRange(id); }

    public void AddRange(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        lock (_indexGate)
        {
            if (_index.ListRanges().Any(range =>
                string.Equals(range.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            _index.UpsertRange(SearchRange.For(path));
        }
    }
    public void RemoveRange(Guid id)
    {
        lock (_indexGate)
        {
            if (_jobs.Remove(id, out var job)) job.Lifetime.Cancel();
            if (_watchers.Remove(id, out var watcher)) watcher.Dispose();
            _index.DeleteRange(id);
            _revision++;
        }
    }

    public void MarkState(Guid id, SearchRangeState state, string? error = null, DateTimeOffset? when = null)
    {
        lock (_indexGate)
        {
            var r = _index.GetRange(id) ?? throw new InvalidOperationException("range missing");
            _index.UpsertRange(r with { State = state, LastError = error, LastIndexedAt = when ?? r.LastIndexedAt });
        }
    }

    /// <summary>
    /// Index one range. Replaces its existing items. Reports progress via
    /// <paramref name="progress"/> (called with a count after each batch).
    /// Cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public Task IndexRangeAsync(Guid rangeId, IProgress<int>? progress = null, int batchSize = 500, CancellationToken ct = default)
        => RunManagedIndexAsync(rangeId, progress, batchSize, ct);

    public Task IndexApplicationsAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var rangeId = Guid.Empty; // pseudo-range for synthetic app rows
            var rows = new List<SearchItemRow>();
            // We insert app rows under a dedicated range to keep the
            // schema homogeneous; mark it with a sentinel GUID stored in
            // the path field's first char (e.g. "<app>").
            foreach (var entry in _appEntries()
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Target))
                .GroupBy(entry => NormalizeTarget(entry.Target), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(entry => entry.DisplayName.Length).First()))
            {
                ct.ThrowIfCancellationRequested();
                // AppEntry is a stand-alone object; convert to SearchItemRow
                // via a small helper.
                rows.Add(new SearchItemRow(
                    RangeId: rangeId,
                    Name: entry.DisplayName,
                    FullPath: entry.Target,
                    RelativePath: entry.Target,
                    Extension: ".lnk",
                    Kind: SearchItemKind.Application,
                    SizeBytes: 0,
                    LastModifiedUtc: DateTimeOffset.UtcNow));
                progress?.Report(1);
            }
            lock (_indexGate)
            {
                ct.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                _index.ClearItemsForRange(rangeId);
                _index.InsertItems(rows);
                _revision++;
            }
        }, ct);

    public IReadOnlyList<SearchItem> Search(string? query, SearchItemKind? kindFilter = null, int limit = 100)
    {
        lock (_indexGate) return _index.Search(query, kindFilter, limit);
    }

    public IReadOnlyList<SearchItem> Search(
        string? query,
        SearchItemKind? kindFilter,
        SearchQueryOptions options)
    {
        lock (_indexGate) return _index.Search(query, kindFilter, options);
    }

    private void EnsureWatcher(SearchRange range)
    {
        lock (_indexGate)
        {
            if (_disposed || _index.GetRange(range.Id) is null || _watchers.ContainsKey(range.Id) || !Directory.Exists(range.Path)) return;
            try { _watchers[range.Id] = new SearchIndexWatcher(range.Path, (paths, overflowed) => QueueChanges(range.Id, paths, overflowed)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static SearchItemRow? TryBuildRow(SearchRange range, string path)
    {
        try
        {
            var root = Path.GetFullPath(range.Path);
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return new SearchItemRow(range.Id, info.Name, info.FullName, Path.GetRelativePath(root, info.FullName), string.Empty, SearchItemKind.Folder, 0, info.LastWriteTimeUtc);
            }
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return new SearchItemRow(range.Id, info.Name, info.FullName, Path.GetRelativePath(root, info.FullName), info.Extension, SearchItemKindClassifier.Classify(info.Extension, false), info.Length, info.LastWriteTimeUtc);
            }
        }
        catch { }
        return null;
    }

    private static string NormalizeTarget(string target)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)); }
        catch { return target.Trim().Trim('"'); }
    }

    // ---------------- default scan / app providers ----------------

    private static IEnumerable<SearchItemRow> DefaultDirectoryScanner(SearchRange range)
    {
        var root = range.Path;
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException();
        var rootFull = Path.GetFullPath(root);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
        };
        foreach (var dir in Directory.EnumerateDirectories(rootFull, "*", opts))
        {
            SearchItemRow? row = null;
            try
            {
                var info = new DirectoryInfo(dir);
                row = new SearchItemRow(
                    range.Id,
                    info.Name,
                    info.FullName,
                    Path.GetRelativePath(rootFull, info.FullName),
                    string.Empty,
                    SearchItemKind.Folder,
                    0,
                    info.LastWriteTimeUtc);
            }
            catch { /* skip unreadable */ }
            if (row is not null) yield return row;
        }
        foreach (var file in Directory.EnumerateFiles(rootFull, "*", opts))
        {
            SearchItemRow? row = null;
            try
            {
                var info = new FileInfo(file);
                row = new SearchItemRow(
                    range.Id,
                    info.Name,
                    info.FullName,
                    Path.GetRelativePath(rootFull, info.FullName),
                    info.Extension,
                    SearchItemKindClassifier.Classify(info.Extension, isDirectory: false),
                    info.Length,
                    info.LastWriteTimeUtc);
            }
            catch { /* skip unreadable */ }
            if (row is not null) yield return row;
        }
    }

    private static string GetStableIndexError(Exception ex) => ex switch
    {
        DirectoryNotFoundException => "path_unavailable",
        UnauthorizedAccessException => "access_denied",
        IOException => "io_error",
        _ => "index_failed",
    };

    private static IReadOnlyList<ApplicationEntry> DefaultApplicationProvider()
    {
        var list = new List<ApplicationEntry>();
        // Start Menu shortcuts
        var sm = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        ScanShortcutsForApps(sm, list);
        var smPrograms = Path.Combine(sm, "Programs");
        if (Directory.Exists(smPrograms))
            ScanShortcutsForApps(smPrograms, list);
        // App Paths registry: HKLM + HKCU \SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths
        ScanAppPaths(list);
        return list;
    }

    private static void ScanShortcutsForApps(string dir, List<ApplicationEntry> list)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                list.Add(new ApplicationEntry(name, lnk));
            }
        }
        catch { /* ignore */ }
    }

    private static void ScanAppPaths(List<ApplicationEntry> list)
    {
        const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(appPaths);
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var appKey = key.OpenSubKey(sub);
                    var path = appKey?.GetValue(string.Empty) as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (path.StartsWith('"') && path.EndsWith('"')) path = path.Substring(1, path.Length - 2);
                    if (!File.Exists(path)) continue;
                    var name = Path.GetFileNameWithoutExtension(path);
                    list.Add(new ApplicationEntry(name, path));
                }
            }
            catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        lock (_indexGate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var job in _jobs.Values) job.Lifetime.Cancel();
            _jobs.Clear();
            foreach (var watcher in _watchers.Values) watcher.Dispose();
            _watchers.Clear();
            _index.Dispose();
        }
    }
}

public sealed record ApplicationEntry(string DisplayName, string Target);
