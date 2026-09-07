using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AiPet.Prototypes;

public sealed record TextIndexResult(int Indexed, int Skipped, long BytesRead);

/// <summary>Experimental only. This assembly is never referenced or packaged by the app.</summary>
public sealed class ControlledTextIndex : IDisposable
{
    public const int MaximumFileBytes = 128 * 1024;
    public const int MaximumFiles = 1000;
    public const int MaximumCorpusBytes = 16 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly string _database;
    private string? _authorizedRoot;
    private CancellationTokenSource? _authorization;
    private bool _disposed;

    public ControlledTextIndex(string isolatedDirectory)
    {
        Directory.CreateDirectory(isolatedDirectory);
        _database = Path.Combine(isolatedDirectory, "experimental-content.db");
        // Consent is intentionally not persisted. Restart requires renewed explicit consent.
        Purge();
    }

    public void Authorize(string root, bool explicitContentConsent)
    {
        if (!explicitContentConsent) throw new InvalidOperationException("Separate content consent is required.");
        var full = Path.GetFullPath(root);
        EnsureNoLinks(full);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RevokeCore();
            _authorizedRoot = full;
            _authorization = new();
        }
    }

    public async Task<TextIndexResult> BuildAsync(CancellationToken token = default)
    {
        string root; CancellationToken authorization;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            root = _authorizedRoot ?? throw new InvalidOperationException("No content authorization.");
            authorization = _authorization!.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, authorization);
        return await Task.Run(() =>
        {
            var rows = new List<(string Path, string Body)>();
            var skipped = 0; long read = 0;
            // Inaccessible directories abort the staged rebuild; existing metadata search is unrelated.
            foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories=true, IgnoreInaccessible=false, AttributesToSkip=FileAttributes.ReparsePoint }))
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                EnsureNoLinks(file);
                var size = new FileInfo(file).Length;
                if (size > MaximumFileBytes) { skipped++; continue; }
                if (rows.Count >= MaximumFiles || read + size > MaximumCorpusBytes) throw new InvalidDataException("Experimental corpus budget exceeded.");
                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var bytes = new byte[MaximumFileBytes + 1];
                    var count = 0;
                    while (count < bytes.Length)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        var n = stream.Read(bytes,count,bytes.Length-count);
                        if (n==0) break;
                        count+=n;
                    }
                    if (count > MaximumFileBytes) { skipped++; continue; }
                    read += count;
                    if (read > MaximumCorpusBytes) throw new InvalidDataException("Experimental corpus budget exceeded.");
                    var body = new UTF8Encoding(false,true).GetString(bytes,0,count).TrimStart('\uFEFF');
                    if (body.Contains('\0')) { skipped++; continue; }
                    rows.Add((Path.GetRelativePath(root,file),body));
                }
                catch (DecoderFallbackException) { skipped++; }
            }
            lock (_gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                using var clear = connection.CreateCommand(); clear.Transaction=transaction; clear.CommandText="DELETE FROM content"; clear.ExecuteNonQuery();
                foreach (var row in rows)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    using var insert=connection.CreateCommand(); insert.Transaction=transaction;
                    insert.CommandText="INSERT INTO content(path,body) VALUES($path,$body)";
                    insert.Parameters.AddWithValue("$path",row.Path); insert.Parameters.AddWithValue("$body",row.Body); insert.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            return new TextIndexResult(rows.Count,skipped,read);
        }, linked.Token);
    }

    public IReadOnlyList<string> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length>100) return Array.Empty<string>();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed,this);
            if (_authorizedRoot is null || !File.Exists(_database)) return Array.Empty<string>();
            EnsureNoLinks(_authorizedRoot);
            using var connection=Open(); using var query=connection.CreateCommand();
            query.CommandText="SELECT path FROM content WHERE instr(lower(body),lower($query)) > 0 ORDER BY path LIMIT 50";
            query.Parameters.AddWithValue("$query",text);
            using var reader=query.ExecuteReader(); var matches=new List<string>();
            while (reader.Read()) matches.Add(reader.GetString(0));
            return matches;
        }
    }
    public void Revoke() { lock (_gate) RevokeCore(); }
    private void RevokeCore()
    {
        _authorization?.Cancel(); _authorization?.Dispose(); _authorization=null; _authorizedRoot=null;
        Purge();
    }
    private void Purge()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            EnsureNoLinks(_database+suffix);
            if (File.Exists(_database+suffix)) File.Delete(_database+suffix);
        }
    }
    private SqliteConnection Open()
    {
        EnsureNoLinks(_database);
        var connection=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=_database,Pooling=false}.ToString()); connection.Open();
        using var command=connection.CreateCommand(); command.CommandText="PRAGMA secure_delete=ON; CREATE TABLE IF NOT EXISTS content(path TEXT PRIMARY KEY,body TEXT NOT NULL)"; command.ExecuteNonQuery();
        return connection;
    }
    internal static void EnsureNoLinks(string path)
    {
        for (var current=Path.GetFullPath(path);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if ((File.Exists(current)||Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("Links are outside the prototype contract.");
    }
    public void Dispose() { lock (_gate) { RevokeCore(); _disposed=true; } }
}
