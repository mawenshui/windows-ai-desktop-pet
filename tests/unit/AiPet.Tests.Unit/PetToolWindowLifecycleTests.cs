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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "WPF lifecycle test timed out.");
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

            provider.SelectedValue = "qwen";
            PumpDispatcher();
            Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", endpoint.Text);
            Assert.Equal("qwen-plus", model.Text);

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
            var vm = Assert.IsType<HomeViewModel>(window.DataContext);
            Assert.Equal("图片", category.SelectedItem);
            Assert.Equal("图片", vm.Category);
            var categoryPresenter = category.Template?.FindName("ContentSite", category) as ContentPresenter;
            Assert.NotNull(categoryPresenter);
            Assert.Equal("图片", categoryPresenter.Content);

            window.AllowClose();
            window.Close();
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
            emptyState.Visibility = Visibility.Collapsed;
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
        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "WPF interaction test timed out.");
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
