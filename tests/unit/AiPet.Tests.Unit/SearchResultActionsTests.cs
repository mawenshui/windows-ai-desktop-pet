using System.Diagnostics;
using System.IO;
using AiPet.Search;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class SearchResultActionsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "aipet-result-actions-" + Guid.NewGuid().ToString("N"));

    public SearchResultActionsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Open_uses_the_default_shell_without_building_a_command_string()
    {
        var path = Path.Combine(_root, "中文 & notes.txt");
        File.WriteAllText(path, "notes");
        ProcessStartInfo? captured = null;
        var actions = new WindowsSearchResultActions(startInfo => captured = startInfo, _ => { });

        actions.Open(Item(path));

        Assert.NotNull(captured);
        Assert.Equal(path, captured!.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Empty(captured.ArgumentList);
    }

    [Fact]
    public void Reveal_passes_the_entire_path_as_one_explorer_argument()
    {
        var path = Path.Combine(_root, "report & final.txt");
        File.WriteAllText(path, "report");
        ProcessStartInfo? captured = null;
        var actions = new WindowsSearchResultActions(startInfo => captured = startInfo, _ => { });

        actions.Reveal(Item(path));

        Assert.NotNull(captured);
        Assert.Equal("explorer.exe", captured!.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Equal(new[] { "/select," + path }, captured.ArgumentList);
    }

    [Fact]
    public void Missing_targets_cannot_be_opened_or_revealed_but_their_path_can_be_copied()
    {
        var path = Path.Combine(_root, "missing.txt");
        var copied = string.Empty;
        var item = Item(path);
        var actions = new WindowsSearchResultActions(_ => { }, value => copied = value);

        Assert.Throws<FileNotFoundException>(() => actions.Open(item));
        Assert.Throws<FileNotFoundException>(() => actions.Reveal(item));

        actions.CopyPath(item);
        Assert.Equal(path, copied);
    }

    private static SearchItem Item(string path) => new(
        1,
        Path.GetFileName(path),
        path,
        Path.GetFileName(path),
        Path.GetExtension(path),
        SearchItemKind.Document,
        File.Exists(path) ? new FileInfo(path).Length : 0,
        DateTimeOffset.UtcNow,
        Guid.NewGuid(),
        true);
}
