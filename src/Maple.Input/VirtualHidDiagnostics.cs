namespace Maple.Input
{
    public sealed class HidDeviceEvidence
    {
        public string Status { get; set; }
        public bool DeviceInstalled { get; set; }
        public bool DescriptorMatched { get; set; }
        public bool SignedInstallation { get; set; }
        public bool NeutralReportVerified { get; set; }
    }

    public sealed class HidOsEvidence
    {
        public string Status { get; set; }
        public bool RawInputReceived { get; set; }
        public int KeydownKeyupPairs { get; set; }
        public int StuckKeysAfterReleaseAll { get; set; }
        public bool HeartbeatTimeoutReleasedAll { get; set; }
    }

    public sealed class HidClientEvidence
    {
        public string Status { get; set; }
        public bool SingleKeyVisualResponse { get; set; }
        public bool FocusLossPaused { get; set; }
        public bool ProcessExitReleasedAll { get; set; }
        public bool EmergencyStopReleasedAll { get; set; }
        public bool AutomaticResumeObserved { get; set; }
    }

    public sealed class VirtualHidDiagnosticResult
    {
        public bool Passed { get; internal set; }
        public string Code { get; internal set; }
        public string Message { get; internal set; }
    }

    public static class VirtualHidDiagnostics
    {
        public static VirtualHidDiagnosticResult Evaluate(VirtualHidDeviceContract contract, HidDeviceEvidence device, HidOsEvidence os, HidClientEvidence client)
        {
            if (contract == null || !contract.IsComplete()) return Fail("HID_CONTRACT_UNVERIFIED", "设备合同不完整");
            if (device == null || device.Status != "PASS" || !device.DeviceInstalled || !device.DescriptorMatched || !device.SignedInstallation || !device.NeutralReportVerified) return Fail("HID_DEVICE_LAYER_FAILED", "设备安装或描述符层未通过");
            if (os == null || os.Status != "PASS" || !os.RawInputReceived || os.KeydownKeyupPairs < 1 || os.StuckKeysAfterReleaseAll != 0 || !os.HeartbeatTimeoutReleasedAll) return Fail("HID_OS_LAYER_FAILED", "操作系统输入层未通过");
            if (client == null || client.Status != "PASS" || !client.SingleKeyVisualResponse || !client.FocusLossPaused || !client.ProcessExitReleasedAll || !client.EmergencyStopReleasedAll || client.AutomaticResumeObserved) return Fail("HID_CLIENT_LAYER_FAILED", "授权客户端响应层未通过");
            return new VirtualHidDiagnosticResult { Passed = true, Code = "HID_THREE_LAYER_PASS", Message = "设备、操作系统和授权客户端三层证据均通过" };
        }

        private static VirtualHidDiagnosticResult Fail(string code, string message)
        {
            return new VirtualHidDiagnosticResult { Passed = false, Code = code, Message = message };
        }
    }
}
