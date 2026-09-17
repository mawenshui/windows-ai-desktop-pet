using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AiPet.Common;
using AiPet.Storage;

namespace AiPet.Pet;

/// <summary>
/// Transparent, borderless desktop pet window.
/// PRD: PET-01 / PET-02 (drag + position memory) / WIN-01 (single click toggles
/// tool window) / mouse head-direction tracking (memory entry "pet 头朝向跟随鼠标").
/// </summary>
public partial class PetWindow : Window
{
    private readonly PetFrameCache _frames;
    private readonly PetManifest _manifest;
    private readonly SettingsStore _settings;
    private bool _enableRoaming = true;
    private bool _enableBubbleAnimation = true;
    private bool _enableFollowMotion = true;
    private bool _quietMode;

    private readonly DispatcherTimer _frameTimer;
    private readonly DispatcherTimer _directionTimer;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _singleClickTimer;
    private readonly DispatcherTimer _idleBehaviorTimer;
    private readonly DispatcherTimer _roamTimer;
    private readonly Random _random = new();
    private readonly Stopwatch _roamStopwatch = new();

    // Animation state
    private string _currentAction = "idle";
    private Direction8 _currentDirection = Direction8.Down;
    private int _currentFrame;
    private int _frameCount = 4;
    private int _fps = 12;
    private bool _playOnce;
    private string? _returnToAction; // after playOnce, return to this action

    // Drag state
    private bool _isDragging;
    private bool _isRoaming;
    private bool _reminderBubbleActive;
    private bool _companionWindowVisible;
    private DateTime _lastUserInteractionUtc = DateTime.UtcNow;
    private System.Windows.Point _roamStart;
    private System.Windows.Point _roamTarget;
    private System.Windows.Point _dragOffset;
    private System.Windows.Point _downPoint;
    private const int HeadFollowIntervalMs = 100;
    private const int DirectionDeadZonePx = 8;
    private const int FrontPoseDistancePx = 180;
    private static readonly TimeSpan RoamDuration = TimeSpan.FromSeconds(2.2);
    private static readonly string[] IdlePhrases =
    {
        "我在这里，随时可以开工",
        "要找文件就点我一下",
        "休息一会儿，也别忘了伸伸腰",
        "桌面很安静，我陪你待会儿",
        "今天也慢慢把事情做好",
    };

    /// <summary>Raised when the user single-clicks the pet (post-drag check).</summary>
    public event EventHandler? PetClicked;

    /// <summary>Raised when the pet context menu requests hiding the pet.</summary>
    public event EventHandler? HideRequested;

    /// <summary>Raised when the pet context menu toggles the companion window.</summary>
    public event EventHandler? ToolWindowToggleRequested;

    /// <summary>
    /// Raised synchronously after the pet moves so an open companion popover
    /// can remain visually attached during dragging without a polling timer.
    /// </summary>
    public event EventHandler? VisualPositionChanged;

    /// <summary>Raised while the pet owns the pointer, including click and drag.</summary>
    public event EventHandler? PointerInteractionStarted;

    /// <summary>Raised after the pet releases pointer ownership.</summary>
    public event EventHandler? PointerInteractionCompleted;

    /// <summary>
    /// Returns the on-screen bounds of the rendered character rather than the
    /// transparent host window. Tool popovers use this as their visual anchor.
    /// </summary>
    public Rect GetVisualBounds()
    {
        if (!IsLoaded || PetImage.ActualWidth <= 0 || PetImage.ActualHeight <= 0)
            return new Rect(Left, Top, Width, Height);

        var origin = PetImage.TranslatePoint(new System.Windows.Point(0, 0), this);
        return new Rect(
            Left + origin.X,
            Top + origin.Y,
            PetImage.ActualWidth,
            PetImage.ActualHeight);
    }

