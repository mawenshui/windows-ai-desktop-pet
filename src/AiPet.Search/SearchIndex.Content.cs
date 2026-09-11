using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace AiPet.Search;

public sealed partial class SearchIndex
{
    internal void InsertStagedContentItems(IReadOnlyList<ContentIndexRow> rows)
    {
        if (rows.Count == 0) return;
        using var transaction = _conn.BeginTransaction();
        using var command = _conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO staged_content_items (range_id, full_path, body, byte_count)
            VALUES ($range, $path, $body, $bytes)
            """;
        var range = command.Parameters.Add("$range", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var body = command.Parameters.Add("$body", SqliteType.Text);
        var bytes = command.Parameters.Add("$bytes", SqliteType.Integer);
        foreach (var row in rows)
        {
            range.Value = row.RangeId.ToString("D");
            path.Value = row.FullPath;
            body.Value = row.Body;
            bytes.Value = row.ByteCount;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public ContentIndexSummary GetContentSummary(Guid? rangeId = null)
    {
        using var command = _conn.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(byte_count), 0),
                   COALESCE((SELECT SUM(skipped) FROM content_index_stats WHERE ($range IS NULL OR range_id = $range)), 0),
                   (SELECT MAX(last_index_at) FROM content_index_stats WHERE ($range IS NULL OR range_id = $range))
            FROM content_items
            WHERE ($range IS NULL OR range_id = $range)
            """;
        command.Parameters.AddWithValue("$range", rangeId?.ToString("D") ?? (object)DBNull.Value);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new ContentIndexSummary(
            reader.GetInt32(0),
            reader.GetInt32(2),
            reader.GetInt64(1),
            reader.IsDBNull(3)
                ? null
                : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
    }

    public void PurgeAllContent()
    {
        using var command = _conn.CreateCommand();
        command.CommandText = """
            DELETE FROM content_items;
            DELETE FROM staged_content_items;
            DELETE FROM content_index_stats;
            PRAGMA wal_checkpoint(TRUNCATE);
            """;
        command.ExecuteNonQuery();
    }

    internal ContentIndexSummary ReplaceContentSubtree(
        Guid rangeId,
        string fullPath,
        IReadOnlyList<ContentIndexRow> replacements,
        DateTimeOffset indexedAt)
    {
        using var transaction = _conn.BeginTransaction();
        using (var delete = _conn.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM content_items WHERE range_id = $id AND (full_path = $path COLLATE NOCASE OR full_path LIKE $prefix ESCAPE '\\')";
            delete.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            delete.Parameters.AddWithValue("$path", fullPath);
            delete.Parameters.AddWithValue("$prefix", EscapeLike(Path.TrimEndingDirectorySeparator(fullPath) + Path.DirectorySeparatorChar) + "%");
            delete.ExecuteNonQuery();
        }

        var (existingCount, existingBytes) = ReadContentTotals(rangeId, transaction);
        var inserted = 0;
        long insertedBytes = 0;
        foreach (var replacement in replacements)
        {
            if (existingCount + inserted >= ContentSearchPolicy.MaximumFilesPerRange
                || existingBytes + insertedBytes + replacement.ByteCount > ContentSearchPolicy.MaximumCorpusBytesPerRange)
            {
                continue;
            }
            using var insert = _conn.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR REPLACE INTO content_items (range_id,full_path,body,byte_count) VALUES ($id,$path,$body,$bytes)";
            insert.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            insert.Parameters.AddWithValue("$path", replacement.FullPath);
            insert.Parameters.AddWithValue("$body", replacement.Body);
            insert.Parameters.AddWithValue("$bytes", replacement.ByteCount);
            insert.ExecuteNonQuery();
            inserted++;
            insertedBytes += replacement.ByteCount;
        }

        using (var stats = _conn.CreateCommand())
        {
            stats.Transaction = transaction;
            stats.CommandText = """
                INSERT INTO content_index_stats (range_id, skipped, last_index_at)
                VALUES ($id, $skipped, $at)
                ON CONFLICT(range_id) DO UPDATE SET
                    skipped = excluded.skipped,
                    last_index_at = excluded.last_index_at
                """;
            stats.Parameters.AddWithValue("$id", rangeId.ToString("D"));
            stats.Parameters.AddWithValue("$skipped", ReadSkippedTotal(rangeId, transaction));
            stats.Parameters.AddWithValue("$at", indexedAt.ToString("O"));
            stats.ExecuteNonQuery();
        }
        transaction.Commit();
        return GetContentSummary(rangeId);
    }

    private (int Count, long Bytes) ReadContentTotals(Guid rangeId, SqliteTransaction transaction)
    {
        using var command = _conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(byte_count),0) FROM content_items WHERE range_id=$id";
        command.Parameters.AddWithValue("$id", rangeId.ToString("D"));
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private int ReadSkippedTotal(Guid rangeId, SqliteTransaction transaction)
    {
        using var command = _conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MAX(
                (SELECT COUNT(*) FROM items WHERE range_id=$id AND kind<>$folder)
                - (SELECT COUNT(*) FROM content_items WHERE range_id=$id),
                0)
            """;
        command.Parameters.AddWithValue("$id", rangeId.ToString("D"));
        command.Parameters.AddWithValue("$folder", (int)SearchItemKind.Folder);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
    }

    private void DeleteContentRows(string tableName, Guid rangeId)
    {
        if (tableName is not ("content_items" or "staged_content_items"))
            throw new ArgumentOutOfRangeException(nameof(tableName));
        using var command = _conn.CreateCommand();
        command.CommandText = $"DELETE FROM {tableName} WHERE range_id = $id";
        command.Parameters.AddWithValue("$id", rangeId.ToString("D"));
        command.ExecuteNonQuery();
    }
}
