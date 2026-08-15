using Maple.Core;
using Xunit;

namespace Maple.Runtime.Tests.Runtime;

public sealed class ActionTimingRandomizerTests
{
    [Fact]
    public void SameSeedProducesSameBoundedDuration()
    {
        var first = new ActionTimingRandomizer(42, maximumFraction: 0.08);
        var second = new ActionTimingRandomizer(42, maximumFraction: 0.08);

        Assert.Equal(first.Apply(500, 100, 700), second.Apply(500, 100, 700));
    }

    [Theory]
    [InlineData(100, 100, 300)]
    [InlineData(500, 120, 520)]
    [InlineData(10, 40, 80)]
    public void ResultNeverExceedsSafetyBounds(int baseline, int minimum, int maximum)
    {
        int value = new ActionTimingRandomizer(7, 0.08).Apply(baseline, minimum, maximum);

        Assert.InRange(value, minimum, maximum);
    }

    [Fact]
    public void TraceRecordsSeedAndExactVariation()
    {
        var randomizer = new ActionTimingRandomizer(99, 0.08);

        ActionTimingDecision result = randomizer.ApplyWithTrace(250, 80, 400);

        Assert.Equal(99, result.Seed);
        Assert.Equal(250, result.BaselineHoldMs);
        Assert.Equal(result.FinalHoldMs - result.BaselineHoldMs, result.VariationMs);
        Assert.InRange(result.FinalHoldMs, 80, 400);
    }
}
