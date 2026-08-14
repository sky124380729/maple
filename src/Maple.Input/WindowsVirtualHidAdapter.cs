using System;
using System.Collections.Generic;
using System.Linq;
using Maple.Contracts;

namespace Maple.Input
{
    public sealed class VirtualHidDeviceContract
    {
        public string DeviceInterfacePath { get; set; }
        public int Vid { get; set; }
        public int Pid { get; set; }
        public string ReportDescriptorSha256 { get; set; }
        public string Transport { get; set; }
        public int InputReportLength { get; set; }
        public int OutputReportLength { get; set; }
        public bool SignedInstallation { get; set; }
        public bool NeutralReportVerified { get; set; }

        public bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(DeviceInterfacePath) && Vid >= 0 && Vid <= 0xffff && Pid >= 0 && Pid <= 0xffff && !string.IsNullOrWhiteSpace(ReportDescriptorSha256) && ReportDescriptorSha256.Length == 64 && !string.IsNullOrWhiteSpace(Transport) && InputReportLength > 0 && OutputReportLength > 0 && SignedInstallation && NeutralReportVerified;
        }
    }

    public interface IVirtualHidTransport : IDisposable
    {
        bool Open(VirtualHidDeviceContract contract, out string error);
        bool WriteReport(byte[] report, out string error);
        bool Heartbeat(out string error);
    }

    public interface IVirtualHidReportEncoder
    {
        byte[] EncodeState(IReadOnlyCollection<string> activeKeys, VirtualHidDeviceContract contract);
    }

    /// <summary>
    /// Device-agnostic HID lifecycle. Concrete device access and report encoding
    /// are injected only after Windows evidence establishes the exact contract.
    /// </summary>
    public sealed class WindowsVirtualHidAdapter : IInputAdapter, IDisposable
    {
        private readonly VirtualHidDeviceContract contract;
        private readonly IVirtualHidTransport transport;
        private readonly IVirtualHidReportEncoder encoder;
        private readonly ActiveKeyRegistry registry = new ActiveKeyRegistry();
        private bool ready;
        private string statusCode = "HID_CONTRACT_UNVERIFIED";
        private long lastHeartbeat;

        public WindowsVirtualHidAdapter() { }

        public WindowsVirtualHidAdapter(VirtualHidDeviceContract contract, IVirtualHidTransport transport, IVirtualHidReportEncoder encoder)
        {
            this.contract = contract;
            this.transport = transport;
            this.encoder = encoder;
            if (contract == null || !contract.IsComplete() || transport == null || encoder == null) return;
            string error;
            ready = transport.Open(contract, out error);
            statusCode = ready ? "HID_READY" : "HID_OPEN_FAILED:" + (error ?? "UNKNOWN");
        }

        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs)
        {
            if (!ready) return Unavailable(action, nowMonoMs);
            Validate(action, key);
            var nextKeys = new List<string>(registry.ActiveKeys);
            if (!nextKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) nextKeys.Add(key);
            string error;
            if (!transport.WriteReport(encoder.EncodeState(nextKeys, contract), out error)) return Fail(action, nowMonoMs, error);
            registry.KeyDown(key);
            return Result(action.ActionId, InputStatus.Accepted, nowMonoMs, null, "HID key-down 已发送");
        }

        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs)
        {
            if (!ready) return Unavailable(action, nowMonoMs);
            Validate(action, key);
            var nextKeys = new List<string>(registry.ActiveKeys);
            nextKeys.RemoveAll(active => string.Equals(active, key, StringComparison.OrdinalIgnoreCase));
            string error;
            if (!transport.WriteReport(encoder.EncodeState(nextKeys, contract), out error)) return Fail(action, nowMonoMs, error);
            registry.KeyUp(key);
            return Result(action.ActionId, InputStatus.Completed, nowMonoMs, new List<string> { key }, "HID key-up 已发送");
        }

        public InputResult Press(AbstractAction action, string key, long nowMonoMs)
        {
            InputResult down = KeyDown(action, key, nowMonoMs);
            if (down.Status != InputStatus.Accepted) return down;
            return KeyUp(action, key, nowMonoMs + action.HoldMs);
        }

        public InputResult ReleaseAll(long nowMonoMs)
        {
            IList<string> released = registry.ActiveKeys;
            if (!ready) return Result("release-all", InputStatus.Completed, nowMonoMs, new List<string>(released), statusCode);
            string error;
            if (!transport.WriteReport(encoder.EncodeState(Array.Empty<string>(), contract), out error))
            {
                ready = false;
                statusCode = "HID_NEUTRAL_REPORT_FAILED:" + (error ?? "UNKNOWN");
                return Result("release-all", InputStatus.Failed, nowMonoMs, new List<string>(released), statusCode);
            }
            registry.ReleaseAll();
            return Result("release-all", InputStatus.Completed, nowMonoMs, new List<string>(released), "HID neutral report 已发送");
        }

        public bool Heartbeat(long nowMonoMs)
        {
            if (!ready) return false;
            string error;
            if (!transport.Heartbeat(out error))
            {
                ReleaseAll(nowMonoMs);
                ready = false;
                statusCode = "HID_HEARTBEAT_FAILED:" + (error ?? "UNKNOWN");
                return false;
            }
            lastHeartbeat = nowMonoMs;
            return true;
        }

        public InputAdapterStatus GetStatus()
        {
            return new InputAdapterStatus { AdapterName = "WindowsVirtualHidAdapter", Code = statusCode, IsHealthy = ready, InjectionEnabled = ready, LastHeartbeatMonoMs = lastHeartbeat, ActiveKeys = registry.ActiveKeys };
        }

        public void Dispose()
        {
            ReleaseAll(Environment.TickCount);
            if (transport != null) transport.Dispose();
            ready = false;
        }

        private static void Validate(AbstractAction action, string key)
        {
            ContractValidationResult validation = ContractValidation.ValidateAction(action);
            if (!validation.IsValid) throw new ArgumentException("动作契约无效：" + validation.Error, "action");
            if (string.IsNullOrWhiteSpace(key) || key.Length > 32) throw new ArgumentException("按键名称无效", "key");
        }

        private InputResult Fail(AbstractAction action, long nowMonoMs, string error)
        {
            ready = false;
            statusCode = "HID_WRITE_FAILED:" + (error ?? "UNKNOWN");
            return Result(action.ActionId, InputStatus.Failed, nowMonoMs, new List<string>(), statusCode);
        }

        private InputResult Unavailable(AbstractAction action, long nowMonoMs)
        {
            return Result(action == null ? "invalid" : action.ActionId, InputStatus.Rejected, nowMonoMs, new List<string>(), statusCode);
        }

        private static InputResult Result(string actionId, InputStatus status, long nowMonoMs, List<string> releasedKeys, string message)
        {
            return new InputResult { SchemaVersion = ContractConstants.SchemaVersion, ActionId = actionId, Status = status, StartedAtMonoMs = nowMonoMs, EndedAtMonoMs = status == InputStatus.Accepted ? (long?)null : nowMonoMs, ReleasedKeys = releasedKeys ?? new List<string>(), Message = message };
        }
    }
}