    public PetWindow(
        PetFrameCache frames,
        PetManifest manifest,
        string character,
        SettingsStore settings)
    {
        InitializeComponent();
        _frames = frames;
        _manifest = manifest;
        _settings = settings;
        ApplyAppearancePreferences(settings.Load().Appearance);

        _fps = manifest.Render?.Fps is > 0 ? manifest.Render.Fps : 12;
        if (manifest.FrameInventory?.FramesPerAction is { } fpa
            && fpa.TryGetValue("idle", out var idleFrames) && idleFrames > 0)
        {
            _frameCount = idleFrames;
        }

        // Frame animation: advance at fps, loop idle / play once for jump.
        _frameTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / _fps)
        };
        _frameTimer.Tick += (_, _) => AdvanceFrame();
        _frameTimer.Start();

        // Direction tracking: re-sample mouse every HeadFollowIntervalMs.
        _directionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(HeadFollowIntervalMs)
        };
        _directionTimer.Tick += (_, _) => { if (_enableFollowMotion && !_quietMode) UpdateHeadDirectionFromMouse(); };
        _directionTimer.Start();

        _bubbleTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1.8)
        };
        _bubbleTimer.Tick += (_, _) => HideBubble();

        _singleClickTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(PetPointerGesture.SingleClickDelayMs),
        };
        _singleClickTimer.Tick += (_, _) =>
        {
            _singleClickTimer.Stop();
            TriggerClick();
        };

        _idleBehaviorTimer = new DispatcherTimer(DispatcherPriority.Background);
        _idleBehaviorTimer.Tick += (_, _) =>
        {
            _idleBehaviorTimer.Stop();
            TryRunIdleBehavior();
            ScheduleNextIdleBehavior();
        };

        _roamTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _roamTimer.Tick += (_, _) => AdvanceRoam();

        MouseEnter += (_, _) =>
        {
            RecordUserInteraction();
            CancelRoam();
            if (!_isDragging && !_reminderBubbleActive)
                ShowBubble("点我打开工具", autoHide: false);
        };
        MouseLeave += (_, _) =>
        {
            if (!_isDragging && !_reminderBubbleActive) HideBubble();
        };

        // Apply initial layout if available, otherwise fall back to default
        // bottom-right of primary monitor.
        ApplyLayoutOrDefault();
        ClampToVisibleWorkArea();
        RenderFrame();
        ScheduleNextIdleBehavior();
    }

    private void ApplyLayoutOrDefault()
    {
        var layout = _settings.TryLoadLayout();
        if (layout is not null)
        {
            Left = layout.PetX;
            Top = layout.PetY;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            // 80px above the bottom-right corner of the primary monitor
            Left = work.Right - Width - 40;
            Top = work.Bottom - Height - 80;
        }
    }

    // -------- Mouse handling (drag + click + direction follow) --------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        RecordUserInteraction();
        CancelRoam();
        CaptureMouse();
        if (e.ClickCount >= 2) _singleClickTimer.Stop();
        _isDragging = false;
        _downPoint = e.GetPosition(this);
        _dragOffset = e.GetPosition(this);
        PointerInteractionStarted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var cur = e.GetPosition(this);
        var dx = cur.X - _downPoint.X;
        var dy = cur.Y - _downPoint.Y;
        if (!_isDragging && PetPointerGesture.ShouldStartDrag(dx, dy))
        {
            _isDragging = true;
            // Pause direction tracking while dragging so the head does not
            // jitter from the cursor's own position.
            _directionTimer.Stop();
            SetLoopingAction("jump");
            ShowBubble("放到喜欢的位置", autoHide: false);
        }
        if (_isDragging)
        {
            var screen = PointToScreen(cur);
            Left = screen.X - _dragOffset.X;
            Top = screen.Y - _dragOffset.Y;
            VisualPositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();

        var cur = e.GetPosition(this);
        var dx = cur.X - _downPoint.X;
        var dy = cur.Y - _downPoint.Y;
        var moved = Math.Sqrt(dx * dx + dy * dy);
        var releaseAction = PetPointerGesture.ResolveRelease(_isDragging, moved, e.ClickCount);

        if (_isDragging)
        {
            _isDragging = false;
            ClampToVisibleWorkArea();
            VisualPositionChanged?.Invoke(this, EventArgs.Empty);
            PersistPosition();
            SetLoopingAction("idle");
            if (!_quietMode && _enableFollowMotion) _directionTimer.Start();
            ShowBubble("就待在这里啦", autoHide: true);
        }
        else if (releaseAction != PetPointerReleaseAction.None)
        {
            if (releaseAction == PetPointerReleaseAction.DoubleClickAction)
            {
                // A double-click is an animation gesture only. It must never
                // pass through the single-click tool-window toggle path.
                _singleClickTimer.Stop();
                TriggerOneShot("jump", "idle");
                ShowBubble("接到你的召唤啦", autoHide: true);
            }
            else
            {
                // Defer until the double-click window has elapsed.
                _singleClickTimer.Stop();
                _singleClickTimer.Start();
            }
        }

        PointerInteractionCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        RecordUserInteraction();
        CancelRoam();
        var menu = new System.Windows.Controls.ContextMenu();
        var tool = new System.Windows.Controls.MenuItem { Header = "显示 / 收起工具窗口" };
        tool.Click += (_, _) => ToolWindowToggleRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(tool);
        var hide = new System.Windows.Controls.MenuItem { Header = "隐藏桌宠" };
        hide.Click += (_, _) => HideRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(hide);
        menu.Items.Add(new System.Windows.Controls.Separator());
        var exit = new System.Windows.Controls.MenuItem { Header = "退出" };
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(exit);
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void PersistPosition()
    {
        var layout = _settings.TryLoadLayout() ?? new WindowLayout();
        layout.PetX = (int)Left;
        layout.PetY = (int)Top;
        _settings.SaveLayout(layout);
    }

    private void ClampToVisibleWorkArea()
    {
        var workAreas = System.Windows.Forms.Screen.AllScreens
            .Select(screen => new Rect(
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height))
            .ToArray();
        var position = PetWindowPositioner.Clamp(
            new Rect(Left, Top, Width, Height),
            workAreas);
        Left = position.X;
        Top = position.Y;
    }

    private void TriggerClick()
    {
        // Play "jump" once, return to "idle" afterwards.
        TriggerOneShot("jump", "idle");
        ShowBubble("今天想找什么？", autoHide: true);
        PetClicked?.Invoke(this, EventArgs.Empty);
    }

    public void SetCharacter(string character)
    {
        var previous = _frames.Character;
        try
        {
            _frames.SetCharacter(character);
            if (_frames.GetFrame("idle", Direction8.Down, 0) is null)
                throw new InvalidDataException("角色首帧无法读取。");
            _currentAction = "idle";
            _currentFrame = 0;
            _playOnce = false;
            RenderFrame();
        }
        catch
        {
            _frames.SetCharacter(previous);
            _currentAction = "idle";
            _currentFrame = 0;
            _playOnce = false;
            RenderFrame();
            throw;
        }

        if (!_quietMode) ShowBubble("新造型准备好了", autoHide: true);
    }

    public string CurrentCharacter => _frames.Character;

    /// <summary>
    /// The companion popover owns the user's attention while visible, so
    /// autonomous movement is paused and any in-flight roam is interrupted.
    /// </summary>
    public void SetCompanionWindowVisible(bool visible)
    {
        _companionWindowVisible = visible;
        if (visible)
        {
            RecordUserInteraction();
            CancelRoam();
        }
    }

    /// <summary>
    /// Shows a reminder around the pet and optionally starts a short roaming
    /// animation.  The bubble is rendered inside the same window as the pet,
    /// so every Left/Top update during roaming moves both together.
    /// </summary>
    public void ShowReminderNotification(string title, bool showBubble, bool roam)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => ShowReminderNotification(title, showBubble, roam)));
            return;
        }

        RecordUserInteraction();
        if (showBubble)
            ShowBubble($"提醒：{title}", autoHide: true, TimeSpan.FromSeconds(8), isReminder: true);
        else
            HideBubble();

        // Respect Windows' reduced-motion preference. The bubble remains a
        // useful reminder even when movement is disabled by accessibility
        // settings.
        if (roam && !_quietMode && _enableRoaming && SystemParameters.ClientAreaAnimation)
            StartRoam();
    }

    private void ShowBubble(string text, bool autoHide) =>
        ShowBubble(text, autoHide, duration: null, isReminder: false);

    private void ShowBubble(string text, bool autoHide, TimeSpan? duration, bool isReminder)
    {
        _reminderBubbleActive = isReminder;
        StatusText.Text = text;
        StatusBubble.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();
        if (autoHide)
        {
            _bubbleTimer.Interval = _enableBubbleAnimation ? duration ?? TimeSpan.FromSeconds(1.8) : TimeSpan.FromSeconds(4);
            _bubbleTimer.Start();
        }
    }

    private void HideBubble()
    {
        _bubbleTimer.Stop();
        _reminderBubbleActive = false;
        StatusBubble.Visibility = Visibility.Collapsed;
    }

    // -------- Frame animation --------

    private void AdvanceFrame()
    {
        if (_playOnce)
        {
            _currentFrame++;
            if (_currentFrame >= _frameCount)
            {
                _playOnce = false;
                _currentFrame = 0;
                if (_returnToAction is not null)
                {
                    _currentAction = _returnToAction;
                    _returnToAction = null;
                    if (_currentAction == "idle") _frameCount = 4;
                    else if (_currentAction == "jump") _frameCount = 8;
                }
            }
        }
        else
        {
            // Loop
            _currentFrame = (_currentFrame + 1) % Math.Max(1, _frameCount);
        }

        RenderFrame();
    }

    private void RenderFrame()
    {
        _currentDirection = DirectionMapper.ToFrontFacing(_currentDirection);
        BitmapSource? bmp = _currentAction == "death"
            ? _frames.GetDeathFrame(_currentFrame)
            : _frames.GetFrame(_currentAction, _currentDirection, _currentFrame);

        if (bmp is null)
        {
            // Fallback: clear image so we don't show a stale frame.
            PetImage.Source = null;
            return;
        }
        PetImage.Source = bmp;

        // Apply horizontal mirror for derived directions.
        if (DirectionMapper.NeedsMirror(_currentDirection))
            MirrorTransform.ScaleX = -1;
        else
            MirrorTransform.ScaleX = 1;
    }

    /// <summary>
    /// Public hook so the App layer can drive "background task running" /
    /// "failed" transitions without depending on the internal timer.
    /// </summary>
    public void TriggerOneShot(string action, string? returnToAction)
    {
        _returnToAction = returnToAction;
        _currentAction = action;
        _currentFrame = 0;
        _playOnce = true;
        if (action == "jump") _frameCount = 8;
        else if (action == "death") _frameCount = 7;
    }

    public void SetLoopingAction(string action)
    {
        _currentAction = action;
        _returnToAction = null;
        _playOnce = false;
        _currentFrame = 0;
        if (action == "idle") _frameCount = 4;
        else if (action == "jump") _frameCount = 8;
    }

    // -------- Head direction --------

    private void UpdateHeadDirectionFromMouse()
    {
        if (_isDragging || _isRoaming) return;
        try
        {
            if (!GetCursorPos(out var pt)) return;
            // GetCursorPos is in device pixels; PointFromScreen converts it to
            // this WPF window's logical coordinate space on mixed-DPI setups.
            var cursor = PointFromScreen(new System.Windows.Point(pt.X, pt.Y));
            var dx = cursor.X - ActualWidth / 2;
            var dy = cursor.Y - ActualHeight / 2;
            if (Math.Abs(dx) < DirectionDeadZonePx && Math.Abs(dy) < DirectionDeadZonePx)
                return; // dead-zone: do not flip-flop
            var newDir = DirectionMapper.FromFrontBiasedVector(dx, dy, FrontPoseDistancePx);
            if (newDir == _currentDirection) return;
            _currentDirection = newDir;
            // Direction changed; the next frame render will pick the right
            // column. We don't reset _currentFrame so the animation stays
            // smooth across direction changes.
        }
        catch
        {
            // GetCursorPos can fail under RDP / low-integrity contexts; ignore.
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // -------- Low-frequency idle companion behavior --------

    private void ScheduleNextIdleBehavior()
    {
        _idleBehaviorTimer.Stop();
        if (_quietMode) return;
        _idleBehaviorTimer.Interval = PetIdleBehaviorPolicy.NextDelay(_random.Next());
        _idleBehaviorTimer.Start();
    }

    private void TryRunIdleBehavior()
    {
        if (_quietMode || !IsVisible || _isDragging || _isRoaming || _companionWindowVisible || IsMouseOver)
            return;
        if (DateTime.UtcNow - _lastUserInteractionUtc < TimeSpan.FromSeconds(12))
            return;

        _currentDirection = Direction8.Down;
        var behavior = PetIdleBehaviorPolicy.Choose(_random.Next());
        if (behavior == PetIdleBehaviorKind.Roam && _enableRoaming && SystemParameters.ClientAreaAnimation)
        {
            StartRoam();
            return;
        }

        if (behavior == PetIdleBehaviorKind.Gesture)
        {
            TriggerOneShot("jump", "idle");
            if (_random.Next(2) == 0)
                ShowBubble(IdlePhrases[_random.Next(IdlePhrases.Length)], autoHide: true);
            return;
        }

        ShowBubble(IdlePhrases[_random.Next(IdlePhrases.Length)], autoHide: true);
    }

    private void StartRoam()
    {
        var work = GetCurrentWorkArea();
        var distance = PetIdleBehaviorPolicy.RoamDistance(_random.Next());
        var preferRight = _random.Next(2) == 0;
        var rightSpace = work.Right - (Left + Width);
        var leftSpace = Left - work.Left;
        var direction = preferRight ? 1d : -1d;
        if (direction > 0 && rightSpace < Math.Min(48, distance)) direction = -1;
        else if (direction < 0 && leftSpace < Math.Min(48, distance)) direction = 1;

        var target = PetWindowPositioner.Clamp(
            new Rect(Left + direction * distance, Top, Width, Height),
            new[] { work });
        if (Math.Abs(target.X - Left) < 1) return;

        _isRoaming = true;
        _directionTimer.Stop();
        _roamStart = new System.Windows.Point(Left, Top);
        _roamTarget = new System.Windows.Point(target.X, target.Y);
        _currentDirection = target.X < Left ? Direction8.Left : Direction8.Right;
        SetLoopingAction("jump");
        _roamStopwatch.Restart();
        _roamTimer.Start();
    }

    public void ApplyAppearancePreferences(AppearanceSettings settings)
    {
        _enableRoaming = settings.EnablePetRoaming;
        _enableBubbleAnimation = settings.EnableBubbleAnimation;
        _enableFollowMotion = settings.EnableFollowMotion;
        if (!_enableRoaming) CancelRoam();
        if (!_enableFollowMotion || _quietMode) { _directionTimer.Stop(); _currentDirection = Direction8.Down; RenderFrame(); }
        else if (IsLoaded) _directionTimer.Start();
    }

    public void SetQuietMode(bool enabled)
    {
        if (_quietMode == enabled) return;
        _quietMode = enabled;
        if (enabled)
        {
            CancelRoam();
            _idleBehaviorTimer.Stop();
            _directionTimer.Stop();
            if (!_reminderBubbleActive) HideBubble();
            _currentDirection = Direction8.Down;
            SetLoopingAction("idle");
            RenderFrame();
            return;
        }

        if (_enableFollowMotion) _directionTimer.Start();
        ScheduleNextIdleBehavior();
    }

    public void ShowFocusCompleted()
    {
        if (SystemParameters.ClientAreaAnimation) TriggerOneShot("jump", "idle");
        ShowBubble("本次专注完成", autoHide: true, TimeSpan.FromSeconds(4), isReminder: false);
    }

    private void AdvanceRoam()
    {
        if (!_isRoaming) return;
        var rawProgress = _roamStopwatch.Elapsed.TotalMilliseconds / RoamDuration.TotalMilliseconds;
        var progress = PetIdleBehaviorPolicy.EaseInOutCubic(rawProgress);
        Left = _roamStart.X + (_roamTarget.X - _roamStart.X) * progress;
        Top = _roamStart.Y + (_roamTarget.Y - _roamStart.Y) * progress;
        VisualPositionChanged?.Invoke(this, EventArgs.Empty);
        if (rawProgress < 1) return;

        Left = _roamTarget.X;
        Top = _roamTarget.Y;
        FinishRoam(persistPosition: true);
    }

    private void CancelRoam()
    {
        if (!_isRoaming) return;
        FinishRoam(persistPosition: true);
    }

    private void FinishRoam(bool persistPosition)
    {
        _isRoaming = false;
        _roamTimer.Stop();
        _roamStopwatch.Stop();
        _currentDirection = Direction8.Down;
        SetLoopingAction("idle");
        if (!_quietMode && _enableFollowMotion) _directionTimer.Start();
        if (persistPosition) PersistPosition();
        VisualPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private Rect GetCurrentWorkArea()
    {
        var point = new System.Drawing.Point(
            (int)Math.Round(Left + Width / 2),
            (int)Math.Round(Top + Height / 2));
        var area = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
        return new Rect(area.Left, area.Top, area.Width, area.Height);
    }

    private void RecordUserInteraction()
    {
        _lastUserInteractionUtc = DateTime.UtcNow;
        ScheduleNextIdleBehavior();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frameTimer.Stop();
        _directionTimer.Stop();
        _bubbleTimer.Stop();
        _singleClickTimer.Stop();
        _idleBehaviorTimer.Stop();
        _roamTimer.Stop();
        base.OnClosed(e);
    }
}
