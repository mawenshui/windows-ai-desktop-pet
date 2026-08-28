using System.Windows;
using AiPet.Pet;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetWindowPositionerTests
{
    private static readonly Rect Primary = new(0, 0, 1920, 1040);

    [Fact]
    public void Keeps_visible_position_unchanged()
    {
        var result = PetWindowPositioner.Clamp(new Rect(1600, 700, 176, 198), [Primary]);

        Assert.Equal(new Point(1600, 700), result);
    }

    [Fact]
    public void Clamps_position_saved_beyond_right_and_bottom_edges()
    {
        var result = PetWindowPositioner.Clamp(new Rect(2040, 1200, 176, 198), [Primary]);

        Assert.Equal(new Point(1736, 834), result);
    }

    [Fact]
    public void Moves_position_from_removed_monitor_to_nearest_work_area()
    {
        var result = PetWindowPositioner.Clamp(new Rect(3100, 500, 176, 198), [Primary]);

        Assert.Equal(new Point(1736, 500), result);
    }

    [Fact]
    public void Preserves_position_on_negative_coordinate_monitor()
    {
        var leftMonitor = new Rect(-1280, 0, 1280, 984);

        var result = PetWindowPositioner.Clamp(new Rect(-300, 400, 176, 198), [leftMonitor, Primary]);

        Assert.Equal(new Point(-300, 400), result);
    }
}
