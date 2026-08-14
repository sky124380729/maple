using Maple.Contracts;

namespace Maple.Runtime;

public interface IActionExecutor
{
    ValueTask KeyDownAsync(AbstractAction action, CancellationToken cancellationToken);
    ValueTask KeyUpAsync(AbstractAction action, CancellationToken cancellationToken);
    ValueTask ReleaseAllAsync(CancellationToken cancellationToken);
}
