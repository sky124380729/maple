namespace Maple.Core;

public interface IRandomSource
{
    int Next(int minInclusive, int maxExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    public int Next(int minInclusive, int maxExclusive) => Random.Shared.Next(minInclusive, maxExclusive);
}

public enum HorizontalDirection
{
    Left,
    Right
}

public sealed class StationaryAttackRhythmOptions
{
    public int PrimaryAttackWeight { get; init; } = 88;
    public int MediumAttackWeight { get; init; } = 9;
    public int ShortAttackWeight { get; init; } = 3;
    public int PrimaryAttackMinMs { get; init; } = 20_000;
    public int PrimaryAttackMaxMs { get; init; } = 30_000;
    public int MediumAttackMinMs { get; init; } = 10_000;
    public int MediumAttackMaxExclusiveMs { get; init; } = 20_000;
    public int ShortAttackMinMs { get; init; } = 1_000;
    public int ShortAttackMaxExclusiveMs { get; init; } = 10_000;
    // Keep the stationary correction short enough to avoid walking off a platform.
    public int MovementMinHoldMs { get; init; } = 50;
    public int MovementMaxHoldMs { get; init; } = 220;
    public int MovementMinGapMs { get; init; } = 60;
    public int MovementMaxGapMs { get; init; } = 280;
    public int RestProbabilityPercent { get; init; } = 25;
    public int RestMinMs { get; init; } = 2_000;
    public int RestMaxMs { get; init; } = 5_000;

    internal void Validate()
    {
        if (PrimaryAttackWeight < 0 || MediumAttackWeight < 0 || ShortAttackWeight < 0
            || PrimaryAttackWeight + MediumAttackWeight + ShortAttackWeight != 100)
        {
            throw new ArgumentException("攻击时长权重总和必须为 100");
        }
        ValidateInclusiveRange(PrimaryAttackMinMs, PrimaryAttackMaxMs, nameof(PrimaryAttackMinMs));
        ValidateExclusiveRange(MediumAttackMinMs, MediumAttackMaxExclusiveMs, nameof(MediumAttackMinMs));
        ValidateExclusiveRange(ShortAttackMinMs, ShortAttackMaxExclusiveMs, nameof(ShortAttackMinMs));
        ValidateInclusiveRange(MovementMinHoldMs, MovementMaxHoldMs, nameof(MovementMinHoldMs));
        ValidateInclusiveRange(MovementMinGapMs, MovementMaxGapMs, nameof(MovementMinGapMs));
        ValidateInclusiveRange(RestMinMs, RestMaxMs, nameof(RestMinMs));
        if (RestProbabilityPercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(RestProbabilityPercent));
    }

    private static void ValidateInclusiveRange(int minimum, int maximum, string parameterName)
    {
        if (minimum < 0 || maximum < minimum) throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateExclusiveRange(int minimum, int maximumExclusive, string parameterName)
    {
        if (minimum < 0 || maximumExclusive <= minimum) throw new ArgumentOutOfRangeException(parameterName);
    }
}

public sealed class StationaryAttackRhythmSampler
{
    private readonly IRandomSource random;
    private readonly StationaryAttackRhythmOptions options;

    public StationaryAttackRhythmSampler(IRandomSource random, StationaryAttackRhythmOptions options)
    {
        this.random = random ?? throw new ArgumentNullException(nameof(random));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
    }

    public int SampleAttackHoldMs()
    {
        int selector = random.Next(0, 100);
        if (selector < options.PrimaryAttackWeight)
        {
            return random.Next(options.PrimaryAttackMinMs, checked(options.PrimaryAttackMaxMs + 1));
        }
        if (selector < options.PrimaryAttackWeight + options.MediumAttackWeight)
        {
            return random.Next(options.MediumAttackMinMs, options.MediumAttackMaxExclusiveMs);
        }
        return random.Next(options.ShortAttackMinMs, options.ShortAttackMaxExclusiveMs);
    }

    public HorizontalDirection SampleFirstDirection()
    {
        return random.Next(0, 2) == 0 ? HorizontalDirection.Left : HorizontalDirection.Right;
    }

    public int SampleMovementHoldMs()
    {
        return random.Next(options.MovementMinHoldMs, checked(options.MovementMaxHoldMs + 1));
    }

    public int SampleMovementGapMs()
    {
        return random.Next(options.MovementMinGapMs, checked(options.MovementMaxGapMs + 1));
    }

    public bool ShouldRest()
    {
        return random.Next(0, 100) < options.RestProbabilityPercent;
    }

    public int SampleRestMs()
    {
        return random.Next(options.RestMinMs, checked(options.RestMaxMs + 1));
    }
}
