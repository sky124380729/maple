using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Maple.Capture;
using Maple.Contracts;
using Maple.Vision;
using OpenCvSharp;

namespace Maple.Host;

public sealed record OfflineVisionDiagnosticReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string ImagePath,
    int Width,
    int Height,
    double ElapsedMs,
    bool ModelReady,
    string ModelId,
    string Provider,
    bool OcrReady,
    string PipelineStatus,
    string Diagnostic,
    bool CanDriveActions,
    FixedUiVisionResult? FixedUi,
    DynamicVisionResult? Dynamic,
    ObservationSnapshot? Observation);

public static class OfflineVisionDiagnostics
{
    public static async Task<int> RunAsync(
        string imagePath,
        string outputPath,
        IOcrEngine? ocrEngine,
        IOcrEngine? resourceOcrEngine,
        CancellationToken cancellationToken)
    {
        string fullImagePath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullImagePath))
        {
            WriteMissingImage(outputPath, fullImagePath);
            return 2;
        }

        VisionRuntimeBootstrapResult bootstrap = VisionRuntimeBootstrap.Load(
            ocrEngine: ocrEngine,
            resourceOcrEngine: resourceOcrEngine,
            dynamicTimeout: TimeSpan.FromSeconds(5));

        if (!bootstrap.Ready || bootstrap.Pipeline is null)
        {
            Write(outputPath, fullImagePath, 0, 0, 0, bootstrap, ocrEngine is not null,
                "NOT_RUN", bootstrap.Diagnostic, null);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using Mat source = Cv2.ImRead(fullImagePath, ImreadModes.Unchanged);
            if (source.Empty())
            {
                Write(outputPath, fullImagePath, 0, 0, stopwatch.Elapsed.TotalMilliseconds, bootstrap,
                    ocrEngine is not null, "NOT_RUN", "IMAGE_DECODE_FAILED", null);
                return 2;
            }

            using Mat normalized = NormalizeClientImage(source);
            using Mat bgra = ConvertToBgra(normalized);
            var target = new TargetBinding
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                Hwnd = "offline",
                Pid = 0,
                ClientWidth = bgra.Width,
                ClientHeight = bgra.Height,
                Dpi = 96,
            };

            VisionPipelineResult? result = null;
            for (long frameId = 1; frameId <= 3; frameId++)
            {
                using CapturedFrame frame = CreateFrame(bgra, frameId);
                result = await bootstrap.Pipeline
                    .ProcessAsync(frame, target, frame.Metadata.CapturedAtMonoMs, cancellationToken)
                    .ConfigureAwait(false);
            }
            stopwatch.Stop();
            Write(outputPath, fullImagePath, bgra.Width, bgra.Height, stopwatch.Elapsed.TotalMilliseconds,
                bootstrap, ocrEngine is not null, result!.Status.ToString(), result.Diagnostic, result);
            return result.Status == VisionPipelineStatus.Ready ? 0 : 2;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            stopwatch.Stop();
            Write(outputPath, fullImagePath, 0, 0, stopwatch.Elapsed.TotalMilliseconds, bootstrap,
                ocrEngine is not null, "FAILED", exception.GetType().Name + ":" + exception.Message, null);
            return 2;
        }
    }

    private static void WriteMissingImage(string outputPath, string imagePath)
    {
        WindowsRuntimeDiagnostics.WriteJson(outputPath, new OfflineVisionDiagnosticReport(
            1,
            DateTimeOffset.UtcNow,
            imagePath,
            0,
            0,
            0,
            false,
            string.Empty,
            "none",
            false,
            "NOT_RUN",
            "IMAGE_NOT_FOUND",
            false,
            null,
            null,
            null));
    }

    internal static Mat NormalizeClientImage(Mat source)
    {
        int left = 0;
        while (left < source.Width && IsDarkColumn(source, left)) left++;
        int right = source.Width - 1;
        while (right >= left && IsDarkColumn(source, right)) right--;
        int contentWidth = right - left + 1;
        bool meaningfulCrop = left + (source.Width - right - 1) >= source.Width * 0.02;
        if (!meaningfulCrop || contentWidth < 800) return source.Clone();
        return new Mat(source, new Rect(left, 0, contentWidth, source.Height)).Clone();
    }

    private static bool IsDarkColumn(Mat source, int x)
    {
        Scalar mean = Cv2.Mean(source.Col(x));
        return Math.Max(mean.Val0, Math.Max(mean.Val1, mean.Val2)) < 25;
    }

    private static Mat ConvertToBgra(Mat source)
    {
        var bgra = new Mat();
        switch (source.Channels())
        {
            case 4:
                source.CopyTo(bgra);
                break;
            case 3:
                Cv2.CvtColor(source, bgra, ColorConversionCodes.BGR2BGRA);
                break;
            case 1:
                Cv2.CvtColor(source, bgra, ColorConversionCodes.GRAY2BGRA);
                break;
            default:
                bgra.Dispose();
                throw new InvalidDataException("IMAGE_CHANNELS_UNSUPPORTED");
        }
        return bgra;
    }

    private static CapturedFrame CreateFrame(Mat bgra, long frameId)
    {
        int stride = checked(bgra.Width * 4);
        int length = checked(stride * bgra.Height);
        byte[] pixels = new byte[length];
        for (int y = 0; y < bgra.Height; y++)
            Marshal.Copy(bgra.Ptr(y), pixels, y * stride, stride);

        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        pixels.CopyTo(owner.Memory.Span);
        long capturedAtMonoMs = Environment.TickCount64;
        return new CapturedFrame(
            new CaptureFrameMetadata
            {
                SchemaVersion = ContractConstants.SchemaVersion,
                FrameId = frameId,
                CapturedAtMonoMs = capturedAtMonoMs,
                ClientWidth = bgra.Width,
                ClientHeight = bgra.Height,
                Dpi = 96,
                CaptureBackend = CaptureBackend.BitBlt,
                CaptureDurationMs = 0,
                DroppedReason = DroppedFrameReason.None,
            },
            bgra.Width,
            bgra.Height,
            stride,
            CapturedPixelFormat.Bgra32,
            owner,
            length);
    }

    private static void Write(
        string outputPath,
        string imagePath,
        int width,
        int height,
        double elapsedMs,
        VisionRuntimeBootstrapResult bootstrap,
        bool ocrReady,
        string pipelineStatus,
        string diagnostic,
        VisionPipelineResult? result)
    {
        WindowsRuntimeDiagnostics.WriteJson(outputPath, new OfflineVisionDiagnosticReport(
            1,
            DateTimeOffset.UtcNow,
            imagePath,
            width,
            height,
            elapsedMs,
            bootstrap.Ready,
            bootstrap.ModelId,
            RuntimeTelemetryCollector.ProviderLabel(bootstrap.Provider),
            ocrReady,
            pipelineStatus,
            diagnostic,
            result?.Dynamic?.CanDriveActions == true && result.Observation is not null,
            result?.FixedUi,
            result?.Dynamic,
            result?.Observation));
    }
}
