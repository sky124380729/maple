using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Contracts;

namespace Maple.Host;

public static class CombatRhythmMessageSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(CombatRhythmSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(new
        {
            schemaVersion = ContractConstants.SchemaVersion,
            type = "combat.rhythm.updated",
            payload = snapshot
        }, JsonOptions);
    }
}
