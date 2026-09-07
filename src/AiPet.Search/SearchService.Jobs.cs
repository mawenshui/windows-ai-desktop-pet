using System.IO;

namespace AiPet.Search;

public sealed partial class SearchService
{
    private sealed class RangeJob
    {
        public CancellationTokenSource Lifetime { get; } = new();
        public SemaphoreSlim Serial { get; } = new(1,1);
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Overflow { get; set; }
        public bool Running { get; set; }
    }
    private readonly Dictionary<Guid,RangeJob> _jobs = new();
    private bool _disposed;
    private long _revision;
    public long Revision { get { lock (_indexGate) return _revision; } }
    public void SetPinned(SearchItem item,bool pinned) { lock (_indexGate) { _index.SetPinned(item,pinned); _revision++; } }
    public void RecordUse(SearchItem item) { lock (_indexGate) { _index.RecordUse(item,DateTimeOffset.UtcNow); _revision++; } }
    public void ClearHistory() { lock (_indexGate) { _index.ClearHistory(); _revision++; } }
    public (long Revision, IReadOnlyList<SearchItem> Items) SearchPage(string query, SearchItemKind? kind, SearchQueryOptions options, CancellationToken ct = default)
    { lock (_indexGate) return (_revision,_index.Search(query,kind,options,ct)); }
    private RangeJob GetJob(Guid id)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if (_index.GetRange(id) is null) throw new OperationCanceledException("授权范围已撤销。");
        if (!_jobs.TryGetValue(id,out var job)) _jobs[id]=job=new();
        return job;
    }
    private void CheckJob(Guid id, RangeJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_disposed || !_jobs.TryGetValue(id,out var current) || !ReferenceEquals(job,current) || _index.GetRange(id) is null)
            throw new OperationCanceledException("授权范围已撤销。");
    }
    private async Task RunManagedIndexAsync(Guid id,IProgress<int>? progress,int batchSize,CancellationToken ct)
    {
        if (batchSize is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        RangeJob job;
        lock (_indexGate) job=GetJob(id);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,job.Lifetime.Token);
        await job.Serial.WaitAsync(linked.Token).ConfigureAwait(false);
        try { await Task.Run(()=>IndexCore(id,job,progress,batchSize,linked.Token),linked.Token).ConfigureAwait(false); }
        finally { job.Serial.Release(); }
    }
    private void IndexCore(Guid id,RangeJob job,IProgress<int>? progress,int batchSize,CancellationToken ct)
    {
        SearchRange range;
        lock (_indexGate) { CheckJob(id,job,ct); range=_index.GetRange(id)!; _index.UpsertRange(range with { State=SearchRangeState.Preparing,LastError=null }); }
        try
        {
            if (!Directory.Exists(range.Path)) throw new DirectoryNotFoundException();
            if (!SafeMetadataPath(range.Path,range.Path)) throw new UnauthorizedAccessException();
            var batch=new List<SearchItemRow>(batchSize); var indexed=0;
            lock (_indexGate) { CheckJob(id,job,ct); _index.PrepareStagedItems(id); }
            foreach (var row in _scan(range))
            {
                ct.ThrowIfCancellationRequested();
                if (row.RangeId != id || !IsUnder(range.Path,row.FullPath)) continue;
                batch.Add(row);
                if (batch.Count<batchSize) continue;
                lock (_indexGate) { CheckJob(id,job,ct); _index.InsertStagedItems(batch); }
                indexed+=batch.Count; progress?.Report(indexed); batch.Clear();
            }
            lock (_indexGate)
            {
                CheckJob(id,job,ct);
                if (batch.Count>0) _index.InsertStagedItems(batch);
                _index.CommitStagedItems(id);
                _index.UpsertRange(range with { State=SearchRangeState.Ready,LastIndexedAt=DateTimeOffset.UtcNow,LastError=null });
                _revision++;
            }
            progress?.Report(indexed+batch.Count);
            EnsureWatcher(range);
        }
        catch (Exception ex)
        {
            lock (_indexGate)
            {
                if (!_disposed && _jobs.TryGetValue(id,out var current) && ReferenceEquals(current,job) && _index.GetRange(id) is not null)
                {
                    _index.DiscardStagedItems(id);
                    _index.UpsertRange(range with { State=ex is OperationCanceledException?SearchRangeState.Cancelled:ex is DirectoryNotFoundException?SearchRangeState.PathUnavailable:SearchRangeState.Failed,LastError=ex is OperationCanceledException?"cancelled":GetStableIndexError(ex) });
                }
            }
            throw;
        }
    }
    private void QueueChanges(Guid id,IReadOnlyList<string> paths,bool overflow)
    {
        lock (_indexGate)
        {
            if (_disposed || _index.GetRange(id) is null) return;
            var job=GetJob(id);
            foreach (var path in paths.Take(2049)) job.Paths.Add(path);
            job.Overflow |= overflow || job.Paths.Count>2048;
            if (job.Overflow) job.Paths.Clear();
            if (job.Running) return;
            job.Running=true;
            _=Task.Run(()=>ProcessChangesAsync(id,job));
        }
    }
    private async Task ProcessChangesAsync(Guid id,RangeJob job)
    {
        var completedNormally = false;
        try
        {
            while (!job.Lifetime.IsCancellationRequested)
            {
                string[] paths; bool full;
                lock (_indexGate)
                {
                    CheckJob(id,job,job.Lifetime.Token);
                    paths=job.Paths.ToArray(); job.Paths.Clear(); full=job.Overflow; job.Overflow=false;
                    if (!full && paths.Length==0) { job.Running=false; completedNormally=true; return; }
                }
                await job.Serial.WaitAsync(job.Lifetime.Token).ConfigureAwait(false);
                try
                {
                    if (full) IndexCore(id,job,null,500,job.Lifetime.Token);
                    else ReconcilePaths(id,job,paths);
                }
                finally { job.Serial.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            lock (_indexGate)
                if (!_disposed && _index.GetRange(id) is { } range)
                    _index.UpsertRange(range with { State=SearchRangeState.Failed,LastError="watcher_rebuild_required" });
        }
        finally { if (!completedNormally) { lock (_indexGate) job.Running=false; } }
    }
    private void ReconcilePaths(Guid id,RangeJob job,string[] paths)
    {
        SearchRange range;
        lock (_indexGate) { CheckJob(id,job,job.Lifetime.Token); range=_index.GetRange(id)!; }
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            job.Lifetime.Token.ThrowIfCancellationRequested();
            if (!IsUnder(range.Path,path)) continue;
            var rows=new List<SearchItemRow>();
            if (SafeMetadataPath(range.Path,path))
            {
                if (TryBuildRow(range,path) is { } row) rows.Add(row);
                if (Directory.Exists(path))
                    foreach (var child in DefaultDirectoryScanner(range with { Path=path }))
                    { job.Lifetime.Token.ThrowIfCancellationRequested(); rows.Add(child with { RelativePath=Path.GetRelativePath(range.Path,child.FullPath) }); }
            }
            lock (_indexGate)
            {
                CheckJob(id,job,job.Lifetime.Token);
                _index.ReplaceSubtree(id,path,rows);
                _revision++;
            }
        }
    }
    private static bool IsUnder(string root,string path)
    {
        try { var fullRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); var full=Path.GetFullPath(path); var prefix=Path.EndsInDirectorySeparator(fullRoot)?fullRoot:fullRoot+Path.DirectorySeparatorChar; return full.Equals(fullRoot,StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    private static bool SafeMetadataPath(string root,string path)
    {
        if (!IsUnder(root,path)) return false;
        try
        {
            for (var current=Path.GetFullPath(path); !string.IsNullOrEmpty(current); current=Path.GetDirectoryName(current))
                if (File.Exists(current)||Directory.Exists(current))
                {
                    var attributes=File.GetAttributes(current);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
                    if (IsUnder(root,current) && !Path.GetFullPath(current).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase) && (attributes&(FileAttributes.Hidden|FileAttributes.System))!=0) return false;
                }
            return true;
        }
        catch { return false; }
    }
}
