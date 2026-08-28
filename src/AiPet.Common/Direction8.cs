namespace AiPet.Common;

/// <summary>
/// 8-direction model. The "authored" set is the directions actually present
/// in the RGS sprite source; the "derived" set is obtained at runtime by
/// horizontal mirroring via <c>ScaleTransform(-1, 1)</c> over the authored
/// counterpart.
/// </summary>
public enum Direction8
{
    Down,
    DownRight,
    Right,
    UpRight,
    Up,
    UpLeft,
    Left,
    DownLeft
}
