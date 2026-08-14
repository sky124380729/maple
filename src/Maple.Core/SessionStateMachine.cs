using Maple.Contracts;

namespace Maple.Core
{
    public sealed class SessionTransitionResult
    {
        public SessionTransitionResult(bool accepted, SessionState state, string message)
        {
            Accepted = accepted;
            State = state;
            Message = message;
        }

        public bool Accepted { get; private set; }
        public SessionState State { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class SessionStateMachine
    {
        private SessionState state;
        private PauseReason pauseReason;

        public SessionStateMachine(SessionState initialState)
        {
            state = initialState;
            pauseReason = PauseReason.None;
        }

        public SessionState State { get { return state; } }
        public PauseReason PauseReason { get { return pauseReason; } }

        public SessionTransitionResult Request(SessionState next)
        {
            if (state == SessionState.EmergencyStop) return Reject("紧急停止后必须重新创建会话");
            if (next == SessionState.EmergencyStop) return EmergencyStop();
            if (!IsAllowed(state, next)) return Reject("非法状态转换：" + state + " -> " + next);
            state = next;
            if (next != SessionState.Paused) pauseReason = PauseReason.None;
            return Accept("状态已切换");
        }

        public SessionTransitionResult Pause(PauseReason reason)
        {
            if (state == SessionState.EmergencyStop) return Reject("紧急停止后不能暂停");
            state = SessionState.Paused;
            pauseReason = reason == PauseReason.None ? PauseReason.OperatorRequested : reason;
            return Accept("会话已暂停");
        }

        public SessionTransitionResult Resume()
        {
            if (state == SessionState.EmergencyStop) return Reject("紧急停止后必须重新创建会话");
            if (state != SessionState.Paused) return Reject("只有暂停状态可以恢复");
            state = SessionState.Arming;
            pauseReason = PauseReason.None;
            return Accept("恢复前重新执行安全门");
        }

        public SessionTransitionResult EmergencyStop()
        {
            state = SessionState.EmergencyStop;
            pauseReason = PauseReason.SafetyViolation;
            return Accept("已执行紧急停止并清空动作队列");
        }

        private SessionTransitionResult Accept(string message) { return new SessionTransitionResult(true, state, message); }
        private SessionTransitionResult Reject(string message) { return new SessionTransitionResult(false, state, message); }

        private static bool IsAllowed(SessionState from, SessionState to)
        {
            if (from == SessionState.Stopped && to == SessionState.Arming) return true;
            if (from == SessionState.Arming && (to == SessionState.Observing || to == SessionState.MapScanning || to == SessionState.Paused)) return true;
            if (from == SessionState.Observing && (to == SessionState.MapScanning || to == SessionState.Navigating || to == SessionState.Paused)) return true;
            if (from == SessionState.MapScanning && (to == SessionState.MapCalibrating || to == SessionState.Paused)) return true;
            if (from == SessionState.MapCalibrating && (to == SessionState.Navigating || to == SessionState.Paused)) return true;
            if (from == SessionState.Navigating && (to == SessionState.Attacking || to == SessionState.Looting || to == SessionState.UsingPotion || to == SessionState.Paused)) return true;
            if (from == SessionState.Attacking && (to == SessionState.Navigating || to == SessionState.Looting || to == SessionState.Paused)) return true;
            if (from == SessionState.Looting && (to == SessionState.Navigating || to == SessionState.Paused)) return true;
            if (from == SessionState.UsingPotion && (to == SessionState.Navigating || to == SessionState.Paused)) return true;
            if (from == SessionState.Paused && to == SessionState.Arming) return true;
            return false;
        }
    }
}
