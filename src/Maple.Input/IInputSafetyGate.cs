namespace Maple.Input
{
    public interface IInputSafetyGate
    {
        bool CanSend(string reason);
    }
}
