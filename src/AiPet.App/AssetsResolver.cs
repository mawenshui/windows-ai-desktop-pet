using System;
using System.IO;

namespace AiPet.App;

/// <summary>
/// Locates the on-disk <c>assets/pets/</c> tree at runtime. Tries, in order:
/// 1. <c>AIPET_ASSETS</c> env var (set by the install adapter)
/// 2. <c>{AppContext.BaseDirectory}/assets/pets</c>
/// 3. Walking up from <c>AppContext.BaseDirectory</c> looking for
///    <c>assets/pets/pet.json</c> in any ancestor (dev tree layout).
/// </summary>
public static class AssetsResolver
{
    public static string? FindPetsRoot()
    {
        var byEnv = Environment.GetEnvironmentVariable("AIPET_ASSETS");
        if (!string.IsNullOrEmpty(byEnv) && Directory.Exists(byEnv))
        {
            // Accept either "assets/" (the repo root) or "assets/pets/"
            // (the actual pets root) — normalise to the latter.
            var normalised = NormaliseToPetsRoot(byEnv);
            if (normalised is not null) return normalised;
        }

        var byBase = Path.Combine(AppContext.BaseDirectory, "assets", "pets");
        var byBaseHit = FindPetJsonUnder(byBase);
        if (byBaseHit is not null) return byBase;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "pets");
            var hit = FindPetJsonUnder(candidate);
            if (hit is not null) return hit;

            var localRgs = Path.Combine(dir.FullName, "res", "images", "RGS_8Directional");
            if (File.Exists(Path.Combine(localRgs, "pet.json"))) return localRgs;
        }
        return null;
    }

    /// <summary>
    /// Smoke / packaging helper: returns the first existing <c>assets/pets</c>
    /// path by walking up from <paramref name="fromDirectory"/>. Used so the
    /// dev machine can run <c>WindowsAiDesktopPet.exe --smoke</c> directly
    /// from the build output without copying assets next to the EXE.
    /// </summary>
    public static string? FindPetsRootFrom(string fromDirectory)
    {
        var dir = new DirectoryInfo(fromDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var hit = FindPetJsonUnder(Path.Combine(dir.FullName, "assets", "pets"));
            if (hit is not null) return hit;
            var localRgs = Path.Combine(dir.FullName, "res", "images", "RGS_8Directional");
            if (File.Exists(Path.Combine(localRgs, "pet.json"))) return localRgs;
        }
        return null;
    }

    private static string? NormaliseToPetsRoot(string path)
    {
        // Path may be either the assets root or the assets/pets root.
        // We probe the latter via FindPetJsonUnder so case and naming
        // variations of the per-pack subdirectory (e.g. RGS_8Directional)
        // resolve uniformly.
        if (Directory.Exists(path))
        {
            if (File.Exists(Path.Combine(path, "pet.json"))) return path;
            var nested = Path.Combine(path, "pets");
            var hit = FindPetJsonUnder(nested);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Enumerate immediate subdirectories of <paramref name="petsRoot"/>
    /// and return the first one that contains a <c>pet.json</c>.
    /// This avoids hard-coding the pack directory name (which may be
    /// <c>rgs-8dir</c>, <c>RGS_8Directional</c>, or any future pack).
    /// </summary>
    private static string? FindPetJsonUnder(string? petsRoot)
    {
        if (petsRoot is null || !Directory.Exists(petsRoot)) return null;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(petsRoot))
            {
                if (File.Exists(Path.Combine(sub, "pet.json"))) return petsRoot;
            }
        }
        catch { /* ignore */ }
        return null;
    }
}
