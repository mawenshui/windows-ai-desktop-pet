using System;
using System.Collections.Generic;

namespace AiPet.Common;

/// <summary>
/// Maps a 2D vector (or an angle in degrees) to one of the 8 cardinal/diagonal
/// directions. Mirrors the layout documented in
/// <c>res/images/RGS_8Directional/pet.json::directionModel.angleMap8Dir</c>.
/// </summary>
public static class DirectionMapper
{
    /// <summary>
    /// Discrete 8-direction bins, each 45° wide, centred on the canonical
    /// compass direction. Convention: 0° = +X = "right" (east), +90° = +Y
    /// = "down" (south) in WPF screen-space because WPF's Y axis grows
    /// downward. The same convention is returned by
    /// <c>Math.Atan2(dy, dx)</c> when fed WPF screen coordinates, which is
    /// what <see cref="FromVector"/> uses.
    /// </summary>
    private static readonly (Direction8 Direction, double Start, double End)[] Bins =
    {
        (Direction8.Right,     337.5,  22.5),
        (Direction8.DownRight,  22.5,  67.5),
        (Direction8.Down,       67.5, 112.5),
        (Direction8.DownLeft,  112.5, 157.5),
        (Direction8.Left,      157.5, 202.5),
        (Direction8.UpLeft,    202.5, 247.5),
        (Direction8.Up,        247.5, 292.5),
        (Direction8.UpRight,   292.5, 337.5),
    };

    /// <summary>
    /// Whether a direction is "authored" by the RGS source (true) or must be
    /// derived at runtime via horizontal mirroring (false).
    /// </summary>
    public static bool IsAuthored(Direction8 direction) => direction switch
    {
        Direction8.Down or Direction8.DownRight or Direction8.Right
            or Direction8.UpRight or Direction8.Up => true,
        _ => false,
    };

    /// <summary>
    /// The authored direction that should be rendered, then mirrored
    /// horizontally, to display the requested (possibly derived) direction.
    /// </summary>
    public static Direction8 AuthoredMirror(Direction8 direction) => direction switch
    {
        Direction8.Left      => Direction8.Right,
        Direction8.UpLeft    => Direction8.UpRight,
        Direction8.DownLeft  => Direction8.DownRight,
        _ => direction,
    };

    public static bool NeedsMirror(Direction8 direction) => !IsAuthored(direction);

    /// <summary>
    /// Angle → <see cref="Direction8"/>. Angle is in degrees, 0 = +X axis
    /// (math convention), growing counter-clockwise. Mouse angles from
    /// <c>Math.Atan2(deltaY, deltaX)</c> use the same convention.
    /// </summary>
    public static Direction8 FromAngle(double degrees)
    {
        // Normalize to [0, 360)
        var a = degrees % 360.0;
        if (a < 0) a += 360.0;

        foreach (var (direction, start, end) in Bins)
        {
            // Special case: the first bin wraps around 0°.
            if (start > end)
            {
                if (a >= start || a < end) return direction;
            }
            else
            {
                if (a >= start && a < end) return direction;
            }
        }
        // Should be unreachable.
        return Direction8.Right;
    }

    /// <summary>
    /// Convenience: vector (dx, dy) → <see cref="Direction8"/>. dy grows
    /// downward in WPF screen coordinates, which matches the conventional
    /// mouse-pointer angle.
    /// </summary>
    public static Direction8 FromVector(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return Direction8.Down;
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        return FromAngle(angle);
    }

    /// <summary>
    /// Maps pointer movement to the five front/side-facing directions used by
    /// the desktop companion. Rear-facing upward frames are intentionally
    /// folded toward a side or front pose so the pet never turns its back on
    /// the user.
    /// </summary>
    public static Direction8 FromFrontFacingVector(double dx, double dy)
    {
        return ToFrontFacing(FromVector(dx, dy));
    }

    /// <summary>
    /// Keeps nearby pointer tracking expressive, but returns to the authored
    /// front pose once the pointer is outside the interaction radius. This
    /// prevents a distant cursor from leaving the companion permanently in a
    /// side/rear-looking silhouette.
    /// </summary>
    public static Direction8 FromFrontBiasedVector(
        double dx,
        double dy,
        double frontPoseDistance)
    {
        if (frontPoseDistance <= 0) throw new ArgumentOutOfRangeException(nameof(frontPoseDistance));
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared >= frontPoseDistance * frontPoseDistance)
            return Direction8.Down;
        return FromFrontFacingVector(dx, dy);
    }

    /// <summary>
    /// Enforces the desktop-pet presentation rule at the render boundary:
    /// rear-facing frames are folded into a front or side pose.
    /// </summary>
    public static Direction8 ToFrontFacing(Direction8 direction)
    {
        return direction switch
        {
            Direction8.UpLeft => Direction8.Left,
            Direction8.Up => Direction8.Down,
            Direction8.UpRight => Direction8.Right,
            _ => direction,
        };
    }
}
