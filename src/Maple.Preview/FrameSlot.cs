using System;
using System.Threading;

namespace Maple.Preview
{
    /// <summary>
    /// A bounded two-slot latest-frame buffer. Publishing never waits for the
    /// render or vision consumer; an unread publication is replaced and counted.
    /// </summary>
    public sealed class FrameSlot<T> where T : class
    {
        private sealed class Publication
        {
            internal Publication(long sequence, long capturedAtMonoMs, T frame)
            {
                Sequence = sequence;
                CapturedAtMonoMs = capturedAtMonoMs;
                Frame = frame;
            }

            internal readonly long Sequence;
            internal readonly long CapturedAtMonoMs;
            internal readonly T Frame;
        }

        private readonly Publication[] slots = new Publication[2];
        private int publishedSlot = -1;
        private long nextSequence;
        private long lastReadSequence = -1;
        private long droppedFrames;

        public long DroppedFrames { get { return Interlocked.Read(ref droppedFrames); } }

        public void Publish(T frame, long capturedAtMonoMs)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            int currentSlot = Volatile.Read(ref publishedSlot);
            int nextSlot = currentSlot < 0 ? 0 : (currentSlot + 1) & 1;
            var publication = new Publication(Interlocked.Increment(ref nextSequence), capturedAtMonoMs, frame);
            var previous = Interlocked.Exchange(ref slots[nextSlot], publication);
            if (previous != null && previous.Sequence > Interlocked.Read(ref lastReadSequence))
            {
                Interlocked.Increment(ref droppedFrames);
            }
            Volatile.Write(ref publishedSlot, nextSlot);
        }

        public bool TryRead(long nowMonoMs, out FrameRead<T> read)
        {
            int currentSlot = Volatile.Read(ref publishedSlot);
            if (currentSlot < 0)
            {
                read = null;
                return false;
            }

            var publication = Volatile.Read(ref slots[currentSlot]);
            if (publication == null)
            {
                read = null;
                return false;
            }

            Interlocked.Exchange(ref lastReadSequence, publication.Sequence);
            read = new FrameRead<T>(publication.Sequence, publication.CapturedAtMonoMs, Math.Max(0, nowMonoMs - publication.CapturedAtMonoMs), publication.Frame);
            return true;
        }
    }

    public sealed class FrameRead<T> where T : class
    {
        internal FrameRead(long sequence, long capturedAtMonoMs, long ageMs, T frame)
        {
            Sequence = sequence;
            CapturedAtMonoMs = capturedAtMonoMs;
            AgeMs = ageMs;
            Frame = frame;
        }

        public long Sequence { get; private set; }
        public long CapturedAtMonoMs { get; private set; }
        public long AgeMs { get; private set; }
        public T Frame { get; private set; }
    }
}
