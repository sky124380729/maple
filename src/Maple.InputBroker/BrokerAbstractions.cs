using Maple.Input;

namespace Maple.InputBroker;

public interface IBrokerKeySender
{
    void Send(BrokerKeyEncoding encoding, bool isKeyUp);
}

public interface IBrokerClock
{
    long NowMonoMs { get; }
}

public interface IBrokerSafetyGate
{
    BrokerSafetyResult Arm(ArmTargetPayload target);
    BrokerSafetyResult Evaluate(BrokerActionPayload action);
}

public sealed record BrokerSafetyResult(bool Allowed, string Code)
{
    public static BrokerSafetyResult Allow() => new(true, "SAFETY_OK");
    public static BrokerSafetyResult Reject(string code) => new(false, code);
}

public sealed class SystemBrokerClock : IBrokerClock
{
    public long NowMonoMs => System.Environment.TickCount64;
}
