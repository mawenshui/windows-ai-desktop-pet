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
        Assert.DoesNotContain("SelectedValue=\"{Binding SelectedSearchScopeId", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"首次搜索范围授权\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"确认并建立索引\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SearchPrivacyNotice}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding MatchSnippet}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"3\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Grid.Row=\"4\"", xaml, StringComparison.Ordinal);
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

        // Every former dropdown is now a native single-selection ListBox in
        // the window's own visual tree, so selection never crosses a Popup.
        Assert.DoesNotContain("<ComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BareComboBox", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"InlineChoiceList\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"InlineChoiceItem\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionMark\"", theme, StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.DirectionalNavigation", theme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_SelectedContentHost\"", theme, StringComparison.Ordinal);
        foreach (var selectorName in new[]
                 {
                     "SearchScopeSelector", "CategoryFilterSelector", "AiTargetSelector", "TodoFilterSelector",
                     "ThemeSelector", "AiTemplateSelector", "SavedAiConfigurationSelector", "ProviderSelector",
                 })
            Assert.Contains($"<ListBox x:Name=\"{selectorName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AiTemplateSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"从所选模板新建\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PART_ContentHost\"", theme, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource CreamScrollBar}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CreamScrollBarThumb\"", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Width\" Value=\"10\" />", theme, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ScrollBar}\" BasedOn=\"{StaticResource CreamScrollBar}\"", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void Former_dropdowns_are_visible_single_selection_surfaces()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var theme = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "ToolWindowTheme.xaml"));

        Assert.DoesNotContain("<ComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PART_Popup", theme, StringComparison.Ordinal);
        Assert.Contains("SelectionMode\" Value=\"Single", theme, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText", theme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionMark\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SelectionBar\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"InlineSegmentGroup\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"InlineChoiceSurface\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CompactInlineChoiceItem\"", theme, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsMouseCaptured\" Value=\"True\"", theme, StringComparison.Ordinal);
        Assert.Contains("Property=\"IsDragging\" Value=\"True\"", theme, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource InlineSegmentGroup}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource InlineChoiceSurface}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemContainerStyle=\"{StaticResource CompactInlineChoiceItem}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Essential_hotkey_and_backup_controls_are_keyboard_accessible_and_report_status()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));

        Assert.Contains("Content=\"启用全局快捷键\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"打开搜索快捷键\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"快速记待办快捷键\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding SaveGlobalHotkeysCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"启用每日自动备份\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"自动备份保留份数\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CreateAutomaticBackupNowCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GlobalHotkeyStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AutomaticBackupStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TodoTitleBox\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_search_controls_explain_consent_limits_status_and_revocation()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));

        Assert.Contains("x:Name=\"ContentSearchToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ContentSearchToggle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding EnableContentSearch, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("不超过 128 KiB 的 .txt 和 .md", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ContentSearchStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RebuildContentIndexCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DisableContentSearchCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_results_expose_selection_and_keyboard_accessible_follow_up_actions()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));

        Assert.Contains("SelectedItem=\"{Binding SelectedResult, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding HasSelectedResult, Converter={StaticResource BoolToVisibility}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding OpenSelectedResultCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RevealSelectedResultCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CopySelectedResultPathCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding AddSelectedResultToShortcutsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ResultsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"OpenSelectedResultButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"RevealSelectedResultButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"CopySelectedResultPathButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"AddSelectedResultToShortcutsButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"在文件资源管理器中定位所选搜索结果\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"复制所选搜索结果路径\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_plan_exposes_explicit_selection_preview_confirm_and_undo_controls()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));

        Assert.Contains("Text=\"今日安排\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TodayPlanCandidates}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding GenerateAiTodayPlanCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding GenerateLocalTodayPlanCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding TodayPlanDraft}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ApplyTodayPlanCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding UndoTodayPlanCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"确认应用今日安排\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TodayPlanCandidateSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"GenerateLocalTodayPlanButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ApplyTodayPlanButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"UndoTodayPlanButton\"", xaml, StringComparison.Ordinal);
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
