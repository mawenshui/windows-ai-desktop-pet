using AiPet.Common;
using Xunit;

namespace AiPet.Tests.Unit;

public class DirectionMapperTests
{
    [Theory]
    // Canonical compass directions
    // 0° = +X = right, 90° = +Y = down (WPF screen), 180° = -X = left,
    // 270° = -Y = up (WPF screen).
    [InlineData(0.0,    Direction8.Right)]
    [InlineData(45.0,   Direction8.DownRight)]
    [InlineData(90.0,   Direction8.Down)]
    [InlineData(135.0,  Direction8.DownLeft)]
    [InlineData(180.0,  Direction8.Left)]
    [InlineData(225.0,  Direction8.UpLeft)]
    [InlineData(270.0,  Direction8.Up)]
    [InlineData(315.0,  Direction8.UpRight)]
    // Wrap-around at -22.5 / 337.5
    [InlineData(350.0,  Direction8.Right)]
    [InlineData(-10.0,  Direction8.Right)]
    public void Angle_maps_to_expected_direction(double angle, Direction8 expected)
    {
        Assert.Equal(expected, DirectionMapper.FromAngle(angle));
    }

    [Theory]
    [InlineData(0,  1, Direction8.Down)]
    [InlineData(1,  1, Direction8.DownRight)]
    [InlineData(1,  0, Direction8.Right)]
    [InlineData(1, -1, Direction8.UpRight)]
    [InlineData(0, -1, Direction8.Up)]
    [InlineData(-1, -1, Direction8.UpLeft)]
    [InlineData(-1,  0, Direction8.Left)]
    [InlineData(-1,  1, Direction8.DownLeft)]
    public void Vector_maps_to_expected_direction(int dx, int dy, Direction8 expected)
    {
        Assert.Equal(expected, DirectionMapper.FromVector(dx, dy));
    }

    [Theory]
    [InlineData(-1, -1, Direction8.Left)]
    [InlineData(0, -1, Direction8.Down)]
    [InlineData(1, -1, Direction8.Right)]
    [InlineData(-1, 0, Direction8.Left)]
    [InlineData(1, 0, Direction8.Right)]
    [InlineData(-1, 1, Direction8.DownLeft)]
    [InlineData(0, 1, Direction8.Down)]
    [InlineData(1, 1, Direction8.DownRight)]
    public void Front_facing_vector_never_returns_a_rear_pose(int dx, int dy, Direction8 expected)
    {
        Assert.Equal(expected, DirectionMapper.FromFrontFacingVector(dx, dy));
    }

    [Fact]
    public void Render_boundary_sanitizer_never_returns_a_rear_pose()
    {
        var allowed = new[]
        {
            Direction8.Down, Direction8.DownLeft, Direction8.DownRight,
            Direction8.Left, Direction8.Right,
        };

        foreach (var direction in System.Enum.GetValues<Direction8>())
            Assert.Contains(DirectionMapper.ToFrontFacing(direction), allowed);
    }

    [Theory]
    [InlineData(500, 0)]
    [InlineData(-500, 0)]
    [InlineData(0, 500)]
    [InlineData(0, -500)]
    [InlineData(500, 500)]
    public void Distant_pointer_returns_to_the_front_pose(double dx, double dy)
    {
        Assert.Equal(Direction8.Down, DirectionMapper.FromFrontBiasedVector(dx, dy, 180));
    }

    [Fact]
    public void Nearby_pointer_keeps_front_safe_direction_tracking()
    {
        Assert.Equal(Direction8.Right, DirectionMapper.FromFrontBiasedVector(80, -40, 180));
        Assert.Equal(Direction8.Left, DirectionMapper.FromFrontBiasedVector(-80, -40, 180));
    }

    [Fact]
    public void Authored_set_is_5_directions()
    {
        Assert.True (DirectionMapper.IsAuthored(Direction8.Down));
        Assert.True (DirectionMapper.IsAuthored(Direction8.DownRight));
        Assert.True (DirectionMapper.IsAuthored(Direction8.Right));
        Assert.True (DirectionMapper.IsAuthored(Direction8.UpRight));
        Assert.True (DirectionMapper.IsAuthored(Direction8.Up));
        Assert.False(DirectionMapper.IsAuthored(Direction8.UpLeft));
        Assert.False(DirectionMapper.IsAuthored(Direction8.Left));
        Assert.False(DirectionMapper.IsAuthored(Direction8.DownLeft));
    }

    [Theory]
    [InlineData(Direction8.Left,      Direction8.Right)]
    [InlineData(Direction8.UpLeft,    Direction8.UpRight)]
    [InlineData(Direction8.DownLeft,  Direction8.DownRight)]
    [InlineData(Direction8.Right,     Direction8.Right)]   // authored: identity
    [InlineData(Direction8.Up,        Direction8.Up)]      // authored: identity
    public void AuthoredMirror_returns_mirror_for_derived_and_identity_for_authored(Direction8 dir, Direction8 expected)
    {
        Assert.Equal(expected, DirectionMapper.AuthoredMirror(dir));
    }

    [Fact]
    public void Authored_mirror_targets_are_actually_authored()
    {
        // Documented authored set: down / down_right / right / up_right / up.
        // Derived set must mirror to an authored one; authored set must
        // mirror to itself.
        foreach (Direction8 d in System.Enum.GetValues<Direction8>())
        {
            var mirror = DirectionMapper.AuthoredMirror(d);
            Assert.True(DirectionMapper.IsAuthored(mirror),
                $"AuthoredMirror({d}) returned {mirror}, which is not authored");
        }
    }
}
