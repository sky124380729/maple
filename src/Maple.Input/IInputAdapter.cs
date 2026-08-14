using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Input
{
    public interface IInputAdapter
    {
        InputResult KeyDown(AbstractAction action, string key, long nowMonoMs);
        InputResult KeyUp(AbstractAction action, string key, long nowMonoMs);
        InputResult Press(AbstractAction action, string key, long nowMonoMs);
        InputResult ReleaseAll(long nowMonoMs);
        bool Heartbeat(long nowMonoMs);
        InputAdapterStatus GetStatus();
    }

    public sealed class InputAdapterStatus
    {
        public string AdapterName { get; internal set; }
        public string Code { get; internal set; }
        public bool IsHealthy { get; internal set; }
        public bool InjectionEnabled { get; internal set; }
        public long LastHeartbeatMonoMs { get; internal set; }
        public IList<string> ActiveKeys { get; internal set; }
    }
}
