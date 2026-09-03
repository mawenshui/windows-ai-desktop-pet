using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiPet.AI;
using AiPet.Storage;
using AiPet.ToolWindow;
using AiPet.Shortcuts;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetToolWindowLifecycleTests
{
    [Fact]
    public void Close_button_hides_reusable_window()
    {
        Exception? failure = null;
        var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var window = new PetToolWindow();
                window.ShowNear(
                    new Rect(900, 800, 176, 148),
                    new Rect(0, 0, 1920, 1040));

                Assert.True(window.IsVisible);
                var closeButton = FindButton(window, "收起工具窗口");
                Assert.NotNull(closeButton);

                closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.IsVisible);

                window.ShowNear(
                    new Rect(900, 800, 176, 148),
                    new Rect(0, 0, 1920, 1040));
                Assert.True(window.IsVisible);

                window.AllowClose();
                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)), "WPF lifecycle test timed out.");
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    [Fact]
    public void Escape_hides_window_and_reopen_returns_to_home()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040), showSettings: true);
            PumpDispatcher();

            var tabs = FindElement<TabControl>(window, "ShellTabs");
            Assert.NotNull(tabs);
            Assert.Equal(3, tabs.SelectedIndex);

            SendPreviewKey(window, Key.Escape);
            Assert.False(window.IsVisible);

            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            PumpDispatcher();
            Assert.True(window.IsVisible);
            Assert.Equal(0, tabs.SelectedIndex);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void All_pages_render_with_usable_controls_and_character_choices()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            PumpDispatcher();
            window.UpdateLayout();

            var tabs = FindElement<TabControl>(window, "ShellTabs");
            var search = FindElement<TextBox>(window, "SearchBox");
            var close = FindButton(window, "收起工具窗口");
            var stayOpen = FindElement<System.Windows.Controls.Primitives.ToggleButton>(window, "StayOpenButton");
            var alwaysOnTop = FindElement<System.Windows.Controls.Primitives.ToggleButton>(window, "AlwaysOnTopButton");
            var scope = FindElement<ComboBox>(window, "SearchScopeSelector");
            Assert.NotNull(tabs);
            Assert.NotNull(search);
            Assert.NotNull(close);
            Assert.NotNull(stayOpen);
            Assert.NotNull(alwaysOnTop);
            Assert.NotNull(scope);
            Assert.Equal(4, tabs.Items.Count);
            Assert.True(search.ActualHeight >= 44);
            Assert.InRange(close.ActualWidth, 26, 30);
            Assert.InRange(close.ActualHeight, 26, 30);
            Assert.InRange(stayOpen.ActualWidth, 26, 30);
            Assert.InRange(stayOpen.ActualHeight, 26, 30);
            Assert.InRange(alwaysOnTop.ActualWidth, 26, 30);
            Assert.InRange(alwaysOnTop.ActualHeight, 26, 30);
            Assert.True(scope.ActualHeight >= 44);
            Assert.True(scope.Items.Count >= 2);
            Assert.IsType<SearchScopeOption>(scope.SelectedItem);

            var settingsTab = Assert.IsType<TabItem>(tabs.Items[3]);
            settingsTab.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                Environment.TickCount,
                MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent,
                Source = settingsTab,
            });
            PumpDispatcher();
            Assert.Equal(3, tabs.SelectedIndex);

            for (var page = 0; page < tabs.Items.Count; page++)
            {
                tabs.SelectedIndex = page;
                PumpDispatcher();
                window.UpdateLayout();
                Assert.True(window.ActualWidth >= 360);
                Assert.True(window.ActualHeight >= 286);
                AssertVisualHasContent(window, $"tool-window-page-{page}");
            }

            var characters = FindElement<ListBox>(window, "CharacterList");
            var settingsScroll = FindElement<ScrollViewer>(window, "SettingsScroll");
            var wildcard = FindElement<CheckBox>(window, "WildcardSearchToggle");
            var regex = FindElement<CheckBox>(window, "RegexSearchToggle");
            var aiSettings = FindElement<Expander>(window, "AiSettingsExpander");
            Assert.NotNull(characters);
            Assert.NotNull(settingsScroll);
            Assert.NotNull(wildcard);
            Assert.NotNull(regex);
            Assert.NotNull(aiSettings);
            Assert.Equal(4, characters.Items.Count);
            Assert.True(settingsScroll.ActualHeight >= 200);
            Assert.True(wildcard.MinHeight >= 36);
            Assert.True(regex.MinHeight >= 36);

            aiSettings.IsExpanded = true;
            aiSettings.BringIntoView();
            PumpDispatcher();
            window.UpdateLayout();
            var testConnection = FindButton(window, "测试 AI 连接");
            var saveAi = FindButton(window, "保存 AI 配置");
            Assert.NotNull(testConnection);
            Assert.NotNull(saveAi);
            Assert.False(testConnection.IsEnabled);
            Assert.False(saveAi.IsEnabled);
            AssertVisualHasContent(window, "tool-window-ai-settings");
            saveAi.BringIntoView();
            PumpDispatcher();
            window.UpdateLayout();
            AssertVisualHasContent(window, "tool-window-ai-actions");

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Reposition_preserves_the_active_page_and_does_not_reopen_a_hidden_window()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            var workArea = new Rect(0, 0, 1920, 1040);
            window.ShowNear(new Rect(900, 800, 176, 148), workArea, showSettings: true);
            PumpDispatcher();

            var tabs = FindElement<TabControl>(window, "ShellTabs");
            Assert.NotNull(tabs);
            Assert.Equal(3, tabs.SelectedIndex);

            window.RepositionNear(new Rect(1100, 760, 176, 148), workArea);
            PumpDispatcher();
            Assert.Equal(3, tabs.SelectedIndex);
            Assert.True(window.IsVisible);

            window.HideToTray();
            window.RepositionNear(new Rect(1200, 700, 176, 148), workArea);
            Assert.False(window.IsVisible);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Pet_pointer_interaction_suppresses_auto_hide_until_drag_or_click_completes()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = true };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            PumpDispatcher();

            window.BeginAnchorInteraction();
            InvokeProtected(window, "OnDeactivated", EventArgs.Empty);
            PumpDispatcherFor(TimeSpan.FromMilliseconds(180));
            Assert.True(window.IsVisible);

            window.EndAnchorInteraction();
            window.HideToTray();
            Assert.False(window.IsVisible);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Header_controls_toggle_stay_open_and_always_on_top_without_auto_hiding()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = true };
            var preferenceChanges = 0;
            window.WindowPreferencesChanged += (_, _) => preferenceChanges++;
            window.ApplyWindowPreferences(stayOpen: false, alwaysOnTop: true);
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            PumpDispatcher();

            var stayOpen = FindElement<System.Windows.Controls.Primitives.ToggleButton>(window, "StayOpenButton");
            var alwaysOnTop = FindElement<System.Windows.Controls.Primitives.ToggleButton>(window, "AlwaysOnTopButton");
            Assert.NotNull(stayOpen);
            Assert.NotNull(alwaysOnTop);
            Assert.Equal("常驻工具窗口", AutomationProperties.GetName(stayOpen));
            Assert.Equal("保持窗口置顶", AutomationProperties.GetName(alwaysOnTop));

            stayOpen.IsChecked = true;
            alwaysOnTop.IsChecked = false;
            PumpDispatcher();

            Assert.True(window.StayOpen);
            Assert.False(window.AlwaysOnTop);
            Assert.False(window.Topmost);
            Assert.Equal(2, preferenceChanges);

            InvokeProtected(window, "OnDeactivated", EventArgs.Empty);
            PumpDispatcherFor(TimeSpan.FromMilliseconds(180));
            Assert.True(window.IsVisible);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Provider_and_category_dropdowns_update_bound_fields()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040), showSettings: true);
            PumpDispatcher();
            window.UpdateLayout();

            var tabs = FindElement<TabControl>(window, "ShellTabs");
            var aiSettings = FindElement<Expander>(window, "AiSettingsExpander");
            Assert.NotNull(aiSettings);
            aiSettings.IsExpanded = true;
            PumpDispatcher();
            window.UpdateLayout();
            var provider = FindElement<ComboBox>(window, "ProviderSelector");
            var endpoint = FindElement<TextBox>(window, "EndpointBox");
            var model = FindElement<TextBox>(window, "ModelBox");
            Assert.NotNull(tabs);
            Assert.NotNull(provider);
            Assert.NotNull(endpoint);
            Assert.NotNull(model);

            var providerToggle = provider.Template?.FindName("DropDownToggle", provider) as ToggleButton;
            Assert.NotNull(providerToggle);
            RaiseMouseClick(providerToggle);
            PumpDispatcher();
            Assert.True(provider.IsDropDownOpen);
            provider.UpdateLayout();
            var qwen = Assert.Single(provider.Items.Cast<AiProviderDescriptor>(), option => option.Id == "qwen");
            var qwenItem = provider.ItemContainerGenerator.ContainerFromItem(qwen) as ComboBoxItem;
            Assert.NotNull(qwenItem);
            RaiseMouseClick(qwenItem);
            PumpDispatcher();
            var vm = Assert.IsType<HomeViewModel>(window.DataContext);
            Assert.Equal("qwen", vm.Provider);
            Assert.Same(qwen, provider.SelectedItem);
            Assert.Equal("qwen", vm.SelectedProvider?.Id);
            Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", endpoint.Text);
            Assert.Equal("qwen-plus", model.Text);
            var providerPresenter = provider.Template?.FindName("ContentSite", provider) as ContentPresenter;
            Assert.NotNull(providerPresenter);
            Assert.IsType<AiProviderDescriptor>(providerPresenter.Content);
            Assert.Equal(qwen.DisplayName, ((AiProviderDescriptor)providerPresenter.Content).DisplayName);
            Assert.Equal(qwen.DisplayName, FindTextBlock(providerPresenter)?.Text);

            tabs.SelectedIndex = 0;
            PumpDispatcher();
            window.UpdateLayout();
            var category = FindElement<ComboBox>(window, "CategoryFilterSelector");
            Assert.NotNull(category);
            category.IsDropDownOpen = true;
            PumpDispatcher();
            category.UpdateLayout();
            var categoryItem = category.ItemContainerGenerator.ContainerFromItem("图片") as ComboBoxItem;
            Assert.NotNull(categoryItem);
            RaiseMouseClick(categoryItem);
            PumpDispatcher();
            Assert.Equal("图片", category.SelectedItem);
            Assert.Equal("图片", vm.Category);
            var categoryPresenter = category.Template?.FindName("ContentSite", category) as ContentPresenter;
            Assert.NotNull(categoryPresenter);
            Assert.Equal("图片", categoryPresenter.Content);

            // Search scope uses the same shared dropdown template. Selecting
            // an option must update both the control and its scalar VM value.
            var scope = FindElement<ComboBox>(window, "SearchScopeSelector");
            Assert.NotNull(scope);
            scope.IsDropDownOpen = true;
            PumpDispatcher();
            var appsScope = Assert.Single(scope.Items.Cast<SearchScopeOption>(), option => option.Id == "apps");
            var appsScopeItem = scope.ItemContainerGenerator.ContainerFromItem(appsScope) as ComboBoxItem;
            Assert.NotNull(appsScopeItem);
            RaiseMouseClick(appsScopeItem);
            PumpDispatcher();
            Assert.Equal("apps", vm.SelectedSearchScopeId);
            Assert.Same(appsScope, scope.SelectedItem);

            // The todo date filter also shares BareComboBox and binds through
            // SelectedValue so it cannot remain visually stuck on its default.
            tabs.SelectedIndex = 1;
            PumpDispatcher();
            window.UpdateLayout();
            var todoFilter = FindElement<ComboBox>(window, "TodoFilterSelector");
            Assert.NotNull(todoFilter);
            todoFilter.IsDropDownOpen = true;
            PumpDispatcher();
            todoFilter.UpdateLayout();
            var completedFilter = Assert.Single(todoFilter.Items.Cast<TodoFilterOption>(), option => option.Id == "completed");
            var completedFilterItem = todoFilter.ItemContainerGenerator.ContainerFromItem(completedFilter) as ComboBoxItem;
            Assert.NotNull(completedFilterItem);
            RaiseMouseClick(completedFilterItem);
            PumpDispatcher();
            Assert.Equal("completed", vm.Todo.SelectedFilterId);
            Assert.Same(completedFilter, todoFilter.SelectedItem);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Todo_ai_input_keeps_text_editing_and_contrast_after_chinese_input()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            window.SelectTodoTab();
            PumpDispatcher();
            window.UpdateLayout();

            var input = FindElement<TextBox>(window, "TodoAiInputBox");
            Assert.NotNull(input);
            var todo = Assert.IsType<TodoViewModel>(input.DataContext);
            const string text = "明天下午三点提醒我提交周报";

            Assert.True(input.Focusable);
            Assert.False(input.IsReadOnly);
            input.Focus();
            input.Text = text;
            PumpDispatcher();
            input.SelectAll();

            Assert.Equal(text, input.Text);
            Assert.Equal(text, todo.AiInput);
            Assert.Equal(text.Length, input.SelectionLength);
            Assert.True(input.IsKeyboardFocusWithin);

            var foreground = Assert.IsType<SolidColorBrush>(input.Foreground).Color;
            var background = Assert.IsType<SolidColorBrush>(input.Background).Color;
            Assert.True(ContrastRatio(foreground, background) >= 4.5,
                $"Todo input contrast is too low: foreground {foreground}, background {background}.");
            var contentHost = input.Template?.FindName("PART_ContentHost", input) as ScrollViewer;
            Assert.NotNull(contentHost);
            var hostForeground = TextElement.GetForeground(contentHost);
            Assert.IsType<SolidColorBrush>(hostForeground);
            Assert.Equal(foreground, ((SolidColorBrush)hostForeground).Color);
            input.Select(input.Text.Length, 0);
            PumpDispatcher();
            input.UpdateLayout();
            var probe = new TextBox { Width = 256, Height = 44, Text = text };
            var probeWindow = new Window { Width = 300, Height = 100, Content = probe };
            probeWindow.Show();
            probeWindow.UpdateLayout();
            var probeView = FindVisualByTypeName(probe, "TextBoxView");
            Assert.NotNull(probeView);
            Assert.True(probeView is FrameworkElement { ActualHeight: >= 10 },
                $"Default TextBox probe did not layout text: {probeView?.GetType().FullName} {(probeView as FrameworkElement)?.ActualWidth}x{(probeView as FrameworkElement)?.ActualHeight}.");
            probeWindow.Close();
            AssertRenderedText(input, background);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Reminder_editor_exposes_independent_pet_channel_switches()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            window.SelectTodoTab();
            PumpDispatcher();
            var todo = Assert.IsType<HomeViewModel>(window.DataContext).Todo;
            todo.NewTodoCommand.Execute(null);
            PumpDispatcher();
            window.UpdateLayout();

            var reminderToggle = FindElement<CheckBox>(window, "EditorIsReminderToggle");
            var roamToggle = FindElement<CheckBox>(window, "EditorReminderRoamToggle");
            var bubbleToggle = FindElement<CheckBox>(window, "EditorReminderBubbleToggle");
            Assert.False(todo.EditorIsReminder);
            Assert.NotNull(reminderToggle);
            Assert.NotNull(roamToggle);
            Assert.NotNull(bubbleToggle);
            Assert.False(roamToggle.IsVisible);
            Assert.False(bubbleToggle.IsVisible);

            reminderToggle.IsChecked = true;
            PumpDispatcher();
            Assert.True(roamToggle.IsVisible);
            Assert.True(bubbleToggle.IsVisible);
            Assert.True(bubbleToggle.IsChecked);
            Assert.Equal("新建提醒项", todo.EditorHeading);
            Assert.Equal("创建提醒项", todo.EditorSaveLabel);

            roamToggle.IsChecked = true;
            bubbleToggle.IsChecked = false;
            Assert.True(todo.EditorReminderRoamEnabled);
            Assert.False(todo.EditorReminderBubbleEnabled);
            window.UpdateLayout();
            AssertVisualHasContent(window, "tool-window-reminder-editor");

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Attached_ai_settings_test_enables_the_bound_save_button()
    {
        RunSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "aipet-ui-ai-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using var search = new AiPet.Search.SearchService(Path.Combine(root, "index.db"), appProvider: () => []);
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040), showSettings: true);
            PumpDispatcher();

            var vm = Assert.IsType<HomeViewModel>(window.DataContext);
            vm.Attach(
                search,
                new AiPet.Shortcuts.ShortcutStore(Path.Combine(root, "shortcuts")),
                new ConnectedAiClient(),
                new SettingsStore(Path.Combine(root, "settings")),
                new InMemoryAiSecretStore());
            PumpDispatcher();
            window.UpdateLayout();

            var aiSettings = FindElement<Expander>(window, "AiSettingsExpander");
            Assert.NotNull(aiSettings);
            aiSettings.IsExpanded = true;
            PumpDispatcher();
            window.UpdateLayout();
            var apiKey = FindElement<PasswordBox>(window, "ApiKeyBox");
            var testConnection = FindButton(window, "测试 AI 连接");
            var save = FindButton(window, "保存 AI 配置");
            Assert.NotNull(apiKey);
            Assert.NotNull(testConnection);
            Assert.NotNull(save);

            apiKey.Password = "attached-ui-test-key";
            PumpDispatcher();
            Assert.True(testConnection.IsEnabled);
            Assert.False(save.IsEnabled);

            Assert.Same(vm.TestConnectionCommand, testConnection.Command);
            testConnection.Command.Execute(testConnection.CommandParameter);
            PumpDispatcher();
            Assert.Equal("连接正常", vm.AiStatus);
            Assert.True(vm.CanSaveAiConfig);
            Assert.True(save.IsEnabled);

            window.AllowClose();
            window.Close();
            try { Directory.Delete(root, recursive: true); } catch { }
        });
    }

    [Fact]
    public void Homepage_search_text_has_a_centered_content_host_and_keeps_the_full_query()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            PumpDispatcher();
            window.UpdateLayout();

            var search = FindElement<TextBox>(window, "SearchBox");
            Assert.NotNull(search);
            const string query = "quarterly-report-2026-final.pdf";
            search.Text = query;
            PumpDispatcher();
            search.UpdateLayout();

            Assert.Equal(query, search.Text);
            var contentHost = search.Template?.FindName("PART_ContentHost", search) as ScrollViewer;
            Assert.NotNull(contentHost);
            Assert.Equal(VerticalAlignment.Center, contentHost.VerticalAlignment);
            Assert.True(contentHost.ActualHeight > 0);
            Assert.True(contentHost.ActualWidth > 0);
            var renderedLine = new FormattedText(
                query,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(search.FontFamily, search.FontStyle, search.FontWeight, search.FontStretch),
                search.FontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(search).PixelsPerDip);
            Assert.True(contentHost.ActualHeight >= renderedLine.Height,
                $"Search content host {contentHost.ActualHeight:F1}px is shorter than its {renderedLine.Height:F1}px text line.");
            AssertVisualHasContent(window, "tool-window-search-text");

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Homepage_shortcuts_render_as_six_column_icon_pages_with_vertical_scrolling()
    {
        RunSta(() =>
        {
            var window = new PetToolWindow { AutoHideOnDeactivate = false };
            window.ShowNear(new Rect(900, 800, 176, 148), new Rect(0, 0, 1920, 1040));
            PumpDispatcher();
            var vm = Assert.IsType<HomeViewModel>(window.DataContext);
            var shortcutPaths = new List<string>();
            for (var index = 0; index < 14; index++)
            {
                var shortcutPath = Path.Combine(
                    Path.GetTempPath(),
                    $"aipet-shortcut-{Guid.NewGuid():N}.txt");
                File.WriteAllText(shortcutPath, $"shortcut {index + 1}");
                shortcutPaths.Add(shortcutPath);
                vm.Shortcuts.Add(new ShortcutItem
                {
                    DisplayName = $"入口 {index + 1}",
                    Description = $"第 {index + 1} 个常用入口",
                    TargetPath = shortcutPath,
                    Kind = ShortcutKind.File,
                    Order = index,
                });
            }

            var scroller = FindElement<ScrollViewer>(window, "ShortcutScroller");
            var emptyState = FindElement<Border>(window, "ShortcutEmptyState");
            Assert.NotNull(scroller);
            Assert.NotNull(emptyState);
            PumpDispatcher();
            Assert.True(vm.HasShortcuts);
            Assert.Equal(Visibility.Collapsed, emptyState.Visibility);
            scroller.Visibility = Visibility.Visible;
            window.UpdateLayout();
            PumpDispatcher();

            var items = FindElement<ItemsControl>(window, "ShortcutItems");
            var panel = FindElement<UniformGrid>(window, "ShortcutGridPanel");
            Assert.NotNull(items);
            Assert.NotNull(panel);
            Assert.Equal(6, panel.Columns);
            Assert.Equal(14, items.Items.Count);
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            Assert.True(panel.ActualHeight > scroller.ViewportHeight,
                "Fourteen shortcuts should create a third row below the two-row viewport.");
            AssertVisualHasContent(window, "tool-window-shortcut-grid");

            foreach (var shortcutPath in shortcutPaths)
                File.Delete(shortcutPath);

            window.AllowClose();
            window.Close();
        });
    }

    [Fact]
    public void Hover_lift_uses_transform_and_returns_without_layout_shift()
    {
        RunSta(() =>
        {
            var card = new Border { Width = 120, Height = 48 };
            HoverLift.SetIsEnabled(card, true);
            var host = new Window
            {
                Width = 180,
                Height = 100,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Content = card,
            };
            host.Show();
            PumpDispatcher();

            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = Mouse.MouseEnterEvent,
            });
            PumpDispatcherFor(TimeSpan.FromMilliseconds(180));
            var transform = Assert.IsType<TranslateTransform>(card.RenderTransform);
            Assert.InRange(transform.Y, -2.01, -1.99);

            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
            {
                RoutedEvent = Mouse.MouseLeaveEvent,
            });
            PumpDispatcherFor(TimeSpan.FromMilliseconds(160));
            Assert.InRange(transform.Y, -0.01, 0.01);
            Assert.Equal(120, card.Width);
            Assert.Equal(48, card.Height);
            host.Close();
        });
    }

    [Fact]
    public void Rounded_dropdown_converter_uses_display_name_and_keeps_plain_text()
    {
        var converter = new DisplayNameConverter();
        Assert.Equal(
            "全部范围",
            converter.Convert(new SearchScopeOption("all", "全部范围", null), typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(
            "图片",
            converter.Convert("图片", typeof(string), null, CultureInfo.InvariantCulture));
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { completed.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)), "WPF interaction test timed out.");
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpDispatcherFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void InvokeProtected(PetToolWindow window, string methodName, EventArgs args)
    {
        var method = typeof(PetToolWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        method.Invoke(window, [args]);
    }

    private static T? FindElement<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element && element.Name == name) return element;
            var nested = FindElement<T>(child, name);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static TextBlock? FindTextBlock(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text)) return textBlock;
            var nested = FindTextBlock(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void SendPreviewKey(PetToolWindow window, Key key)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("Window has no presentation source.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        var handler = typeof(PetToolWindow).GetMethod(
            "OnPreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Preview key handler was not found.");
        handler.Invoke(window, [args]);
    }

    private static void RaiseMouseClick(UIElement element)
    {
        var timestamp = Environment.TickCount;
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Source = element,
        });
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = element,
        });
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
            Source = element,
        });
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = element,
        });
    }

    private static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R))
            + (0.7152 * Channel(color.G))
            + (0.0722 * Channel(color.B));
    }

    private static void AssertRenderedText(TextBox input, Color background)
    {
        var window = Window.GetWindow(input);
        Assert.NotNull(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var contentHost = input.Template?.FindName("PART_ContentHost", input) as ScrollViewer;
        Assert.NotNull(contentHost);
        var origin = contentHost.TranslatePoint(new Point(0, 0), window);
        var left = Math.Max(0, (int)Math.Floor(origin.X));
        var top = Math.Max(0, (int)Math.Floor(origin.Y));
        var right = Math.Min(width, (int)Math.Ceiling(origin.X + contentHost.ActualWidth));
        var bottom = Math.Min(height, (int)Math.Ceiling(origin.Y + contentHost.ActualHeight));
        var contrastingPixels = 0;
        var textView = FindVisualByTypeName(contentHost, "TextBoxView") as FrameworkElement;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = ((y * width) + x) * 4;
                var alpha = pixels[offset + 3];
                if (alpha == 0) continue;
                var pixel = Color.FromArgb(alpha, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
                if (ContrastRatio(pixel, background) >= 4.5) contrastingPixels++;
            }
        }

        Assert.True(contrastingPixels >= 5,
            $"Expected input text to render inside the content host; found {contrastingPixels} contrasting pixels " +
            $"(textView={textView?.ActualWidth:F1}x{textView?.ActualHeight:F1}, host={contentHost.ActualWidth:F1}x{contentHost.ActualHeight:F1}).");
    }

    private static DependencyObject? FindVisualByTypeName(DependencyObject parent, string typeName)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child.GetType().Name == typeName) return child;
            var nested = FindVisualByTypeName(child, typeName);
            if (nested is not null) return nested;
        }

        return null;
    }

    private sealed class ConnectedAiClient : IAiClient
    {
        public Task<AiConnectionResult> TestConnectionAsync(
            string endpoint,
            string model,
            string apiKey,
            CancellationToken ct) => Task.FromResult(AiConnectionResult.Connected(5));
    }

    private sealed class InMemoryAiSecretStore : IAiSecretStore
    {
        private readonly Dictionary<string, string> _values = new();

        public string? Load(string targetName) => _values.TryGetValue(targetName, out var value) ? value : null;
        public void Save(string targetName, string secret) => _values[targetName] = secret;
        public void Delete(string targetName) => _values.Remove(targetName);
    }

    private static void AssertVisualHasContent(Window window, string snapshotName)
    {
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        SaveSnapshotWhenRequested(bitmap, snapshotName);
        var visiblePixels = 0;
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] > 0) visiblePixels++;
        Assert.True(visiblePixels > width * height / 3,
            $"Expected page to render visible content, got {visiblePixels} pixels.");
    }

    private static void SaveSnapshotWhenRequested(BitmapSource bitmap, string snapshotName)
    {
        var directory = Environment.GetEnvironmentVariable("AIPET_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"{snapshotName}.png"));
        encoder.Save(stream);
    }

    private static Button? FindButton(DependencyObject parent, string automationName)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button button && AutomationProperties.GetName(button) == automationName)
                return button;

            var nested = FindButton(child, automationName);
            if (nested is not null) return nested;
        }

        return null;
    }
}
