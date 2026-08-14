using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class CapturePollingPolicyTests
{
    [Fact]
    public void SuccessfulCaptureUsesActiveFrameInterval()
    {
        Assert.Equal(33, CapturePollingPolicy.NextIntervalMs(new CaptureTickResult(true, "OK", 1)));
    }

    [Theory]
    [InlineData("TARGET_NOT_FOREGROUND")]
    [InlineData("TARGET_MINIMIZED")]
    [InlineData("TARGET_NOT_FOUND")]
    [InlineData("CAPTURE_FRAME_UNAVAILABLE")]
    public void PausedCaptureUsesLowFrequencyDiscovery(string code)
    {
        Assert.Equal(1000, CapturePollingPolicy.NextIntervalMs(new CaptureTickResult(false, code)));
    }
}
