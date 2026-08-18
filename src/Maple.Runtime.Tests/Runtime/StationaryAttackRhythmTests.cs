using Maple.Core;
using Maple.Runtime;
using Xunit;

namespace Maple.Runtime.Tests.Runtime;

public sealed class StationaryAttackRhythmTests
{
    [Fact]
    public void StationaryRhythmIsEnabledByDefault()
    {
        Assert.True(new OrchestratorOptions().StationaryRhythmEnabled);
    }

    [Theory]
    [InlineData(0, 20_000)]
    [InlineData(87, 30_000)]
    [InlineData(88, 10_000)]
    [InlineData(96, 19_999)]
    [InlineData(97, 1_000)]
    [InlineData(99, 9_999)]
    public void AttackHoldUsesWeightedBands(int selector, int sampledDurationMs)
    {
        var sampler = CreateSampler(selector, sampledDurationMs);

        Assert.Equal(sampledDurationMs, sampler.SampleAttackHoldMs());
    }

    [Theory]
    [InlineData(0, HorizontalDirection.Left)]
    [InlineData(1, HorizontalDirection.Right)]
    public void FirstMovementDirectionIsRandomized(int selector, HorizontalDirection expected)
    {
        var sampler = CreateSampler(selector);

        Assert.Equal(expected, sampler.SampleFirstDirection());
    }

    [Fact]
    public void MovementDurationsAndGapAreSampledIndependently()
    {
        var sampler = CreateSampler(60, 400, 50);

        Assert.Equal(60, sampler.SampleMovementHoldMs());
        Assert.Equal(400, sampler.SampleMovementHoldMs());
        Assert.Equal(50, sampler.SampleMovementGapMs());
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(24, true)]
    [InlineData(25, false)]
    [InlineData(99, false)]
    public void RestDecisionUsesTwentyFivePercentBranch(int selector, bool expected)
    {
        var sampler = CreateSampler(selector);

        Assert.Equal(expected, sampler.ShouldRest());
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(5_000)]
    public void RestDurationUsesInclusiveBounds(int sampledDurationMs)
    {
        var sampler = CreateSampler(sampledDurationMs);

        Assert.Equal(sampledDurationMs, sampler.SampleRestMs());
    }

    [Fact]
    public void InvalidWeightsAreRejected()
    {
        var options = new StationaryAttackRhythmOptions { PrimaryAttackWeight = 87 };

        Assert.Throws<ArgumentException>(() => new StationaryAttackRhythmSampler(new ScriptedRandomSource(), options));
    }

    private static StationaryAttackRhythmSampler CreateSampler(params int[] values)
    {
        return new StationaryAttackRhythmSampler(new ScriptedRandomSource(values), new StationaryAttackRhythmOptions());
    }

    private sealed class ScriptedRandomSource(params int[] values) : IRandomSource
    {
        private readonly Queue<int> values = new(values);

        public int Next(int minInclusive, int maxExclusive)
        {
            if (values.Count == 0) throw new InvalidOperationException("No scripted random value remains");
            int value = values.Dequeue();
            if (value < minInclusive || value >= maxExclusive)
            {
                throw new InvalidOperationException($"Scripted value {value} is outside [{minInclusive}, {maxExclusive})");
            }
            return value;
        }
    }
}
