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
public sealed class SearchService : IDisposable
{
    private readonly SearchIndex _index;
    private readonly object _indexGate = new();
    private readonly Func<SearchRange, IReadOnlyList<SearchItemRow>> _scan;
    private readonly Func<IReadOnlyList<ApplicationEntry>> _appEntries;

    public SearchService(
        string dbPath,
        Func<SearchRange, IReadOnlyList<SearchItemRow>>? directoryScanner = null,
        Func<IReadOnlyList<ApplicationEntry>>? appProvider = null)
    {
        _index = new SearchIndex(dbPath);
        _scan = directoryScanner ?? DefaultDirectoryScanner;
        _appEntries = appProvider ?? DefaultApplicationProvider;
    }

    public IReadOnlyList<SearchRange> ListRanges() { lock (_indexGate) return _index.ListRanges(); }
    public SearchRange? GetRange(Guid id) { lock (_indexGate) return _index.GetRange(id); }
    public int CountItems(Guid id) { lock (_indexGate) return _index.CountItemsInRange(id); }

    public void AddRange(string path)
    {
        lock (_indexGate)
        {
            if (_index.ListRanges().Any(range =>
                string.Equals(range.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            _index.UpsertRange(SearchRange.For(path));
        }
    }
    public void RemoveRange(Guid id) { lock (_indexGate) _index.DeleteRange(id); }

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
    public Task IndexRangeAsync(
        Guid rangeId,
        IProgress<int>? progress = null,
        int batchSize = 500,
        CancellationToken ct = default)
        => Task.Run(() =>
        {
            SearchRange range;
            lock (_indexGate) range = _index.GetRange(rangeId) ?? throw new InvalidOperationException("range missing");
            MarkState(rangeId, SearchRangeState.Preparing);
            try
            {
                var rows = _scan(range);
                var batch = new List<SearchItemRow>(batchSize);
                lock (_indexGate) _index.ClearItemsForRange(rangeId);
                foreach (var row in rows)
                {
                    ct.ThrowIfCancellationRequested();
                    batch.Add(row);
                    if (batch.Count >= batchSize)
                    {
                        lock (_indexGate) _index.InsertItems(batch);
                        progress?.Report(batch.Count);
                        batch.Clear();
                    }
                }
                if (batch.Count > 0) lock (_indexGate) _index.InsertItems(batch);
                lock (_indexGate)
                    _index.UpsertRange(range with
                    {
                        State = SearchRangeState.Ready,
                        LastIndexedAt = DateTimeOffset.UtcNow,
                        LastError = null,
                    });
            }
            catch (OperationCanceledException)
            {
                lock (_indexGate)
                    _index.UpsertRange(range with { State = SearchRangeState.NotConfigured, LastError = "cancelled" });
                throw;
            }
            catch (Exception ex)
            {
                lock (_indexGate)
                    _index.UpsertRange(range with { State = SearchRangeState.Failed, LastError = ex.Message });
                throw;
            }
        }, ct);

    public Task IndexApplicationsAsync(IProgress<int>? progress = null, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var rangeId = Guid.Empty; // pseudo-range for synthetic app rows
            var rows = new List<SearchItemRow>();
            // We insert app rows under a dedicated range to keep the
            // schema homogeneous; mark it with a sentinel GUID stored in
            // the path field's first char (e.g. "<app>").
            foreach (var entry in _appEntries())
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
                _index.ClearItemsForRange(rangeId);
                _index.InsertItems(rows);
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

    // ---------------- default scan / app providers ----------------

    private static IReadOnlyList<SearchItemRow> DefaultDirectoryScanner(SearchRange range)
    {
        var root = range.Path;
        if (!Directory.Exists(root)) return Array.Empty<SearchItemRow>();
        var list = new List<SearchItemRow>();
        var rootFull = Path.GetFullPath(root);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.Hidden | FileAttributes.ReparsePoint,
        };
        foreach (var dir in Directory.EnumerateDirectories(rootFull, "*", opts))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                list.Add(new SearchItemRow(
                    range.Id,
                    info.Name,
                    info.FullName,
                    Path.GetRelativePath(rootFull, info.FullName),
                    string.Empty,
                    SearchItemKind.Folder,
                    0,
                    info.LastWriteTimeUtc));
            }
            catch { /* skip unreadable */ }
        }
        foreach (var file in Directory.EnumerateFiles(rootFull, "*", opts))
        {
            try
            {
                var info = new FileInfo(file);
                list.Add(new SearchItemRow(
                    range.Id,
                    info.Name,
                    info.FullName,
                    Path.GetRelativePath(rootFull, info.FullName),
                    info.Extension,
                    SearchItemKindClassifier.Classify(info.Extension, isDirectory: false),
                    info.Length,
                    info.LastWriteTimeUtc));
            }
            catch { /* skip unreadable */ }
        }
        return list;
    }

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
        lock (_indexGate) _index.Dispose();
    }
}

public sealed record ApplicationEntry(string DisplayName, string Target);
