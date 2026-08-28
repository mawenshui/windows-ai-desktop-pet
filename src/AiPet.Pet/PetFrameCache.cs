using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using AiPet.Common;

namespace AiPet.Pet;

/// <summary>
/// Loads and caches <see cref="BitmapSource"/> frames for a single
/// (character, action, direction) triple, and exposes a fast lookup for the
/// animation loop. Disk reads are one-time; subsequent frame switches are
/// in-memory.
/// </summary>
public sealed class PetFrameCache
{
    private readonly string _packageRoot;
    private readonly PetManifest _manifest;
    private string _character;
    private readonly Dictionary<(string Action, Direction8 Direction, int Frame), BitmapSource> _cache = new();

    public PetFrameCache(string packageRoot, PetManifest manifest, string character)
    {
        _packageRoot = packageRoot;
        _manifest = manifest;
        _character = character;
    }

    public string Character => _character;

    public void SetCharacter(string character)
    {
        if (string.IsNullOrWhiteSpace(character))
            throw new ArgumentException("Character is required.", nameof(character));
        if (_manifest.FrameInventory?.Characters.Contains(character) != true)
            throw new ArgumentOutOfRangeException(nameof(character), character, "Character is not declared by pet.json.");

        _character = character;
        _cache.Clear();
    }

    public BitmapSource? GetFrame(string action, Direction8 direction, int frameIndex)
    {
        var key = (action, direction, frameIndex);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var path = PetManifestLoader.ResolveFramePath(
            _packageRoot, _manifest, _character, action, direction, frameIndex);
        if (!File.Exists(path)) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // avoid file handle lock
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze(); // make cross-thread safe
            var visible = CropTransparentPadding(bmp);
            _cache[key] = visible;
            return visible;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource CropTransparentPadding(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var left = converted.PixelWidth;
        var top = converted.PixelHeight;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = 0; x < converted.PixelWidth; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top) return source;

        // Keep a small optical margin so animated limbs do not touch the edge.
        const int padding = 3;
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(converted.PixelWidth - 1, right + padding);
        bottom = Math.Min(converted.PixelHeight - 1, bottom + padding);

        var crop = new CroppedBitmap(converted, new System.Windows.Int32Rect(
            left, top, right - left + 1, bottom - top + 1));
        crop.Freeze();
        return crop;
    }

    public BitmapSource? GetDeathFrame(int frameIndex)
    {
        var key = ("death", Direction8.Down, frameIndex);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var path = PetManifestLoader.ResolveDeathFramePath(_packageRoot, _manifest, frameIndex);
        if (!File.Exists(path)) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            _cache[key] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
