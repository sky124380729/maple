using System.Text.Json;
using Maple.Contracts;
using Maple.Host;
using Xunit;

namespace Maple.Host.Tests;

public sealed class CombatRhythmMessageSerializerTests
{
    [Fact]
    public void SerializesTheVersionedCombatRhythmEnvelope()
    {
        string json = CombatRhythmMessageSerializer.Serialize(new CombatRhythmSnapshot
        {
            SchemaVersion = ContractConstants.SchemaVersion,
            CycleId = 7,
            Phase = CombatRhythmPhase.AttackHolding,
            SampledDurationMs = 26_430,
            RemainingMs = 18_620,
            UpdatedAtMonoMs = 120_000
        });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement payload = root.GetProperty("payload");

        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("combat.rhythm.updated", root.GetProperty("type").GetString());
        Assert.Equal("attackHolding", payload.GetProperty("phase").GetString());
        Assert.Equal(26_430, payload.GetProperty("sampledDurationMs").GetInt32());
        Assert.Equal(18_620, payload.GetProperty("remainingMs").GetInt32());
    }
}
