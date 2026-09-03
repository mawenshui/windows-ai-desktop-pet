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
        _watcher.Error += (_, _) => { lock (_gate) _overflowed = true; Rearm(debounce); };
        _watcher.EnableRaisingEvents = true;
    }

    public TimeSpan Debounce { get; }
    private void OnPath(object sender, FileSystemEventArgs args) => Queue(args.FullPath);
    private void Queue(params string[] paths)
    {
        lock (_gate) foreach (var path in paths) _paths.Add(path);
        Rearm(Debounce);
    }
    private void Rearm(TimeSpan? delay) => _timer.Change(delay ?? Debounce, Timeout.InfiniteTimeSpan);
    private void Flush()
    {
        string[] paths;
        bool overflowed;
        lock (_gate) { paths = _paths.ToArray(); _paths.Clear(); overflowed = _overflowed; _overflowed = false; }
        _flush(paths, overflowed);
    }
    public void Dispose() { _watcher.Dispose(); _timer.Dispose(); }
}
