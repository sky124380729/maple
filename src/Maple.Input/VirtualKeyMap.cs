using System;
using System.Collections.Generic;

namespace Maple.Input
{
    public static class VirtualKeyMap
    {
        public const ushort Left = 0x25;
        public const ushort Up = 0x26;
        public const ushort Right = 0x27;
        public const ushort Down = 0x28;
        public const ushort Alt = 0x12;
        public const ushort Ctrl = 0x11;
        public const ushort Space = 0x20;
        public const ushort A = 0x41;
        public const ushort C = 0x43;
        public const ushort D = 0x44;
        public const ushort J = 0x4A;
        public const ushort K = 0x4B;
        public const ushort X = 0x58;
        public const ushort Z = 0x5A;

        public const uint LeftScanCode = 0x4B;
        public const uint UpScanCode = 0x48;
        public const uint RightScanCode = 0x4D;
        public const uint DownScanCode = 0x50;

        private static readonly IDictionary<string, ushort> Values =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                ["left"] = Left,
                ["right"] = Right,
                ["up"] = Up,
                ["down"] = Down,
                ["alt"] = Alt,
                ["ctrl"] = Ctrl,
                ["space"] = Space,
                ["a"] = A,
                ["c"] = C,
                ["d"] = D,
                ["j"] = J,
                ["k"] = K,
                ["x"] = X,
                ["z"] = Z
            };

        public static bool TryGet(string key, out ushort virtualKey)
        {
            virtualKey = 0;
            return !string.IsNullOrWhiteSpace(key) && Values.TryGetValue(key, out virtualKey);
        }

        public static bool TryGetArrowScanCode(string key, out uint scanCode)
        {
            scanCode = 0;
            if (string.IsNullOrWhiteSpace(key)) return false;

            if (string.Equals(key, "left", StringComparison.OrdinalIgnoreCase)) scanCode = LeftScanCode;
            else if (string.Equals(key, "up", StringComparison.OrdinalIgnoreCase)) scanCode = UpScanCode;
            else if (string.Equals(key, "right", StringComparison.OrdinalIgnoreCase)) scanCode = RightScanCode;
            else if (string.Equals(key, "down", StringComparison.OrdinalIgnoreCase)) scanCode = DownScanCode;
            else return false;

            return true;
        }
    }
}
