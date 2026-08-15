using System;
using System.Text.Json;
using Maple.Input;
using Xunit;

namespace Maple.Input.Tests;

public sealed class BrokerProtocolTests
{
    [Fact]
    public void ActionRequestContainsOnlyAbstractActionFields()
    {
        var request = new BrokerRequest(
            BrokerProtocol.Version,
            1,
            BrokerRequestKind.KeyDownAction,
            new BrokerActionPayload("a-1", BrokerActionKind.MoveLeft, null, 120, 300));

        string json = JsonSerializer.Serialize(request);

        Assert.DoesNotContain("virtualKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scanCode", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flags", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MoveLeft", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestPayloadCannotBeAnArbitraryRawKeyboardObject()
    {
        Assert.False(typeof(BrokerRequest).GetProperty(nameof(BrokerRequest.Payload))!
            .PropertyType.IsAssignableFrom(typeof(object)));
        Assert.Equal(typeof(BrokerPayload),
            typeof(BrokerRequest).GetProperty(nameof(BrokerRequest.Payload))!.PropertyType);
    }

    [Fact]
    public void ProtocolVersionIsStable()
    {
        Assert.Equal(1, BrokerProtocol.Version);
    }
}
