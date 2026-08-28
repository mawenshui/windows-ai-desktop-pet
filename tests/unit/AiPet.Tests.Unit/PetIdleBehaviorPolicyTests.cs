using AiPet.Pet;
using Xunit;

namespace AiPet.Tests.Unit;

public sealed class PetIdleBehaviorPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(34)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Idle_delay_stays_low_frequency_and_bounded(int sample)
    {
        var delay = PetIdleBehaviorPolicy.NextDelay(sample);
        Assert.InRange(delay.TotalSeconds, 32, 66);
    }

    [Fact]
    public void Behavior_policy_includes_speech_gesture_and_roam()
    {
        Assert.Equal(PetIdleBehaviorKind.Speech, PetIdleBehaviorPolicy.Choose(0));
        Assert.Equal(PetIdleBehaviorKind.Gesture, PetIdleBehaviorPolicy.Choose(5));
        Assert.Equal(PetIdleBehaviorKind.Roam, PetIdleBehaviorPolicy.Choose(8));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void Roam_easing_is_clamped_and_has_stable_endpoints(double input, double expected)
    {
        Assert.Equal(expected, PetIdleBehaviorPolicy.EaseInOutCubic(input), precision: 6);
    }
}
