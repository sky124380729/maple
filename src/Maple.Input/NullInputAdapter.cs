using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Input
{
    public sealed class NullInputAdapter : IInputAdapter
    {
        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs) { return Disabled(action, nowMonoMs); }
        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs) { return Disabled(action, nowMonoMs); }
        public InputResult Press(AbstractAction action, string key, long nowMonoMs) { return Disabled(action, nowMonoMs); }

        public InputResult ReleaseAll(long nowMonoMs)
        {
            return new InputResult { SchemaVersion = ContractConstants.SchemaVersion, ActionId = "release-all", Status = InputStatus.Completed, StartedAtMonoMs = nowMonoMs, EndedAtMonoMs = nowMonoMs, ReleasedKeys = new List<string>(), Message = "INPUT_INJECTION=DISABLED" };
        }

        public bool Heartbeat(long nowMonoMs) { return true; }

        public InputAdapterStatus GetStatus()
        {
            return new InputAdapterStatus { AdapterName = "NullInputAdapter", Code = "INPUT_INJECTION=DISABLED", IsHealthy = true, InjectionEnabled = false, ActiveKeys = new List<string>().AsReadOnly() };
        }

        private static InputResult Disabled(AbstractAction action, long nowMonoMs)
        {
            return new InputResult { SchemaVersion = ContractConstants.SchemaVersion, ActionId = action == null ? "invalid" : action.ActionId, Status = InputStatus.Rejected, StartedAtMonoMs = nowMonoMs, EndedAtMonoMs = nowMonoMs, ReleasedKeys = new List<string>(), Message = "INPUT_INJECTION=DISABLED" };
        }
    }
}
