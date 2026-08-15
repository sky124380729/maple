using System;
using System.IO;
using System.Text.Json;

namespace Maple.InputProbe;

internal sealed class ProbeLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        string line = JsonSerializer.Serialize(evidence, JsonOptions);
        lock (sync)
        {
            File.AppendAllText(jsonlPath, line + Environment.NewLine);
        }
    }
}

