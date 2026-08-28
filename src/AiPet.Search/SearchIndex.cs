using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AiPet.Search;

/// <summary>
/// SQLite-backed search index. We deliberately use a tiny hand-written
/// schema (no FTS5 / no ORM) so the binary stays a self-contained ~5 MB
/// Windows desktop app and the test surface is small and easy to mock.
/// </summary>
public sealed class SearchIndex : IDisposable
{
    private readonly SqliteConnection _conn;

    public SearchIndex(string dbPath)
    {
        // Microsoft.Data.Sqlite 7+ ships the e_sqlite3 provider via its
        // dependency on SQLitePCLRaw.batteries_e_sqlite3, so no explicit
        // init is required here. The Core package (without the bundle)
        // would need SQLitePCL.Batteries.Init() at startup.
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var csb = new SqliteConnectionStringBuilder { DataSource = dbPath };
        _conn = new SqliteConnection(csb.ConnectionString);
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ranges (
                id            TEXT PRIMARY KEY,
                path          TEXT NOT NULL,
                state         INTEGER NOT NULL,
                last_error    TEXT,
                last_index_at TEXT
            );
            CREATE TABLE IF NOT EXISTS items (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                range_id      TEXT NOT NULL,
                name          TEXT NOT NULL,
                full_path     TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                extension     TEXT NOT NULL,
                kind          INTEGER NOT NULL,
                size_bytes    INTEGER NOT NULL,
                last_modified TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_items_name ON items(name COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_items_range ON items(range_id);
            """;
        cmd.ExecuteNonQuery();
    }

    // ---------------- ranges ----------------

    public void UpsertRange(SearchRange range)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ranges (id, path, state, last_error, last_index_at)
            VALUES ($id, $path, $state, $err, $at)
            ON CONFLICT(id) DO UPDATE SET
                path = excluded.path,
                state = excluded.state,
                last_error = excluded.last_error,
                last_index_at = excluded.last_index_at;
            """;
        cmd.Parameters.AddWithValue("$id",   range.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$path", range.Path);
        cmd.Parameters.AddWithValue("$state",(int)range.State);
        cmd.Parameters.AddWithValue("$err",  (object?)range.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$at",   range.LastIndexedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public SearchRange? GetRange(Guid id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT path, state, last_error, last_index_at FROM ranges WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SearchRange(
            id,
            r.GetString(0),
            (SearchRangeState)r.GetInt32(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture));
    }

    public IReadOnlyList<SearchRange> ListRanges()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, path, state, last_error, last_index_at FROM ranges ORDER BY path";
        using var r = cmd.ExecuteReader();
        var list = new List<SearchRange>();
        while (r.Read())
        {
            list.Add(new SearchRange(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                (SearchRangeState)r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture)));
        }
        return list;
    }

    public void DeleteRange(Guid id)
    {
        using var tx = _conn.BeginTransaction();
        using (var delItems = _conn.CreateCommand())
        {
            delItems.Transaction = tx;
            delItems.CommandText = "DELETE FROM items WHERE range_id = $id";
            delItems.Parameters.AddWithValue("$id", id.ToString("D"));
            delItems.ExecuteNonQuery();
        }
        using (var delRange = _conn.CreateCommand())
        {
            delRange.Transaction = tx;
            delRange.CommandText = "DELETE FROM ranges WHERE id = $id";
            delRange.Parameters.AddWithValue("$id", id.ToString("D"));
            delRange.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public int CountItemsInRange(Guid id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM items WHERE range_id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    // ---------------- items ----------------

    public void InsertItems(IReadOnlyList<SearchItemRow> rows)
    {
        if (rows.Count == 0) return;
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO items (range_id, name, full_path, relative_path, extension, kind, size_bytes, last_modified)
            VALUES ($rid, $n, $fp, $rp, $ex, $k, $sz, $lm);
            """;
        var pRid = cmd.Parameters.Add("$rid", SqliteType.Text);
        var pN   = cmd.Parameters.Add("$n",   SqliteType.Text);
        var pFp  = cmd.Parameters.Add("$fp",  SqliteType.Text);
        var pRp  = cmd.Parameters.Add("$rp",  SqliteType.Text);
        var pEx  = cmd.Parameters.Add("$ex",  SqliteType.Text);
        var pK   = cmd.Parameters.Add("$k",   SqliteType.Integer);
        var pSz  = cmd.Parameters.Add("$sz",  SqliteType.Integer);
        var pLm  = cmd.Parameters.Add("$lm",  SqliteType.Text);
        foreach (var r in rows)
        {
            pRid.Value = r.RangeId.ToString("D");
            pN.Value   = r.Name;
            pFp.Value  = r.FullPath;
            pRp.Value  = r.RelativePath;
            pEx.Value  = r.Extension;
            pK.Value   = (int)r.Kind;
            pSz.Value  = r.SizeBytes;
            pLm.Value  = r.LastModifiedUtc.ToString("O");
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void ClearItemsForRange(Guid rangeId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE range_id = $id";
        cmd.Parameters.AddWithValue("$id", rangeId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Substring (case-insensitive) search. We use a single LIKE so the
    /// plan is a fast index scan on <c>idx_items_name</c>. Per PRD §3
    /// SRCH-06, "exact" ranking is left as a future improvement.
    /// </summary>
    public IReadOnlyList<SearchItem> Search(
        string? query,
        SearchItemKind? kindFilter,
        SearchQueryOptions options)
    {
        var normalized = query?.Trim() ?? string.Empty;
        var mode = GetMatchMode(normalized, options);
        Regex? regex = null;
        if (mode == SearchMatchMode.Regex)
        {
            if (normalized.Length == 3)
                throw new SearchQueryException("请在 re: 后输入正则表达式。");
            try
            {
                regex = new Regex(
                    normalized[3..],
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException ex)
            {
                throw new SearchQueryException("正则表达式无效，请检查 re: 后的语法。", ex);
            }
        }

        using var cmd = _conn.CreateCommand();
        var sql = """
            SELECT id, range_id, name, full_path, relative_path, extension, kind, size_bytes, last_modified
            FROM items
            WHERE 1 = 1
            """;
        if (mode is SearchMatchMode.Literal or SearchMatchMode.Wildcard && normalized.Length > 0)
        {
            sql += " AND name LIKE $q ESCAPE '\\'";
            var pattern = mode == SearchMatchMode.Wildcard
                ? WildcardToLike(normalized)
                : "%" + EscapeLike(normalized) + "%";
            cmd.Parameters.AddWithValue("$q", pattern);
        }
        if (kindFilter is { } k && k != SearchItemKind.Other)
        {
            sql += " AND kind = $k";
            cmd.Parameters.AddWithValue("$k", (int)k);
        }
        if (options.RangeId is { } rangeId)
        {
            sql += " AND range_id = $rid";
            cmd.Parameters.AddWithValue("$rid", rangeId.ToString("D"));
        }
        sql += " ORDER BY last_modified DESC";
        if (mode != SearchMatchMode.Regex)
        {
            sql += " LIMIT $lim";
            cmd.Parameters.AddWithValue("$lim", options.Limit);
        }
        cmd.CommandText = sql;

        using var r = cmd.ExecuteReader();
        var list = new List<SearchItem>();
        while (r.Read())
        {
            if (regex is not null)
            {
                bool matches;
                try { matches = regex.IsMatch(r.GetString(2)); }
                catch (RegexMatchTimeoutException ex)
                {
                    throw new SearchQueryException("正则表达式执行超时，请缩小表达式范围。", ex);
                }
                if (!matches) continue;
            }
            var rid = Guid.Parse(r.GetString(1));
            list.Add(new SearchItem(
                r.GetInt64(0),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                (SearchItemKind)r.GetInt32(6),
                r.GetInt64(7),
                DateTimeOffset.Parse(r.GetString(8), CultureInfo.InvariantCulture),
                rid,
                IsValid: true));
            if (list.Count >= options.Limit) break;
        }
        return list;
    }

    public IReadOnlyList<SearchItem> Search(string? query, SearchItemKind? kindFilter, int limit = 100) =>
        Search(query, kindFilter, new SearchQueryOptions(Limit: limit));

    private static SearchMatchMode GetMatchMode(string query, SearchQueryOptions options)
    {
        if (options.EnableRegexSearch && query.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
            return SearchMatchMode.Regex;
        if (options.EnableWildcardSearch && query.IndexOfAny(['*', '?']) >= 0)
            return SearchMatchMode.Wildcard;
        return SearchMatchMode.Literal;
    }

    private static string EscapeLike(string s) =>
        s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static string WildcardToLike(string value) =>
        EscapeLike(value).Replace("*", "%").Replace("?", "_");

    public void Dispose() => _conn.Dispose();
}

public sealed record SearchItemRow(
    Guid RangeId,
    string Name,
    string FullPath,
    string RelativePath,
    string Extension,
    SearchItemKind Kind,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
