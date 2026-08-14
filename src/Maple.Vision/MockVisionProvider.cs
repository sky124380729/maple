using Maple.Capture;

namespace Maple.Vision;

public sealed class MockVisionProvider : IDynamicVisionProvider, IFixedUiVisionProvider
{
    private readonly Queue<DynamicVisionResult> dynamicResults = new();
    private readonly Queue<FixedUiVisionResult> fixedResults = new();

    public void Enqueue(DynamicVisionResult dynamicResult, FixedUiVisionResult fixedResult)
    {
        ArgumentNullException.ThrowIfNull(dynamicResult);
        ArgumentNullException.ThrowIfNull(fixedResult);
        dynamicResults.Enqueue(dynamicResult);
        fixedResults.Enqueue(fixedResult);
    }

    public ValueTask<DynamicVisionResult> ObserveDynamicAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dynamicResults.Count == 0) throw new InvalidOperationException("模拟动态视觉结果已耗尽");
        return ValueTask.FromResult(dynamicResults.Dequeue());
    }

    public ValueTask<FixedUiVisionResult> ObserveFixedUiAsync(CapturedFrame frame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fixedResults.Count == 0) throw new InvalidOperationException("模拟固定 UI 视觉结果已耗尽");
        return ValueTask.FromResult(fixedResults.Dequeue());
    }
}
