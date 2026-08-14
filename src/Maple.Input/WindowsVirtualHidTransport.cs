using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Maple.Input
{
    public interface IMapleHidDeviceIo : IDisposable
    {
        bool Open(string deviceInterfacePath, out string error);
        bool Invoke(uint ioctl, byte[] input, out string error);
    }

    public sealed class WindowsVirtualHidTransport : IVirtualHidTransport
    {
        public const string TransportName = "MapleVhfIoctlV1";

        private readonly IMapleHidDeviceIo deviceIo;
        private uint sequence;
        private bool opened;
        private bool disposed;

        public WindowsVirtualHidTransport() : this(new WindowsMapleHidDeviceIo()) { }

        public WindowsVirtualHidTransport(IMapleHidDeviceIo deviceIo)
        {
            this.deviceIo = deviceIo ?? throw new ArgumentNullException(nameof(deviceIo));
        }

        public bool Open(VirtualHidDeviceContract contract, out string error)
        {
            if (disposed) { error = "HID_TRANSPORT_DISPOSED"; return false; }
            if (opened) { error = "HID_DEVICE_ALREADY_OPEN"; return false; }
            if (contract == null || !contract.IsComplete()) { error = "HID_CONTRACT_UNVERIFIED"; return false; }
            if (!string.Equals(contract.Transport, TransportName, StringComparison.Ordinal)
                || contract.InputReportLength != MapleHidProtocol.KeyboardReportLength
                || contract.OutputReportLength != MapleHidProtocol.KeyboardReportLength)
            {
                error = "HID_CONTRACT_PROTOCOL_MISMATCH";
                return false;
            }

            if (!deviceIo.Open(contract.DeviceInterfacePath, out error)) return false;
            sequence = 0;
            if (!Invoke(MapleHidProtocol.IoctlSubmitReport, MapleHidProtocol.EncodeSubmitReport(NextSequence(), new byte[8]), out error))
            {
                deviceIo.Dispose();
                return false;
            }

            opened = true;
            return true;
        }

        public bool WriteReport(byte[] report, out string error)
        {
            if (!opened) { error = "HID_DEVICE_NOT_OPEN"; return false; }
            if (report == null || report.Length != MapleHidProtocol.KeyboardReportLength)
            {
                error = "HID_REPORT_LENGTH_INVALID";
                return false;
            }

            return Invoke(
                MapleHidProtocol.IoctlSubmitReport,
                MapleHidProtocol.EncodeSubmitReport(NextSequence(), report),
                out error);
        }

        public bool Heartbeat(out string error)
        {
            if (!opened) { error = "HID_DEVICE_NOT_OPEN"; return false; }
            return Invoke(
                MapleHidProtocol.IoctlHeartbeat,
                MapleHidProtocol.EncodeHeartbeat(NextSequence()),
                out error);
        }

        private bool Invoke(uint ioctl, byte[] request, out string error)
        {
            if (deviceIo.Invoke(ioctl, request, out error)) return true;
            opened = false;
            return false;
        }

        private uint NextSequence()
        {
            if (sequence == uint.MaxValue) throw new InvalidOperationException("HID sequence exhausted");
            return ++sequence;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (opened)
            {
                _ = deviceIo.Invoke(
                    MapleHidProtocol.IoctlSubmitReport,
                    MapleHidProtocol.EncodeSubmitReport(NextSequence(), new byte[8]),
                    out _);
            }
            opened = false;
            deviceIo.Dispose();
        }
    }

    public sealed class WindowsMapleHidDeviceIo : IMapleHidDeviceIo
    {
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint ShareRead = 0x00000001;
        private const uint ShareWrite = 0x00000002;
        private const uint OpenExisting = 3;

        private SafeFileHandle handle;

        public bool Open(string deviceInterfacePath, out string error)
        {
            if (!OperatingSystem.IsWindows()) { error = "HID_PLATFORM_NOT_SUPPORTED"; return false; }
            if (string.IsNullOrWhiteSpace(deviceInterfacePath)) { error = "HID_DEVICE_PATH_MISSING"; return false; }
            handle?.Dispose();
            handle = CreateFile(
                deviceInterfacePath,
                GenericRead | GenericWrite,
                ShareRead | ShareWrite,
                nint.Zero,
                OpenExisting,
                0,
                nint.Zero);
            if (!handle.IsInvalid) { error = string.Empty; return true; }
            int code = Marshal.GetLastWin32Error();
            handle.Dispose();
            handle = null;
            error = Win32Error("HID_DEVICE_OPEN_FAILED", code);
            return false;
        }

        public bool Invoke(uint ioctl, byte[] input, out string error)
        {
            if (handle == null || handle.IsInvalid || handle.IsClosed)
            {
                error = "HID_DEVICE_NOT_OPEN";
                return false;
            }

            bool succeeded = DeviceIoControl(
                handle,
                ioctl,
                input,
                input?.Length ?? 0,
                nint.Zero,
                0,
                out _,
                nint.Zero);
            if (succeeded) { error = string.Empty; return true; }
            error = Win32Error("HID_IOCTL_FAILED", Marshal.GetLastWin32Error());
            return false;
        }

        public void Dispose()
        {
            handle?.Dispose();
            handle = null;
        }

        private static string Win32Error(string prefix, int code) =>
            prefix + ":" + code + ":" + new Win32Exception(code).Message;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            nint securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            nint templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint ioControlCode,
            byte[] inputBuffer,
            int inputBufferSize,
            nint outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            nint overlapped);
    }
}
