namespace Maple.Cloud
{
    public enum MockBailianMode { Success, Timeout, Malformed, Offline }

    public sealed class MockBailianMapClient : IBailianMapClient
    {
        private readonly MockBailianMode mode;
        private readonly InitialMapAnnotation response;

        public MockBailianMapClient(MockBailianMode mode, InitialMapAnnotation response)
        {
            this.mode = mode;
            this.response = response;
        }

        public BailianMapResult Annotate(MapAnnotationRequest request)
        {
            if (mode == MockBailianMode.Offline) return Result(BailianMapStatus.Offline, null, "离线模式，不调用云端");
            if (request == null || !request.CloudUploadApproved) return Result(BailianMapStatus.UploadNotApproved, null, "用户尚未明确批准云端图像上传");
            if (mode == MockBailianMode.Timeout) return Result(BailianMapStatus.Timeout, null, "模拟请求超时");
            if (mode == MockBailianMode.Malformed || !BailianSchemaValidation.Validate(response).IsValid) return Result(BailianMapStatus.MalformedResponse, null, "云端返回结构无效");
            return Result(BailianMapStatus.Success, response, "初始地图结构标注已返回，仍需本地验证");
        }

        public Task<BailianMapResult> AnnotateAsync(
            MapAnnotationRequest request,
            IReadOnlyList<BailianMapImage> images,
            string modelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Annotate(request));
        }

        private static BailianMapResult Result(BailianMapStatus status, InitialMapAnnotation annotation, string message)
        {
            return new BailianMapResult { Status = status, Annotation = annotation, Message = message };
        }
    }
}
