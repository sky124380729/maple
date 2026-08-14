using Maple.Host;
using Maple.Input;
using Xunit;

namespace Maple.Host.Tests;

public sealed class HidDeviceSelfTestRunnerTests
{
    [Fact]
    public void NeutralAndHeartbeatProduceDeviceIoPass()
    {
        var transport = new FakeTransport();
        var runner = new HidDeviceSelfTestRunner(new FakeLocator(true), () => transport);

        HidDeviceSelfTestReport report = runner.Run();

        Assert.True(report.Success);
        Assert.Equal("HID_DEVICE_IO_PASS", report.Code);
        Assert.True(report.NeutralReportVerified);
        Assert.True(report.HeartbeatVerified);
        Assert.True(report.ExitNeutralVerified);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public void MissingDeviceDoesNotCreateTransport()
    {
        int created = 0;
        var runner = new HidDeviceSelfTestRunner(new FakeLocator(false), () => { created++; return new FakeTransport(); });

        HidDeviceSelfTestReport report = runner.Run();

        Assert.False(report.Success);
        Assert.Equal("HID_DEVICE_NOT_INSTALLED", report.Code);
        Assert.Equal(0, created);
    }

    [Fact]
    public void OpenFailureIsReportedWithoutClaimingNeutral()
    {
        var transport = new FakeTransport { OpenResult = false, Error = "ACCESS_DENIED" };
        var runner = new HidDeviceSelfTestRunner(new FakeLocator(true), () => transport);

        HidDeviceSelfTestReport report = runner.Run();

        Assert.False(report.Success);
        Assert.Equal("HID_DEVICE_OPEN_FAILED:ACCESS_DENIED", report.Code);
        Assert.False(report.NeutralReportVerified);
        Assert.False(report.HeartbeatVerified);
    }

    private sealed class FakeLocator(bool result) : IMapleHidDeviceLocator
    {
        public bool TryLocate(out string devicePath, out string error)
        {
            devicePath = result ? @"\\?\root#maplevhfkeyboard#one" : string.Empty;
            error = result ? string.Empty : "HID_DEVICE_NOT_INSTALLED";
            return result;
        }
    }

    private sealed class FakeTransport : IVirtualHidTransport
    {
        public bool OpenResult { get; init; } = true;
        public string Error { get; init; } = string.Empty;
        public bool Disposed { get; private set; }

        public bool Open(VirtualHidDeviceContract contract, out string error) { error = Error; return OpenResult; }
        public bool WriteReport(byte[] report, out string error) { error = Error; return true; }
        public bool Heartbeat(out string error) { error = Error; return true; }
        public void Dispose() => Disposed = true;
    }
}
