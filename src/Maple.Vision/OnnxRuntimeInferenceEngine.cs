using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Maple.Capture;

namespace Maple.Vision;

/// <summary>ONNX adapter for supported fixed-NMS and Ultralytics YOLO tensor layouts.</summary>
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
        Tensor<float> output = outputs.Single().AsTensor<float>();
        IReadOnlyList<DetectionCandidate> detections = YoloTensorDecoder.Decode(
            output.ToArray(), output.Dimensions.ToArray(), new ModelClassMap(manifest.Classes, manifest.ClassRoles),
            manifest.ConfidenceThreshold, manifest.NmsThreshold, manifest.InputWidth, manifest.InputHeight);
        return ValueTask.FromResult(detections);
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
