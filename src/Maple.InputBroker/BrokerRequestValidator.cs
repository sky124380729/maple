using Maple.Input;

namespace Maple.InputBroker;

public sealed record BrokerValidationResult(bool Accepted, string Code)
{
    public static BrokerValidationResult Accept() => new(true, "REQUEST_VALID");
    public static BrokerValidationResult Reject(string code) => new(false, code);
}

public sealed class BrokerRequestValidator
{
    private long lastSequence;

    public BrokerValidationResult Validate(BrokerRequest request)
    {
        if (request == null) return BrokerValidationResult.Reject("REQUEST_REQUIRED");
        if (request.Version != BrokerProtocol.Version)
            return BrokerValidationResult.Reject("PROTOCOL_VERSION_MISMATCH");
        if (request.Sequence <= lastSequence)
            return BrokerValidationResult.Reject("SEQUENCE_NOT_MONOTONIC");
        if (!PayloadMatches(request))
            return BrokerValidationResult.Reject("PAYLOAD_KIND_MISMATCH");

        lastSequence = request.Sequence;
        return BrokerValidationResult.Accept();
    }

    private static bool PayloadMatches(BrokerRequest request) => request.Kind switch
    {
        BrokerRequestKind.ArmTarget => request.Payload is ArmTargetPayload,
        BrokerRequestKind.KeyDownAction => request.Payload is BrokerActionPayload,
        BrokerRequestKind.KeyUpAction => request.Payload is BrokerActionPayload,
        BrokerRequestKind.PressAction => request.Payload is BrokerActionPayload,
        BrokerRequestKind.Heartbeat => request.Payload == null,
        BrokerRequestKind.ReleaseAll => request.Payload == null,
        BrokerRequestKind.Shutdown => request.Payload == null,
        _ => false
    };
}
