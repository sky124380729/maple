using System;

namespace Maple.Core;

public sealed record ActionTimingDecision(
    int Seed,
    int BaselineHoldMs,
    int VariationMs,
    int FinalHoldMs);

public sealed class ActionTimingRandomizer
{
    private readonly Random random;
    private readonly double maximumFraction;

    public ActionTimingRandomizer(int seed, double maximumFraction)
    {
        if (maximumFraction is < 0 or > 0.25)
            throw new ArgumentOutOfRangeException(nameof(maximumFraction));
        Seed = seed;
        this.maximumFraction = maximumFraction;
        random = new Random(seed);
    }

    public int Seed { get; }

    public int Apply(int baselineHoldMs, int minimumHoldMs, int maximumHoldMs) =>
        ApplyWithTrace(baselineHoldMs, minimumHoldMs, maximumHoldMs).FinalHoldMs;

    public ActionTimingDecision ApplyWithTrace(
        int baselineHoldMs,
        int minimumHoldMs,
        int maximumHoldMs)
    {
        if (minimumHoldMs < 0) throw new ArgumentOutOfRangeException(nameof(minimumHoldMs));
        if (maximumHoldMs < minimumHoldMs) throw new ArgumentOutOfRangeException(nameof(maximumHoldMs));

        int amplitude = checked((int)Math.Round(
            Math.Max(0, baselineHoldMs) * maximumFraction,
            MidpointRounding.AwayFromZero));
        int sampledVariation = amplitude == 0 ? 0 : random.Next(-amplitude, amplitude + 1);
        long sampled = (long)baselineHoldMs + sampledVariation;
        int final = (int)Math.Clamp(sampled, minimumHoldMs, maximumHoldMs);
        return new ActionTimingDecision(
            Seed,
            baselineHoldMs,
            final - baselineHoldMs,
            final);
    }
}
