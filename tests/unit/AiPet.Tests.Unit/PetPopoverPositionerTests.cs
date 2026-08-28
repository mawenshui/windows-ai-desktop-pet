using System.Windows;
using AiPet.ToolWindow;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetPopoverPositionerTests
{
    [Fact]
    public void Prefers_above_and_centers_on_pet_when_space_is_available()
    {
        var result = PetPopoverPositioner.Calculate(
            new Rect(900, 800, 176, 198),
            new Rect(0, 0, 1920, 1040));

        Assert.Equal(PetPopoverSide.Above, result.Side);
        Assert.Equal(440, result.Width);
        Assert.Equal(536, result.Height);
        Assert.Equal(768, result.Left);
        Assert.Equal(258, result.Top);
        Assert.Equal(220, result.PointerOffsetX);
    }

    [Fact]
    public void Uses_below_when_top_edge_has_insufficient_space()
    {
        var result = PetPopoverPositioner.Calculate(
            new Rect(500, 40, 176, 198),
            new Rect(0, 0, 1920, 1040));

        Assert.Equal(PetPopoverSide.Below, result.Side);
        Assert.Equal(244, result.Top);
        Assert.True(result.Top >= 40 + 198);
    }

    [Fact]
    public void Clamps_to_offset_monitor_and_keeps_pointer_toward_pet()
    {
        var result = PetPopoverPositioner.Calculate(
            new Rect(-1260, 700, 176, 198),
            new Rect(-1280, 0, 1280, 984));

        Assert.Equal(-1272, result.Left);
        Assert.InRange(result.PointerOffsetX, 32, result.Width - 32);
        Assert.True(result.Left >= -1272);
    }

    [Fact]
    public void Shrinks_on_larger_side_instead_of_overlapping_pet()
    {
        var pet = new Rect(400, 300, 176, 198);
        var result = PetPopoverPositioner.Calculate(pet, new Rect(0, 0, 1024, 768));

        Assert.Equal(PetPopoverSide.Above, result.Side);
        Assert.Equal(286, result.Height);
        Assert.Equal(8, result.Top);
        Assert.True(result.Top + result.Height <= pet.Top - PetPopoverPositioner.PetGap);
    }
}
