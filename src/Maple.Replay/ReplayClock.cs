using System;

namespace Maple.Replay
{
    public sealed class ReplayClock
    {
        private double speed = 1.0;

        public ReplayClock(long initialMonoMs)
        {
            NowMonoMs = initialMonoMs;
            IsPaused = true;
        }

        public long NowMonoMs { get; private set; }
        public bool IsPaused { get; private set; }
        public double Speed { get { return speed; } }

        public void Pause() { IsPaused = true; }
        public void Resume() { IsPaused = false; }

        public void SetSpeed(double value)
        {
            if (value <= 0 || value > 16) throw new ArgumentOutOfRangeException("value");
            speed = value;
        }

        public void Advance(long elapsedMs)
        {
            if (elapsedMs < 0) throw new ArgumentOutOfRangeException("elapsedMs");
            if (!IsPaused) NowMonoMs += (long)Math.Round(elapsedMs * speed, MidpointRounding.AwayFromZero);
        }

        public void Step(long deltaMs)
        {
            if (deltaMs < 0) throw new ArgumentOutOfRangeException("deltaMs");
            NowMonoMs += deltaMs;
        }
    }
}
