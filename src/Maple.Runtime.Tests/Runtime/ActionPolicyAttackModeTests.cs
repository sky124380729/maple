using Maple.Contracts;
using Maple.Core;
using Xunit;

namespace Maple.Runtime.Tests.Runtime;

public sealed class ActionPolicyAttackModeTests
{
    [Fact]
    public void AbsolutePotionThresholdUsesRecognizedCurrentValue()
    {
        ActionPolicySettings settings = Settings(AttackSelectionMode.Single);
        settings.HpPotionThresholdMode = ResourceMode.Absolute;
        settings.HpPotionThreshold = 70;
        ObservationSnapshot observation = Observation(1_000, monsterCount: 1);
        observation.Hp.CurrentValue = 66;
        observation.Hp.MaximumValue = 98;

        ActionDecision decision = Decide(new ActionPolicy(new MovementDurationEstimator()), observation, settings, 1_000);

        Assert.Equal(ActionType.UsePotion, decision.Action.Type);
        Assert.Equal(ActionProfileId.HpPotion, decision.Action.ProfileId);
    }
    [Theory]
    [InlineData(AttackSelectionMode.Single, 3, ActionProfileId.SingleAttack)]
    [InlineData(AttackSelectionMode.Group, 1, ActionProfileId.AreaAttack)]
    [InlineData(AttackSelectionMode.Auto, 2, ActionProfileId.SingleAttack)]
    [InlineData(AttackSelectionMode.Auto, 3, ActionProfileId.AreaAttack)]
    public void SelectsConfiguredAttackProfile(AttackSelectionMode mode, int monsterCount, ActionProfileId expected)
    {
        long now = 1_000;
        var observation = Observation(now, monsterCount);
        var settings = Settings(mode);
        var policy = new ActionPolicy(new MovementDurationEstimator());

        ActionDecision decision = policy.Decide(new ActionPolicyContext
        {
            Observation = observation,
            Safety = new SafetyGateDecision(true, PauseReason.None, "test"),
            Platform = new PlatformContext { SamePlatform = true, CurrentPlatformId = "p1", TargetPlatformId = "p1", CameraStable = true, Facing = FacingDirection.Right, DistanceToBoundaryPx = 500 },
            Settings = settings,
            NowMonoMs = now,
        });

        Assert.Equal(ActionType.Attack, decision.Action.Type);
        Assert.Equal(expected, decision.Action.ProfileId);
    }

    [Fact]
    public void AutoModeHonorsProfileSwitchCooldown()
    {
        var policy = new ActionPolicy(new MovementDurationEstimator());
        ActionPolicySettings settings = WithCooldown(Settings(AttackSelectionMode.Auto), 1_200);

        ActionDecision first = Decide(policy, Observation(1_000, 3), settings, 1_000);
        ActionDecision insideCooldown = Decide(policy, Observation(1_500, 1), settings, 1_500);
        ActionDecision afterCooldown = Decide(policy, Observation(2_300, 1), settings, 2_300);

        Assert.Equal(ActionProfileId.AreaAttack, first.Action.ProfileId);
        Assert.Equal(ActionProfileId.AreaAttack, insideCooldown.Action.ProfileId);
        Assert.Equal(ActionProfileId.SingleAttack, afterCooldown.Action.ProfileId);
    }

    private static ActionDecision Decide(ActionPolicy policy, ObservationSnapshot observation, ActionPolicySettings settings, long now) =>
        policy.Decide(new ActionPolicyContext
        {
            Observation = observation,
            Safety = new SafetyGateDecision(true, PauseReason.None, "test"),
            Platform = new PlatformContext { SamePlatform = true, CurrentPlatformId = "p1", TargetPlatformId = "p1", CameraStable = true, Facing = FacingDirection.Right, DistanceToBoundaryPx = 500 },
            Settings = settings,
            NowMonoMs = now,
        });

    private static ActionPolicySettings Settings(AttackSelectionMode mode) => new()
    {
        ClientWidthPx = 1_280,
        AttackRangePx = 100,
        SelfConfidenceThreshold = 0.9,
        TargetConfidenceThreshold = 0.8,
        ObservedSpeedPxPerSecond = 320,
        MinMoveHoldMs = 60,
        MaxMoveHoldMs = 400,
        AttackHoldMs = 80,
        HpPotionThreshold = 0.35,
        MpPotionThreshold = 0.3,
        PickupEnabled = false,
        MaxAttackNoFeedbackAttempts = 2,
        AttackMode = mode,
        AreaTargetCount = 3,
    };

    private static ActionPolicySettings WithCooldown(ActionPolicySettings settings, int milliseconds)
    {
        settings.AttackProfileSwitchCooldownMs = milliseconds;
        return settings;
    }

    private static ObservationSnapshot Observation(long now, int monsterCount)
    {
        long fresh = now + 250;
        return new ObservationSnapshot
        {
            SchemaVersion = 2,
            FrameId = now,
            CapturedAtMonoMs = now,
            Target = new TargetBinding { SchemaVersion = 2, Hwnd = "0x1", Pid = 1, ClientWidth = 1_280, ClientHeight = 720, Dpi = 96 },
            Self = new SelfObservation { Box = [0.50, 0.50, 0.06, 0.16], Confidence = 0.99, FreshUntilMonoMs = fresh },
            Players = [],
            Monsters = Enumerable.Range(0, monsterCount).Select(index => new MonsterObservation
            {
                Class = "snail",
                TargetId = "m" + index,
                Box = [0.54 + index * 0.01, 0.50, 0.04, 0.12],
                Confidence = 0.95,
                FreshUntilMonoMs = fresh,
            }).ToList(),
            Loot = new LootObservation { Visible = false, Confidence = 0, FreshUntilMonoMs = fresh },
            Hp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.9, Confidence = 0.99, FreshUntilMonoMs = fresh },
            Mp = new ResourceObservation { Mode = ResourceMode.Percent, Value = 0.9, Confidence = 0.99, FreshUntilMonoMs = fresh },
            Map = new MapObservation { MapId = "test", State = MapArchiveState.Validated, Confidence = 0.99, FreshUntilMonoMs = fresh },
            State = SessionState.Observing,
        };
    }
}
