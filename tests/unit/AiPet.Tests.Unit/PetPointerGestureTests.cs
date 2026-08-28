using AiPet.Pet;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetPointerGestureTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(8, 8, false)]
    [InlineData(12, 0, true)]
    [InlineData(9, 9, true)]
    public void Drag_starts_only_after_a_deliberate_pointer_move(double x, double y, bool expected)
    {
        Assert.Equal(expected, PetPointerGesture.ShouldStartDrag(x, y));
    }

    [Theory]
    [InlineData(false, 2, 1, PetPointerReleaseAction.DeferSingleClick)]
    [InlineData(false, 2, 2, PetPointerReleaseAction.DoubleClickAction)]
    [InlineData(true, 20, 1, PetPointerReleaseAction.None)]
    [InlineData(false, 12, 1, PetPointerReleaseAction.None)]
    public void Release_routes_single_double_and_drag_without_overlap(
        bool wasDragging,
        double moved,
        int clickCount,
        PetPointerReleaseAction expected)
    {
        Assert.Equal(expected, PetPointerGesture.ResolveRelease(wasDragging, moved, clickCount));
    }
}
