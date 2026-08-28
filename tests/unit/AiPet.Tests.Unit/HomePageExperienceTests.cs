using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiPet.AI;
using AiPet.Search;
using AiPet.Shortcuts;
using AiPet.Storage;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

/// <summary>
/// Home-page scenarios written from the user's point of view.  The tests use
/// temporary folders and the same stores/services that production binds to,
/// so they also protect the persistence and cancellation boundaries behind
/// the visual controls.
/// </summary>
public sealed class HomePageExperienceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aipet-home-page-" + Guid.NewGuid().ToString("N"));
    private readonly SearchService _search;

    public HomePageExperienceTests()
    {
        Directory.CreateDirectory(_root);
        _search = new SearchService(Path.Combine(_root, "index.db"), appProvider: () => []);
    }

    public void Dispose()
    {
        _search.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Adding_a_folder_creates_a_folder_shortcut_with_a_useful_default()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "常用资料")).FullName;
        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var store = new ShortcutStore(Path.Combine(_root, "shortcuts"));
        var vm = new HomeViewModel(_search, store, new OpenAiCompatibleClient(), settings);

        vm.AddShortcut(folder);

        var item = Assert.Single(store.Load());
        Assert.Equal(ShortcutKind.Folder, item.Kind);
        Assert.Equal("常用资料", item.DisplayName);
        Assert.Equal(item.DisplayName, item.Description);
        Assert.True(item.ExistsNow);
        Assert.True(vm.HasShortcuts);
    }

    [Fact]
    public void Duplicate_shortcut_requires_confirmation_before_a_second_record_is_saved()
    {
        var target = Path.Combine(_root, "same-target.txt");
        File.WriteAllText(target, "keep");
        var settings = new SettingsStore(Path.Combine(_root, "duplicate-settings"));
        var store = new ShortcutStore(Path.Combine(_root, "duplicate-shortcuts"));
        var vm = new HomeViewModel(_search, store, new OpenAiCompatibleClient(), settings);

        Assert.True(vm.AddShortcut(target));
        Assert.True(vm.HasShortcutTarget(target));
        Assert.False(vm.AddShortcut(target));
        Assert.Contains("已存在", vm.Status, StringComparison.Ordinal);
        Assert.Single(vm.Shortcuts);

        Assert.True(vm.AddShortcut(target, allowDuplicate: true));
        Assert.Equal(2, vm.Shortcuts.Count);
        Assert.Equal("keep", File.ReadAllText(target));
    }

    [Fact]
    public void Shortcut_can_be_edited_relocated_and_reordered_without_touching_targets()
    {
        var first = Path.Combine(_root, "first.txt");
        var second = Path.Combine(_root, "second.txt");
        var replacement = Path.Combine(_root, "replacement.pdf");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        File.WriteAllText(replacement, "replacement");
        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var store = new ShortcutStore(Path.Combine(_root, "shortcuts"));
        var vm = new HomeViewModel(_search, store, new OpenAiCompatibleClient(), settings);

        vm.AddShortcut(first);
        vm.AddShortcut(second);
        var firstItem = store.Load().First(item => item.TargetPath == first);
        var secondItem = store.Load().First(item => item.TargetPath == second);

        vm.UpdateShortcut(firstItem.Id, "工作资料", "打开工作资料", iconSourcePath: null);
        vm.RelocateShortcut(firstItem.Id, replacement);
        Assert.True(vm.MoveShortcutDownCommand.CanExecute(firstItem.Id));
        vm.MoveShortcutDownCommand.Execute(firstItem.Id);

        var saved = store.Load().ToList();
        Assert.Equal(secondItem.Id, saved[0].Id);
        var updated = saved[1];
        Assert.Equal("工作资料", updated.DisplayName);
        Assert.Equal("打开工作资料", updated.Description);
        Assert.Equal(replacement, updated.TargetPath);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void Shortcut_edit_rejects_empty_names_and_unreadable_custom_icons_without_saving()
    {
        var target = Path.Combine(_root, "notes.txt");
        File.WriteAllText(target, "notes");
        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var store = new ShortcutStore(Path.Combine(_root, "shortcuts"));
        var vm = new HomeViewModel(_search, store, new OpenAiCompatibleClient(), settings);

        vm.AddShortcut(target);
        var item = Assert.Single(store.Load());

        Assert.False(vm.UpdateShortcut(item.Id, "   ", "ignored", iconSourcePath: null));
        Assert.Contains("名称不能为空", vm.Status);
        Assert.Equal("notes", Assert.Single(store.Load()).DisplayName);

        var invalidPng = Path.Combine(_root, "not-an-image.png");
        File.WriteAllText(invalidPng, "not an image");
        Assert.False(vm.UpdateShortcut(item.Id, "新名称", "ignored", invalidPng));
        Assert.Contains("图标", vm.Status);
        var unchanged = Assert.Single(store.Load());
        Assert.Equal("notes", unchanged.DisplayName);
        Assert.Null(unchanged.IconPath);
    }

    [Fact]
    public void Valid_custom_icon_is_copied_and_unsupported_icon_is_rejected()
    {
        var target = Path.Combine(_root, "notes.txt");
        File.WriteAllText(target, "notes");
        var validPng = Path.Combine(_root, "icon.png");
        File.WriteAllBytes(validPng, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var unsupported = Path.Combine(_root, "icon.gif");
        File.WriteAllBytes(unsupported, File.ReadAllBytes(validPng));
        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var store = new ShortcutStore(Path.Combine(_root, "shortcuts"));
        var vm = new HomeViewModel(_search, store, new OpenAiCompatibleClient(), settings);

        vm.AddShortcut(target);
        var item = Assert.Single(store.Load());

        Assert.True(vm.UpdateShortcut(item.Id, "带图标", "说明", validPng));
        var saved = Assert.Single(store.Load());
        Assert.Equal("带图标", saved.DisplayName);
        Assert.NotNull(saved.IconPath);
        Assert.True(File.Exists(saved.IconPath));

        Assert.False(vm.UpdateShortcut(item.Id, "再试一次", "说明", unsupported));
        Assert.Contains("ICO", vm.Status);
        Assert.Equal("带图标", Assert.Single(store.Load()).DisplayName);
    }

    [Fact]
    public void Missing_shortcut_can_be_relocated_and_move_commands_disable_at_boundaries()
    {
        var first = Path.Combine(_root, "first.txt");
        var second = Path.Combine(_root, "second.txt");
        var replacementFolder = Directory.CreateDirectory(Path.Combine(_root, "replacement")).FullName;
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var store = new ShortcutStore(Path.Combine(_root, "shortcuts"));
        var vm = new HomeViewModel(_search, store, new OpenAiCompatibleClient(), settings);

        vm.AddShortcut(first);
        vm.AddShortcut(second);
        var firstItem = store.Load().First(item => item.TargetPath == first);
        var secondItem = store.Load().First(item => item.TargetPath == second);

        Assert.False(vm.MoveShortcutUpCommand.CanExecute(firstItem.Id));
        Assert.True(vm.MoveShortcutDownCommand.CanExecute(firstItem.Id));
        Assert.True(vm.MoveShortcutUpCommand.CanExecute(secondItem.Id));
        Assert.False(vm.MoveShortcutDownCommand.CanExecute(secondItem.Id));

        File.Delete(first);
        Assert.False(Assert.Single(vm.Shortcuts, item => item.Id == firstItem.Id).ExistsNow);
        vm.RelocateShortcut(firstItem.Id, replacementFolder);

        var restored = Assert.Single(store.Load(), item => item.Id == firstItem.Id);
        Assert.Equal(ShortcutKind.Folder, restored.Kind);
        Assert.Equal(replacementFolder, restored.TargetPath);
        Assert.True(restored.ExistsNow);

        vm.MoveShortcutDownCommand.Execute(firstItem.Id);
        Assert.False(vm.MoveShortcutDownCommand.CanExecute(firstItem.Id));
        Assert.True(vm.MoveShortcutUpCommand.CanExecute(firstItem.Id));
    }

    [Fact]
    public async Task Changing_scope_requeries_the_current_query_and_keeps_results_isolated()
    {
        var firstRoot = Directory.CreateDirectory(Path.Combine(_root, "范围一")).FullName;
        var secondRoot = Directory.CreateDirectory(Path.Combine(_root, "范围二")).FullName;
        File.WriteAllText(Path.Combine(firstRoot, "same-name.txt"), "one");
        File.WriteAllText(Path.Combine(secondRoot, "same-name.txt"), "two");
        _search.AddRange(firstRoot);
        _search.AddRange(secondRoot);
        var firstRange = _search.ListRanges().Single(item => item.Path == firstRoot);
        var secondRange = _search.ListRanges().Single(item => item.Path == secondRoot);
        await _search.IndexRangeAsync(firstRange.Id);
        await _search.IndexRangeAsync(secondRange.Id);

        var settings = new SettingsStore(Path.Combine(_root, "settings"));
        var savedSettings = settings.Load();
        savedSettings.Search.Ranges.Add(firstRoot);
        savedSettings.Search.Ranges.Add(secondRoot);
        settings.Save(savedSettings);
        var vm = new HomeViewModel(
            _search,
            new ShortcutStore(Path.Combine(_root, "shortcuts")),
            new OpenAiCompatibleClient(),
            settings);

        vm.Query = "same-name";
        await WaitUntilAsync(() => vm.Results.Count == 2);
        vm.SelectedSearchScopeId = firstRange.Id.ToString("D");
        await WaitUntilAsync(() => vm.Results.Count == 1);
        Assert.Equal(firstRange.Id, Assert.Single(vm.Results).RangeId);

        vm.SelectedSearchScopeId = secondRange.Id.ToString("D");
        await WaitUntilAsync(() => vm.Results.Count == 1 && vm.Results[0].RangeId == secondRange.Id);
        Assert.Equal(secondRange.Id, Assert.Single(vm.Results).RangeId);
        Assert.Equal("same-name", vm.Query);
    }

    [Fact]
    public void Icon_converter_always_returns_a_frozen_image_and_falls_back_for_missing_targets()
    {
        var converter = new ShellIconConverter();
        var file = Path.Combine(_root, "readme.txt");
        var folder = Path.Combine(_root, "folder");
        File.WriteAllText(file, "readme");
        Directory.CreateDirectory(folder);

        var fileIcon = Assert.IsAssignableFrom<System.Windows.Media.ImageSource>(
            converter.Convert(new ShortcutItem { TargetPath = file, Kind = ShortcutKind.File },
                typeof(System.Windows.Media.ImageSource), null, System.Globalization.CultureInfo.InvariantCulture));
        var folderIcon = Assert.IsAssignableFrom<System.Windows.Media.ImageSource>(
            converter.Convert(new ShortcutItem { TargetPath = folder, Kind = ShortcutKind.Folder },
                typeof(System.Windows.Media.ImageSource), null, System.Globalization.CultureInfo.InvariantCulture));
        var fallbackIcon = Assert.IsAssignableFrom<System.Windows.Media.ImageSource>(
            converter.Convert(new ShortcutItem { TargetPath = Path.Combine(_root, "gone.exe"), Kind = ShortcutKind.Application },
                typeof(System.Windows.Media.ImageSource), null, System.Globalization.CultureInfo.InvariantCulture));

        Assert.True(fileIcon.IsFrozen);
        Assert.True(folderIcon.IsFrozen);
        Assert.True(fallbackIcon.IsFrozen);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 60 && !predicate(); i++)
            await Task.Delay(50);
        Assert.True(predicate());
    }
}
