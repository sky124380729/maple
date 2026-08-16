using System.Drawing;
using Xunit;

namespace Maple.Host.Tests;

public sealed class PreviewLayoutTests
{
    [Theory]
    [InlineData("{\"schemaVersion\":2,\"type\":\"preview.boundsChanged\",\"payload\":{}}", true)]
    [InlineData("{\"schemaVersion\":2,\"type\":\"session.pause\",\"payload\":{}}", false)]
    [InlineData("not-json", false)]
    public void IsPreviewBoundsCommandRecognizesOnlyTheLayoutCommand(string json, bool expected)
    {
        Assert.Equal(expected, PreviewLayout.IsPreviewBoundsCommand(json));
    }

    [Fact]
    public void ResolveClampsPreviewToBrowserClientArea()
    {
        Rectangle result = PreviewLayout.Resolve(
            new PreviewBoundsIntent(260, 64, 900, 700, 1),
            new Size(1200, 760));

        Assert.Equal(new Rectangle(260, 64, 900, 696), result);
    }

    [Fact]
    public void ResolveConvertsWebViewCssPixelsToWinFormsDevicePixels()
    {
        Rectangle result = PreviewLayout.Resolve(
            new PreviewBoundsIntent(200, 100, 600, 400, 1.5),
            new Size(1440, 900));

        Assert.Equal(new Rectangle(300, 150, 900, 600), result);
    }

    [Theory]
    [InlineData(double.NaN, 64, 900, 700)]
    [InlineData(260, 64, 0, 700)]
    [InlineData(1200, 64, 900, 700)]
    public void ResolveRejectsInvalidOrOffscreenBounds(double left, double top, double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviewLayout.Resolve(
            new PreviewBoundsIntent(left, top, width, height, 1),
            new Size(1200, 760)));
    }
}
