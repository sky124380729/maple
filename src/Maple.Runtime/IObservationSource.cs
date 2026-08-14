using Maple.Contracts;
using Maple.Core;

namespace Maple.Runtime;

public sealed record RuntimeObservationContext(
    ObservationSnapshot Snapshot,
    PlatformContext Platform,
    bool TargetBound,
    bool IsForeground,
    bool FrameFresh,
    bool HpHealthy,
    bool MpHealthy,
    bool InputAdapterHealthy,
    bool EmergencyStop);

public interface IObservationSource
{
    ValueTask<RuntimeObservationContext> ReadNextAsync(CancellationToken cancellationToken);
}
