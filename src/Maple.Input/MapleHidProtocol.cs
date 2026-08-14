using System;
using System.Buffers.Binary;

namespace Maple.Input
{
    public enum MapleHidCommand : ushort
    {
        SubmitReport = 1,
        Heartbeat = 2,
        GetStatus = 3,
    }

    public static class MapleHidProtocol
    {
        public const uint Magic = 0x4449484D;
        public const ushort Version = 1;
        public const int KeyboardReportLength = 8;
        public const int RequestLength = 20;

        public const uint IoctlSubmitReport = 0x222004;
        public const uint IoctlHeartbeat = 0x222008;
        public const uint IoctlGetStatus = 0x22200C;

        public static byte[] EncodeSubmitReport(uint sequence, ReadOnlySpan<byte> report)
        {
            if (report.Length != KeyboardReportLength)
            {
                throw new ArgumentException("Keyboard report must contain exactly 8 bytes", nameof(report));
            }

            return Encode(MapleHidCommand.SubmitReport, sequence, report);
        }

        public static byte[] EncodeHeartbeat(uint sequence) =>
            Encode(MapleHidCommand.Heartbeat, sequence, new byte[KeyboardReportLength]);

        private static byte[] Encode(MapleHidCommand command, uint sequence, ReadOnlySpan<byte> report)
        {
            if (sequence == 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            var frame = new byte[RequestLength];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6), (ushort)command);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8), sequence);
            report.CopyTo(frame.AsSpan(12));
            return frame;
        }
    }
}
