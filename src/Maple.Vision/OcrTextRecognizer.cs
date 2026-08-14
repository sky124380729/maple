using OpenCvSharp;
using Maple.Capture;

namespace Maple.Vision;

public interface IOcrEngine
{
    ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken);
}

public sealed class OcrTextRecognizer(IOcrEngine engine)
{
    private readonly IOcrEngine engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public async ValueTask<string> RecognizeAsync(CapturedFrame frame, PixelRegion region, CancellationToken cancellationToken)
    {
        using Mat source = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, frame.Pixels.ToArray(), frame.Stride);
        using Mat roi = new(source, new Rect(region.X, region.Y, region.Width, region.Height));
        using Mat gray = new();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGRA2GRAY);
        Cv2.Threshold(gray, gray, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        Cv2.ImEncode(".png", gray, out byte[] encoded);
        return (await engine.RecognizeAsync(encoded, cancellationToken).ConfigureAwait(false)).Trim();
    }
}

public sealed class TesseractOcrEngine : IOcrEngine, IDisposable
{
    private readonly Tesseract.TesseractEngine engine;
    private readonly object gate = new();

    public TesseractOcrEngine(string dataPath, string language = "chi_sim+eng")
    {
        if (!Directory.Exists(dataPath)) throw new DirectoryNotFoundException($"OCR 数据目录不存在: {dataPath}");
        engine = new Tesseract.TesseractEngine(dataPath, language, Tesseract.EngineMode.LstmOnly);
    }

    public ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            using var pix = Tesseract.Pix.LoadFromMemory(encodedPng.ToArray());
            using Tesseract.Page page = engine.Process(pix, Tesseract.PageSegMode.SingleLine);
            return ValueTask.FromResult(page.GetText());
        }
    }

    public void Dispose() => engine.Dispose();
}
