using System.Buffers;
using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class OcrCharacterNameMatcherTests
{
    [Fact]
    public async Task MatchesCharacterNameplateAgainstHudCharacterName()
    {
        var ocr = new QueueOcrEngine("Hello`Ya", "other", "Hello Ya");
        var matcher = new OcrCharacterNameMatcher(ocr, scanIntervalMs: 0);
        DetectionCandidate other = Character(0.18);
        DetectionCandidate self = Character(0.62);
        using CapturedFrame frame = Frame();

        DetectionCandidate? matched = await matcher.FindSelfAsync(frame, [other, self], 1000, CancellationToken.None);

        Assert.Same(self, matched);
    }

    private static DetectionCandidate Character(double x) =>
        new("character", 0.5, [x, 0.58, 0.08, 0.18], DetectionRole.CharacterCandidate);

    private static CapturedFrame Frame()
    {
        const int width = 1000;
        const int height = 700;
        int length = width * height * 4;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        return new CapturedFrame(new CaptureFrameMetadata
        {
            SchemaVersion = 2,
            FrameId = 1,
            CapturedAtMonoMs = 1000,
            ClientWidth = width,
            ClientHeight = height,
            Dpi = 96,
            CaptureBackend = Maple.Contracts.CaptureBackend.Wgc,
            DroppedReason = Maple.Contracts.DroppedFrameReason.None,
        }, width, height, width * 4, CapturedPixelFormat.Bgra32, owner, length);
    }

    private sealed class QueueOcrEngine(params string[] values) : IOcrEngine
    {
        private readonly Queue<string> values = new(values);
        public ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken) =>
            ValueTask.FromResult(values.Dequeue());
    }
}
