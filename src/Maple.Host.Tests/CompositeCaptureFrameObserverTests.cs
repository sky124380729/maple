using Maple.Capture;
using Xunit;

namespace Maple.Host.Tests;

public sealed class CompositeCaptureFrameObserverTests
{
    [Fact]
    public void ObserveInvokesReadersInDeterministicOrderWithoutOwningSource()
    {
        List<string> calls = [];
        var observer = new CompositeCaptureFrameObserver(
            new RecordingObserver("map", calls),
            new RecordingObserver("vision", calls));
        using CapturedFrame frame = LatestVisionFrameQueueTests.Frame(3, 33);

        observer.Observe(frame);

        Assert.Equal(["map:3:33", "vision:3:33"], calls);
        Assert.Equal(33, frame.Pixels.Span[0]);
    }

    private sealed class RecordingObserver(string name, List<string> calls) : ICaptureFrameObserver
    {
        public void Observe(CapturedFrame frame) => calls.Add($"{name}:{frame.Metadata.FrameId}:{frame.Pixels.Span[0]}");
    }
}
