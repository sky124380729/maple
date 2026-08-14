using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class WgcReadbackResourcePolicyTests
{
    [Theory]
    [InlineData(false, 1366, 768, 87, 1366, 768, 87, true)]
    [InlineData(true, 1366, 768, 87, 1366, 768, 87, false)]
    [InlineData(true, 1366, 768, 87, 1920, 1080, 87, true)]
    [InlineData(true, 1366, 768, 87, 1366, 768, 28, true)]
    public void RecreatesOnlyWhenResourceIsMissingOrShapeChanges(
        bool exists,
        int currentWidth,
        int currentHeight,
        int currentFormat,
        int requestedWidth,
        int requestedHeight,
        int requestedFormat,
        bool expected)
    {
        Assert.Equal(expected, WgcReadbackResourcePolicy.ShouldRecreate(
            exists,
            currentWidth,
            currentHeight,
            currentFormat,
            requestedWidth,
            requestedHeight,
            requestedFormat));
    }
}
