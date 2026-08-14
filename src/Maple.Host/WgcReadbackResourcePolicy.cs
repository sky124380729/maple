namespace Maple.Host;

public static class WgcReadbackResourcePolicy
{
    public static bool ShouldRecreate(
        bool exists,
        int currentWidth,
        int currentHeight,
        int currentFormat,
        int requestedWidth,
        int requestedHeight,
        int requestedFormat) =>
        !exists
        || currentWidth != requestedWidth
        || currentHeight != requestedHeight
        || currentFormat != requestedFormat;
}
