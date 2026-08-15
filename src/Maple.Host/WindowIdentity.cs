using System.Text.Json.Serialization;

namespace Maple.Host;

public sealed record WindowCandidate(
    nint Hwnd,
    int Pid,
    string Title,
    string ClassName,
    bool IsVisible,
    bool IsMinimized,
    bool IsForeground,
    int ClientLeft,
    int ClientTop,
    int ClientWidth,
    int ClientHeight,
    int Dpi,
    DateTimeOffset ProcessStartedAtUtc,
    string ProcessPath,
    string ProcessVersion);

public sealed record WindowIdentity(
    string Hwnd,
    int Pid,
    string Title,
    string ClassName,
    bool IsForeground,
    bool IsMinimized,
    int ClientLeft,
    int ClientTop,
    int ClientWidth,
    int ClientHeight,
    int Dpi,
    DateTimeOffset ProcessStartedAtUtc,
    string ProcessPathSha256,
    string ProcessVersion,
    [property: JsonIgnore] string ProcessPath = "");

public enum TargetWindowDiscoveryStatus
{
    NotFound,
    Found,
    SelectionRequired,
}

public sealed record TargetWindowDiscoveryResult(
    TargetWindowDiscoveryStatus Status,
    string DiagnosticCode,
    IReadOnlyList<WindowIdentity> Candidates)
{
    public WindowIdentity? Target => Status == TargetWindowDiscoveryStatus.Found && Candidates.Count == 1
        ? Candidates[0]
        : null;
}
