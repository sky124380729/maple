using System.Buffers.Binary;
using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class MapleHidProtocolTests
{
    [Fact]
    public void SubmitReportFrameHasStableWireLayout()
    {
        byte[] report = [0x04, 0, 0x50, 0, 0, 0, 0, 0];

        byte[] frame = MapleHidProtocol.EncodeSubmitReport(7, report);

        Assert.Equal(20, frame.Length);
        Assert.Equal(MapleHidProtocol.Magic, BinaryPrimitives.ReadUInt32LittleEndian(frame));
        Assert.Equal(MapleHidProtocol.Version, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)));
        Assert.Equal((ushort)MapleHidCommand.SubmitReport, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)));
        Assert.Equal(7U, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)));
        Assert.Equal(report, frame[12..]);
    }

    [Fact]
    public void HeartbeatFrameAlwaysContainsNeutralReport()
    {
        byte[] frame = MapleHidProtocol.EncodeHeartbeat(12);

        Assert.Equal((ushort)MapleHidCommand.Heartbeat, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)));
        Assert.Equal(12U, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)));
        Assert.Equal(new byte[8], frame[12..]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(9)]
    public void SubmitReportRejectsWrongLength(int length)
    {
        Assert.Throws<ArgumentException>(() => MapleHidProtocol.EncodeSubmitReport(1, new byte[length]));
    }

    [Fact]
    public void SequenceMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapleHidProtocol.EncodeHeartbeat(0));
    }

    [Fact]
    public void IoctlCodesRemainStable()
    {
        Assert.Equal(0x222004U, MapleHidProtocol.IoctlSubmitReport);
        Assert.Equal(0x222008U, MapleHidProtocol.IoctlHeartbeat);
        Assert.Equal(0x22200CU, MapleHidProtocol.IoctlGetStatus);
    }
}
