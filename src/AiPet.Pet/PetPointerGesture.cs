using System;

namespace AiPet.Pet;

/// <summary>
/// Shared pointer thresholds for the pet. Keeping the decision pure makes the
/// click-versus-drag boundary deterministic and regression-testable.
/// </summary>
public static class PetPointerGesture
{
    public const double DragStartDistancePx = 12.0;
    public const int SingleClickDelayMs = 250;

    public static bool ShouldStartDrag(double deltaX, double deltaY) =>
        Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) >= DragStartDistancePx;

    public static PetPointerReleaseAction ResolveRelease(
        bool wasDragging,
        double movedDistance,
        int clickCount)
    {
        if (wasDragging || movedDistance >= DragStartDistancePx)
            return PetPointerReleaseAction.None;
        return clickCount >= 2
            ? PetPointerReleaseAction.DoubleClickAction
            : PetPointerReleaseAction.DeferSingleClick;
    }
}

public enum PetPointerReleaseAction
{
    None,
    DeferSingleClick,
    DoubleClickAction,
}
