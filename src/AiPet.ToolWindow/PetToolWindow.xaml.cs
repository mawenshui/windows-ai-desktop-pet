using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AiPet.Search;
using AiPet.Shortcuts;
using Microsoft.Win32;

namespace AiPet.ToolWindow;

internal static class DoubleUtil
{
    public static bool AreClose(double left, double right) => Math.Abs(left - right) < 0.1;
}

public partial class PetToolWindow : Window
{
    private bool _suppressApiKeyEcho;
    private bool _suppressAutoHide;
    private bool _anchorInteractionActive;
    private bool _allowClose;
    private bool _applyingWindowPreferences;
    private readonly DispatcherTimer _autoHideTimer;

    public bool AutoHideOnDeactivate { get; set; } = true;

    public static readonly DependencyProperty StayOpenProperty = DependencyProperty.Register(
        nameof(StayOpen),
        typeof(bool),
        typeof(PetToolWindow),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWindowPreferenceChanged));

    public static readonly DependencyProperty AlwaysOnTopProperty = DependencyProperty.Register(
        nameof(AlwaysOnTop),
        typeof(bool),
        typeof(PetToolWindow),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWindowPreferenceChanged));

    public bool StayOpen
    {
        get => (bool)GetValue(StayOpenProperty);
        set => SetValue(StayOpenProperty, value);
    }

    public bool AlwaysOnTop
    {
        get => (bool)GetValue(AlwaysOnTopProperty);
        set => SetValue(AlwaysOnTopProperty, value);
    }

    public event EventHandler? WindowPreferencesChanged;

    public PetToolWindow()
    {
        InitializeComponent();
        DataContext = FindResource("HomeVM");
        DataContextChanged += OnDataContextChanged;

        _autoHideTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            if (AutoHideOnDeactivate && !IsActive && !IsAutoHideSuppressed) HideToTray();
        };

        if (DataContext is HomeViewModel vm)
        {
            vm.PropertyChanged += OnHomeVmPropertyChanged;
            vm.AddShortcutRequested += OnAddShortcutRequested;
            vm.EditShortcutRequested += OnEditShortcutRequested;
            vm.RelocateShortcutRequested += OnRelocateShortcutRequested;
            SyncApiKeyFromVm(vm);
        }

        Topmost = AlwaysOnTop;
    }

    public void ApplyWindowPreferences(bool stayOpen, bool alwaysOnTop)
    {
        _applyingWindowPreferences = true;
        try
        {
            StayOpen = stayOpen;
            AlwaysOnTop = alwaysOnTop;
            Topmost = alwaysOnTop;
        }
        finally
        {
            _applyingWindowPreferences = false;
        }
    }

    private static void OnWindowPreferenceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var window = (PetToolWindow)sender;
        if (e.Property == AlwaysOnTopProperty)
            window.Topmost = (bool)e.NewValue;
        if (!window._applyingWindowPreferences)
            window.WindowPreferencesChanged?.Invoke(window, EventArgs.Empty);
    }

    public void ShowNear(Rect petBounds, Rect workArea, bool showSettings = false)
    {
        var placement = PetPopoverPositioner.Calculate(petBounds, workArea);
        ApplyPlacement(placement);
        ShellTabs.SelectedIndex = showSettings ? 3 : 0;

        Show();
        Activate();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            Control target = showSettings ? CharacterList : SearchBox;
            target.Focus();
            Keyboard.Focus(target);
        }));
    }

    /// <summary>
    /// Keeps an already-open popover attached to the moving pet without
    /// changing the active page, focus, activation, or visibility state.
    /// </summary>
    public void RepositionNear(Rect petBounds, Rect workArea)
    {
        if (!IsVisible) return;
        ApplyPlacement(PetPopoverPositioner.Calculate(petBounds, workArea));
    }

    private void ApplyPlacement(PetPopoverPlacement placement)
    {
        if (!DoubleUtil.AreClose(Width, placement.Width)) Width = placement.Width;
        if (!DoubleUtil.AreClose(Height, placement.Height)) Height = placement.Height;
        Left = placement.Left;
        Top = placement.Top;
        ApplyPointer(placement);
    }

    private void ApplyPointer(PetPopoverPlacement placement)
    {
        var pointerLeft = placement.PointerOffsetX - 12;
        TopPointer.Margin = new Thickness(pointerLeft, 0, 0, 0);
        BottomPointer.Margin = new Thickness(pointerLeft, 0, 0, 0);
        TopPointer.Visibility = placement.Side == PetPopoverSide.Below ? Visibility.Visible : Visibility.Collapsed;
        BottomPointer.Visibility = placement.Side == PetPopoverSide.Above ? Visibility.Visible : Visibility.Collapsed;
    }

    public void HideToTray()
    {
        _autoHideTimer.Stop();
        Hide();
    }

    public void BeginAnchorInteraction()
    {
        _anchorInteractionActive = true;
        _autoHideTimer.Stop();
    }

    public void EndAnchorInteraction() => _anchorInteractionActive = false;

    private bool IsAutoHideSuppressed => StayOpen || _suppressAutoHide || _anchorInteractionActive;

    public void AllowClose() => _allowClose = true;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is HomeViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnHomeVmPropertyChanged;
            oldVm.AddShortcutRequested -= OnAddShortcutRequested;
            oldVm.EditShortcutRequested -= OnEditShortcutRequested;
            oldVm.RelocateShortcutRequested -= OnRelocateShortcutRequested;
        }
        if (e.NewValue is HomeViewModel newVm)
        {
            newVm.PropertyChanged += OnHomeVmPropertyChanged;
            newVm.AddShortcutRequested += OnAddShortcutRequested;
            newVm.EditShortcutRequested += OnEditShortcutRequested;
            newVm.RelocateShortcutRequested += OnRelocateShortcutRequested;
            SyncApiKeyFromVm(newVm);
        }
    }

    private void OnHomeVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.ApiKey) && DataContext is HomeViewModel vm)
            SyncApiKeyFromVm(vm);
    }

    private void SyncApiKeyFromVm(HomeViewModel vm)
    {
        if (ApiKeyBox.Password == vm.ApiKey) return;
        _suppressApiKeyEcho = true;
        try { ApiKeyBox.Password = vm.ApiKey ?? string.Empty; }
        finally { _suppressApiKeyEcho = false; }
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressApiKeyEcho) return;
        if (DataContext is HomeViewModel vm && sender is PasswordBox box)
            vm.ApiKey = box.Password;
    }

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: SearchItem item } && DataContext is HomeViewModel vm)
            vm.OpenResult(item);
    }

    private void ShellTab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && sender is TabItem tab)
        {
            ShellTabs.SelectedItem = tab;
            tab.Focus();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void OnAddShortcutRequested(object? sender, EventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加快捷入口",
            Filter = "应用和常用文件|*.exe;*.lnk;*.url;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.md|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) == true && DataContext is HomeViewModel vm)
                TryAddShortcutWithDuplicateConfirmation(vm, dialog.FileName);
        }
        finally { _suppressAutoHide = false; }
    }

    private void BrowseShortcutFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "添加文件夹快捷入口",
            Multiselect = false,
        };
        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) == true && DataContext is HomeViewModel vm)
                TryAddShortcutWithDuplicateConfirmation(vm, dialog.FolderName);
        }
        finally { _suppressAutoHide = false; }
    }

    private void ShortcutBar_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDroppableShortcutTarget(e.Data)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ShortcutBar_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not HomeViewModel vm
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            e.Handled = true;
            return;
        }

        var added = 0;
        var skipped = 0;
        foreach (var path in paths)
        {
            if (!IsShortcutTarget(path))
            {
                skipped++;
                continue;
            }
            if (TryAddShortcutWithDuplicateConfirmation(vm, path)) added++;
            else skipped++;
        }

        vm.Status = added switch
        {
            0 => "没有添加新的快捷入口。",
            1 when skipped == 0 => "快捷入口已添加",
            _ when skipped == 0 => $"已添加 {added} 个快捷入口",
            _ => $"已添加 {added} 个快捷入口，跳过 {skipped} 个无效或重复目标",
        };
        e.Handled = true;
    }

    private bool TryAddShortcutWithDuplicateConfirmation(HomeViewModel vm, string path)
    {
        var allowDuplicate = false;
        if (vm.HasShortcutTarget(path))
        {
            var previousSuppression = _suppressAutoHide;
            _suppressAutoHide = true;
            try
            {
                var answer = MessageBox.Show(
                    this,
                    "该目标已经存在于快捷入口中。仍要再添加一次吗？",
                    "重复快捷入口",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    vm.Status = "已取消重复添加。";
                    return false;
                }

                allowDuplicate = true;
            }
            finally
            {
                _suppressAutoHide = previousSuppression;
            }
        }

        return vm.AddShortcut(path, allowDuplicate);
    }

    private static bool HasDroppableShortcutTarget(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] paths) return false;
        foreach (var path in paths)
            if (IsShortcutTarget(path)) return true;
        return false;
    }

    private static bool IsShortcutTarget(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && (System.IO.File.Exists(path) || System.IO.Directory.Exists(path));

    private void OpenShortcutMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject source) return;
        var card = FindVisualParent<Border>(source);
        if (card?.ContextMenu is not { } menu) return;

        menu.PlacementTarget = card;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ShortcutMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu
            || menu.PlacementTarget is not FrameworkElement target
            || target.DataContext is not ShortcutItem item
            || DataContext is not HomeViewModel vm)
            return;

        foreach (var entry in menu.Items)
        {
            if (entry is not MenuItem menuItem) continue;
            switch (menuItem.Header as string)
            {
                case "上移":
                    menuItem.IsEnabled = vm.CanMoveShortcutUp(item.Id);
                    break;
                case "下移":
                    menuItem.IsEnabled = vm.CanMoveShortcutDown(item.Id);
                    break;
            }
        }
    }

    private void EditShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveShortcut(sender) is not { } item || DataContext is not HomeViewModel vm) return;

        var nameBox = new TextBox
        {
            Text = item.DisplayName,
            Style = (Style)FindResource("Field"),
        };
        var descriptionBox = new TextBox
        {
            Text = item.Description ?? string.Empty,
            Style = (Style)FindResource("Field"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 66,
        };
        var iconPathBox = new TextBox
        {
            Text = item.IconPath ?? string.Empty,
            Style = (Style)FindResource("Field"),
            IsReadOnly = true,
            ToolTip = "留空即可使用目标的 Windows 图标",
        };

        var editor = new Window
        {
            Title = "编辑快捷入口",
            Width = 390,
            Height = 360,
            MinWidth = 350,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)FindResource("Paper"),
            FontFamily = this.FontFamily,
            FontSize = this.FontSize,
        };

        var chooseIconButton = new Button
        {
            Content = "选择图片",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(8, 0, 0, 0),
        };
        chooseIconButton.Click += (_, _) =>
        {
            var iconDialog = new OpenFileDialog
            {
                Title = "选择快捷入口图标",
                Filter = "图标和图片|*.ico;*.png;*.jpg;*.jpeg|所有文件|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (iconDialog.ShowDialog(editor) == true) iconPathBox.Text = iconDialog.FileName;
        };
        var clearIconButton = new Button
        {
            Content = "使用目标图标",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(8, 0, 0, 0),
        };
        clearIconButton.Click += (_, _) => iconPathBox.Clear();

        var saveButton = new Button
        {
            Content = "保存",
            Style = (Style)FindResource("PrimaryButton"),
            Padding = new Thickness(16, 8, 16, 8),
            IsDefault = true,
        };
        var cancelButton = new Button
        {
            Content = "取消",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(16, 8, 16, 8),
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var validationText = new TextBlock
        {
            Foreground = (Brush)FindResource("Error"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        saveButton.Click += (_, _) =>
        {
            if (!vm.UpdateShortcut(item.Id, nameBox.Text, descriptionBox.Text, iconPathBox.Text))
            {
                validationText.Text = vm.Status;
                validationText.Visibility = Visibility.Visible;
                return;
            }
            editor.DialogResult = true;
            editor.Close();
        };
        cancelButton.Click += (_, _) => editor.Close();

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "名称", Style = (Style)FindResource("SectionLabel") });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "描述", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 12, 0, 7) });
        panel.Children.Add(descriptionBox);
        panel.Children.Add(new TextBlock { Text = "图标", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 12, 0, 7) });
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
        iconPathBox.Width = 170;
        iconRow.Children.Add(iconPathBox);
        iconRow.Children.Add(chooseIconButton);
        iconRow.Children.Add(clearIconButton);
        panel.Children.Add(iconRow);
        panel.Children.Add(validationText);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        actions.Children.Add(saveButton);
        actions.Children.Add(cancelButton);
        panel.Children.Add(actions);
        editor.Content = panel;
        editor.ShowDialog();
    }

    private void RelocateShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveShortcut(sender) is not { } item || DataContext is not HomeViewModel vm) return;
        _suppressAutoHide = true;
        try
        {
            string? path = null;
            if (item.Kind == ShortcutKind.Folder)
            {
                var dialog = new OpenFolderDialog { Title = "重新定位文件夹快捷入口", Multiselect = false };
                if (dialog.ShowDialog(this) == true) path = dialog.FolderName;
            }
            else
            {
                var dialog = new OpenFileDialog { Title = "重新定位快捷入口", Filter = "所有文件|*.*", CheckFileExists = true };
                if (dialog.ShowDialog(this) == true) path = dialog.FileName;
            }
            if (!string.IsNullOrWhiteSpace(path)) vm.RelocateShortcut(item.Id, path);
        }
        finally { _suppressAutoHide = false; }
    }

    private void MoveShortcutUp_Click(object sender, RoutedEventArgs e) =>
        ExecuteShortcutCommand(sender, vm => vm.MoveShortcutUpCommand);

    private void MoveShortcutDown_Click(object sender, RoutedEventArgs e) =>
        ExecuteShortcutCommand(sender, vm => vm.MoveShortcutDownCommand);

    private void RemoveShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveShortcut(sender) is not { } item || DataContext is not HomeViewModel vm) return;
        var answer = MessageBox.Show(
            this,
            $"移除“{item.DisplayName}”快捷入口？\n不会删除目标文件或文件夹。",
            "移除快捷入口",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes
            && vm.RemoveShortcutCommand.CanExecute(item.Id))
            vm.RemoveShortcutCommand.Execute(item.Id);
    }

    private void OnEditShortcutRequested(ShortcutItem? item)
    {
        if (item is null) return;
        EditShortcut_Click(new Button { DataContext = item }, new RoutedEventArgs());
    }

    private void OnRelocateShortcutRequested(ShortcutItem? item)
    {
        if (item is null) return;
        RelocateShortcut_Click(new Button { DataContext = item }, new RoutedEventArgs());
    }

    private static ShortcutItem? ResolveShortcut(object sender)
    {
        if (sender is FrameworkElement element && element.DataContext is ShortcutItem item)
            return item;
        if (sender is MenuItem menuItem
            && menuItem.Parent is ContextMenu menu
            && menu.PlacementTarget is FrameworkElement target
            && target.DataContext is ShortcutItem targetItem)
            return targetItem;
        return null;
    }

    private void ExecuteShortcutCommand(object sender, Func<HomeViewModel, ICommand> commandSelector)
    {
        if (ResolveShortcut(sender) is not { } item || DataContext is not HomeViewModel vm) return;
        var command = commandSelector(vm);
        if (command.CanExecute(item.Id)) command.Execute(item.Id);
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match) return match;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void BrowseRange_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择允许搜索的文件夹",
            Multiselect = false,
        };
        _suppressAutoHide = true;
        try
        {
            if (dialog.ShowDialog(this) == true && DataContext is HomeViewModel vm)
            {
                vm.NewRangePath = dialog.FolderName;
                if (vm.AddRangeCommand.CanExecute(null)) vm.AddRangeCommand.Execute(null);
            }
        }
        finally { _suppressAutoHide = false; }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (AutoHideOnDeactivate && !IsAutoHideSuppressed)
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Start();
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        _autoHideTimer.Stop();
        base.OnActivated(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShellTabs.SelectedIndex = ShellNavigation.NextTabIndex(
                ShellTabs.SelectedIndex,
                ShellTabs.Items.Count,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ShellTabs.SelectedIndex == 0
                 && (SearchBox.IsKeyboardFocusWithin || ResultsList.IsKeyboardFocusWithin)
                 && DataContext is HomeViewModel vm
                 && (ResultsList.SelectedItem ?? (ResultsList.Items.Count > 0 ? ResultsList.Items[0] : null)) is SearchItem item)
        {
            vm.OpenResult(item);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _autoHideTimer.Stop();
        base.OnClosing(e);
    }
}
