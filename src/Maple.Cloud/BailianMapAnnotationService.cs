#nullable enable

namespace Maple.Cloud;

public interface IMapImageSource
{
    ValueTask<IReadOnlyList<BailianMapImage>> ReadAsync(
        string mapId,
        IReadOnlyList<long> frameIds,
        CancellationToken cancellationToken);
}

public sealed class BailianMapAnnotationService(IBailianMapClient client, IMapImageSource imageSource)
{
    private readonly IBailianMapClient client = client ?? throw new ArgumentNullException(nameof(client));
    private readonly IMapImageSource imageSource = imageSource ?? throw new ArgumentNullException(nameof(imageSource));

    public async Task<BailianMapResult> AnnotateAsync(
        MapAnnotationRequest request,
        string modelId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<BailianMapImage> images = await imageSource
            .ReadAsync(request.MapId, request.SourceFrameIds, cancellationToken)
            .ConfigureAwait(false);
        return await client.AnnotateAsync(request, images, modelId, cancellationToken).ConfigureAwait(false);
    }
}
