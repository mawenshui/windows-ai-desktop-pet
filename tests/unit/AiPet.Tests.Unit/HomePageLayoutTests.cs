using System;
using System.IO;
using Xunit;

namespace AiPet.Tests.Unit;

/// <summary>
/// Lightweight UI contract checks for the homepage.  They run in the unit
/// suite so a layout regression is caught in CI even when a desktop UI
/// runner is not available on the build host.
/// </summary>
public sealed class HomePageLayoutTests
{
    [Fact]
    public void Homepage_search_surface_is_compact_and_results_precede_shortcuts()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));

        Assert.DoesNotContain("今天想找什么？", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("输入文件名，结果会在下方即时出现", xaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"35,0,12,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedSearchScope, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath=\"Id\" SelectedValue=\"{Binding SelectedSearchScopeId", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"2\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Grid.Row=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"ShortcutBar_DragOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"ShortcutBar_Drop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UniformGrid x:Name=\"ShortcutGridPanel\" Columns=\"6\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"48\" Height=\"48\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Description}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TargetPath}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Border}", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Homepage_window_actions_and_shortcut_management_are_exposed_in_the_same_surface()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var theme = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "ToolWindowTheme.xaml"));

        Assert.Contains("x:Name=\"ShellTabs\" Grid.RowSpan=\"2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"StayOpenButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AlwaysOnTopButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CloseButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Opened=\"ShortcutMenu_Opened\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"2,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"28\" />", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"28\" />", theme, StringComparison.Ordinal);
        Assert.Contains("Width=\"14\" Height=\"14\"", xaml, StringComparison.Ordinal);

        var ellipsis = xaml.IndexOf("Content=\"⋯\"", StringComparison.Ordinal);
        Assert.True(ellipsis >= 0, "The shortcut management button should remain discoverable.");
        var ellipsisEnd = xaml.IndexOf("/>", ellipsis, StringComparison.Ordinal);
        Assert.True(ellipsisEnd > ellipsis);
        Assert.Contains("OpenShortcutMenu_Click", xaml[ellipsis..ellipsisEnd], StringComparison.Ordinal);

        // The selected-value presenter must inherit the foreground from the
        // ComboBox so the scope text remains visible on the cream field.
        Assert.Contains("Foreground=\"{TemplateBinding Foreground}\"", theme, StringComparison.Ordinal);
        Assert.Contains("Content=\"{TemplateBinding SelectionBoxItem}\"", theme, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_ContentHost\"", theme, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource CreamScrollBar}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CreamScrollBarThumb\"", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"8\" />", theme, StringComparison.Ordinal);
    }

    private static string ReadProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "src", "AiPet.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
