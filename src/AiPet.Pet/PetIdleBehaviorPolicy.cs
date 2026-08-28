using System;

namespace AiPet.Pet;

public enum PetIdleBehaviorKind
{
    Speech,
    Gesture,
    Roam,
}

/// <summary>Deterministic policy helpers for low-frequency companion behavior.</summary>
public static class PetIdleBehaviorPolicy
{
    public static TimeSpan NextDelay(int sample)
    {
        var seconds = 32 + PositiveModulo(sample, 35); // 32–66 seconds
        return TimeSpan.FromSeconds(seconds);
    }

    public static PetIdleBehaviorKind Choose(int sample) => PositiveModulo(sample, 10) switch
    {
        <= 4 => PetIdleBehaviorKind.Speech,
        <= 7 => PetIdleBehaviorKind.Gesture,
        _ => PetIdleBehaviorKind.Roam,
    };

    public static double RoamDistance(int sample) => 96 + PositiveModulo(sample, 129);

    public static double EaseInOutCubic(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
