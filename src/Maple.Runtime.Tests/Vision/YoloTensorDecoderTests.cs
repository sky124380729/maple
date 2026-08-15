using Maple.Vision;
using Xunit;

namespace Maple.Runtime.Tests.Vision;

public sealed class YoloTensorDecoderTests
{
    private static readonly ModelClassMap Classes = new(
        ["character", "mob"],
        new Dictionary<string, DetectionRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["character"] = DetectionRole.CharacterCandidate,
            ["mob"] = DetectionRole.Monster,
        });

    [Fact]
    public void DecodeYoloChannelsFirstMapsMobToMonsterAndSuppressesOverlap()
    {
        float[] tensor =
        [
            160, 164, 40,
            160, 164, 40,
            80, 80, 20,
            80, 80, 20,
            0.05f, 0.05f, 0.92f,
            0.95f, 0.90f, 0.04f,
        ];

        IReadOnlyList<DetectionCandidate> result = YoloTensorDecoder.Decode(
            tensor, [1, 6, 3], Classes, 0.6, 0.45, inputWidth: 320, inputHeight: 320);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Role == DetectionRole.Monster && item.Class == "mob");
        Assert.Contains(result, item => item.Role == DetectionRole.CharacterCandidate);
    }

    [Fact]
    public void DecodeYoloChannelsLastReadsEveryCandidate()
    {
        float[] tensor =
        [
            80, 160, 40, 80, 0.91f, 0.05f,
            240, 160, 40, 80, 0.02f, 0.88f,
        ];

        IReadOnlyList<DetectionCandidate> result = YoloTensorDecoder.Decode(
            tensor, [1, 2, 6], Classes, 0.6, 0.45, inputWidth: 320, inputHeight: 320);

        Assert.Equal(2, result.Count);
        Assert.Equal(DetectionRole.CharacterCandidate, result[0].Role);
        Assert.Equal(DetectionRole.Monster, result[1].Role);
    }

    [Fact]
    public void DecodeFixedNmsRowsRejectsInvalidDimensions()
    {
        float[] rows = [32, 32, 96, 128, 0.9f, 1];

        IReadOnlyList<DetectionCandidate> result = YoloTensorDecoder.Decode(
            rows, [1, 6], Classes, 0.6, 0.45, inputWidth: 320, inputHeight: 320);

        DetectionCandidate monster = Assert.Single(result);
        Assert.Equal(DetectionRole.Monster, monster.Role);
        Assert.Equal([0.1, 0.1, 0.2, 0.3], monster.Box, new DoubleArrayComparer(0.0001));
        Assert.Throws<NotSupportedException>(() => YoloTensorDecoder.Decode(
            [1, 2, 3, 4], [1, 2, 2], Classes, 0.6, 0.45, 320, 320));
    }

    private sealed class DoubleArrayComparer(double tolerance) : IEqualityComparer<double[]>
    {
        public bool Equals(double[]? x, double[]? y) => x is not null && y is not null && x.Length == y.Length
            && x.Zip(y).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance);
        public int GetHashCode(double[] obj) => 0;
    }
}
