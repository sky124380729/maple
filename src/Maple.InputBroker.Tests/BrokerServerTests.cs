using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Maple.Input;
using Maple.InputBroker;
using Xunit;

namespace Maple.InputBroker.Tests;

public sealed class BrokerServerTests
{
    [Fact]
    public void ValidatorRejectsWrongProtocolVersion()
    {
        var validator = new BrokerRequestValidator();
        var request = new BrokerRequest(
            BrokerProtocol.Version + 1,
            1,
            BrokerRequestKind.Heartbeat,
            null);

        BrokerValidationResult result = validator.Validate(request);

        Assert.False(result.Accepted);
        Assert.Equal("PROTOCOL_VERSION_MISMATCH", result.Code);
    }

    [Fact]
    public void ValidatorRejectsDuplicateOrOutOfOrderSequence()
    {
        var validator = new BrokerRequestValidator();
        Assert.True(validator.Validate(Heartbeat(7)).Accepted);

        BrokerValidationResult duplicate = validator.Validate(Heartbeat(7));
        BrokerValidationResult older = validator.Validate(Heartbeat(6));

        Assert.Equal("SEQUENCE_NOT_MONOTONIC", duplicate.Code);
        Assert.Equal("SEQUENCE_NOT_MONOTONIC", older.Code);
    }

    [Fact]
    public void ValidatorRejectsPayloadKindMismatch()
    {
        var validator = new BrokerRequestValidator();
        var request = new BrokerRequest(
            BrokerProtocol.Version,
            1,
            BrokerRequestKind.Heartbeat,
            new BrokerActionPayload("a-1", BrokerActionKind.Jump, null, 20, 100));

        BrokerValidationResult result = validator.Validate(request);

        Assert.False(result.Accepted);
        Assert.Equal("PAYLOAD_KIND_MISMATCH", result.Code);
    }

    [Fact]
    public async Task CodecRejectsUnknownRawKeyboardFields()
    {
        const string json = "{\"version\":1,\"sequence\":1,\"kind\":\"KeyDownAction\",\"payload\":{" +
            "\"payloadType\":\"action\",\"actionId\":\"a-1\",\"action\":\"MoveLeft\"," +
            "\"logicalKey\":null,\"holdMs\":100,\"maximumDurationMs\":300," +
            "\"frameFreshUntilMonoMs\":9999,\"scanCode\":75}}";
        using MemoryStream stream = Frame(json);

        await Assert.ThrowsAsync<JsonException>(() =>
            new BrokerMessageCodec().ReadRequestAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task CodecReadsStrictHeartbeatFrame()
    {
        using MemoryStream stream = Frame(
            "{\"version\":1,\"sequence\":9,\"kind\":\"Heartbeat\",\"payload\":null}");

        BrokerRequest request = await new BrokerMessageCodec()
            .ReadRequestAsync(stream, CancellationToken.None);

        Assert.Equal(9, request.Sequence);
        Assert.Equal(BrokerRequestKind.Heartbeat, request.Kind);
        Assert.Null(request.Payload);
    }

    [Fact]
    public async Task CodecRejectsOversizedFrameBeforeAllocatingPayload()
    {
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(
            prefix,
            BrokerMessageCodec.MaximumMessageBytes + 1);
        using var stream = new MemoryStream(prefix);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new BrokerMessageCodec().ReadRequestAsync(stream, CancellationToken.None));

        Assert.Equal("MESSAGE_SIZE_INVALID", exception.Message);
    }

    private static BrokerRequest Heartbeat(long sequence) =>
        new(BrokerProtocol.Version, sequence, BrokerRequestKind.Heartbeat, null);

    private static MemoryStream Frame(string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), payload.Length);
        payload.CopyTo(frame, 4);
        return new MemoryStream(frame);
    }
}
