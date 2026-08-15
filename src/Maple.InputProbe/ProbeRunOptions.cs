using System;
using System.IO;

namespace Maple.InputProbe;

internal sealed class ProbeRunOptions
{
    public int HoldMs { get; init; } = 500;
    public int CountdownSeconds { get; init; } = 3;
    public int BetweenActionsMs { get; init; } = 3000;
    public string OutputRoot { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Maple", "input-probe");
}

internal sealed class ProbeRunResult
{
    public string SessionDirectory { get; init; } = "";
    public string EvidencePath { get; init; } = "";
    public bool AllKeysReleased { get; init; }
}

