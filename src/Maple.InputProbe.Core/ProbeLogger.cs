using System;
using System.IO;

namespace Maple.InputProbe;

public sealed class ProbeLogger
{
    private readonly object sync = new();
    private readonly string jsonlPath;

    public ProbeLogger(string rootDirectory, string sessionId)
    {
        SessionDirectory = Path.Combine(rootDirectory, sessionId);
        Directory.CreateDirectory(SessionDirectory);
        jsonlPath = Path.Combine(SessionDirectory, "probe-evidence.jsonl");
    }

    public string SessionDirectory { get; }
    public string JsonlPath => jsonlPath;

    public void Append(ProbeEvidence evidence)
    {
        string line = ProbeEvidenceJson.Serialize(evidence);
        lock (sync)
        {
            File.AppendAllText(jsonlPath, line + Environment.NewLine);
        }
    }
}
