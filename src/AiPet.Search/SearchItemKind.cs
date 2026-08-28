using System;
using System.Collections.Generic;

namespace AiPet.Search;

/// <summary>
/// Item categories used by the home-page filter. Mirrors PRD §3 SRCH-03.
/// Items whose extension does not match a known bucket are bucketed as
/// "other" and only appear under "all".
/// </summary>
public enum SearchItemKind
{
    Folder = 0,
    Document = 1,
    Application = 2,
    Image = 3,
    Video = 4,
    Audio = 5,
    Other = 99,
}

public static class SearchItemKindClassifier
{
    private static readonly Dictionary<string, SearchItemKind> ExtMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents
        [".txt"] = SearchItemKind.Document, [".md"] = SearchItemKind.Document,
        [".doc"] = SearchItemKind.Document, [".docx"] = SearchItemKind.Document,
        [".pdf"] = SearchItemKind.Document, [".rtf"] = SearchItemKind.Document,
        [".odt"] = SearchItemKind.Document, [".xls"] = SearchItemKind.Document,
        [".xlsx"] = SearchItemKind.Document, [".ppt"] = SearchItemKind.Document,
        [".pptx"] = SearchItemKind.Document, [".csv"] = SearchItemKind.Document,
        [".json"] = SearchItemKind.Document, [".xml"] = SearchItemKind.Document,
        [".html"] = SearchItemKind.Document, [".htm"] = SearchItemKind.Document,
        [".log"] = SearchItemKind.Document,
        // Images
        [".png"] = SearchItemKind.Image, [".jpg"] = SearchItemKind.Image,
        [".jpeg"] = SearchItemKind.Image, [".gif"] = SearchItemKind.Image,
        [".bmp"] = SearchItemKind.Image, [".webp"] = SearchItemKind.Image,
        [".svg"] = SearchItemKind.Image, [".ico"] = SearchItemKind.Image,
        // Video
        [".mp4"] = SearchItemKind.Video, [".mkv"] = SearchItemKind.Video,
        [".avi"] = SearchItemKind.Video, [".mov"] = SearchItemKind.Video,
        [".wmv"] = SearchItemKind.Video, [".flv"] = SearchItemKind.Video,
        [".webm"] = SearchItemKind.Video,
        // Audio
        [".mp3"] = SearchItemKind.Audio, [".wav"] = SearchItemKind.Audio,
        [".flac"] = SearchItemKind.Audio, [".aac"] = SearchItemKind.Audio,
        [".ogg"] = SearchItemKind.Audio, [".wma"] = SearchItemKind.Audio,
        [".m4a"] = SearchItemKind.Audio,
        // Application shortcuts
        [".lnk"] = SearchItemKind.Application, [".exe"] = SearchItemKind.Application,
    };

    public static SearchItemKind Classify(string? extension, bool isDirectory)
    {
        if (isDirectory) return SearchItemKind.Folder;
        if (string.IsNullOrEmpty(extension)) return SearchItemKind.Other;
        return ExtMap.TryGetValue(extension, out var k) ? k : SearchItemKind.Other;
    }
}
