namespace Maple.Input
{
    public interface IKeyboardEventSender
    {
        void Send(ushort virtualKey, uint scanCode, uint flags);
    }
}
