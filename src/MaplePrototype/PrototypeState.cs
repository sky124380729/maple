using System;

namespace MapleVisualPrototype
{
    internal enum SessionState
    {
        Stopped,
        Observing,
        MapScanning,
        MapCalibrating,
        Paused,
        EmergencyStop
    }

    internal static class SessionStateText
    {
        internal static string ToChinese(SessionState state)
        {
            switch (state)
            {
                case SessionState.Observing: return "观察中";
                case SessionState.MapScanning: return "地图扫描";
                case SessionState.MapCalibrating: return "地图标定";
                case SessionState.Paused: return "已暂停";
                case SessionState.EmergencyStop: return "紧急停止";
                default: return "已停止";
            }
        }

        internal static string ColorKey(SessionState state)
        {
            switch (state)
            {
                case SessionState.EmergencyStop: return "danger";
                case SessionState.Paused: return "warning";
                case SessionState.MapScanning:
                case SessionState.MapCalibrating: return "accent";
                case SessionState.Observing: return "success";
                default: return "muted";
            }
        }
    }

    internal sealed class PrototypeTelemetry
    {
        internal int CaptureFps { get; set; }
        internal int RecognitionFps { get; set; }
        internal int FrameLatencyMs { get; set; }
        internal int QueueAgeMs { get; set; }
        internal int DroppedFrames { get; set; }
        internal double MemoryMb { get; set; }
        internal string CpuProvider { get; set; }
        internal string GpuProvider { get; set; }
        internal string HidStatus { get; set; }
        internal string HidHeartbeat { get; set; }
        internal string PauseReason { get; set; }

        internal PrototypeTelemetry()
        {
            CpuProvider = "OpenCV 模拟";
            GpuProvider = "未启用";
            HidStatus = "原型锁定";
            HidHeartbeat = "未发送";
            PauseReason = "无";
        }
    }
}
