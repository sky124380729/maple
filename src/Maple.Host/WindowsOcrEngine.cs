using Maple.Vision;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Maple.Host;

public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine engine;
    private readonly SemaphoreSlim gate = new(1, 1);

    private WindowsOcrEngine(OcrEngine engine) => this.engine = engine;

    public static WindowsOcrEngine? TryCreate()
    {
        try
        {
            var simplifiedChinese = new Language("zh-Hans");
            OcrEngine? selected = OcrEngine.IsLanguageSupported(simplifiedChinese)
                ? OcrEngine.TryCreateFromLanguage(simplifiedChinese)
                : OcrEngine.TryCreateFromUserProfileLanguages();
            return selected is null ? null : new WindowsOcrEngine(selected);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    public async ValueTask<string> RecognizeAsync(ReadOnlyMemory<byte> encodedPng, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (encodedPng.IsEmpty) return string.Empty;

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(encodedPng.ToArray());
                await writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
                writer.DetachStream();
            }
            stream.Seek(0);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
            using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied).AsTask(cancellationToken).ConfigureAwait(false);
            OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
            return result.Text ?? string.Empty;
        }
        finally
        {
            gate.Release();
        }
    }
}
