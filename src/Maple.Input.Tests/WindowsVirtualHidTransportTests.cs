using System.Buffers.Binary;
using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class WindowsVirtualHidTransportTests
{
    [Fact]
    public void OpenSendsNeutralBeforeTransportBecomesAvailable()
    {
        var device = new RecordingDeviceIo();
        using var transport = new WindowsVirtualHidTransport(device);

        bool opened = transport.Open(Contract(), out string error);

        Assert.True(opened, error);
        DeviceCall call = Assert.Single(device.Calls);
        Assert.Equal(MapleHidProtocol.IoctlSubmitReport, call.Ioctl);
        Assert.Equal(1U, Sequence(call.Frame));
        Assert.Equal(new byte[8], call.Frame[12..]);
    }

    [Fact]
    public void ReportsAndHeartbeatUseMonotonicSequences()
    {
        var device = new RecordingDeviceIo();
        using var transport = new WindowsVirtualHidTransport(device);
        Assert.True(transport.Open(Contract(), out _));

        Assert.True(transport.WriteReport([0, 0, 0x50, 0, 0, 0, 0, 0], out _));
        Assert.True(transport.Heartbeat(out _));

        Assert.Collection(
            device.Calls,
            neutral => Assert.Equal(1U, Sequence(neutral.Frame)),
            report =>
            {
                Assert.Equal(MapleHidProtocol.IoctlSubmitReport, report.Ioctl);
                Assert.Equal(2U, Sequence(report.Frame));
            },
            heartbeat =>
            {
                Assert.Equal(MapleHidProtocol.IoctlHeartbeat, heartbeat.Ioctl);
                Assert.Equal(3U, Sequence(heartbeat.Frame));
            });
    }

    [Fact]
    public void InvalidReportNeverReachesDevice()
    {
        var device = new RecordingDeviceIo();
        using var transport = new WindowsVirtualHidTransport(device);
        Assert.True(transport.Open(Contract(), out _));

        bool written = transport.WriteReport(new byte[7], out string error);

        Assert.False(written);
        Assert.Equal("HID_REPORT_LENGTH_INVALID", error);
        Assert.Single(device.Calls);
    }

    [Fact]
    public void DriverOpenFailureKeepsTransportUnavailable()
    {
        var device = new RecordingDeviceIo { OpenResult = false, OpenError = "ACCESS_DENIED" };
        using var transport = new WindowsVirtualHidTransport(device);

        Assert.False(transport.Open(Contract(), out string error));
        Assert.Equal("ACCESS_DENIED", error);
        Assert.False(transport.Heartbeat(out string heartbeatError));
        Assert.Equal("HID_DEVICE_NOT_OPEN", heartbeatError);
        Assert.Empty(device.Calls);
    }

    [Fact]
    public void ContractMustNameMapleProtocolAndEightByteReport()
    {
        var device = new RecordingDeviceIo();
        using var transport = new WindowsVirtualHidTransport(device);
        VirtualHidDeviceContract contract = Contract();
        contract.Transport = "Unknown";

        Assert.False(transport.Open(contract, out string error));
        Assert.Equal("HID_CONTRACT_PROTOCOL_MISMATCH", error);
        Assert.Equal(0, device.OpenCalls);
    }

    private static uint Sequence(byte[] frame) => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8));

    private static VirtualHidDeviceContract Contract() => new()
    {
        DeviceInterfacePath = @"\\?\root#maplevhfkeyboard#test",
        Vid = 0xF1AE,
        Pid = 1,
        ReportDescriptorSha256 = new string('a', 64),
        Transport = WindowsVirtualHidTransport.TransportName,
        InputReportLength = 8,
        OutputReportLength = 8,
        SignedInstallation = true,
        NeutralReportVerified = true,
    };

    private sealed record DeviceCall(uint Ioctl, byte[] Frame);

    private sealed class RecordingDeviceIo : IMapleHidDeviceIo
    {
        public bool OpenResult { get; init; } = true;
        public string OpenError { get; init; } = string.Empty;
        public int OpenCalls { get; private set; }
        public List<DeviceCall> Calls { get; } = [];

        public bool Open(string deviceInterfacePath, out string error)
        {
            OpenCalls++;
            error = OpenError;
            return OpenResult;
        }

        public bool Invoke(uint ioctl, byte[] input, out string error)
        {
            Calls.Add(new DeviceCall(ioctl, input.ToArray()));
            error = string.Empty;
            return true;
        }

        public void Dispose() { }
    }
}
