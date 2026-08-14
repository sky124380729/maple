using System;

namespace Maple.Core
{
    public sealed class MovementDurationInput
    {
        public double HorizontalDistancePx { get; set; }
        public double AttackRangePx { get; set; }
        public double ObservedSpeedPxPerSecond { get; set; }
        public int MinHoldMs { get; set; }
        public int MaxHoldMs { get; set; }
        public double DistanceToEdgePx { get; set; }
        public bool CameraStable { get; set; }
    }

    public sealed class MovementDurationEstimate
    {
        public int HoldMs { get; internal set; }
        public double DistanceToTravelPx { get; internal set; }
        public bool UsedObservedSpeed { get; internal set; }
        public bool RequiresReplan { get; internal set; }
    }

    public sealed class MovementDurationEstimator
    {
        public const double FallbackSpeedPxPerSecond = 180;

        public MovementDurationEstimate Estimate(MovementDurationInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (input.MinHoldMs < 0 || input.MaxHoldMs < input.MinHoldMs) throw new ArgumentException("移动保持时长边界无效");
            if (input.AttackRangePx < 0) throw new ArgumentException("攻击范围不能为负数");

            double distance = Math.Max(0, Math.Abs(input.HorizontalDistancePx) - input.AttackRangePx);
            if (distance <= 0)
            {
                return new MovementDurationEstimate { HoldMs = 0, DistanceToTravelPx = 0, UsedObservedSpeed = input.ObservedSpeedPxPerSecond > 0 };
            }

            double speed = input.ObservedSpeedPxPerSecond > 0 ? input.ObservedSpeedPxPerSecond : FallbackSpeedPxPerSecond;
            int rawHold = (int)Math.Ceiling(distance / speed * 1000);
            int safeMax = input.MaxHoldMs;
            if (input.DistanceToEdgePx >= 0 && input.DistanceToEdgePx < double.MaxValue)
            {
                int edgeSafe = (int)Math.Floor(input.DistanceToEdgePx / speed * 1000 * 0.75);
                safeMax = Math.Min(safeMax, Math.Max(input.MinHoldMs, edgeSafe));
            }

            return new MovementDurationEstimate
            {
                HoldMs = Math.Max(input.MinHoldMs, Math.Min(rawHold, safeMax)),
                DistanceToTravelPx = distance,
                UsedObservedSpeed = input.ObservedSpeedPxPerSecond > 0,
                RequiresReplan = !input.CameraStable
            };
        }
    }
}
