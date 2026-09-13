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
        Assert.Contains("Padding=\"35,0,46,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalContentAlignment=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedSearchScope, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{Binding SelectedSearchScopeId", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"首次搜索范围授权\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"确认并建立索引\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SearchPrivacyNotice}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding MatchSnippet}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"3\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Grid.Row=\"5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<DockPanel Grid.Row=\"6\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"*\" MinHeight=\"44\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("AllowDrop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"ShortcutBar_DragOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"ShortcutBar_Drop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UniformGrid x:Name=\"ShortcutGridPanel\" Columns=\"6\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\" VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"48\" Height=\"48\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DisplayName}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Description}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding TargetPath}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0.72\" />", xaml, StringComparison.Ordinal);
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
        Assert.Contains("<Setter Property=\"Width\" Value=\"36\" />", theme, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"36\" />", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"CompactButton\"", theme, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DangerSecondaryButton\"", theme, StringComparison.Ordinal);
        Assert.Contains("Width=\"14\" Height=\"14\"", xaml, StringComparison.Ordinal);

        var ellipsis = xaml.IndexOf("Content=\"⋯\"", StringComparison.Ordinal);
        Assert.True(ellipsis >= 0, "The shortcut management button should remain discoverable.");
        var ellipsisEnd = xaml.IndexOf(">", ellipsis, StringComparison.Ordinal);
        Assert.True(ellipsisEnd > ellipsis);
        Assert.Contains("OpenShortcutMenu_Click", xaml[ellipsis..ellipsisEnd], StringComparison.Ordinal);
        Assert.Contains("Width=\"24\" Height=\"24\"", xaml[ellipsis..ellipsisEnd], StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{Binding DisplayName, StringFormat=快捷入口目标已失效：{0}，请重新定位或移除}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Data=\"M1,0 V5 M1,7 V8\"", xaml, StringComparison.Ordinal);

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
        Assert.Contains("Content=\"{Binding GlobalHotkeySaveLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ClearSearchButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ClearSearchCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding SelectedResultShortcutLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"启用每日自动备份\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"自动备份保留份数\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CreateAutomaticBackupNowCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding AutomaticBackupSaveLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding UpdateSettingsSaveLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding GlobalHotkeyStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding AutomaticBackupStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TodoTitleBox\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_settings_use_builtin_routes_without_professional_inputs()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "HomeViewModel.Updates.cs"));

        Assert.Contains("x:Name=\"SmartUpdateRouteStatus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UpdateRouteStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding PeriodicUpdateChecksEnabled}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CheckForUpdatesButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"立即检查\" Style=\"{StaticResource PrimaryButton}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding UpdateDownloadLabel}\" Style=\"{StaticResource PrimaryButton}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAccelerationTemplate", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GitHubUpdateTokenBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveUpdateAccessTokenCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateAccessTokenInput", code, StringComparison.Ordinal);
        Assert.Contains("settings.Updates.AccelerationTemplate = string.Empty;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Shortcut_manager_exposes_context_state_and_safe_keyboard_removal()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "ShortcutManagerWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "ShortcutManagerWindow.xaml.cs"));

        Assert.Contains("x:Name=\"SelectionText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"Entries_SelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ScanButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CancelScanButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Delete 预览移除", xaml, StringComparison.Ordinal);
        Assert.Contains("e.Key == Key.Delete", code, StringComparison.Ordinal);
        Assert.Contains("ShowRemovalPreview();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Cache_reset_uses_explicit_risk_styling_and_verb_confirmation()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs"));

        Assert.Contains("Content=\"退出并重建缓存\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource DangerSecondaryButton}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowResetCachesDialog()", code, StringComparison.Ordinal);
        Assert.Contains("DecisionButton(\"退出并重建\"", code, StringComparison.Ordinal);
        Assert.Contains("DecisionButton(\"保留并返回\"", code, StringComparison.Ordinal);
        Assert.Contains("设置、待办、快捷入口", code, StringComparison.Ordinal);
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
    public void Settings_expose_keyboard_directory_and_named_focus_targets()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs"));

        Assert.Contains("x:Name=\"SettingsSectionSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=\"SettingsSectionSelector_SelectionChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<UniformGrid Columns=\"5\" />", xaml, StringComparison.Ordinal);
        foreach (var target in new[]
                 {
                     "AppearanceSettingsSection", "HotkeySettingsSection", "UpdateSettingsSection",
                     "SearchSettingsSection", "AiSettingsSection",
                 })
            Assert.Contains($"x:Name=\"{target}\"", xaml, StringComparison.Ordinal);
        foreach (var target in new[]
                 {
                     "CharacterList", "HotkeyEnabledToggle", "PeriodicUpdateCheckToggle",
                     "ContentSearchToggle", "AiTemplateSelector",
                 })
            Assert.Contains($"x:Name=\"{target}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TryResolveUnsavedAiChanges(\"切换设置分组\")", code, StringComparison.Ordinal);
        Assert.Contains("AiSettingsExpander.IsExpanded = true;", code, StringComparison.Ordinal);
        Assert.Contains("target.BringIntoView", code, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Focus(focusTarget);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsSection\" Style=\"{StaticResource SectionCard}\" Margin=\"0,0,0,12\" Focusable=\"True\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("private bool FocusFirstSearchResult()", ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("SearchBox.IsKeyboardFocusWithin", ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("ResultsList.SelectedIndex <= 0", ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs")), StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"TodayPlanExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding HasTodayPlanDraft", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding HasTodayPlanError", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Todo_manual_path_precedes_progressively_disclosed_ai_tools()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs"));

        Assert.Contains("x:Name=\"NewTodoButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TodoAiExpander\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Row=\"0\" Margin=\"1,0,0,9\">", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"1\" Style=\"{StaticResource SectionCard}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ItemsControl Grid.Row=\"2\" ItemsSource=\"{Binding Items}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"3\" Background=\"{StaticResource AccentTint}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"NewTodo_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"TodoEditorClose_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TodoTitleBox.Focus();", code, StringComparison.Ordinal);
        Assert.Contains("NewTodoButton.Focus();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Keyboard_search_todo_and_maintenance_optimizations_remain_discoverable_and_accessible()
    {
        var xaml = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml"));
        var code = ReadProjectFile(Path.Combine("src", "AiPet.ToolWindow", "PetToolWindow.xaml.cs"));

        Assert.Contains("主页 · Alt+1 · Ctrl+L 聚焦搜索", xaml, StringComparison.Ordinal);
        Assert.Contains("待办 · Alt+2 · Ctrl+N 快速新建", xaml, StringComparison.Ordinal);
        Assert.Contains("F1 打开手册", xaml, StringComparison.Ordinal);
        Assert.Contains("key == Key.N && modifiers == ModifierKeys.Control", code, StringComparison.Ordinal);
        Assert.Contains("key == Key.F1 && modifiers == ModifierKeys.None", code, StringComparison.Ordinal);
        Assert.Contains("key == Key.Enter && modifiers == ModifierKeys.Control", code, StringComparison.Ordinal);
        Assert.Contains("Todo.IsEditorOpen: true", code, StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding SearchResultCountText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"重置搜索条件\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ResetSearchContextCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"管理搜索范围\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding FilterSummary}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding EmptyActionLabel}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding EmptyStateCommand}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding EditorStateHint}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding ShowDiscardEditorConfirmation", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding KeepEditingCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DiscardEditorCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.Todo.DeleteTodoCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding UndoLastActionCommand}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteTodo_Click", code, StringComparison.Ordinal);

        Assert.Contains("Command=\"{Binding SelectFirstTodayPlanCandidatesCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ClearTodayPlanSelectionCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("24 小时 HH:mm", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"32\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"34\"", xaml, StringComparison.Ordinal);

        Assert.Contains("Text=\"{Binding MaintenanceStatus}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", code, StringComparison.Ordinal);
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
