using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiPet.Search;
using AiPet.Shortcuts;

namespace AiPet.ToolWindow;

/// <summary>
/// Resolves the icon a Windows user already associates with a target.  The
/// resolver deliberately lives at the WPF boundary: shortcut data remains a
/// portable JSON record while the presentation can ask Windows for the
/// current file, folder, or application icon at render time.
/// </summary>
public sealed class ShellIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
    {
        return value switch
        {
            ShortcutItem shortcut => ShellIconProvider.ForShortcut(shortcut),
            SearchItem result => ShellIconProvider.ForSearchItem(result),
            string path => ShellIconProvider.ForPath(path),
            _ => ShellIconProvider.ApplicationIcon,
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Small Windows shell adapter used by both the shortcut strip and search
/// result list.  All returned images are frozen so they can safely be shared
/// by WPF's layout/rendering threads and are independent of native icon
/// handles after the call returns.
/// </summary>
internal static class ShellIconProvider
{
    private static readonly string[] SupportedCustomIconExtensions =
        [".ico", ".png", ".jpg", ".jpeg"];

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x000000010;
    private const uint FileAttributeNormal = 0x000000080;

    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(
        StringComparer.OrdinalIgnoreCase);

    public static ImageSource ApplicationIcon { get; } = ResolveApplicationIcon();

    public static ImageSource ForShortcut(ShortcutItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconPath))
        {
            var custom = LoadImageFile(item.IconPath!);
            if (custom is not null) return custom;
        }

        return ForPath(item.TargetPath,
            item.Kind == ShortcutKind.Folder ? SearchItemKind.Folder :
            item.Kind == ShortcutKind.Application ? SearchItemKind.Application : null);
    }

    public static ImageSource ForSearchItem(SearchItem item) =>
        ForPath(item.FullPath, item.Kind);

    /// <summary>
    /// Validates a user-selected custom icon before it is copied into the
    /// application data directory.  Extension filtering alone is not enough:
    /// a renamed or truncated image must not be persisted as a custom icon.
    /// </summary>
    public static bool TryValidateCustomIcon(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var extension = Path.GetExtension(path);
            if (!Array.Exists(SupportedCustomIconExtensions,
                    item => string.Equals(item, extension, StringComparison.OrdinalIgnoreCase)))
                return false;
            return LoadImageFile(path) is not null;
        }
        catch { return false; }
    }

    public static ImageSource ForPath(string? path, SearchItemKind? kind = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return ApplicationIcon;

        var normalized = NormalizePath(path);
        var isDirectory = kind == SearchItemKind.Folder || Directory.Exists(path);
        var cacheKey = normalized + "|" + (isDirectory ? "folder" : "file");
        return Cache.GetOrAdd(cacheKey, _ =>
            ResolvePath(path, isDirectory) ?? ApplicationIcon);
    }

    private static ImageSource? ResolvePath(string path, bool isDirectory)
    {
        try
        {
            // A custom folder path gets the familiar Explorer folder icon.
            // For a file that moved, USEFILEATTRIBUTES still lets Windows
            // return the icon associated with its extension.
            var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
            var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
            var flags = ShgfiIcon | ShgfiSmallIcon;
            if (!exists) flags |= ShgfiUseFileAttributes;

            var info = new ShFileInfo();
            var result = SHGetFileInfo(path, attributes, ref info,
                (uint)Marshal.SizeOf<ShFileInfo>(), flags);
            if (result == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;

            try { return NativeIconToImage(info.Icon); }
            finally { DestroyIcon(info.Icon); }
        }
        catch
        {
            // Shell extensions and malformed paths must never prevent the
            // home page from rendering.  The caller supplies the app icon.
            return null;
        }
    }

    private static ImageSource? LoadImageFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var cacheKey = "custom|" + NormalizePath(path);
            if (Cache.TryGetValue(cacheKey, out var cached)) return cached;
            var loaded = LoadImageFileCore(path);
            if (loaded is not null) Cache.TryAdd(cacheKey, loaded);
            return loaded;
        }
        catch { return null; }
    }

    private static ImageSource? LoadImageFileCore(string path)
    {
        try
        {
            var decoder = new BitmapImage();
            decoder.BeginInit();
            decoder.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            decoder.CacheOption = BitmapCacheOption.OnLoad;
            decoder.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            decoder.EndInit();
            decoder.Freeze();
            return decoder;
        }
        catch
        {
            // ICO files with an unusual frame can fail through BitmapImage;
            // use the shell icon path as a final custom-icon fallback.
            return null;
        }
    }

    private static ImageSource NativeIconToImage(IntPtr handle)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(
            handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        image.Freeze();
        return image;
    }

    private static ImageSource ResolveApplicationIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path)
                && ResolvePath(path, isDirectory: false) is { } image)
                return image;
        }
        catch { }

        // A deterministic vector fallback keeps the home page usable when a
        // shell extension or a restricted test host cannot provide an icon.
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(180, 94, 50)),
            new Pen(new SolidColorBrush(Color.FromRgb(147, 70, 34)), 1),
            new RectangleGeometry(new Rect(3, 3, 26, 26), 6, 6)));
        group.Children.Add(new GeometryDrawing(
            null,
            new Pen(Brushes.White, 1.6),
            Geometry.Parse("M9,14 C10,18 22,18 23,14 M10,10 L13,7 M22,10 L19,7")));
        var fallback = new DrawingImage(group);
        fallback.Freeze();
        return fallback;
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path.Trim(); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string? TypeName;
    }
}
