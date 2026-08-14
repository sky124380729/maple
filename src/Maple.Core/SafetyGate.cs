using System;
using Maple.Contracts;

namespace Maple.Core
{
    public sealed class SafetyGateContext
    {
        public bool TargetBound { get; set; }
        public bool IsForeground { get; set; }
        public bool FrameFresh { get; set; }
        public double SelfConfidence { get; set; }
        public bool MapValidated { get; set; }
        public bool HpHealthy { get; set; }
        public bool MpHealthy { get; set; }
        public bool InputAdapterHealthy { get; set; }
        public bool EmergencyStop { get; set; }
    }

    public sealed class SafetyGateDecision
    {
        public SafetyGateDecision(bool canAct, PauseReason reason, string message)
        {
            CanAct = canAct;
            Reason = reason;
            Message = message;
        }

        public bool CanAct { get; private set; }
        public PauseReason Reason { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class SafetyGate
    {
        private readonly double selfConfidenceThreshold;

        public SafetyGate(double selfConfidenceThreshold)
        {
            if (selfConfidenceThreshold < 0 || selfConfidenceThreshold > 1) throw new ArgumentOutOfRangeException("selfConfidenceThreshold");
            this.selfConfidenceThreshold = selfConfidenceThreshold;
        }

        public SafetyGateDecision Evaluate(SafetyGateContext context)
        {
            if (context == null) return Block(PauseReason.SafetyViolation, "安全门上下文缺失");
            if (context.EmergencyStop) return Block(PauseReason.SafetyViolation, "已触发紧急停止");
            if (!context.TargetBound) return Block(PauseReason.SafetyViolation, "目标窗口未绑定");
            if (!context.IsForeground) return Block(PauseReason.WindowNotForeground, "目标窗口不在前台");
            if (!context.FrameFresh) return Block(PauseReason.StaleFrame, "最新画面已过期");
            if (context.SelfConfidence < selfConfidenceThreshold) return Block(PauseReason.CalibrationRequired, "Self 置信度不足，程序需要自动校准");
            if (!context.MapValidated) return Block(PauseReason.MapNotValidated, "地图尚未验证");
            if (!context.HpHealthy || !context.MpHealthy) return Block(PauseReason.HealthUnknown, "HP/MP 状态不可安全确认");
            if (!context.InputAdapterHealthy) return Block(PauseReason.InputUnavailable, "输入适配器不可用");
            return new SafetyGateDecision(true, PauseReason.None, "所有安全条件已满足");
        }

        private static SafetyGateDecision Block(PauseReason reason, string message)
        {
            return new SafetyGateDecision(false, reason, message);
        }
    }
}
