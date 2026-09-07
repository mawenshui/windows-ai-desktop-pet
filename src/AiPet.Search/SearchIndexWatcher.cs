using System.IO;

namespace AiPet.Search;

/// <summary>Coalesces noisy FileSystemWatcher events and reports overflow for a full rebuild.</summary>
public sealed class SearchIndexWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<IReadOnlyList<string>, bool> _flush;
    private bool _overflowed;
    private bool _disposed;

    public SearchIndexWatcher(string root, Action<IReadOnlyList<string>, bool> flush, TimeSpan? debounce = null)
    {
        _flush = flush ?? throw new ArgumentNullException(nameof(flush));
        Debounce = debounce ?? TimeSpan.FromMilliseconds(350);
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 32 * 1024,
        };
        _watcher.Changed += OnPath;
        _watcher.Created += OnPath;
        _watcher.Deleted += OnPath;
        _watcher.Renamed += (_, args) => Queue(args.OldFullPath, args.FullPath);
        _watcher.Error += (_, _) => { lock (_gate) { if (_disposed) return; _overflowed = true; } Rearm(debounce); };
        _watcher.EnableRaisingEvents = true;
    }

    public TimeSpan Debounce { get; }
    private void OnPath(object sender, FileSystemEventArgs args) => Queue(args.FullPath);
    private void Queue(params string[] paths)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var path in paths) _paths.Add(path);
            if (_paths.Count > 2048) { _paths.Clear(); _overflowed = true; }
        }
        Rearm(Debounce);
    }
    private void Rearm(TimeSpan? delay) { try { _timer.Change(delay ?? Debounce, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { } }
    private void Flush()
    {
        string[] paths;
        bool overflowed;
        lock (_gate) { if (_disposed) return; paths = _paths.ToArray(); _paths.Clear(); overflowed = _overflowed; _overflowed = false; }
        try { _flush(paths, overflowed); } catch { /* Callback failures must not escape a Timer thread. */ }
    }
    public void Dispose() { lock (_gate) _disposed = true; _watcher.Dispose(); _timer.Dispose(); }
}
