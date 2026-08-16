using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class SelfMotionConfirmationTests
{
    [Fact]
    public void NameMatchedCandidateBecomesSelfEvenWhenAnotherPlayerScoresHigher()
    {
        var tracker = Tracker();
        DetectionCandidate named = Character(0.62, 0.35);

        SelfIdentityResult result = tracker.Update(
            [Character(0.18, 0.92), named],
            nowMonoMs: 100,
            monsterRoleAvailable: true,
            preferredSelfBox: named.Box);

        Assert.NotNull(result.Self);
        Assert.Equal(named.Box, result.Self!.Box);
        Assert.DoesNotContain(result.Players, player => player.Box.SequenceEqual(named.Box));
    }
    [Fact]
    public void LowConfidenceTrackRemainsDisplayOnlyUntilControlledMotionMatches()
    {
        var tracker = Tracker();
        tracker.Update([Character(0.20, 0.32)], 100, monsterRoleAvailable: true);
        SelfIdentityResult pending = tracker.Update([Character(0.20, 0.34)], 140, monsterRoleAvailable: true);

        Assert.NotNull(pending.Self);
        Assert.False(pending.CanDriveActions);
        Assert.True(tracker.BeginMotionCalibration());

        tracker.Update([Character(0.26, 0.33)], 180, monsterRoleAvailable: true);
        SelfMotionConfirmation confirmation = tracker.ConfirmMotion(expectedHorizontalDirection: 1, minimumDisplacement: 0.03);
        SelfIdentityResult ready = tracker.Update([Character(0.27, 0.31)], 220, monsterRoleAvailable: true);

        Assert.True(confirmation.Confirmed);
        Assert.Equal("SELF_MOTION_CONFIRMED", confirmation.Diagnostic);
        Assert.Equal(SelfIdentityStatus.Ready, ready.Status);
        Assert.NotNull(ready.Self);
        Assert.True(ready.Self.Confidence >= 0.9);
        Assert.True(ready.CanDriveActions);
    }

    [Fact]
    public void OppositeDirectionDoesNotConfirmSelf()
    {
        var tracker = Tracker();
        tracker.Update([Character(0.50, 0.35)], 100, true);
        tracker.Update([Character(0.50, 0.35)], 140, true);
        Assert.True(tracker.BeginMotionCalibration());

        tracker.Update([Character(0.44, 0.34)], 180, true);
        SelfMotionConfirmation result = tracker.ConfirmMotion(expectedHorizontalDirection: 1, minimumDisplacement: 0.03);

        Assert.False(result.Confirmed);
        Assert.Equal("SELF_MOTION_NOT_OBSERVED", result.Diagnostic);
    }

    [Fact]
    public void MultipleTracksMovingTogetherRemainAmbiguous()
    {
        var tracker = Tracker();
        DetectionCandidate[] baseline = [Character(0.20, 0.32), Character(0.65, 0.31)];
        tracker.Update(baseline, 100, true);
        tracker.Update(baseline, 140, true);
        Assert.True(tracker.BeginMotionCalibration());

        tracker.Update([Character(0.26, 0.33), Character(0.71, 0.32)], 180, true);
        SelfMotionConfirmation result = tracker.ConfirmMotion(expectedHorizontalDirection: 1, minimumDisplacement: 0.03);

        Assert.False(result.Confirmed);
        Assert.Equal("SELF_MOTION_AMBIGUOUS", result.Diagnostic);
    }

    private static SelfIdentityTracker Tracker() => new(new SelfIdentityOptions
    {
        WarmupFrames = 2,
        DetectionFloor = 0.25,
        MinimumConfidence = 0.60,
        MotionConfirmationConfidence = 0.95,
        OcclusionTtlMs = 200,
    });

    private static DetectionCandidate Character(double x, double confidence) =>
        new("character", confidence, [x, 0.65, 0.08, 0.16], DetectionRole.CharacterCandidate);
}
