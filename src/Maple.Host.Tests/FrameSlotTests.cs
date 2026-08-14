using Maple.Preview;
using Xunit;

namespace Maple.Host.Tests;

public sealed class FrameSlotTests
{
    [Fact]
    public void ReplacingSlotsAndDisposingBufferReleaseEveryFrame()
    {
        var released = new List<int>();
        using (var slot = new FrameSlot<TestFrame>(frame => released.Add(frame.Id)))
        {
            slot.Publish(new TestFrame(1), 1);
            slot.Publish(new TestFrame(2), 2);
            slot.Publish(new TestFrame(3), 3);

            Assert.Equal([1], released);
        }

        Assert.Equal([1, 2, 3], released.Order());
    }

    private sealed record TestFrame(int Id);
}
