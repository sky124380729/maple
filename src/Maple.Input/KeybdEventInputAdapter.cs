using System;
using System.Collections.Generic;
using System.Linq;
using Maple.Contracts;

namespace Maple.Input
{
    public sealed class KeybdEventInputAdapter : IInputAdapter
    {
        public const uint KeyEventFKeyUp = 0x0002;

        private readonly IKeyboardEventSender sender;
        private readonly IInputSafetyGate safetyGate;
        private readonly ActiveKeyRegistry registry = new ActiveKeyRegistry();
        private bool lastGateAllowed;
        private long lastHeartbeat;

        public KeybdEventInputAdapter(IKeyboardEventSender sender, IInputSafetyGate safetyGate)
        {
            this.sender = sender ?? throw new ArgumentNullException(nameof(sender));
            this.safetyGate = safetyGate ?? throw new ArgumentNullException(nameof(safetyGate));
        }

        public InputResult KeyDown(AbstractAction action, string key, long nowMonoMs)
        {
            string actionId = GetActionId(action);
            if (!VirtualKeyMap.TryGet(key, out ushort virtualKey))
            {
                return Result(actionId, InputStatus.Rejected, nowMonoMs, null, "UNKNOWN_KEY");
            }

            if (!safetyGate.CanSend("KeyDown:" + key))
            {
                lastGateAllowed = false;
                ReleaseAll(nowMonoMs);
                return Result(actionId, InputStatus.Rejected, nowMonoMs, null, "INPUT_SAFETY_GATE_REJECTED");
            }

            lastGateAllowed = true;
            try
            {
                ReleaseOppositeDirection(key);
                if (!registry.KeyDown(key))
                {
                    return Result(actionId, InputStatus.Accepted, nowMonoMs, null, "KEY_ALREADY_DOWN");
                }

                try
                {
                    sender.Send(virtualKey, 0, 0);
                }
                catch
                {
                    registry.KeyUp(key);
                    throw;
                }

                return Result(actionId, InputStatus.Accepted, nowMonoMs, null, "KEY_DOWN_SENT");
            }
            catch (Exception exception)
            {
                return Result(actionId, InputStatus.Failed, nowMonoMs, null, "KEY_DOWN_FAILED:" + exception.GetType().Name);
            }
        }

        public InputResult KeyUp(AbstractAction action, string key, long nowMonoMs)
        {
            string actionId = GetActionId(action);
            if (!VirtualKeyMap.TryGet(key, out ushort virtualKey))
            {
                return Result(actionId, InputStatus.Rejected, nowMonoMs, null, "UNKNOWN_KEY");
            }

            registry.KeyUp(key);
            try
            {
                sender.Send(virtualKey, 0, KeyEventFKeyUp);
                return Result(actionId, InputStatus.Completed, nowMonoMs, new List<string> { key }, "KEY_UP_SENT");
            }
            catch (Exception exception)
            {
                return Result(actionId, InputStatus.Failed, nowMonoMs, new List<string> { key }, "KEY_UP_FAILED:" + exception.GetType().Name);
            }
        }

        public InputResult Press(AbstractAction action, string key, long nowMonoMs)
        {
            InputResult down = KeyDown(action, key, nowMonoMs);
            if (down.Status != InputStatus.Accepted)
            {
                return down;
            }

            long releaseAt = nowMonoMs + Math.Max(0, action == null ? 0 : action.HoldMs);
            return KeyUp(action, key, releaseAt);
        }

        public InputResult ReleaseAll(long nowMonoMs)
        {
            IList<string> activeKeys = registry.ReleaseAll();
            var failed = new List<string>();
            foreach (string key in activeKeys)
            {
                if (!VirtualKeyMap.TryGet(key, out ushort virtualKey))
                {
                    failed.Add(key);
                    continue;
                }

                try
                {
                    sender.Send(virtualKey, 0, KeyEventFKeyUp);
                }
                catch
                {
                    failed.Add(key);
                }
            }

            return Result(
                "release-all",
                failed.Count == 0 ? InputStatus.Completed : InputStatus.Failed,
                nowMonoMs,
                new List<string>(activeKeys),
                failed.Count == 0 ? "ALL_KEYS_RELEASED" : "KEY_RELEASE_FAILED:" + string.Join(",", failed));
        }

        public bool Heartbeat(long nowMonoMs)
        {
            lastHeartbeat = nowMonoMs;
            return true;
        }

        public InputAdapterStatus GetStatus()
        {
            return new InputAdapterStatus
            {
                AdapterName = "KeybdEventInputAdapter",
                Code = lastGateAllowed ? "KEYBD_EVENT_READY" : "KEYBD_EVENT_GATED",
                IsHealthy = true,
                InjectionEnabled = lastGateAllowed,
                LastHeartbeatMonoMs = lastHeartbeat,
                ActiveKeys = registry.ActiveKeys
            };
        }

        private void ReleaseOppositeDirection(string key)
        {
            string opposite = OppositeOf(key);
            if (opposite == null || !registry.ActiveKeys.Any(active => string.Equals(active, opposite, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (!VirtualKeyMap.TryGet(opposite, out ushort virtualKey))
            {
                registry.KeyUp(opposite);
                return;
            }

            sender.Send(virtualKey, 0, KeyEventFKeyUp);
            registry.KeyUp(opposite);
        }

        private static string OppositeOf(string key)
        {
            if (string.Equals(key, "left", StringComparison.OrdinalIgnoreCase)) return "Right";
            if (string.Equals(key, "right", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (string.Equals(key, "up", StringComparison.OrdinalIgnoreCase)) return "Down";
            if (string.Equals(key, "down", StringComparison.OrdinalIgnoreCase)) return "Up";
            return null;
        }

        private static string GetActionId(AbstractAction action)
        {
            return string.IsNullOrWhiteSpace(action?.ActionId) ? "invalid" : action.ActionId;
        }

        private static InputResult Result(
            string actionId,
            InputStatus status,
            long nowMonoMs,
            List<string> releasedKeys,
            string message)
        {
            return new InputResult
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                ActionId = actionId,
                Status = status,
                StartedAtMonoMs = nowMonoMs,
                EndedAtMonoMs = status == InputStatus.Accepted ? (long?)null : nowMonoMs,
                ReleasedKeys = releasedKeys ?? new List<string>(),
                Message = message
            };
        }
    }
}
