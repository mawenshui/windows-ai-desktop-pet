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

        _petVisibilityItem = new ToolStripMenuItem("隐藏桌宠") { CheckOnClick = false };
        _petVisibilityItem.Click += (_, _) => PetVisibilityClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_petVisibilityItem);

        _toolWindowItem = new ToolStripMenuItem("显示工具窗口") { CheckOnClick = false };
        _toolWindowItem.Click += (_, _) => ToolWindowClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_toolWindowItem);
        _menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("设置");
        settings.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(settings);

        _autostartItem = new ToolStripMenuItem("开机自启") { CheckOnClick = false };
        _autostartItem.Click += (_, _) => AutostartClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_autostartItem);

        var help = new ToolStripMenuItem("帮助");
        help.Click += (_, _) => HelpClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(help);
        _menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(exit);

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Windows AI Desktop Pet",
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
        _menu.Dispose();
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
