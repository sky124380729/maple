using System;
using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Core
{
    public enum FacingDirection { Unknown, Left, Right }
    public enum AttackSelectionMode { Single, Auto, Group }

    public sealed class PlatformContext
    {
        public string CurrentPlatformId { get; set; }
        public string TargetPlatformId { get; set; }
        public bool SamePlatform { get; set; }
        public bool CanJump { get; set; }
        public bool CanClimbUp { get; set; }
        public bool CanClimbDown { get; set; }
        public double DistanceToBoundaryPx { get; set; }
        public bool CameraStable { get; set; }
        public FacingDirection Facing { get; set; }
    }

    public sealed class ActionPolicySettings
    {
        public int ClientWidthPx { get; set; }
        public double AttackRangePx { get; set; }
        public double SelfConfidenceThreshold { get; set; }
        public double TargetConfidenceThreshold { get; set; }
        public double ObservedSpeedPxPerSecond { get; set; }
        public int MinMoveHoldMs { get; set; }
        public int MaxMoveHoldMs { get; set; }
        public int AttackHoldMs { get; set; }
        public AttackSelectionMode AttackMode { get; set; }
        public int AreaTargetCount { get; set; } = 3;
        public int AttackProfileSwitchCooldownMs { get; set; }
        public double HpPotionThreshold { get; set; }
        public double MpPotionThreshold { get; set; }
        public ResourceMode HpPotionThresholdMode { get; set; } = ResourceMode.Percent;
        public ResourceMode MpPotionThresholdMode { get; set; } = ResourceMode.Percent;
        public bool PickupEnabled { get; set; }
        public int MaxAttackNoFeedbackAttempts { get; set; }
    }

    public sealed class ActionPolicyContext
    {
        public ObservationSnapshot Observation { get; set; }
        public SafetyGateDecision Safety { get; set; }
        public PlatformContext Platform { get; set; }
        public ActionPolicySettings Settings { get; set; }
        public long NowMonoMs { get; set; }
        public int ConsecutiveAttackNoFeedback { get; set; }
    }

    public sealed class ActionDecision
    {
        public AbstractAction Action { get; internal set; }
        public PauseReason PauseReason { get; internal set; }
        public string Reason { get; internal set; }
        public bool RequiresObservationAfter { get; internal set; }
    }

    public sealed class ActionPolicy
    {
        private readonly MovementDurationEstimator durationEstimator;
        private long actionSequence;
        private long currentIssuedAtMonoMs;
        private ActionProfileId? lastAttackProfile;
        private long lastAttackProfileChangedAtMonoMs;

        public ActionPolicy(MovementDurationEstimator durationEstimator)
        {
            this.durationEstimator = durationEstimator ?? throw new ArgumentNullException("durationEstimator");
        }

        public ActionDecision Decide(ActionPolicyContext context)
        {
            if (context == null || context.Observation == null || context.Settings == null || context.Platform == null)
            {
                return Pause(PauseReason.SafetyViolation, "动作策略上下文缺失");
            }
            if (context.Safety == null || !context.Safety.CanAct)
            {
                return Pause(context.Safety == null ? PauseReason.SafetyViolation : context.Safety.Reason, "安全门未通过");
            }
            currentIssuedAtMonoMs = context.NowMonoMs;
            var validation = ContractValidation.ValidateObservation(context.Observation);
            if (!validation.IsValid) return Pause(PauseReason.SafetyViolation, "观察快照无效：" + validation.Error);

            if (BelowThreshold(context.Observation.Hp, context.Settings.HpPotionThresholdMode, context.Settings.HpPotionThreshold)) return Action(ActionType.UsePotion, ActionProfileId.HpPotion, 100, "HP 补给优先", true);
            if (BelowThreshold(context.Observation.Mp, context.Settings.MpPotionThresholdMode, context.Settings.MpPotionThreshold)) return Action(ActionType.UsePotion, ActionProfileId.MpPotion, 100, "MP 补给优先", true);
            if (!context.Platform.CameraStable) return Action(ActionType.Replan, 0, "镜头正在移动，等待稳定帧", true);
            if (context.ConsecutiveAttackNoFeedback >= Math.Max(1, context.Settings.MaxAttackNoFeedbackAttempts))
            {
                return Pause(PauseReason.WatchdogTimeout, "攻击连续无视觉反馈，程序暂停并等待诊断");
            }

            MonsterObservation target = SelectTarget(context.Observation.Monsters, CenterX(context.Observation.Self.Box), context.NowMonoMs, context.Settings.TargetConfidenceThreshold);
            if (target == null)
            {
                if (context.Settings.PickupEnabled && context.Observation.Loot.Visible && context.Observation.Loot.Confidence >= context.Settings.TargetConfidenceThreshold)
                {
                    return Action(ActionType.Pickup, 100, "掉落物可拾取", true);
                }
                return Pause(PauseReason.TargetLost, "没有可用怪物目标");
            }

            if (!context.Platform.SamePlatform)
            {
                if (context.Platform.TargetPlatformId != null && context.Platform.CurrentPlatformId != null && context.Platform.TargetPlatformId != context.Platform.CurrentPlatformId)
                {
                    if (context.Platform.CanClimbUp) return Action(ActionType.ClimbUp, 180, "目标在上方平台", true);
                    if (context.Platform.CanClimbDown) return Action(ActionType.ClimbDown, 180, "目标在下方平台", true);
                    if (context.Platform.CanJump) return Action(ActionType.Jump, 120, "目标平台需要跳跃接近", true);
                    return Action(ActionType.Replan, 0, "平台不可达，等待地图拓扑重规划", true);
                }
            }

            double selfCenter = CenterX(context.Observation.Self.Box);
            double targetCenter = CenterX(target.Box);
            double horizontalDistancePx = (targetCenter - selfCenter) * context.Settings.ClientWidthPx;
            double verticalDistancePx = Math.Abs(CenterY(target.Box) - CenterY(context.Observation.Self.Box)) * context.Observation.Target.ClientHeight;
            if (Math.Abs(horizontalDistancePx) <= context.Settings.AttackRangePx && verticalDistancePx <= 90)
            {
                bool facingTarget = (horizontalDistancePx < 0 && context.Platform.Facing == FacingDirection.Left) || (horizontalDistancePx >= 0 && context.Platform.Facing == FacingDirection.Right);
                if (!facingTarget)
                {
                    ActionType turnDirection = horizontalDistancePx < 0 ? ActionType.MoveLeft : ActionType.MoveRight;
                    return Action(turnDirection, Math.Max(context.Settings.MinMoveHoldMs, 40), "先调整角色朝向", true);
                }
                ActionProfileId attackProfile = SelectAttackProfile(context, selfCenter);
                return Action(ActionType.Attack, attackProfile, Math.Max(1, context.Settings.AttackHoldMs), attackProfile == ActionProfileId.AreaAttack ? "多个目标进入范围，使用群体攻击" : "目标已进入攻击范围", true);
            }

            if (context.Platform.DistanceToBoundaryPx <= 24 && !context.Platform.CanJump)
            {
                return Action(ActionType.Replan, 0, "接近平台边界且无法安全跳跃", true);
            }

            MovementDurationEstimate estimate = durationEstimator.Estimate(new MovementDurationInput
            {
                HorizontalDistancePx = horizontalDistancePx,
                AttackRangePx = context.Settings.AttackRangePx,
                ObservedSpeedPxPerSecond = context.Settings.ObservedSpeedPxPerSecond,
                MinHoldMs = context.Settings.MinMoveHoldMs,
                MaxHoldMs = context.Settings.MaxMoveHoldMs,
                DistanceToEdgePx = context.Platform.DistanceToBoundaryPx,
                CameraStable = context.Platform.CameraStable
            });
            ActionType direction = horizontalDistancePx < 0 ? ActionType.MoveLeft : ActionType.MoveRight;
            return Action(direction, estimate.HoldMs, "根据当前距离和位移反馈接近目标", true);
        }

        private AbstractAction NewAction(ActionType type, int holdMs, ActionProfileId? profileId = null)
        {
            return new AbstractAction
            {
                ActionId = "action-" + (++actionSequence),
                Type = type,
                ProfileId = profileId,
                IssuedAtMonoMs = currentIssuedAtMonoMs,
                HoldMs = Math.Max(0, holdMs),
                MaxDurationMs = ContractConstants.MaxActionDurationMs
            };
        }

        private ActionDecision Action(ActionType type, int holdMs, string reason, bool observeAfter)
        {
            return new ActionDecision { Action = NewAction(type, holdMs), Reason = reason, PauseReason = PauseReason.None, RequiresObservationAfter = observeAfter };
        }

        private ActionDecision Action(ActionType type, ActionProfileId profileId, int holdMs, string reason, bool observeAfter)
        {
            return new ActionDecision { Action = NewAction(type, holdMs, profileId), Reason = reason, PauseReason = PauseReason.None, RequiresObservationAfter = observeAfter };
        }

        private static ActionDecision Pause(PauseReason reason, string message)
        {
            return new ActionDecision { Action = new AbstractAction { ActionId = "pause", Type = ActionType.Pause, HoldMs = 0, MaxDurationMs = ContractConstants.MaxActionDurationMs }, Reason = message, PauseReason = reason, RequiresObservationAfter = false };
        }

        private static MonsterObservation SelectTarget(List<MonsterObservation> monsters, double selfCenter, long nowMonoMs, double confidenceThreshold)
        {
            MonsterObservation selected = null;
            double best = double.MaxValue;
            if (monsters == null) return null;
            foreach (MonsterObservation monster in monsters)
            {
                if (monster == null || monster.Confidence < confidenceThreshold || monster.FreshUntilMonoMs < nowMonoMs) continue;
                double distance = Math.Abs(CenterX(monster.Box) - selfCenter);
                if (distance < best) { best = distance; selected = monster; }
            }
            return selected;
        }

        private ActionProfileId SelectAttackProfile(ActionPolicyContext context, double selfCenter)
        {
            ActionProfileId requested = context.Settings.AttackMode switch
            {
                AttackSelectionMode.Group => ActionProfileId.AreaAttack,
                AttackSelectionMode.Auto when CountAttackableTargets(context, selfCenter) >= Math.Max(2, context.Settings.AreaTargetCount) => ActionProfileId.AreaAttack,
                _ => ActionProfileId.SingleAttack,
            };
            if (context.Settings.AttackMode == AttackSelectionMode.Auto
                && lastAttackProfile.HasValue
                && requested != lastAttackProfile.Value
                && context.NowMonoMs - lastAttackProfileChangedAtMonoMs < Math.Max(0, context.Settings.AttackProfileSwitchCooldownMs))
            {
                return lastAttackProfile.Value;
            }
            if (!lastAttackProfile.HasValue || requested != lastAttackProfile.Value)
            {
                lastAttackProfile = requested;
                lastAttackProfileChangedAtMonoMs = context.NowMonoMs;
            }
            return requested;
        }

        private static int CountAttackableTargets(ActionPolicyContext context, double selfCenter)
        {
            if (context.Observation.Monsters == null) return 0;
            int count = 0;
            foreach (MonsterObservation monster in context.Observation.Monsters)
            {
                if (monster == null || monster.Confidence < context.Settings.TargetConfidenceThreshold || monster.FreshUntilMonoMs < context.NowMonoMs) continue;
                double horizontalDistancePx = Math.Abs(CenterX(monster.Box) - selfCenter) * context.Settings.ClientWidthPx;
                double verticalDistancePx = Math.Abs(CenterY(monster.Box) - CenterY(context.Observation.Self.Box)) * context.Observation.Target.ClientHeight;
                if (horizontalDistancePx <= context.Settings.AttackRangePx && verticalDistancePx <= 90) count++;
            }
            return count;
        }

        private static double CenterX(double[] box) { return box[0] + box[2] / 2.0; }
        private static double CenterY(double[] box) { return box[1] + box[3] / 2.0; }

        private static bool BelowThreshold(ResourceObservation resource, ResourceMode thresholdMode, double threshold)
        {
            if (threshold < 0) return false;
            if (thresholdMode == ResourceMode.Absolute)
            {
                double? current = resource.CurrentValue ?? (resource.Mode == ResourceMode.Absolute ? resource.Value : null);
                return current.HasValue && current.Value <= threshold;
            }
            double? percent = resource.Mode == ResourceMode.Percent
                ? resource.Value
                : resource.CurrentValue.HasValue && resource.MaximumValue > 0
                    ? resource.CurrentValue.Value / resource.MaximumValue.Value
                    : null;
            return percent.HasValue && percent.Value <= threshold;
        }
    }
}
