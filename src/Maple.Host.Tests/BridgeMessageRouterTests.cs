using Maple.Contracts;
using Xunit;

namespace Maple.Host.Tests;

public sealed class BridgeMessageRouterTests
{
    [Fact]
    public void AcceptsStationaryAttackToggleWithoutRawInputFields()
    {
        BridgeRouteResult result = router.Route("""
            {"schemaVersion":2,"type":"stationary.attack.set","payload":{"enabled":true}}
            """);

        Assert.True(result.Accepted);
        Assert.Equal(UiCommandType.StationaryAttackSet, result.CommandType);
    }

    [Fact]
    public void AcceptsEmptySamePlatformCombatTrialCommand()
    {
        BridgeRouteResult result = router.Route("""
            {"schemaVersion":2,"type":"combat.trial.start","payload":{}}
            """);

        Assert.True(result.Accepted);
        Assert.Equal(UiCommandType.CombatTrialStart, result.CommandType);
    }
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

    [Fact]
    public void AcceptsAValidPreviewBoundsChangedCommand()
    {
        BridgeRouteResult result = router.Route("""
            {"schemaVersion":2,"type":"preview.boundsChanged","payload":{"left":24,"top":80,"width":1280,"height":720,"devicePixelRatio":1.25}}
            """);

        Assert.True(result.Accepted);
        Assert.Equal("PreviewBoundsChanged", result.CommandType?.ToString());
    }

    [Fact]
    public void AcceptsOnlyBoundedAbstractInputTests()
    {
        BridgeRouteResult accepted = router.Route("""
            {"schemaVersion":2,"type":"input.test","payload":{"kind":"jump","holdMs":90}}
            """);
        BridgeRouteResult rejected = router.Route("""
            {"schemaVersion":2,"type":"input.test","payload":{"kind":"jump","holdMs":2000}}
            """);

        Assert.True(accepted.Accepted);
        Assert.Equal(UiCommandType.InputTest, accepted.CommandType);
        Assert.False(rejected.Accepted);
        Assert.Equal("INVALID_PAYLOAD", rejected.Code);
    }

    [Fact]
    public void AcceptsOnlyAContractShapedMapConfirmation()
    {
        BridgeRouteResult accepted = router.Route("""
            {"schemaVersion":2,"type":"map.calibration.confirm","payload":{"mapId":"forest-east"}}
            """);
        BridgeRouteResult rejected = router.Route("""
            {"schemaVersion":2,"type":"map.calibration.confirm","payload":{"mapId":"","force":true}}
            """);

        Assert.True(accepted.Accepted);
        Assert.Equal(UiCommandType.MapCalibrationConfirm, accepted.CommandType);
        Assert.False(rejected.Accepted);
        Assert.Equal("INVALID_PAYLOAD", rejected.Code);
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

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"type\":\"preview.boundsChanged\",\"payload\":{\"left\":-1,\"top\":0,\"width\":320,\"height\":180,\"devicePixelRatio\":1}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"preview.boundsChanged\",\"payload\":{\"left\":0,\"top\":0,\"width\":319,\"height\":180,\"devicePixelRatio\":1}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"preview.boundsChanged\",\"payload\":{\"left\":0,\"top\":0,\"width\":320,\"height\":180,\"devicePixelRatio\":4.1}}")]
    [InlineData("{\"schemaVersion\":2,\"type\":\"preview.boundsChanged\",\"payload\":{\"left\":0,\"top\":0,\"width\":320,\"height\":180,\"devicePixelRatio\":1,\"extra\":true}}")]
    public void RejectsInvalidPreviewBoundsChangedPayloads(string json)
    {
        BridgeRouteResult result = router.Route(json);

        Assert.False(result.Accepted);
        Assert.Equal("INVALID_PAYLOAD", result.Code);
    }

    [Theory]
    [InlineData("vk")]
    [InlineData("scanCode")]
    [InlineData("flags")]
    [InlineData("rawInputBytes")]
    [InlineData("reportBytes")]
    [InlineData("abstractAction")]
    [InlineData("actionSequence")]
    public void RejectsExplicitRawInputAndActionFieldsRecursively(string field)
    {
        string json = "{\"schemaVersion\":2,\"type\":\"preview.boundsChanged\",\"payload\":{\"left\":0,\"top\":0,\"width\":320,\"height\":180,\"devicePixelRatio\":1,\"nested\":{\""
            + field
            + "\":\"unsafe\"}}}";

        BridgeRouteResult result = router.Route(json);

        Assert.False(result.Accepted);
        Assert.Equal("UNSAFE_PAYLOAD_REJECTED", result.Code);
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
