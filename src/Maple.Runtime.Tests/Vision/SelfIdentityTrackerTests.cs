using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class SelfIdentityTrackerTests
{
    [Fact]
    public void UniqueStableCharacterBecomesSelfAndLaterCharacterIsPlayer()
    {
        var tracker = new SelfIdentityTracker(new SelfIdentityOptions
        {
            WarmupFrames = 2,
            MinimumConfidence = 0.75,
            OcclusionTtlMs = 200,
        });

        SelfIdentityResult first = tracker.Update([Character(0.45, 0.7, 0.92)], nowMonoMs: 100, monsterRoleAvailable: true);
        SelfIdentityResult second = tracker.Update([Character(0.46, 0.7, 0.94)], nowMonoMs: 140, monsterRoleAvailable: true);
        SelfIdentityResult third = tracker.Update([Character(0.47, 0.7, 0.95), Character(0.2, 0.7, 0.9)], nowMonoMs: 180, monsterRoleAvailable: true);

        Assert.Equal(SelfIdentityStatus.WarmingUp, first.Status);
        Assert.Equal(SelfIdentityStatus.Ready, second.Status);
        Assert.NotNull(second.Self);
        Assert.True(second.CanDriveActions);
        Assert.Equal(SelfIdentityStatus.Ready, third.Status);
        Assert.Single(third.Players);
        Assert.True(third.CanDriveActions);
    }

    [Fact]
    public void TemporaryOcclusionPreservesSelfOnlyUntilTtl()
    {
        var tracker = new SelfIdentityTracker(new SelfIdentityOptions { WarmupFrames = 1, MinimumConfidence = 0.75, OcclusionTtlMs = 100 });
        tracker.Update([Character(0.4, 0.7, 0.95)], 100, monsterRoleAvailable: true);

        SelfIdentityResult occluded = tracker.Update([], 170, monsterRoleAvailable: true);
        SelfIdentityResult expired = tracker.Update([], 201, monsterRoleAvailable: true);

        Assert.Equal(SelfIdentityStatus.Occluded, occluded.Status);
        Assert.NotNull(occluded.Self);
        Assert.False(occluded.CanDriveActions);
        Assert.Equal(SelfIdentityStatus.NotFound, expired.Status);
        Assert.Null(expired.Self);
    }

    [Fact]
    public void MultipleInitialCharactersFailClosedAsAmbiguousAndResetClearsTracks()
    {
        var tracker = new SelfIdentityTracker(new SelfIdentityOptions { WarmupFrames = 1, MinimumConfidence = 0.75, OcclusionTtlMs = 100 });

        SelfIdentityResult ambiguous = tracker.Update([Character(0.2, 0.7, 0.9), Character(0.7, 0.7, 0.91)], 100, monsterRoleAvailable: true);
        tracker.Reset();
        SelfIdentityResult reset = tracker.Update([], 120, monsterRoleAvailable: true);

        Assert.Equal(SelfIdentityStatus.Ambiguous, ambiguous.Status);
        Assert.Equal("SELF_AMBIGUOUS", ambiguous.Diagnostic);
        Assert.False(ambiguous.CanDriveActions);
        Assert.Equal(SelfIdentityStatus.NotFound, reset.Status);
    }

    private static DetectionCandidate Character(double x, double y, double confidence) =>
        new("character", confidence, [x, y, 0.08, 0.16], DetectionRole.CharacterCandidate);
}
