using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace AiPet.Search;

public sealed record ContentIndexSummary(
    int Indexed,
    int Skipped,
    long BytesRead,
    DateTimeOffset? LastIndexedAt = null);

internal sealed record ContentIndexRow(
    Guid RangeId,
    string FullPath,
    string Body,
    int ByteCount);

/// <summary>
/// Fixed privacy and resource limits for the opt-in local text index.
/// The reader accepts only small plain-text files below an already
/// authorised, non-linked search root.
/// </summary>
public static class ContentSearchPolicy
{
    public const int MaximumFileBytes = 128 * 1024;
    public const int MaximumFilesPerRange = 1000;
    public const int MaximumCorpusBytesPerRange = 16 * 1024 * 1024;
    public const int MaximumSnippetCharacters = 120;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" };

    internal static bool TryRead(
        string authorisedRoot,
        SearchItemRow item,
        CancellationToken cancellationToken,
        out ContentIndexRow? content)
    {
        content = null;
        if (!SupportedExtensions.Contains(item.Extension)) return false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(authorisedRoot));
            var path = Path.GetFullPath(item.FullPath);
            if (!IsUnder(root, path) || !File.Exists(path) || ContainsReparsePoint(root, path)) return false;

            var info = new FileInfo(path);
            if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0
                || info.Length is < 0 or > MaximumFileBytes)
                return false;

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 8192,
                FileOptions.SequentialScan);
            var bytes = new byte[MaximumFileBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, count, bytes.Length - count);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumFileBytes) return false;

            var body = Decode(bytes.AsSpan(0, count));
            if (body.IndexOf('\0') >= 0) return false;
            content = new ContentIndexRow(item.RangeId, path, body, count);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (DecoderFallbackException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    internal static string BuildSnippet(string body, string query)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(query)) return string.Empty;
        var index = body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return string.Empty;
        var radius = MaximumSnippetCharacters / 2;
        var start = Math.Max(0, index - radius);
        var end = Math.Min(body.Length, start + MaximumSnippetCharacters);
        if (end - start < MaximumSnippetCharacters)
            start = Math.Max(0, end - MaximumSnippetCharacters);
        var raw = body[start..end];
        var builder = new StringBuilder(raw.Length);
        var pendingSpace = false;
        foreach (var character in raw)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace) builder.Append(' ');
            builder.Append(char.IsControl(character) ? ' ' : character);
            pendingSpace = false;
        }
        var snippet = builder.ToString().Trim();
        if (start > 0) snippet = "…" + snippet;
        if (end < body.Length) snippet += "…";
        return snippet.Length <= MaximumSnippetCharacters
            ? snippet
            : snippet[..(MaximumSnippetCharacters - 1)] + "…";
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false, true).GetString(bytes[3..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new UnicodeEncoding(false, true, true).GetString(bytes[2..]);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new UnicodeEncoding(true, true, true).GetString(bytes[2..]);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static bool IsUnder(string root, string path)
    {
        var prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.Equals(root, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePoint(string root, string path)
    {
        for (var current = path; IsUnder(root, current); current = Path.GetDirectoryName(current) ?? string.Empty)
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                return true;
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return false;
    }
}
