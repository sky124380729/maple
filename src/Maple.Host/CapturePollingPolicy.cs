namespace Maple.Host;

public static class CapturePollingPolicy
{
    public const int ActiveIntervalMs = 33;
    public const int PausedIntervalMs = 1000;

    public static int NextIntervalMs(CaptureTickResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Success ? ActiveIntervalMs : PausedIntervalMs;
    }
}
