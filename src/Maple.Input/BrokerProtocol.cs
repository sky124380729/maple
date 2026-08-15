using System.Text.Json.Serialization;

namespace Maple.Input;

public static class BrokerProtocol
{
    public const int Version = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BrokerRequestKind
{
    ArmTarget,
    KeyDownAction,
    KeyUpAction,
    PressAction,
    Heartbeat,
    ReleaseAll,
    Shutdown
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BrokerActionKind
{
    MoveLeft,
    MoveRight,
    Jump,
    ClimbUp,
    ClimbDown,
    SingleAttack,
    AreaAttack,
    Pickup,
    HpPotion,
    MpPotion
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "payloadType")]
[JsonDerivedType(typeof(ArmTargetPayload), "armTarget")]
[JsonDerivedType(typeof(BrokerActionPayload), "action")]
public abstract record BrokerPayload;

public sealed record BrokerRequest(
    int Version,
    long Sequence,
    BrokerRequestKind Kind,
    BrokerPayload Payload);

public sealed record BrokerResponse(
    int Version,
    long Sequence,
    bool Accepted,
    string Code,
    string[] ReleasedKeys);

public sealed record ArmTargetPayload(
    long Hwnd,
    int Pid,
    long StartedAtUtcTicks,
    string ExecutablePath) : BrokerPayload;

public sealed record BrokerActionPayload(
    string ActionId,
    BrokerActionKind Action,
    string LogicalKey,
    int HoldMs,
    int MaximumDurationMs) : BrokerPayload;

public sealed record BrokerKeyEncoding(
    ushort VirtualKey,
    uint ScanCode,
    bool Extended);
