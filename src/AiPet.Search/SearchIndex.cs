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
public sealed partial class SearchIndex : IDisposable
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
            PRAGMA secure_delete=ON;
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
            CREATE TABLE IF NOT EXISTS staged_items (
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
            CREATE INDEX IF NOT EXISTS idx_staged_items_range ON staged_items(range_id);
            CREATE TABLE IF NOT EXISTS content_items (
                range_id  TEXT NOT NULL,
                full_path TEXT NOT NULL COLLATE NOCASE,
                body      TEXT NOT NULL,
                byte_count INTEGER NOT NULL,
                PRIMARY KEY (range_id, full_path)
            );
            CREATE TABLE IF NOT EXISTS staged_content_items (
                range_id  TEXT NOT NULL,
                full_path TEXT NOT NULL COLLATE NOCASE,
                body      TEXT NOT NULL,
                byte_count INTEGER NOT NULL,
                PRIMARY KEY (range_id, full_path)
            );
            CREATE INDEX IF NOT EXISTS idx_content_items_range ON content_items(range_id);
            CREATE INDEX IF NOT EXISTS idx_staged_content_items_range ON staged_content_items(range_id);
            CREATE TABLE IF NOT EXISTS content_index_stats (
                range_id TEXT PRIMARY KEY,
                skipped INTEGER NOT NULL,
                last_index_at TEXT
            );
            CREATE TABLE IF NOT EXISTS search_preferences (
                range_id TEXT NOT NULL, full_path TEXT NOT NULL COLLATE NOCASE,
                pinned INTEGER NOT NULL DEFAULT 0, last_used TEXT,
                PRIMARY KEY (range_id, full_path)
            );
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
            delItems.CommandText = """
                DELETE FROM items WHERE range_id = $id;
                DELETE FROM staged_items WHERE range_id = $id;
                DELETE FROM content_items WHERE range_id = $id;
                DELETE FROM staged_content_items WHERE range_id = $id;
                DELETE FROM content_index_stats WHERE range_id = $id;
                DELETE FROM search_preferences WHERE range_id = $id;
                """;
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
        => InsertRows("items", rows);

    public void PrepareStagedItems(Guid rangeId)
    {
        DeleteRows("staged_items", rangeId);
        DeleteContentRows("staged_content_items", rangeId);
    }

    public void InsertStagedItems(IReadOnlyList<SearchItemRow> rows)
        => InsertRows("staged_items", rows);

    public void CommitStagedItems(
        Guid rangeId,
        ContentIndexSummary? contentSummary = null,
        bool replaceContent = false)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            DELETE FROM items WHERE range_id = $id;
            INSERT INTO items (range_id, name, full_path, relative_path, extension, kind, size_bytes, last_modified)
            SELECT range_id, name, full_path, relative_path, extension, kind, size_bytes, last_modified
            FROM staged_items
            WHERE range_id = $id;
            DELETE FROM staged_items WHERE range_id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", rangeId.ToString("D"));
        cmd.ExecuteNonQuery();
        if (replaceContent)
        {
            using var content = _conn.CreateCommand();
            content.Transaction = tx;
            content.CommandText = """
                DELETE FROM content_items WHERE range_id = $id;
                INSERT INTO content_items (range_id, full_path, body, byte_count)
                SELECT range_id, full_path, body, byte_count
                FROM staged_content_items
                WHERE range_id = $id;
                INSERT INTO content_index_stats (range_id, skipped, last_index_at)
                VALUES ($id, $skipped, $at)
                ON CONFLICT(range_id) DO UPDATE SET
                    skipped = excluded.skipped,
                    last_index_at = excluded.last_index_at;
                """;
            content.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            content.Parameters.AddWithValue("$skipped", contentSummary?.Skipped ?? 0);
            content.Parameters.AddWithValue("$at", contentSummary?.LastIndexedAt?.ToString("O") ?? (object)DBNull.Value);
            content.ExecuteNonQuery();
        }
        using (var stagedContent = _conn.CreateCommand())
        {
            stagedContent.Transaction = tx;
            stagedContent.CommandText = "DELETE FROM staged_content_items WHERE range_id = $id";
            stagedContent.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            stagedContent.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void DiscardStagedItems(Guid rangeId)
    {
        DeleteRows("staged_items", rangeId);
        DeleteContentRows("staged_content_items", rangeId);
    }

    private void InsertRows(string tableName, IReadOnlyList<SearchItemRow> rows)
    {
        if (rows.Count == 0) return;
        if (tableName is not ("items" or "staged_items"))
            throw new ArgumentOutOfRangeException(nameof(tableName));
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT INTO {tableName} (range_id, name, full_path, relative_path, extension, kind, size_bytes, last_modified)
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
        => DeleteRows("items", rangeId);

    public void ReplacePath(Guid rangeId, string fullPath, SearchItemRow? replacement) => ReplaceSubtree(rangeId, fullPath, replacement is null ? Array.Empty<SearchItemRow>() : new[] { replacement });

    public void ReplaceSubtree(Guid rangeId, string fullPath, IReadOnlyList<SearchItemRow> replacements)
    {
        using var tx = _conn.BeginTransaction();
        using (var delete = _conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM items WHERE range_id = $id AND (full_path = $path COLLATE NOCASE OR full_path LIKE $prefix ESCAPE '\\')";
            delete.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            delete.Parameters.AddWithValue("$path", fullPath);
            delete.Parameters.AddWithValue("$prefix", EscapeLike(Path.TrimEndingDirectorySeparator(fullPath) + Path.DirectorySeparatorChar) + "%");
            delete.ExecuteNonQuery();
        }
        foreach (var replacement in replacements)
        {
            using var insert = _conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO items (range_id,name,full_path,relative_path,extension,kind,size_bytes,last_modified) VALUES ($id,$name,$path,$relative,$extension,$kind,$size,$modified)";
            insert.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            insert.Parameters.AddWithValue("$name", replacement.Name);
            insert.Parameters.AddWithValue("$path", replacement.FullPath);
            insert.Parameters.AddWithValue("$relative", replacement.RelativePath);
            insert.Parameters.AddWithValue("$extension", replacement.Extension);
            insert.Parameters.AddWithValue("$kind", (int)replacement.Kind);
            insert.Parameters.AddWithValue("$size", replacement.SizeBytes);
            insert.Parameters.AddWithValue("$modified", replacement.LastModifiedUtc.ToString("O"));
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void DeleteRows(string tableName, Guid rangeId)
    {
        if (tableName is not ("items" or "staged_items"))
            throw new ArgumentOutOfRangeException(nameof(tableName));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {tableName} WHERE range_id = $id";
        cmd.Parameters.AddWithValue("$id", rangeId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Substring (case-insensitive) search with exact, prefix and contains
    /// ranking. Opt-in content is joined only for non-empty literal queries.
    /// </summary>
    public IReadOnlyList<SearchItem> Search(
        string? query,
        SearchItemKind? kindFilter,
        SearchQueryOptions options,
        CancellationToken ct = default)
    {
        if (options.Limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(options), "Limit 必须在 1 到 1000 之间。");
        if (options.Offset < 0) throw new ArgumentOutOfRangeException(nameof(options), "Offset 不能为负数。");
        var normalized = query?.Trim() ?? string.Empty;
        if (!Enum.IsDefined(options.Field)) throw new ArgumentOutOfRangeException(nameof(options));
        var field = options.Field == SearchField.Name ? "items.name" : "items.relative_path";
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
        var includeContent = options.EnableContentSearch
            && mode == SearchMatchMode.Literal
            && normalized.Length > 0;
        var contentColumn = includeContent ? "c.body" : "NULL AS body";
        var contentJoin = includeContent
            ? "LEFT JOIN content_items c ON c.range_id=items.range_id AND c.full_path=items.full_path"
            : string.Empty;
        var sql = $"""
            SELECT items.id, items.range_id, items.name, items.full_path, items.relative_path, items.extension, items.kind, items.size_bytes, items.last_modified,
              COALESCE((SELECT pinned FROM search_preferences p WHERE p.range_id=items.range_id AND p.full_path=items.full_path),0) AS is_pinned,
              (SELECT last_used FROM search_preferences p WHERE p.range_id=items.range_id AND p.full_path=items.full_path) AS used_at,
              {contentColumn}
            FROM items
            {contentJoin}
            WHERE 1 = 1
            """;
        if (mode is SearchMatchMode.Literal or SearchMatchMode.Wildcard && normalized.Length > 0)
        {
            sql += includeContent
                ? $" AND ({field} LIKE $q ESCAPE '\\' OR instr(lower(c.body), lower($contentQuery)) > 0)"
                : $" AND {field} LIKE $q ESCAPE '\\'";
            var pattern = mode == SearchMatchMode.Wildcard
                ? WildcardToLike(normalized)
                : "%" + EscapeLike(normalized) + "%";
            cmd.Parameters.AddWithValue("$q", pattern);
            if (includeContent) cmd.Parameters.AddWithValue("$contentQuery", normalized);
        }
        if (kindFilter is { } k && k != SearchItemKind.Other)
        {
            sql += " AND items.kind = $k";
            cmd.Parameters.AddWithValue("$k", (int)k);
        }
        if (options.RangeId is { } rangeId)
        {
            sql += " AND items.range_id = $rid";
            cmd.Parameters.AddWithValue("$rid", rangeId.ToString("D"));
        }
        if (mode == SearchMatchMode.Literal && normalized.Length > 0)
        {
            sql += includeContent
                ? $" ORDER BY CASE WHEN {field} = $exact COLLATE NOCASE THEN 400 WHEN {field} LIKE $prefix ESCAPE '\\' THEN 300 WHEN {field} LIKE $contains ESCAPE '\\' THEN 200 WHEN instr(lower(c.body), lower($contentQuery)) > 0 THEN 50 ELSE 0 END DESC, items.last_modified DESC, items.name COLLATE NOCASE"
                : $" ORDER BY CASE WHEN {field} = $exact COLLATE NOCASE THEN 400 WHEN {field} LIKE $prefix ESCAPE '\\' THEN 300 WHEN {field} LIKE $contains ESCAPE '\\' THEN 200 ELSE 100 END DESC, items.last_modified DESC, items.name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$exact", normalized);
            cmd.Parameters.AddWithValue("$prefix", EscapeLike(normalized) + "%");
            cmd.Parameters.AddWithValue("$contains", "%" + EscapeLike(normalized) + "%");
        }
        else sql += " ORDER BY items.last_modified DESC, items.name COLLATE NOCASE";
        sql = sql.Replace("ORDER BY ", options.UseRecentHistory ? "ORDER BY is_pinned DESC, used_at DESC, " : "ORDER BY is_pinned DESC, ");
        sql += ", items.full_path COLLATE NOCASE, items.range_id, items.id";
        if (mode != SearchMatchMode.Regex)
        {
            sql += " LIMIT $lim OFFSET $off";
            cmd.Parameters.AddWithValue("$lim", options.Limit);
            cmd.Parameters.AddWithValue("$off", options.Offset);
        }
        cmd.CommandText = sql;

        ct.ThrowIfCancellationRequested();
        using var r = cmd.ExecuteReader();
        var list = new List<SearchItem>();
        var regexMatchesToSkip = mode == SearchMatchMode.Regex ? options.Offset : 0;
        while (r.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (regex is not null)
            {
                bool matches;
                try { matches = regex.IsMatch(r.GetString(options.Field == SearchField.Name ? 2 : 4)); }
                catch (RegexMatchTimeoutException ex)
                {
                    throw new SearchQueryException("正则表达式执行超时，请缩小表达式范围。", ex);
                }
                if (!matches) continue;
                if (regexMatchesToSkip > 0) { regexMatchesToSkip--; continue; }
            }
            var rid = Guid.Parse(r.GetString(1));
            var name = r.GetString(2);
            var score = GetRelevanceScore(r.GetString(options.Field == SearchField.Name ? 2 : 4), normalized, mode);
            var body = r.IsDBNull(11) ? null : r.GetString(11);
            var contentOnlyMatch = includeContent
                && score < 200
                && body?.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0;
            if (contentOnlyMatch) score = 50;
            list.Add(new SearchItem(
                r.GetInt64(0),
                name,
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                (SearchItemKind)r.GetInt32(6),
                r.GetInt64(7),
                DateTimeOffset.Parse(r.GetString(8), CultureInfo.InvariantCulture),
                rid,
                IsValid: true)
            {
                RelevanceScore = score,
                MatchReason = contentOnlyMatch
                    ? "正文连续包含"
                    : options.Field == SearchField.Name ? GetMatchReason(score) : GetMatchReason(score).Replace("名称", "相对路径"),
                MatchSnippet = contentOnlyMatch ? ContentSearchPolicy.BuildSnippet(body!, normalized) : string.Empty,
                IsPinned = r.GetInt32(9) != 0,
            });
            if (list.Count >= options.Limit) break;
        }
        return list;
    }

    private static int GetRelevanceScore(string name, string query, SearchMatchMode mode)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        if (mode != SearchMatchMode.Literal) return 100;
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 400;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 300;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 200;
        return 100;
    }

    private static string GetMatchReason(int score) => score switch
    {
        >= 400 => "名称完全匹配", >= 300 => "名称前缀匹配", >= 200 => "名称连续包含", >= 100 => "名称模式匹配", _ => "按最近修改时间",
    };

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
