using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AiPet.App;

/// <summary>
/// WinForms <see cref="NotifyIcon"/> wrapper. We deliberately use the
/// Windows Forms interop rather than a 3rd-party NotifyIcon package to
/// keep the dependency surface to the BCL (per AGENTS §1.3).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _petVisibilityItem;

    public event EventHandler? PetVisibilityClicked;
    public event EventHandler? ShowPetRequested;
    public event EventHandler? ToolWindowClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? AutostartClicked;
    public event EventHandler? HelpClicked;
    public event EventHandler? ExitClicked;
    public event EventHandler? BalloonClicked;

    public TrayIcon()
    {
        _menu = new ContextMenuStrip();
        _petVisibilityItem = new ToolStripMenuItem("隐藏桌宠") { CheckOnClick = false };
        _petVisibilityItem.Click += (_, _) => PetVisibilityClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(_petVisibilityItem);
        var toolWindow = new ToolStripMenuItem("显示 / 收起工具窗口") { CheckOnClick = false };
        toolWindow.Click += (_, _) => ToolWindowClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(toolWindow);
        _menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("设置");
        settings.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(settings);
        var autostart = new ToolStripMenuItem("开机自启") { CheckOnClick = false };
        autostart.Click += (_, _) => AutostartClicked?.Invoke(this, EventArgs.Empty);
        _menu.Items.Add(autostart);
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
    }

    public void SetPetVisible(bool visible) =>
        _petVisibilityItem.Text = visible ? "隐藏桌宠" : "显示桌宠";

    public void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
