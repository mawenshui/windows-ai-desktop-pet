using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AiPet.App;

public sealed record TrayState(
    bool PetVisible,
    bool ToolWindowVisible,
    bool AutostartEnabled);

/// <summary>
/// WinForms <see cref="NotifyIcon"/> wrapper. The context menu is refreshed
/// from live application state whenever it opens, and the icon is restored
/// when Explorer recreates the Windows taskbar.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _petVisibilityItem;
    private readonly ToolStripMenuItem _toolWindowItem;
    private readonly ToolStripMenuItem _autostartItem;
    private ToolStripMenuItem? _reminderActions;
    private readonly Icon _applicationIcon;
    private readonly Func<TrayState>? _stateProvider;
    private readonly TaskbarCreatedMessageWindow _taskbarCreatedWindow;
    private TrayState _fallbackState = new(true, false, false);
    private bool _disposed;

    public event EventHandler? PetVisibilityClicked;
    public event EventHandler? ShowPetRequested;
    public event EventHandler? ToolWindowClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? AutostartClicked;
    public event EventHandler? HelpClicked;
    public event EventHandler? ExitClicked;
    public event EventHandler? BalloonClicked;

    public TrayIcon(Func<TrayState>? stateProvider = null)
    {
        _stateProvider = stateProvider;
        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => RefreshState();

        _applicationIcon = LoadApplicationIcon();
        _petVisibilityItem = new ToolStripMenuItem("隐藏桌宠") { CheckOnClick = false, Image = CreateMenuGlyph("pet") };
        _petVisibilityItem.Click += (_, _) => PetVisibilityClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_petVisibilityItem);

        _toolWindowItem = new ToolStripMenuItem("显示工具窗口") { CheckOnClick = false, Image = CreateMenuGlyph("window") };
        _toolWindowItem.Click += (_, _) => ToolWindowClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_toolWindowItem);
        _menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("设置") { Image = CreateMenuGlyph("settings") };
        settings.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(settings);

        _autostartItem = new ToolStripMenuItem("开机自启") { CheckOnClick = false, Image = CreateMenuGlyph("startup") };
        _autostartItem.Click += (_, _) => AutostartClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_autostartItem);

        var help = new ToolStripMenuItem("帮助") { Image = CreateMenuGlyph("help") };
        help.Click += (_, _) => HelpClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(help);
        _menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出") { Image = CreateMenuGlyph("exit") };
        exit.Click += (_, _) => ExitClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exit);

        _icon = new NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "Windows AI Desktop Pet · 方块伙伴",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _icon.DoubleClick += (_, _) => ShowPetRequested?.Invoke(this, EventArgs.Empty);
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke(this, EventArgs.Empty);
        _taskbarCreatedWindow = new TaskbarCreatedMessageWindow(RestoreAfterExplorerRestart);
        RefreshState();
    }

    public void SetPetVisible(bool visible)
    {
        _fallbackState = _fallbackState with { PetVisible = visible };
        RefreshState();
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_disposed) return;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(2500);
    }

    public void ShowReminderActions(string title, Action complete, Action snooze, Action open)
    {
        if (_disposed) return;
        ClearReminderActions();
        _reminderActions = new ToolStripMenuItem("待处理提醒") { Image = CreateMenuGlyph("help") };
        _reminderActions.DropDownItems.Add(new ToolStripMenuItem($"打开：{title}", CreateMenuGlyph("window"), (_, _) => open()));
        _reminderActions.DropDownItems.Add(new ToolStripMenuItem("标记完成", CreateMenuGlyph("pet"), (_, _) => { complete(); ClearReminderActions(); }));
        _reminderActions.DropDownItems.Add(new ToolStripMenuItem("10 分钟后提醒", CreateMenuGlyph("startup"), (_, _) => { snooze(); ClearReminderActions(); }));
        _menu.Items.Insert(0, _reminderActions);
    }

    private void ClearReminderActions()
    {
        if (_reminderActions is null) return;
        _menu.Items.Remove(_reminderActions);
        _reminderActions.Dispose();
        _reminderActions = null;
    }

    private void RefreshState()
    {
        if (_disposed) return;
        TrayState state;
        try { state = _stateProvider?.Invoke() ?? _fallbackState; }
        catch { state = _fallbackState; }

        _fallbackState = state;
        _petVisibilityItem.Checked = state.PetVisible;
        _petVisibilityItem.Text = state.PetVisible ? "隐藏桌宠" : "显示桌宠";
        _toolWindowItem.Checked = state.ToolWindowVisible;
        _toolWindowItem.Text = state.ToolWindowVisible ? "收起工具窗口" : "显示工具窗口";
        _autostartItem.Checked = state.AutostartEnabled;
    }

    private void RestoreAfterExplorerRestart()
    {
        if (_disposed) return;
        RefreshState();
        _icon.Visible = false;
        _icon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _taskbarCreatedWindow.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _applicationIcon.Dispose();
        _menu.Dispose();
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            var extracted = string.IsNullOrWhiteSpace(path) ? null : Icon.ExtractAssociatedIcon(path);
            return extracted is null ? (Icon)SystemIcons.Application.Clone() : (Icon)extracted.Clone();
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    private static Bitmap CreateMenuGlyph(string kind)
    {
        var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.FromArgb(74, 66, 61), 1.6f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        using var accent = new SolidBrush(Color.FromArgb(235, 123, 73));
        switch (kind)
        {
            case "pet":
                graphics.FillEllipse(accent, 2, 3, 12, 10);
                graphics.FillEllipse(Brushes.White, 5, 7, 2, 2);
                graphics.FillEllipse(Brushes.White, 9, 7, 2, 2);
                break;
            case "window": graphics.DrawRectangle(pen, 2, 3, 12, 10); graphics.DrawLine(pen, 2, 6, 14, 6); break;
            case "settings": graphics.DrawEllipse(pen, 3, 3, 10, 10); graphics.FillEllipse(accent, 6, 6, 4, 4); break;
            case "startup": graphics.DrawArc(pen, 2, 2, 12, 12, 35, 290); graphics.DrawLine(pen, 11, 2, 14, 3); break;
            case "help": graphics.DrawEllipse(pen, 2, 2, 12, 12); graphics.DrawString("?", new Font("Segoe UI", 9, FontStyle.Bold), accent, 4, 0); break;
            default: graphics.DrawLine(pen, 4, 4, 12, 12); graphics.DrawLine(pen, 12, 4, 4, 12); break;
        }
        return bitmap;
    }

    private sealed class TaskbarCreatedMessageWindow : NativeWindow, IDisposable
    {
        private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        private readonly Action _onTaskbarCreated;

        public TaskbarCreatedMessageWindow(Action onTaskbarCreated)
        {
            _onTaskbarCreated = onTaskbarCreated;
            CreateHandle(new CreateParams
            {
                Caption = "AiPet.TrayMessageWindow",
            });
        }

        protected override void WndProc(ref Message message)
        {
            if (TaskbarCreatedMessage != 0 && message.Msg == TaskbarCreatedMessage)
                _onTaskbarCreated();
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string messageName);
    }
}
