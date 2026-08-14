using System;
using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Input
{
    public sealed class ReplayInputEvent
    {
        public string ActionId { get; internal set; }
        public ActionType ActionType { get; internal set; }
        public string Key { get; internal set; }
        public string Phase { get; internal set; }
        public long TimestampMonoMs { get; internal set; }
    }

    public sealed class ReplayInputAdapter : IInputAdapter
    {
        private readonly ActiveKeyRegistry registry = new ActiveKeyRegistry();
        private readonly List<ReplayInputEvent> events = new List<ReplayInputEvent>();
        private long lastHeartbeat;

        public IList<ReplayInputEvent> Events { get { return events.AsReadOnly(); } }

        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs)
        {
            Validate(action);
            registry.KeyDown(key);
            events.Add(Event(action, key, "KeyDown", nowMonoMs));
            return Result(action.ActionId, InputStatus.Accepted, nowMonoMs, null, "已写入回放，不触碰操作系统");
        }

        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs)
        {
            Validate(action);
            registry.KeyUp(key);
            events.Add(Event(action, key, "KeyUp", nowMonoMs));
            return Result(action.ActionId, InputStatus.Completed, nowMonoMs, new List<string> { key }, "已写入回放，不触碰操作系统");
        }

        public InputResult Press(AbstractAction action, string key, long nowMonoMs)
        {
            InputResult down = KeyDown(action, key, nowMonoMs);
            if (down.Status != InputStatus.Accepted) return down;
            return KeyUp(action, key, nowMonoMs + action.HoldMs);
        }

        public InputResult ReleaseAll(long nowMonoMs)
        {
            IList<string> released = registry.ReleaseAll();
            foreach (string key in released) events.Add(new ReplayInputEvent { ActionId = "release-all", ActionType = ActionType.Pause, Key = key, Phase = "KeyUp", TimestampMonoMs = nowMonoMs });
            return Result("release-all", InputStatus.Completed, nowMonoMs, new List<string>(released), "回放活动键已全部释放");
        }

        public bool Heartbeat(long nowMonoMs) { lastHeartbeat = nowMonoMs; return true; }

        public InputAdapterStatus GetStatus()
        {
            return new InputAdapterStatus { AdapterName = "ReplayInputAdapter", Code = "REPLAY_ONLY", IsHealthy = true, InjectionEnabled = false, LastHeartbeatMonoMs = lastHeartbeat, ActiveKeys = registry.ActiveKeys };
        }

        private static void Validate(AbstractAction action)
        {
            ContractValidationResult validation = ContractValidation.ValidateAction(action);
            if (!validation.IsValid) throw new ArgumentException("动作契约无效：" + validation.Error, "action");
        }

        private static ReplayInputEvent Event(AbstractAction action, string key, string phase, long nowMonoMs)
        {
            return new ReplayInputEvent { ActionId = action.ActionId, ActionType = action.Type, Key = key, Phase = phase, TimestampMonoMs = nowMonoMs };
        }

        private static InputResult Result(string actionId, InputStatus status, long nowMonoMs, List<string> releasedKeys, string message)
        {
            return new InputResult { SchemaVersion = ContractConstants.SchemaVersion, ActionId = actionId, Status = status, StartedAtMonoMs = nowMonoMs, EndedAtMonoMs = status == InputStatus.Accepted ? (long?)null : nowMonoMs, ReleasedKeys = releasedKeys ?? new List<string>(), Message = message };
        }
    }
}
