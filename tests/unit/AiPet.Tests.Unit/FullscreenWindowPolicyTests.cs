using System.Windows;
using AiPet.SystemIntegration;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class FullscreenWindowPolicyTests
{
    [Fact]
    public void Exact_or_two_pixel_monitor_coverage_is_fullscreen()
    {
        var monitor = new Rect(1920, 0, 2560, 1440);
        Assert.True(FullscreenWindowPolicy.CoversMonitor(monitor, monitor));
        Assert.True(FullscreenWindowPolicy.CoversMonitor(new Rect(1921, 1, 2558, 1438), monitor));
    }

    [Fact]
    public void Maximized_work_area_and_zero_size_are_not_fullscreen()
    {
        var monitor = new Rect(0, 0, 1920, 1080);
        Assert.False(FullscreenWindowPolicy.CoversMonitor(new Rect(0, 0, 1920, 1040), monitor));
        Assert.False(FullscreenWindowPolicy.CoversMonitor(new Rect(0, 0, 0, 0), monitor));
    }

    [Fact]
    public void Coverage_on_another_monitor_does_not_match_target_monitor_bounds()
    {
        Assert.False(FullscreenWindowPolicy.CoversMonitor(
            new Rect(1920, 0, 1920, 1080),
            new Rect(0, 0, 1920, 1080)));
    }
}
