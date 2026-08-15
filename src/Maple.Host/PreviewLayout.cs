using System;
using System.Drawing;
using System.Text.Json;

namespace Maple.Host;

public readonly record struct PreviewBoundsIntent(
    double Left,
    double Top,
    double Width,
    double Height,
    double DevicePixelRatio);

public static class PreviewLayout
{
    public static bool IsPreviewBoundsCommand(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && string.Equals(type.GetString(), "preview.boundsChanged", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static Rectangle Resolve(PreviewBoundsIntent intent, Size browserClientSize)
    {
        if (browserClientSize.Width <= 0 || browserClientSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(browserClientSize));
        if (!IsFinite(intent.Left) || !IsFinite(intent.Top) || !IsFinite(intent.Width) || !IsFinite(intent.Height)
            || !IsFinite(intent.DevicePixelRatio)
            || intent.Left < 0 || intent.Top < 0 || intent.Width <= 0 || intent.Height <= 0
            || intent.DevicePixelRatio <= 0
            || intent.Left >= browserClientSize.Width || intent.Top >= browserClientSize.Height)
            throw new ArgumentOutOfRangeException(nameof(intent));

        int left = Math.Clamp((int)Math.Floor(intent.Left), 0, browserClientSize.Width - 1);
        int top = Math.Clamp((int)Math.Floor(intent.Top), 0, browserClientSize.Height - 1);
        int width = Math.Min((int)Math.Round(intent.Width), browserClientSize.Width - left);
        int height = Math.Min((int)Math.Round(intent.Height), browserClientSize.Height - top);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(intent));
        return new Rectangle(left, top, width, height);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
