using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class AdaptiveFixedUiVisionProviderTests
{
    [Theory]
    [InlineData("彩虹岛", true)]
    [InlineData("蜗牛打猎场 I", true)]
    [InlineData("-7FFfJJ-WJ I", false)]
    [InlineData("", false)]
    public void Map_text_requires_a_Cjk_character(string value, bool expected)
    {
        Assert.Equal(expected, AdaptiveFixedUiVisionProvider.ContainsMapText(value));
    }

    [Fact]
    public async Task ResolvesWideClientHudAndReadsHpMpFill()
    {
        const int width = 1000;
        const int height = 700;
        HudLayout layout = AdaptiveHudLayout.Resolve(width, height);
        byte[] pixels = new byte[width * height * 4];
        Fill(pixels, width, layout.Hp, 0.5, b: 20, g: 20, r: 240);
        Fill(pixels, width, layout.Mp, 0.25, b: 240, g: 80, r: 20);
        using CapturedFrame frame = Frame(pixels, width, height);

        FixedUiVisionResult result = await new AdaptiveFixedUiVisionProvider().ObserveFixedUiAsync(frame, CancellationToken.None);

        Assert.InRange(result.HpCandidates.Single().Value, 0.49, 0.52);
        Assert.InRange(result.MpCandidates.Single().Value, 0.24, 0.27);
        Assert.Equal(MapArchiveState.Candidate, result.Map.State);
    }

    [Fact]
    public async Task BlankHudRemainsUnreadableInsteadOfReportingZeroHealth()
    {
        using CapturedFrame frame = Frame(new byte[1000 * 700 * 4], 1000, 700);

        FixedUiVisionResult result = await new AdaptiveFixedUiVisionProvider().ObserveFixedUiAsync(frame, CancellationToken.None);

        Assert.Empty(result.HpCandidates);
        Assert.Empty(result.MpCandidates);
    }

    [Fact]
    public async Task Resource_fill_uses_horizontal_extent_not_colored_area()
    {
        const int width = 1000;
        const int height = 700;
        HudLayout layout = AdaptiveHudLayout.Resolve(width, height);
        byte[] pixels = new byte[width * height * 4];
        FillPartialHeight(pixels, width, layout.Hp, horizontalRatio: 0.5, verticalRatio: 0.4, b: 20, g: 20, r: 240);
        FillPartialHeight(pixels, width, layout.Mp, horizontalRatio: 0.5, verticalRatio: 0.4, b: 240, g: 80, r: 20);
        using CapturedFrame frame = Frame(pixels, width, height);

        FixedUiVisionResult result = await new AdaptiveFixedUiVisionProvider().ObserveFixedUiAsync(frame, CancellationToken.None);

        Assert.Equal(0.5, result.HpCandidates.Single().Value, precision: 1);
        Assert.Equal(0.5, result.MpCandidates.Single().Value, precision: 1);
    }

    [Fact]
    public async Task ExactResourceOcrIsRateLimitedBetweenAdjacentFrames()
    {
        var ocr = new CountingOcrEngine();
        var provider = new AdaptiveFixedUiVisionProvider(ocr);
        using CapturedFrame first = Frame(new byte[1000 * 700 * 4], 1000, 700, frameId: 1);
        using CapturedFrame second = Frame(new byte[1000 * 700 * 4], 1000, 700, frameId: 2);

        await provider.ObserveFixedUiAsync(first, CancellationToken.None);
        await provider.ObserveFixedUiAsync(second, CancellationToken.None);

        Assert.Equal(4, ocr.CallCount);
    }

    [Fact]
    public async Task StableMinimapFingerprintReplacesUnreadableMapText()
    {
        const int width = 1000;
        const int height = 700;
        HudLayout layout = AdaptiveHudLayout.Resolve(width, height);
        var provider = new AdaptiveFixedUiVisionProvider();
        FixedUiVisionResult? result = null;
        for (int frameId = 1; frameId <= 3; frameId++)
        {
            byte[] pixels = new byte[width * height * 4];
            Fill(pixels, width, layout.Hp, 1, b: 20, g: 20, r: 240);
            Fill(pixels, width, layout.Mp, 1, b: 240, g: 80, r: 20);
            DrawMinimap(pixels, width, layout.Minimap!.Value, markerOffset: frameId * 3);
            using CapturedFrame frame = Frame(pixels, width, height, frameId);
            result = await provider.ObserveFixedUiAsync(frame, CancellationToken.None);
        }

        Assert.NotNull(result);
        Assert.StartsWith("visual-", result.Map.MapId, StringComparison.Ordinal);
        Assert.True(result.Map.Confidence >= 0.75);
        Assert.Equal(MapArchiveState.Candidate, result.Map.State);
    }

    private static CapturedFrame Frame(byte[] pixels, int width, int height, long frameId = 1)
    {
        int length = pixels.Length;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        pixels.CopyTo(owner.Memory.Span);
        return new CapturedFrame(new CaptureFrameMetadata
        {
            SchemaVersion = 2, FrameId = frameId, CapturedAtMonoMs = 1000 + frameId * 20, ClientWidth = width, ClientHeight = height,
            Dpi = 96, CaptureBackend = CaptureBackend.Wgc, DroppedReason = DroppedFrameReason.None,
        }, width, height, width * 4, CapturedPixelFormat.Bgra32, owner, length);
    }

    private static void DrawMinimap(byte[] pixels, int width, PixelRegion region, int markerOffset)
    {
        FillRect(pixels, width, region.X + region.Width / 10, region.Y + region.Height / 4, region.Width * 7 / 10, 5, 210);
        FillRect(pixels, width, region.X + region.Width / 4, region.Y + region.Height * 2 / 3, region.Width * 6 / 10, 5, 180);
        FillRect(pixels, width, region.X + region.Width / 2, region.Y + region.Height / 4, 4, region.Height / 2, 150);
        FillRect(pixels, width, region.X + markerOffset, region.Y + region.Height / 2, 3, 3, 255);
    }

    private static void FillRect(byte[] pixels, int width, int x, int y, int fillWidth, int fillHeight, byte value)
    {
        for (int row = y; row < y + fillHeight; row++)
        for (int column = x; column < x + fillWidth; column++)
        {
            int index = (row * width + column) * 4;
            pixels[index] = value;
            pixels[index + 1] = value;
            pixels[index + 2] = value;
            pixels[index + 3] = 255;
        }
    }

    private static void Fill(byte[] pixels, int width, PixelRegion region, double ratio, byte b, byte g, byte r)
    {
        int fillWidth = (int)Math.Round(region.Width * ratio);
        for (int y = region.Y; y < region.Y + region.Height; y++)
        for (int x = region.X; x < region.X + fillWidth; x++)
        {
            int index = (y * width + x) * 4;
            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = 255;
        }
    }

    private static void FillPartialHeight(
        byte[] pixels,
        int width,
        PixelRegion region,
        double horizontalRatio,
        double verticalRatio,
        byte b,
        byte g,
        byte r)
    {
        int fillWidth = (int)Math.Round(region.Width * horizontalRatio);
        int fillHeight = Math.Max(1, (int)Math.Round(region.Height * verticalRatio));
        for (int y = region.Y; y < region.Y + fillHeight; y++)
        for (int x = region.X; x < region.X + fillWidth; x++)
        {
            int index = (y * width + x) * 4;
            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = 255;
        }
    }

    private sealed class CountingOcrEngine : IOcrEngine
    {
        public int CallCount { get; private set; }

        public ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(string.Empty);
        }
    }
}
