using System;
using System.IO;
using AiPet.Shortcuts;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class ShortcutStoreRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aipet-shortcut-recovery-" + Guid.NewGuid().ToString("N"));

    public ShortcutStoreRecoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Add_recovers_when_legacy_build_created_shortcuts_path_as_directory()
    {
        var store = new ShortcutStore(_root);
        Directory.CreateDirectory(store.ShortcutsPath);
        var target = Path.Combine(_root, "notes.txt");
        File.WriteAllText(target, "notes");

        store.Add(new ShortcutItem
        {
            Kind = ShortcutKind.File,
            TargetPath = target,
            DisplayName = "notes",
        });

        Assert.True(File.Exists(store.ShortcutsPath));
        Assert.Single(store.Load());
        Assert.Single(Directory.GetDirectories(_root, "shortcuts.json.invalid-directory-backup*"));
    }
}
