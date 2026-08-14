using System;
using Maple.Contracts;

namespace Maple.Vision
{
    public sealed class ConfidenceDecision
    {
        public bool Ready { get; internal set; }
        public PauseReason PauseReason { get; internal set; }
        public string Message { get; internal set; } = string.Empty;
    }

    public sealed class ConfidencePolicy
    {
        private readonly double selfThreshold;
        private readonly int requiredFrames;

        public ConfidencePolicy(double selfThreshold, int requiredFrames)
        {
            if (selfThreshold < 0 || selfThreshold > 1) throw new ArgumentOutOfRangeException("selfThreshold");
            if (requiredFrames < 1) throw new ArgumentOutOfRangeException("requiredFrames");
            this.selfThreshold = selfThreshold;
            this.requiredFrames = requiredFrames;
        }

        public int ContinuousHighConfidenceFrames { get; private set; }

        public ConfidenceDecision ObserveSelf(double confidence)
        {
            if (confidence < selfThreshold)
            {
                ContinuousHighConfidenceFrames = 0;
                return new ConfidenceDecision { Ready = false, PauseReason = PauseReason.CalibrationRequired, Message = "Self 置信度不足，程序正在自动重标定" };
            }

            ContinuousHighConfidenceFrames++;
            if (ContinuousHighConfidenceFrames < requiredFrames)
            {
                return new ConfidenceDecision { Ready = false, PauseReason = PauseReason.CalibrationRequired, Message = "正在等待连续稳定的 Self 观察" };
            }
            return new ConfidenceDecision { Ready = true, PauseReason = PauseReason.None, Message = "Self 连续置信度已通过" };
        }
    }
}
