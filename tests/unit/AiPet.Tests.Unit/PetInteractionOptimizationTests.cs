using System.Windows;
using AiPet.Common;
using AiPet.Pet;
using AiPet.Todos;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetInteractionOptimizationTests
{
    [Fact]
    public void Presence_reason_prioritizes_manual_hide_over_fullscreen_and_focus()
    {
        Assert.Contains("手动隐藏", PetPresence.Describe(true, true, false, true, false, true));
        Assert.Contains("全屏", PetPresence.Describe(false, true, false, true, false, true));
        Assert.Contains("托盘恢复", PetPresence.Describe(false, false, true, true, false, true));
        Assert.Contains("暂停", PetPresence.Describe(false, false, true, true, true, false));
    }

    [Fact]
    public void Feedback_tracker_reports_each_transition_once_even_with_timer_refreshes()
    {
        var tracker = new FocusFeedbackTracker();
        var id = Guid.NewGuid();
        var running = Snapshot(id, FocusSessionStatus.Running);
        var paused = Snapshot(id, FocusSessionStatus.Paused);
        Assert.Equal(FocusFeedbackKind.None, tracker.Observe(new FocusSessionSnapshot(null, 0, 0, 0)));
        Assert.Equal(FocusFeedbackKind.Started, tracker.Observe(running));
        Assert.Equal(FocusFeedbackKind.None, tracker.Observe(running));
        Assert.Equal(FocusFeedbackKind.Paused, tracker.Observe(paused));
        Assert.Equal(FocusFeedbackKind.None, tracker.Observe(paused));
        Assert.Equal(FocusFeedbackKind.Resumed, tracker.Observe(running));
        Assert.Equal(FocusFeedbackKind.None, tracker.Observe(Snapshot(id, FocusSessionStatus.Completed)));
        Assert.Equal(FocusFeedbackKind.Started, tracker.Observe(Snapshot(Guid.NewGuid(), FocusSessionStatus.Running)));
    }

    [Fact]
    public void Edge_dwell_snaps_only_on_sufficient_overlap_and_usable_work_area()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var target = new Rect(300, 400, 900, 400);
        var visual = new Rect(500, 236, 148, 148);

        Assert.Equal(new Point(500, 258), WindowEdgeDwellPolicy.TryPlace(visual, target, work));
        Assert.Null(WindowEdgeDwellPolicy.TryPlace(new Rect(500, 200, 148, 148), target, work));
        Assert.Null(WindowEdgeDwellPolicy.TryPlace(new Rect(1155, 236, 148, 148), target, work));
        Assert.Null(WindowEdgeDwellPolicy.PlaceOnTop(148, 148, 500, new Rect(300, 80, 900, 400), work));
    }

    [Fact]
    public void Edge_dwell_follows_window_motion_and_rejects_offscreen_or_other_monitor_geometry()
    {
        var leftMonitor = new Rect(-1440, 0, 1440, 900); // Logical DIP after monitor conversion.
        var target = new Rect(-1100, 380, 700, 350);
        var placed = WindowEdgeDwellPolicy.PlaceOnTop(148, 148, -850, target, leftMonitor);
        Assert.Equal(new Point(-850, 238), placed);
        var moved = WindowEdgeDwellPolicy.PlaceOnTop(148, 148, -750, new Rect(-1000, 420, 700, 350), leftMonitor);
        Assert.Equal(new Point(-750, 278), moved);
        Assert.Null(WindowEdgeDwellPolicy.PlaceOnTop(148, 148, -750, new Rect(-1000, 800, 700, 350), leftMonitor));
        Assert.Null(WindowEdgeDwellPolicy.PlaceOnTop(148, 148, -750, new Rect(300, 420, 700, 350), leftMonitor));
    }

    [Fact]
    public void Edge_dwell_lost_target_returns_visual_to_original_monitor_bottom()
    {
        var work = new Rect(-1440, 0, 1440, 900);
        Assert.Equal(new Point(-850, 744),
            WindowEdgeDwellPolicy.PlaceOnDesktop(new Rect(-850, 238, 148, 148), work));
        Assert.Equal(new Point(-156, 744),
            WindowEdgeDwellPolicy.PlaceOnDesktop(new Rect(100, 238, 148, 148), work));
    }

    [Fact]
    public void Edge_dwell_quiet_mode_preserves_only_original_target_until_validation_or_drag()
    {
        var attachment = new WindowEdgeDwellSession();
        var target = new IntPtr(42);
        attachment.Attach(target);
        Assert.True(attachment.CanFollow);

        attachment.SetQuiet(true);
        Assert.Equal(target, attachment.Target);
        Assert.False(attachment.CanFollow);

        attachment.SetQuiet(false);
        Assert.Equal(target, attachment.Target);
        Assert.True(attachment.CanFollow);
        // Invalid target or user drag detaches; resuming cannot attach a new window.
        attachment.Detach();
        attachment.SetQuiet(true);
        attachment.SetQuiet(false);
        Assert.Equal(IntPtr.Zero, attachment.Target);
        Assert.False(attachment.CanFollow);
    }

    private static FocusSessionSnapshot Snapshot(Guid id, FocusSessionStatus status) => new(
        new FocusSession { Id = id, Status = status }, 0, 900, 0);
}
