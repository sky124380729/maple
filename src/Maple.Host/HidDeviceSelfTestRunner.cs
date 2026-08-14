using Maple.Contracts;
using Maple.Input;

namespace Maple.Host;

public sealed record HidDeviceSelfTestReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    bool Success,
    string Code,
    string? DeviceInterfacePath,
    int Vid,
    int Pid,
    string ReportDescriptorSha256,
    bool NeutralReportVerified,
    bool HeartbeatVerified,
    bool ExitNeutralVerified);

public sealed class HidDeviceSelfTestRunner
{
    private readonly IMapleHidDeviceLocator locator;
    private readonly Func<IVirtualHidTransport> transportFactory;

    public HidDeviceSelfTestRunner(
        IMapleHidDeviceLocator locator,
        Func<IVirtualHidTransport> transportFactory)
    {
        this.locator = locator ?? throw new ArgumentNullException(nameof(locator));
        this.transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    public HidDeviceSelfTestReport Run()
    {
        if (!locator.TryLocate(out string path, out string locateError))
            return Report(false, locateError, null, false, false, false);

        var contract = new VirtualHidDeviceContract
        {
            DeviceInterfacePath = path,
            Vid = MapleHidDeviceIdentity.VendorId,
            Pid = MapleHidDeviceIdentity.ProductId,
            ReportDescriptorSha256 = MapleHidDeviceIdentity.ReportDescriptorSha256,
            Transport = WindowsVirtualHidTransport.TransportName,
            InputReportLength = MapleHidProtocol.KeyboardReportLength,
            OutputReportLength = MapleHidProtocol.KeyboardReportLength,
            SignedInstallation = true,
            NeutralReportVerified = true,
        };

        using IVirtualHidTransport transport = transportFactory();
        if (!transport.Open(contract, out string openError))
            return Report(false, "HID_DEVICE_OPEN_FAILED:" + Normalize(openError), path, false, false, false);
        if (!transport.Heartbeat(out string heartbeatError))
            return Report(false, "HID_HEARTBEAT_FAILED:" + Normalize(heartbeatError), path, true, false, false);
        if (!transport.WriteReport(new byte[MapleHidProtocol.KeyboardReportLength], out string neutralError))
            return Report(false, "HID_EXIT_NEUTRAL_FAILED:" + Normalize(neutralError), path, true, true, false);
        return Report(true, "HID_DEVICE_IO_PASS", path, true, true, true);
    }

    private static HidDeviceSelfTestReport Report(
        bool success,
        string code,
        string? path,
        bool neutral,
        bool heartbeat,
        bool exitNeutral) => new(
        ContractConstants.SchemaVersion,
        DateTimeOffset.UtcNow,
        success,
        string.IsNullOrWhiteSpace(code) ? "HID_DEVICE_SELF_TEST_FAILED" : code,
        path,
        MapleHidDeviceIdentity.VendorId,
        MapleHidDeviceIdentity.ProductId,
        MapleHidDeviceIdentity.ReportDescriptorSha256,
        neutral,
        heartbeat,
        exitNeutral);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? "UNKNOWN" : value;
}
