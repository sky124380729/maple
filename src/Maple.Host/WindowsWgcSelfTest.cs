using System.Drawing;
using Maple.Capture;

namespace Maple.Host;

public sealed record WindowsWgcSelfTestReport(
    bool Success,
    string Code,
    int Width,
    int Height,
    string Backend,
    double CaptureDurationMs,
    bool NonBlack,
    bool MapFramePng,
    int MapFrameBytes,
    string Detail);

public static class WindowsWgcSelfTest
{
    public static async Task<WindowsWgcSelfTestReport> RunAsync(CancellationToken cancellationToken)
    {
        using var targetWindow = new Form
        {
            Text = "Maple WGC Self Test",
            ClientSize = new Size(640, 360),
            BackColor = Color.FromArgb(32, 146, 118),
            StartPosition = FormStartPosition.CenterScreen,
        };
        targetWindow.Show();
        Application.DoEvents();
        Point clientOrigin = targetWindow.PointToScreen(Point.Empty);
        var target = new CaptureTarget
        {
            Hwnd = $"0x{targetWindow.Handle.ToInt64():X16}",
            Pid = Environment.ProcessId,
            ClientLeft = clientOrigin.X,
            ClientTop = clientOrigin.Y,
            ClientWidth = targetWindow.ClientSize.Width,
            ClientHeight = targetWindow.ClientSize.Height,
            Dpi = targetWindow.DeviceDpi,
            IsForeground = true,
            IsMinimized = false,
        };

        using var source = new WindowsWgcFrameSource();
        for (long frameId = 1; frameId <= 20; frameId++)
        {
            CapturedFrame? frame = await source
                .TryCaptureAsync(target, frameId, Environment.TickCount64, cancellationToken)
                .ConfigureAwait(true);
            Application.DoEvents();
            if (frame is null) continue;
            using (frame)
            {
                bool nonBlack = HasVisibleRgb(frame.Pixels.Span);
                using var mapFrames = new MapScanFrameStore(new WindowsPngMapFrameEncoder(), minimumFrameIntervalMs: 0, capacity: 1);
                mapFrames.StartScan();
                mapFrames.Observe(frame);
                IReadOnlyList<Maple.Cloud.BailianMapImage> images = await mapFrames
                    .ReadAsync("wgc-self-test", [frame.Metadata.FrameId], cancellationToken)
                    .ConfigureAwait(true);
                bool mapFramePng = IsPng(images[0].Bytes);
                bool success = nonBlack && mapFramePng;
                return new WindowsWgcSelfTestReport(
                    success,
                    success ? "WGC_SELF_TEST_PASS" : nonBlack ? "WGC_SELF_TEST_MAP_FRAME_INVALID" : "WGC_SELF_TEST_BLACK",
                    frame.Width,
                    frame.Height,
                    frame.Metadata.CaptureBackend.ToString(),
                    frame.Metadata.CaptureDurationMs,
                    nonBlack,
                    mapFramePng,
                    images[0].Bytes.Length,
                    source.Status);
            }
        }
        return new WindowsWgcSelfTestReport(false, "WGC_SELF_TEST_NO_FRAME", 0, 0, "Wgc", 0, false, false, 0, source.Status);
    }

    private static bool HasVisibleRgb(ReadOnlySpan<byte> bgra)
    {
        for (int offset = 0; offset + 2 < bgra.Length; offset += 4)
        {
            if (bgra[offset] > 2 || bgra[offset + 1] > 2 || bgra[offset + 2] > 2) return true;
        }
        return false;
    }

    private static bool IsPng(ReadOnlyMemory<byte> image)
    {
        ReadOnlySpan<byte> bytes = image.Span;
        return bytes.Length >= 8
            && bytes[0] == 137
            && bytes[1] == 80
            && bytes[2] == 78
            && bytes[3] == 71
            && bytes[4] == 13
            && bytes[5] == 10
            && bytes[6] == 26
            && bytes[7] == 10;
    }
}
