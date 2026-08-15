using System;
using System.Collections.Generic;

namespace Maple.Input;

public static class BrokerKeyProfile
{
    private static readonly IReadOnlyDictionary<BrokerActionKind, BrokerKeyEncoding> FixedActions =
        new Dictionary<BrokerActionKind, BrokerKeyEncoding>
        {
            [BrokerActionKind.MoveLeft] = new(0x25, 0x4B, true),
            [BrokerActionKind.MoveRight] = new(0x27, 0x4D, true),
            [BrokerActionKind.ClimbUp] = new(0x26, 0x48, true),
            [BrokerActionKind.ClimbDown] = new(0x28, 0x50, true)
        };

    private static readonly IReadOnlyDictionary<BrokerActionKind, string> DefaultLogicalKeys =
        new Dictionary<BrokerActionKind, string>
        {
            [BrokerActionKind.Jump] = "Alt",
            [BrokerActionKind.SingleAttack] = "J",
            [BrokerActionKind.AreaAttack] = "A",
            [BrokerActionKind.Pickup] = "Z",
            [BrokerActionKind.HpPotion] = "1",
            [BrokerActionKind.MpPotion] = "2"
        };

    private static readonly IReadOnlyDictionary<string, BrokerKeyEncoding> LogicalKeys =
        BuildLogicalKeys();

    public static BrokerKeyEncoding For(BrokerActionKind action, string logicalKey = null)
    {
        if (FixedActions.TryGetValue(action, out BrokerKeyEncoding fixedEncoding))
        {
            if (!string.IsNullOrWhiteSpace(logicalKey))
            {
                throw new ArgumentException("ACTION_KEY_CONFLICT", nameof(logicalKey));
            }

            return fixedEncoding;
        }

        if (!DefaultLogicalKeys.TryGetValue(action, out string defaultKey))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "UNSUPPORTED_ACTION");
        }

        return ForLogicalKey(string.IsNullOrWhiteSpace(logicalKey) ? defaultKey : logicalKey);
    }

    public static BrokerKeyEncoding ForLogicalKey(string logicalKey)
    {
        string normalized = logicalKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            !LogicalKeys.TryGetValue(normalized, out BrokerKeyEncoding encoding))
        {
            throw new ArgumentException("UNSUPPORTED_LOGICAL_KEY", nameof(logicalKey));
        }

        return encoding;
    }

    private static IReadOnlyDictionary<string, BrokerKeyEncoding> BuildLogicalKeys()
    {
        var keys = new Dictionary<string, BrokerKeyEncoding>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alt"] = new(0x12, 0x38, false),
            ["Ctrl"] = new(0x11, 0x1D, false),
            ["Shift"] = new(0x10, 0x2A, false),
            ["Space"] = new(0x20, 0x39, false)
        };

        Add(keys, "1", 0x31, 0x02); Add(keys, "2", 0x32, 0x03);
        Add(keys, "3", 0x33, 0x04); Add(keys, "4", 0x34, 0x05);
        Add(keys, "5", 0x35, 0x06); Add(keys, "6", 0x36, 0x07);
        Add(keys, "7", 0x37, 0x08); Add(keys, "8", 0x38, 0x09);
        Add(keys, "9", 0x39, 0x0A); Add(keys, "0", 0x30, 0x0B);

        Add(keys, "Q", 0x51, 0x10); Add(keys, "W", 0x57, 0x11);
        Add(keys, "E", 0x45, 0x12); Add(keys, "R", 0x52, 0x13);
        Add(keys, "T", 0x54, 0x14); Add(keys, "Y", 0x59, 0x15);
        Add(keys, "U", 0x55, 0x16); Add(keys, "I", 0x49, 0x17);
        Add(keys, "O", 0x4F, 0x18); Add(keys, "P", 0x50, 0x19);
        Add(keys, "A", 0x41, 0x1E); Add(keys, "S", 0x53, 0x1F);
        Add(keys, "D", 0x44, 0x20); Add(keys, "F", 0x46, 0x21);
        Add(keys, "G", 0x47, 0x22); Add(keys, "H", 0x48, 0x23);
        Add(keys, "J", 0x4A, 0x24); Add(keys, "K", 0x4B, 0x25);
        Add(keys, "L", 0x4C, 0x26); Add(keys, "Z", 0x5A, 0x2C);
        Add(keys, "X", 0x58, 0x2D); Add(keys, "C", 0x43, 0x2E);
        Add(keys, "V", 0x56, 0x2F); Add(keys, "B", 0x42, 0x30);
        Add(keys, "N", 0x4E, 0x31); Add(keys, "M", 0x4D, 0x32);

        return keys;
    }

    private static void Add(
        IDictionary<string, BrokerKeyEncoding> keys,
        string name,
        ushort virtualKey,
        uint scanCode)
    {
        keys.Add(name, new BrokerKeyEncoding(virtualKey, scanCode, false));
    }
}
