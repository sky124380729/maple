using System;
using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class BrokerKeyProfileTests
{
    [Theory]
    [InlineData(BrokerActionKind.MoveLeft, 0x25, 0x4B)]
    [InlineData(BrokerActionKind.MoveRight, 0x27, 0x4D)]
    [InlineData(BrokerActionKind.ClimbUp, 0x26, 0x48)]
    [InlineData(BrokerActionKind.ClimbDown, 0x28, 0x50)]
    public void ArrowProfilesUseVerifiedExtendedScanCodes(
        BrokerActionKind action,
        ushort virtualKey,
        uint scanCode)
    {
        Assert.Equal(
            new BrokerKeyEncoding(virtualKey, scanCode, true),
            BrokerKeyProfile.For(action));
    }

    [Theory]
    [InlineData(BrokerActionKind.Jump, "Alt")]
    [InlineData(BrokerActionKind.SingleAttack, "J")]
    [InlineData(BrokerActionKind.AreaAttack, "A")]
    [InlineData(BrokerActionKind.Pickup, "Z")]
    [InlineData(BrokerActionKind.HpPotion, "1")]
    [InlineData(BrokerActionKind.MpPotion, "2")]
    public void ConfigurableActionsHaveSpecificationDefaults(BrokerActionKind action, string logicalKey)
    {
        Assert.Equal(BrokerKeyProfile.ForLogicalKey(logicalKey), BrokerKeyProfile.For(action));
    }

    [Fact]
    public void MovementActionsCannotBeRemappedByCallers()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BrokerKeyProfile.For(BrokerActionKind.MoveLeft, "A"));

        Assert.Contains("ACTION_KEY_CONFLICT", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("VK_25")]
    [InlineData("0x4B")]
    [InlineData("Left")]
    public void RawOrUnsupportedLogicalKeysAreRejected(string logicalKey)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            BrokerKeyProfile.ForLogicalKey(logicalKey));

        Assert.Contains("UNSUPPORTED_LOGICAL_KEY", exception.Message, StringComparison.Ordinal);
    }
}
