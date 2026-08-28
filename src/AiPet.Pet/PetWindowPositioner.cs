using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AiPet.Pet;

public static class PetWindowPositioner
{
    public const double WorkAreaMargin = 8;

    public static System.Windows.Point Clamp(Rect desired, IReadOnlyList<Rect> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (workAreas.Count == 0)
            return new System.Windows.Point(desired.Left, desired.Top);

        var target = workAreas
            .Select(area => new
            {
                Area = area,
                Intersection = Rect.Intersect(desired, area),
                Distance = DistanceSquared(desired, area)
            })
            .OrderByDescending(candidate => candidate.Intersection.IsEmpty
                ? 0
                : candidate.Intersection.Width * candidate.Intersection.Height)
            .ThenBy(candidate => candidate.Distance)
            .First()
            .Area;

        var minLeft = target.Left + WorkAreaMargin;
        var minTop = target.Top + WorkAreaMargin;
        var maxLeft = Math.Max(minLeft, target.Right - desired.Width - WorkAreaMargin);
        var maxTop = Math.Max(minTop, target.Bottom - desired.Height - WorkAreaMargin);

        return new System.Windows.Point(
            Math.Clamp(desired.Left, minLeft, maxLeft),
            Math.Clamp(desired.Top, minTop, maxTop));
    }

    private static double DistanceSquared(Rect desired, Rect area)
    {
        var desiredX = desired.Left + desired.Width / 2;
        var desiredY = desired.Top + desired.Height / 2;
        var areaX = Math.Clamp(desiredX, area.Left, area.Right);
        var areaY = Math.Clamp(desiredY, area.Top, area.Bottom);
        var dx = desiredX - areaX;
        var dy = desiredY - areaY;
        return dx * dx + dy * dy;
    }
}
