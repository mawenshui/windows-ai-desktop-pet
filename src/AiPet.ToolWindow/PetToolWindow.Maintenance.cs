using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiPet.Storage;

namespace AiPet.ToolWindow;

public partial class PetToolWindow
{
    private bool? ShowMaintenanceFileDialog(Microsoft.Win32.CommonDialog dialog)
    {
        var previous=_suppressAutoHide; _suppressAutoHide=true;
        try { return dialog.ShowDialog(this); }
        finally { _suppressAutoHide=previous; Activate(); }
    }
    private DataModule SelectMaintenanceModules(IReadOnlyList<RestorePreview> preview, bool restore)
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = restore ? "核对模块后退出，重新启动时恢复。替换前保留快照，失败回滚。" : "选择备份模块；文件包含个人数据，请妥善保存。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) });
        var choices = preview.Select(item => (item.Module, Check: new CheckBox
        {
            Content = $"{item.DisplayName} · 当前 {item.CurrentBytes / 1024d:F1} KB → {item.IncomingBytes / 1024d:F1} KB",
            IsEnabled = item.FileCount > 0,
            IsChecked = item.FileCount > 0 && item.Module != DataModule.Logs,
            MinHeight = 36,
        })).ToArray();
        foreach (var (_, check) in choices) panel.Children.Add(check);
        panel.Children.Add(new TextBlock { Text = "快捷入口包含自定义图标；待办与提醒队列成组处理。搜索索引在本机重建；备份不含 API Key。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,12) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80, Margin = new Thickness(0,0,8,0), MinHeight = 36 };
        var confirm = new Button { Content = restore ? "确认并退出" : "导出所选", MinWidth = 100, MinHeight = 36 };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); panel.Children.Add(buttons);
        cancel.Style = (Style)FindResource("SecondaryButton");
        confirm.Style = (Style)FindResource("PrimaryButton");
        var dialog = new Window { Owner = this, Title = restore ? "恢复预览" : "选择备份模块", Width = 440, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel, Background = (Brush)FindResource("Canvas"), Foreground = Foreground, FontFamily = FontFamily };
        confirm.Click += (_, _) => dialog.DialogResult = true;
        var previous = _suppressAutoHide;
        _suppressAutoHide = true;
        try { return dialog.ShowDialog() == true ? choices.Where(choice => choice.Check.IsChecked == true).Aggregate(DataModule.None, (flags, choice) => flags | choice.Module) : DataModule.None; }
        finally { _suppressAutoHide = previous; Activate(); }
    }
}
