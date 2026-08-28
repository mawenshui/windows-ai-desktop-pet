using System.IO;
using System.Security.Cryptography;
using AiPet.Common;
using AiPet.Pet;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetFrameCacheTests
{
    [Fact]
    public void Rgs_package_loads_a_visible_trimmed_frame_and_switches_character()
    {
        var packageRoot = FindRgsPackageRoot();
        var manifest = PetManifestLoader.Load(Path.Combine(packageRoot, "pet.json"));
        var cache = new PetFrameCache(packageRoot, manifest, "hero");

        var hero = cache.GetFrame("idle", Direction8.Down, 0);

        Assert.NotNull(hero);
        Assert.InRange(hero!.PixelWidth, 1, 127);
        Assert.InRange(hero.PixelHeight, 1, 127);

        cache.SetCharacter("monster");
        var monster = cache.GetFrame("idle", Direction8.Down, 0);

        Assert.NotNull(monster);
        Assert.Equal("monster", cache.Character);
        Assert.NotEqual(PixelHash(hero), PixelHash(monster));
    }

    private static string PixelHash(System.Windows.Media.Imaging.BitmapSource source)
    {
        var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return Convert.ToHexString(SHA256.HashData(pixels));
    }

    [Fact]
    public void Unknown_character_is_rejected()
    {
        var packageRoot = FindRgsPackageRoot();
        var manifest = PetManifestLoader.Load(Path.Combine(packageRoot, "pet.json"));
        var cache = new PetFrameCache(packageRoot, manifest, "hero");

        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetCharacter("unknown"));
    }

    private static string FindRgsPackageRoot()
    {
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            var candidate = Path.Combine(cursor.FullName, "assets", "pets", "RGS_8Directional");
            if (File.Exists(Path.Combine(candidate, "pet.json"))) return candidate;
            cursor = cursor.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate assets/pets/RGS_8Directional from the test output.");
    }
}
