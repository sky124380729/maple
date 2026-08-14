namespace Maple.Cloud;

public interface IBailianMapClient
{
    Task<BailianMapResult> AnnotateAsync(
        MapAnnotationRequest request,
        IReadOnlyList<BailianMapImage> images,
        string modelId,
        CancellationToken cancellationToken);
}
