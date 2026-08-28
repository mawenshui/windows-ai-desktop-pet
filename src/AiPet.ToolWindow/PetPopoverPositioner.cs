using System;
using System.Windows;

namespace AiPet.ToolWindow;

public enum PetPopoverSide
{
    Above,
    Below,
}

public readonly record struct PetPopoverPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    double PointerOffsetX,
    PetPopoverSide Side);

/// <summary>
/// Pure placement math for keeping the tool popover inside the pet's current
/// monitor while preserving a non-overlapping visual relationship.
/// </summary>
public static class PetPopoverPositioner
{
    public const double PreferredWidth = 440;
    public const double PreferredHeight = 536;
    public const double WorkAreaInset = 8;
    public const double PetGap = 6;
    private const double PointerHalfWidth = 12;
    private const double PointerEdgeInset = 20;

    public static PetPopoverPlacement Calculate(Rect petBounds, Rect workArea)
    {
        if (petBounds.IsEmpty) throw new ArgumentException("Pet bounds must not be empty.", nameof(petBounds));
        if (workArea.IsEmpty) throw new ArgumentException("Work area must not be empty.", nameof(workArea));

        var safeLeft = workArea.Left + WorkAreaInset;
        var safeTop = workArea.Top + WorkAreaInset;
        var safeRight = workArea.Right - WorkAreaInset;
        var safeBottom = workArea.Bottom - WorkAreaInset;
        var safeWidth = Math.Max(1, safeRight - safeLeft);

        var width = Math.Min(PreferredWidth, safeWidth);
        var availableAbove = Math.Max(0, petBounds.Top - PetGap - safeTop);
        var availableBelow = Math.Max(0, safeBottom - petBounds.Bottom - PetGap);

        PetPopoverSide side;
        if (availableAbove >= PreferredHeight)
            side = PetPopoverSide.Above;
        else if (availableBelow >= PreferredHeight)
            side = PetPopoverSide.Below;
        else
            side = availableAbove >= availableBelow ? PetPopoverSide.Above : PetPopoverSide.Below;

        var availableHeight = side == PetPopoverSide.Above ? availableAbove : availableBelow;
        var height = Math.Min(PreferredHeight, Math.Max(1, availableHeight));
        var petCenterX = petBounds.Left + petBounds.Width / 2;
        var left = Math.Clamp(petCenterX - width / 2, safeLeft, safeRight - width);
        var top = side == PetPopoverSide.Above
            ? petBounds.Top - PetGap - height
            : petBounds.Bottom + PetGap;

        var pointerMin = PointerEdgeInset + PointerHalfWidth;
        var pointerMax = Math.Max(pointerMin, width - PointerEdgeInset - PointerHalfWidth);
        var pointerOffsetX = Math.Clamp(petCenterX - left, pointerMin, pointerMax);

        return new PetPopoverPlacement(left, top, width, height, pointerOffsetX, side);
    }
}
