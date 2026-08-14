using Maple.Contracts;
using Xunit;

namespace Maple.Host.Tests;

public sealed class BridgeMessageRouterTests
{
    private readonly BridgeMessageRouter router = new();

    [Fact]
    public void AcceptsAValidV2Command()
    {
        BridgeRouteResult result = router.Route("""
            {"schemaVersion":2,"type":"snapshot.request","payload":{}}
            """);

        Assert.True(result.Accepted);
        Assert.Equal(UiCommandType.SnapshotRequest, result.CommandType);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"type\":\"snapshot.request\",\"payload\":{}}", "SCHEMA_VERSION_REJECTED")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"raw.key\",\"payload\":{}}", "UNKNOWN_COMMAND_REJECTED")]
    [InlineData("not-json", "INVALID_JSON")]
    public void RejectsInvalidEnvelopes(string json, string expectedCode)
    {
        BridgeRouteResult result = router.Route(json);

        Assert.False(result.Accepted);
        Assert.Equal(expectedCode, result.Code);
    }

    [Theory]
    [InlineData("action")]
    [InlineData("key")]
    [InlineData("hid")]
    [InlineData("report")]
    [InlineData("image")]
    [InlineData("frame")]
    [InlineData("base64")]
    [InlineData("url")]
    [InlineData("hwnd")]
    public void RejectsUnsafeFieldsAtAnyPayloadDepth(string field)
    {
        string json = "{\"schemaVersion\":2,\"type\":\"config.update\",\"payload\":{\"nested\":{\""
            + field
            + "\":\"unsafe\"}}}";

        BridgeRouteResult result = router.Route(json);

        Assert.False(result.Accepted);
        Assert.Equal("UNSAFE_PAYLOAD_REJECTED", result.Code);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"type\":\"snapshot.request\",\"payload\":{\"message\":\"extra\"}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"session.emergencyStop\",\"payload\":{}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"cloud.credential.set\",\"payload\":{\"apiKey\":\"short\"}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"cloud.config.update\",\"payload\":{\"enabled\":true,\"modelId\":\"custom\",\"uploadConsent\":true}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"config.update\",\"payload\":{\"hpThresholdMode\":\"percent\",\"hpThreshold\":101}}")]
    public void RejectsPayloadsThatDoNotMatchTheCommandContract(string json)
    {
        BridgeRouteResult result = router.Route(json);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_PAYLOAD", result.Code);
    }

    [Fact]
    public void RejectsUnknownEnvelopeFields()
    {
        BridgeRouteResult result = router.Route("""
            {"schemaVersion":2,"type":"snapshot.request","payload":{},"unexpected":true}
            """);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_COMMAND", result.Code);
    }
}
