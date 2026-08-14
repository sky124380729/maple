using System;
using System.Collections.Generic;

namespace Maple.Input
{
    public sealed class BootKeyboardReportEncoder : IVirtualHidReportEncoder
    {
        public const int ReportLength = 8;

        private static readonly IReadOnlyDictionary<string, byte> Modifiers =
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ctrl"] = 0x01,
                ["Control"] = 0x01,
                ["Shift"] = 0x02,
                ["Alt"] = 0x04,
            };

        private static readonly IReadOnlyDictionary<string, byte> Usages =
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = 0x04,
                ["D"] = 0x07,
                ["J"] = 0x0D,
                ["Z"] = 0x1D,
                ["Enter"] = 0x28,
                ["Escape"] = 0x29,
                ["Space"] = 0x2C,
                ["Right"] = 0x4F,
                ["Left"] = 0x50,
                ["Down"] = 0x51,
                ["Up"] = 0x52,
            };

        public byte[] EncodeState(IReadOnlyCollection<string> activeKeys, VirtualHidDeviceContract contract)
        {
            if (activeKeys == null) throw new ArgumentNullException(nameof(activeKeys));
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (contract.InputReportLength != ReportLength)
            {
                throw new ArgumentException("HID input report length must be 8 bytes", nameof(contract));
            }

            var report = new byte[ReportLength];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int usageIndex = 2;
            foreach (string key in activeKeys)
            {
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) continue;
                if (Modifiers.TryGetValue(key, out byte modifier))
                {
                    report[0] |= modifier;
                    continue;
                }

                if (!Usages.TryGetValue(key, out byte usage))
                {
                    throw new ArgumentException("Unsupported HID key: " + key, nameof(activeKeys));
                }

                if (usageIndex >= ReportLength)
                {
                    throw new InvalidOperationException("Boot keyboard reports support at most six regular keys");
                }

                report[usageIndex++] = usage;
            }

            return report;
        }
    }
}
