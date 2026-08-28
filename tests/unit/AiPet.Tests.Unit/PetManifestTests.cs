using System;
using System.IO;
using AiPet.Common;
using Xunit;

namespace AiPet.Tests.Unit;

public class PetManifestTests
{
    private static string WriteTempPetJson(string content)
    {
        var p = Path.Combine(Path.GetTempPath(), "aipet-pet-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Load_minimal_manifest()
    {
        var path = WriteTempPetJson("""
        {
          "id": "rgs-8dir",
          "displayName": "RGS",
          "version": "0.1.0",
          "source": { "license": "CC0-1.0" },
          "frameInventory": {
            "characters": ["base", "hero", "skeleton", "monster"],
            "preferred": "hero",
            "actionsPerCharacter": ["idle", "jump"],
            "directions": ["down", "down_right", "right", "up_right", "up"],
            "framesPerAction": { "idle": 4, "jump": 8 },
            "filePathPattern": "frames/<character>/<action>_<direction>_<NN>.png",
            "deathFilePathPattern": "frames/_global/death_<NN>.png"
          },
          "directionModel": {
            "authored": ["down", "down_right", "right", "up_right", "up"],
            "columnOrder8Dir": ["down", "down_left", "left", "up_left", "up", "up_right", "right", "down_right"]
          },
          "render": { "fps": 12 }
        }
        """);
        try
        {
            var m = PetManifestLoader.Load(path);
            Assert.Equal("rgs-8dir", m.Id);
            Assert.Equal("CC0-1.0",  m.Source?.License);
            Assert.Equal(12,         m.Render?.Fps);
            Assert.Equal(4,          m.FrameInventory?.FramesPerAction["idle"]);
            Assert.Equal(8,          m.FrameInventory?.FramesPerAction["jump"]);
            Assert.Contains("hero",   m.FrameInventory?.Characters ?? new());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_missing_file_throws_FileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            PetManifestLoader.Load(Path.Combine(Path.GetTempPath(), "no-such-file-" + Guid.NewGuid().ToString("N") + ".json")));
    }

    [Fact]
    public void ResolveFramePath_substitutes_tokens()
    {
        var path = WriteTempPetJson("""
        {
          "id": "x",
          "frameInventory": {
            "characters": ["hero"],
            "filePathPattern": "frames/<character>/<action>_<direction>_<NN>.png",
            "deathFilePathPattern": "frames/_global/death_<NN>.png"
          }
        }
        """);
        try
        {
            var m = PetManifestLoader.Load(path);
            var resolved = PetManifestLoader.ResolveFramePath(
                @"C:\assets\pets\x",
                m, "hero", "idle", Direction8.Right, 0);
            // Authored mapping: Right is authored, so token is "right"; frame index 0 -> 01
            Assert.Equal(@"C:\assets\pets\x\frames\hero\idle_right_01.png", resolved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveFramePath_uses_mirror_for_derived_direction()
    {
        var path = WriteTempPetJson("""
        {
          "id": "x",
          "frameInventory": {
            "characters": ["hero"],
            "filePathPattern": "frames/<character>/<action>_<direction>_<NN>.png"
          }
        }
        """);
        try
        {
            var m = PetManifestLoader.Load(path);
            // Left is derived; must look up "right" in the file system.
            var resolved = PetManifestLoader.ResolveFramePath(
                @"C:\assets\pets\x",
                m, "hero", "jump", Direction8.Left, 3);
            Assert.Equal(@"C:\assets\pets\x\frames\hero\jump_right_04.png", resolved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolveDeathFramePath_substitutes_NN()
    {
        var path = WriteTempPetJson("""
        {
          "id": "x",
          "frameInventory": {
            "deathFilePathPattern": "frames/_global/death_<NN>.png"
          }
        }
        """);
        try
        {
            var m = PetManifestLoader.Load(path);
            var resolved = PetManifestLoader.ResolveDeathFramePath(@"C:\assets\pets\x", m, 5);
            Assert.Equal(@"C:\assets\pets\x\frames\_global\death_06.png", resolved);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
