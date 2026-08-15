using System.Text.Json;

namespace Maple.InputProbe;

public static class ProbeEvidenceJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(ProbeEvidence evidence)
    {
        return JsonSerializer.Serialize(evidence, Options);
    }
}
