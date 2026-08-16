using Maple.Vision;

namespace Maple.Host;

public sealed record OcrRuntimeSelection(IOcrEngine? Engine, IOcrEngine? ResourceEngine, string Provider);

public static class OcrRuntime
{
    public static OcrRuntimeSelection TryCreate()
    {
        WindowsOcrEngine? windows = WindowsOcrEngine.TryCreate();
        IOcrEngine? resource = null;
        string provider = windows is null ? "none" : "windowsMediaOcr";
        string tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata");
        try
        {
            if (File.Exists(Path.Combine(tessdata, "eng.traineddata")))
            {
                resource = new TesseractOcrEngine(tessdata, "eng");
                provider = windows is null ? "tesseract-eng-resources" : "windowsMediaOcr+tesseract-eng-resources";
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }

        return new OcrRuntimeSelection(windows ?? resource, resource ?? windows, provider);
    }
}
