using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Maple.Capture;

namespace Maple.Vision;

/// <summary>ONNX adapter for the fixed Maple detector output: cx, cy, width, height, confidence, classIndex.</summary>
public sealed class OnnxRuntimeInferenceEngine : IOnnxInferenceEngine
{
    private readonly InferenceSession session;
    private readonly ModelManifest manifest;

    public OnnxRuntimeInferenceEngine(ModelManifestValidation validation)
    {
        if (validation is null || !validation.IsValid || validation.Manifest is null || validation.ModelPath is null)
            throw new InvalidOperationException("MODEL_NOT_READY");
        manifest = validation.Manifest;
        session = new InferenceSession(validation.ModelPath);
    }

    public ValueTask<IReadOnlyList<DetectionCandidate>> DetectAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        float[] input = ImageTensorPreprocessor.ToNchwFloat32(frame, manifest.InputWidth, manifest.InputHeight);
        var tensor = new DenseTensor<float>(input, [1, 3, manifest.InputHeight, manifest.InputWidth]);
        string inputName = session.InputMetadata.Keys.Single();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        float[] raw = outputs.First().AsEnumerable<float>().ToArray();
        var detections = new List<DetectionCandidate>();
        for (int index = 0; index + 5 < raw.Length; index += 6)
        {
            double confidence = raw[index + 4];
            int classIndex = (int)raw[index + 5];
            if (confidence < manifest.ConfidenceThreshold || classIndex < 0 || classIndex >= manifest.Classes.Length) continue;
            double width = raw[index + 2];
            double height = raw[index + 3];
            detections.Add(new DetectionCandidate(manifest.Classes[classIndex], confidence,
                [Math.Clamp(raw[index] - width / 2, 0, 1), Math.Clamp(raw[index + 1] - height / 2, 0, 1), Math.Clamp(width, 0, 1), Math.Clamp(height, 0, 1)]));
        }
        return ValueTask.FromResult<IReadOnlyList<DetectionCandidate>>(detections);
    }

    public ValueTask DisposeAsync()
    {
        session.Dispose();
        return ValueTask.CompletedTask;
    }
}

public static class ImageTensorPreprocessor
{
    public static float[] ToNchwFloat32(CapturedFrame frame, int width, int height)
    {
        if (frame.PixelFormat != CapturedPixelFormat.Bgra32) throw new NotSupportedException("当前模型只接受 BGRA32 输入");
        float[] output = new float[3 * width * height];
        ReadOnlySpan<byte> source = frame.Pixels.Span;
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(frame.Height - 1, y * frame.Height / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(frame.Width - 1, x * frame.Width / width);
                int sourceIndex = sourceY * frame.Stride + sourceX * 4;
                int offset = y * width + x;
                output[offset] = source[sourceIndex + 2] / 255f;
                output[width * height + offset] = source[sourceIndex + 1] / 255f;
                output[2 * width * height + offset] = source[sourceIndex] / 255f;
            }
        }
        return output;
    }
}
