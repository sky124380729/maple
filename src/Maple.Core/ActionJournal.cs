using System;
using System.Collections.Generic;
using Maple.Contracts;

namespace Maple.Core
{
    public enum ActionLifecyclePhase { Precondition, KeyDown, Observe, EarlyReleaseOrTimeout, KeyUp, Postcondition }

    public sealed class ActionJournalEntry
    {
        public string ActionId { get; internal set; }
        public ActionType ActionType { get; internal set; }
        public ActionLifecyclePhase Phase { get; internal set; }
        public long TimestampMonoMs { get; internal set; }
        public int ComputedHoldMs { get; internal set; }
        public long ObservationFrameId { get; internal set; }
        public string Details { get; internal set; }
    }

    public sealed class ActionJournal
    {
        private readonly int capacity;
        private readonly List<ActionJournalEntry> entries = new List<ActionJournalEntry>();
        private readonly Dictionary<string, ActionLifecyclePhase> phases = new Dictionary<string, ActionLifecyclePhase>();
        private readonly Dictionary<string, ActionType> actionTypes = new Dictionary<string, ActionType>();
        private readonly Dictionary<string, int> holds = new Dictionary<string, int>();

        public ActionJournal(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity");
            this.capacity = capacity;
        }

        public IList<ActionJournalEntry> Entries { get { return entries.AsReadOnly(); } }

        public void Begin(AbstractAction action, long timestampMonoMs)
        {
            if (action == null) throw new ArgumentNullException("action");
            if (!ContractValidation.ValidateAction(action).IsValid) throw new ArgumentException("动作契约无效", "action");
            actionTypes[action.ActionId] = action.Type;
            holds[action.ActionId] = action.HoldMs;
            phases[action.ActionId] = ActionLifecyclePhase.Precondition;
            Add(action.ActionId, action.Type, ActionLifecyclePhase.Precondition, timestampMonoMs, -1, "前置条件已通过");
        }

        public void KeyDown(string actionId, long timestampMonoMs) { AddNext(actionId, ActionLifecyclePhase.KeyDown, timestampMonoMs, -1, "key-down"); }
        public void Observe(string actionId, long timestampMonoMs, long frameId, string details) { AddNext(actionId, ActionLifecyclePhase.Observe, timestampMonoMs, frameId, details); }
        public void EarlyReleaseOrTimeout(string actionId, long timestampMonoMs, long frameId, string details) { AddNext(actionId, ActionLifecyclePhase.EarlyReleaseOrTimeout, timestampMonoMs, frameId, details); }
        public void KeyUp(string actionId, long timestampMonoMs) { AddNext(actionId, ActionLifecyclePhase.KeyUp, timestampMonoMs, -1, "key-up，全键状态已更新"); }
        public void Postcondition(string actionId, long timestampMonoMs, long frameId, string details) { AddNext(actionId, ActionLifecyclePhase.Postcondition, timestampMonoMs, frameId, details); }

        private void AddNext(string actionId, ActionLifecyclePhase next, long timestampMonoMs, long frameId, string details)
        {
            ActionLifecyclePhase current;
            if (!phases.TryGetValue(actionId, out current)) throw new InvalidOperationException("未知动作：" + actionId);
            if (!IsNext(current, next)) throw new InvalidOperationException("动作生命周期顺序无效：" + current + " -> " + next);
            phases[actionId] = next;
            Add(actionId, actionTypes[actionId], next, timestampMonoMs, frameId, details);
        }

        private void Add(string actionId, ActionType type, ActionLifecyclePhase phase, long timestampMonoMs, long frameId, string details)
        {
            entries.Add(new ActionJournalEntry { ActionId = actionId, ActionType = type, Phase = phase, TimestampMonoMs = timestampMonoMs, ComputedHoldMs = holds.ContainsKey(actionId) ? holds[actionId] : 0, ObservationFrameId = frameId, Details = details });
            if (entries.Count > capacity) entries.RemoveAt(0);
        }

        private static bool IsNext(ActionLifecyclePhase current, ActionLifecyclePhase next)
        {
            if (current == ActionLifecyclePhase.Precondition && next == ActionLifecyclePhase.KeyDown) return true;
            if (current == ActionLifecyclePhase.KeyDown && next == ActionLifecyclePhase.Observe) return true;
            if (current == ActionLifecyclePhase.Observe && next == ActionLifecyclePhase.Observe) return true;
            if (current == ActionLifecyclePhase.Observe && next == ActionLifecyclePhase.EarlyReleaseOrTimeout) return true;
            if (current == ActionLifecyclePhase.EarlyReleaseOrTimeout && next == ActionLifecyclePhase.KeyUp) return true;
            if (current == ActionLifecyclePhase.KeyUp && next == ActionLifecyclePhase.Postcondition) return true;
            return false;
        }
    }
}
