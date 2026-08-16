using Maple.Host;
using OpenCvSharp;
using Xunit;

namespace Maple.Host.Tests;

public sealed class OfflineVisionDiagnosticsTests
{
    [Fact]
    public void Pure_black_pillarbox_is_removed_before_hud_layout()
    {
        using var image = new Mat(new Size(1200, 600), MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(image, new Rect(100, 0, 1000, 600), new Scalar(40, 80, 120), -1);

        using Mat normalized = OfflineVisionDiagnostics.NormalizeClientImage(image);

        Assert.Equal(1000, normalized.Width);
        Assert.Equal(600, normalized.Height);
    }

    [Fact]
    public async Task Missing_image_writes_fail_closed_report()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"maple-offline-vision-{Guid.NewGuid():N}");
        string output = Path.Combine(directory, "result.json");
        try
        {
            int exitCode = await OfflineVisionDiagnostics.RunAsync(
                Path.Combine(directory, "missing.bmp"), output, null, null, CancellationToken.None);

            Assert.Equal(2, exitCode);
            string json = File.ReadAllText(output);
            Assert.Contains("IMAGE_NOT_FOUND", json, StringComparison.Ordinal);
            Assert.Contains("\"canDriveActions\": false", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
