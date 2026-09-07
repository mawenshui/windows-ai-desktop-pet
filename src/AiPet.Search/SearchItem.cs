using System;

namespace AiPet.Search;

/// <summary>
/// One row in the local search index. Mirrors the columns we collect per
/// PRD §3 SRCH-01 (name + relative path + extension + type + last modified
/// time; never file content).
/// </summary>
public sealed record SearchItem(
    long Id,
    string Name,
    string FullPath,
    string RelativePath,
    string Extension,
    SearchItemKind Kind,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    Guid RangeId,
    bool IsValid)
{
    public int RelevanceScore { get; init; }
    public bool IsPinned { get; init; }
    public string PinLabel => IsPinned ? "取消固定" : "固定";
    public string MatchReason { get; init; } = "按最近修改时间";
    /// <summary>
    /// True if the underlying file/directory is still present at <see cref="FullPath"/>.
    /// The index row may be stale (e.g. user moved the file); callers must
    /// re-check before opening.
    /// </summary>
    public bool ExistsNow
    {
        get
        {
            if (Kind == SearchItemKind.Folder)
                return System.IO.Directory.Exists(FullPath);
            return System.IO.File.Exists(FullPath);
        }
    }
}
