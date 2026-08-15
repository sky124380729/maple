using System.Globalization;

namespace Maple.Vision;

public enum DetectionRole
{
    Ignore,
    CharacterCandidate,
    Monster,
}

public enum OnnxOutputLayout
{
    Unsupported,
    FixedNmsNx6,
    YoloChannelsFirst,
    YoloChannelsLast,
}

public sealed class ModelClassMap
{
    private readonly IReadOnlyDictionary<string, DetectionRole> roles;

    public ModelClassMap(IReadOnlyList<string> classes, IReadOnlyDictionary<string, DetectionRole> roles)
    {
        Classes = classes ?? throw new ArgumentNullException(nameof(classes));
        this.roles = roles ?? throw new ArgumentNullException(nameof(roles));
    }

    public IReadOnlyList<string> Classes { get; }

    public DetectionRole RoleAt(int index) => index >= 0 && index < Classes.Count
        && roles.TryGetValue(Classes[index], out DetectionRole role) ? role : DetectionRole.Ignore;
}

public static class YoloTensorDecoder
{
    public static IReadOnlyList<DetectionCandidate> Decode(
        IReadOnlyList<float> tensor,
        IReadOnlyList<int> dimensions,
        ModelClassMap classes,
        double confidenceThreshold,
        double nmsThreshold,
        int inputWidth,
        int inputHeight)
    {
        ArgumentNullException.ThrowIfNull(tensor);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(classes);
        if (confidenceThreshold is < 0 or > 1 || nmsThreshold is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        if (inputWidth <= 0 || inputHeight <= 0) throw new ArgumentOutOfRangeException(nameof(inputWidth));

        OnnxOutputLayout layout = OnnxModelInspector.ClassifyOutput(dimensions, classes.Classes.Count);
        List<DetectionCandidate> decoded = layout switch
        {
            OnnxOutputLayout.FixedNmsNx6 => DecodeFixed(tensor, dimensions, classes, confidenceThreshold, inputWidth, inputHeight),
            OnnxOutputLayout.YoloChannelsFirst => DecodeYolo(tensor, dimensions, classes, confidenceThreshold, inputWidth, inputHeight, channelsFirst: true),
            OnnxOutputLayout.YoloChannelsLast => DecodeYolo(tensor, dimensions, classes, confidenceThreshold, inputWidth, inputHeight, channelsFirst: false),
            _ => throw new NotSupportedException($"不支持的 ONNX 输出形状: [{string.Join(',', dimensions)}]"),
        };
        return ApplyClassAwareNms(decoded, nmsThreshold);
    }

    private static List<DetectionCandidate> DecodeFixed(IReadOnlyList<float> tensor, IReadOnlyList<int> dimensions, ModelClassMap classes, double threshold, int width, int height)
    {
        int rows = dimensions.Count == 2 ? dimensions[0] : dimensions[1];
        if (tensor.Count != rows * 6) throw new InvalidDataException("ONNX 输出长度与 [N,6] 形状不一致");
        List<DetectionCandidate> result = [];
        for (int row = 0; row < rows; row++)
        {
            int offset = row * 6;
            double confidence = tensor[offset + 4];
            int classIndex = Convert.ToInt32(tensor[offset + 5], CultureInfo.InvariantCulture);
            if (confidence < threshold || classIndex < 0 || classIndex >= classes.Classes.Count) continue;
            double x1 = Normalize(tensor[offset], width);
            double y1 = Normalize(tensor[offset + 1], height);
            double x2 = Normalize(tensor[offset + 2], width);
            double y2 = Normalize(tensor[offset + 3], height);
            Add(result, classes, classIndex, confidence, x1, y1, x2 - x1, y2 - y1);
        }
        return result;
    }

    private static List<DetectionCandidate> DecodeYolo(IReadOnlyList<float> tensor, IReadOnlyList<int> dimensions, ModelClassMap classes, double threshold, int width, int height, bool channelsFirst)
    {
        int channels = 4 + classes.Classes.Count;
        int candidates = channelsFirst ? dimensions[2] : dimensions[1];
        if (tensor.Count != channels * candidates) throw new InvalidDataException("ONNX 输出长度与 YOLO 形状不一致");
        List<DetectionCandidate> result = [];
        for (int candidate = 0; candidate < candidates; candidate++)
        {
            float Read(int channel) => channelsFirst ? tensor[channel * candidates + candidate] : tensor[candidate * channels + channel];
            int classIndex = 0;
            double confidence = Read(4);
            for (int index = 1; index < classes.Classes.Count; index++)
            {
                double score = Read(4 + index);
                if (score > confidence) { confidence = score; classIndex = index; }
            }
            if (confidence < threshold) continue;
            double centerX = Normalize(Read(0), width);
            double centerY = Normalize(Read(1), height);
            double boxWidth = Normalize(Read(2), width);
            double boxHeight = Normalize(Read(3), height);
            Add(result, classes, classIndex, confidence, centerX - boxWidth / 2, centerY - boxHeight / 2, boxWidth, boxHeight);
        }
        return result;
    }

    private static void Add(List<DetectionCandidate> result, ModelClassMap classes, int classIndex, double confidence, double x, double y, double width, double height)
    {
        double left = Math.Clamp(x, 0, 1);
        double top = Math.Clamp(y, 0, 1);
        double right = Math.Clamp(x + width, 0, 1);
        double bottom = Math.Clamp(y + height, 0, 1);
        if (right <= left || bottom <= top) return;
        DetectionRole role = classes.RoleAt(classIndex);
        if (role == DetectionRole.Ignore) return;
        result.Add(new DetectionCandidate(classes.Classes[classIndex], confidence, [left, top, right - left, bottom - top], role));
    }

    private static IReadOnlyList<DetectionCandidate> ApplyClassAwareNms(List<DetectionCandidate> candidates, double threshold)
    {
        List<DetectionCandidate> kept = [];
        foreach (DetectionCandidate candidate in candidates.OrderByDescending(item => item.Confidence))
        {
            if (kept.Any(existing => existing.Class.Equals(candidate.Class, StringComparison.OrdinalIgnoreCase) && IoU(existing.Box, candidate.Box) > threshold)) continue;
            kept.Add(candidate);
        }
        return kept;
    }

    private static double IoU(double[] first, double[] second)
    {
        double left = Math.Max(first[0], second[0]);
        double top = Math.Max(first[1], second[1]);
        double right = Math.Min(first[0] + first[2], second[0] + second[2]);
        double bottom = Math.Min(first[1] + first[3], second[1] + second[3]);
        double intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        double union = first[2] * first[3] + second[2] * second[3] - intersection;
        return union <= 0 ? 0 : intersection / union;
    }

    private static double Normalize(double value, int extent) => value > 1 ? value / extent : value;
}
