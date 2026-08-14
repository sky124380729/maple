using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class BootKeyboardReportEncoderTests
{
    private readonly BootKeyboardReportEncoder encoder = new();

    [Theory]
    [InlineData("Left", 0x50)]
    [InlineData("Right", 0x4F)]
    [InlineData("Up", 0x52)]
    [InlineData("Down", 0x51)]
    [InlineData("Z", 0x1D)]
    [InlineData("Space", 0x2C)]
    public void EncodesSupportedRegularKey(string key, byte usage)
    {
        byte[] report = encoder.EncodeState([key], Contract());

        Assert.Equal(8, report.Length);
        Assert.Equal(0, report[0]);
        Assert.Equal(usage, report[2]);
        Assert.All(report[3..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void EncodesModifiersAlongsideRegularKeys()
    {
        byte[] report = encoder.EncodeState(["Left", "Alt", "Ctrl"], Contract());

        Assert.Equal(0x05, report[0]);
        Assert.Equal(0x50, report[2]);
    }

    [Fact]
    public void DuplicateKeysDoNotConsumeRolloverSlots()
    {
        byte[] report = encoder.EncodeState(["Right", "right", "Right"], Contract());

        Assert.Equal(0x4F, report[2]);
        Assert.All(report[3..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void EmptyStateIsNeutralReport()
    {
        Assert.Equal(new byte[8], encoder.EncodeState([], Contract()));
    }

    [Fact]
    public void UnknownKeyIsRejected()
    {
        Assert.Throws<ArgumentException>(() => encoder.EncodeState(["Unknown"], Contract()));
    }

    [Fact]
    public void MoreThanSixRegularKeysIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            encoder.EncodeState(["Left", "Right", "Up", "Down", "Z", "Space", "J"], Contract()));
    }

    [Fact]
    public void ContractReportLengthMustMatchBootKeyboardReport()
    {
        VirtualHidDeviceContract contract = Contract();
        contract.InputReportLength = 9;

        Assert.Throws<ArgumentException>(() => encoder.EncodeState(["Left"], contract));
    }

    private static VirtualHidDeviceContract Contract() => new()
    {
        DeviceInterfacePath = "test-device",
        Vid = 0xF1AE,
        Pid = 1,
        ReportDescriptorSha256 = new string('a', 64),
        Transport = "MapleVhfIoctlV1",
        InputReportLength = 8,
        OutputReportLength = 8,
        SignedInstallation = true,
        NeutralReportVerified = true,
    };
}
