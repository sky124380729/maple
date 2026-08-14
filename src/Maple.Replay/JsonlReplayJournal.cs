#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;
using Maple.Runtime;

namespace Maple.Replay;

public sealed class JsonlReplayJournal : IRuntimeJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TextWriter writer;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public JsonlReplayJournal(TextWriter writer)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async ValueTask WriteAsync(RuntimeJournalEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        string json = JsonSerializer.Serialize(entry, JsonOptions);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
