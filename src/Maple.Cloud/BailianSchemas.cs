using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Maple.Contracts;

namespace Maple.Cloud
{
    [DataContract]
    public sealed class MapAnnotationRequest
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "mapId", IsRequired = true)] public string MapId { get; set; }
        [DataMember(Name = "sourceFrameIds", IsRequired = true)] public List<long> SourceFrameIds { get; set; }
        [DataMember(Name = "cloudUploadApproved", IsRequired = true)] public bool CloudUploadApproved { get; set; }
    }

    [DataContract]
    public sealed class InitialMapAnnotation
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "coordinateSystem", IsRequired = true)] public string CoordinateSystem { get; set; }
        [DataMember(Name = "sourceFrameIds", IsRequired = true)] public List<long> SourceFrameIds { get; set; }
        [DataMember(Name = "platforms", IsRequired = true)] public List<MapAnnotationPlatform> Platforms { get; set; }
        [DataMember(Name = "ladders", IsRequired = true)] public List<MapAnnotationLadder> Ladders { get; set; }
        [DataMember(Name = "boundaries", IsRequired = true)] public List<MapAnnotationBoundary> Boundaries { get; set; }
        [DataMember(Name = "connections", IsRequired = true)] public List<MapAnnotationConnection> Connections { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "coverage", IsRequired = true)] public double Coverage { get; set; }
        [DataMember(Name = "calibrationErrorPx", IsRequired = true)] public double CalibrationErrorPx { get; set; }
    }

    [DataContract] public sealed class MapAnnotationPlatform { [DataMember(Name = "platformId", IsRequired = true)] public string PlatformId { get; set; } [DataMember(Name = "x1", IsRequired = true)] public double X1 { get; set; } [DataMember(Name = "x2", IsRequired = true)] public double X2 { get; set; } [DataMember(Name = "y", IsRequired = true)] public double Y { get; set; } [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; } }
    [DataContract] public sealed class MapAnnotationLadder { [DataMember(Name = "ladderId", IsRequired = true)] public string LadderId { get; set; } [DataMember(Name = "fromPlatformId", IsRequired = true)] public string FromPlatformId { get; set; } [DataMember(Name = "toPlatformId", IsRequired = true)] public string ToPlatformId { get; set; } [DataMember(Name = "x", IsRequired = true)] public double X { get; set; } [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; } }
    [DataContract] public sealed class MapAnnotationBoundary { [DataMember(Name = "boundaryId", IsRequired = true)] public string BoundaryId { get; set; } [DataMember(Name = "platformId", IsRequired = true)] public string PlatformId { get; set; } [DataMember(Name = "x", IsRequired = true)] public double X { get; set; } [DataMember(Name = "kind", IsRequired = true)] public string Kind { get; set; } }
    [DataContract] public sealed class MapAnnotationConnection { [DataMember(Name = "connectionId", IsRequired = true)] public string ConnectionId { get; set; } [DataMember(Name = "fromPlatformId", IsRequired = true)] public string FromPlatformId { get; set; } [DataMember(Name = "toPlatformId", IsRequired = true)] public string ToPlatformId { get; set; } [DataMember(Name = "type", IsRequired = true)] public string Type { get; set; } }

    public sealed class BailianSchemaValidationResult
    {
        public bool IsValid { get; internal set; }
        public string Error { get; internal set; }
    }

    public static class BailianSchemaValidation
    {
        public static BailianSchemaValidationResult Validate(InitialMapAnnotation annotation)
        {
            if (annotation == null) return Invalid("annotation");
            if (annotation.SchemaVersion != ContractConstants.SchemaVersion) return Invalid("schemaVersion");
            if (annotation.CoordinateSystem != "mapworld-px" && annotation.CoordinateSystem != "client-normalized") return Invalid("coordinateSystem");
            if (annotation.SourceFrameIds == null || annotation.SourceFrameIds.Count == 0) return Invalid("sourceFrameIds");
            if (annotation.Platforms == null || annotation.Platforms.Count == 0) return Invalid("platforms");
            if (annotation.Ladders == null || annotation.Boundaries == null || annotation.Connections == null) return Invalid("collections");
            if (!Unit(annotation.Confidence) || !Unit(annotation.Coverage) || annotation.CalibrationErrorPx < 0) return Invalid("metrics");
            foreach (MapAnnotationPlatform platform in annotation.Platforms)
            {
                if (platform == null || string.IsNullOrWhiteSpace(platform.PlatformId) || platform.X2 <= platform.X1 || !Unit(platform.Confidence)) return Invalid("platform");
            }
            return new BailianSchemaValidationResult { IsValid = true };
        }

        private static bool Unit(double value) { return value >= 0 && value <= 1; }
        private static BailianSchemaValidationResult Invalid(string error) { return new BailianSchemaValidationResult { IsValid = false, Error = error }; }
    }

    public enum BailianMapStatus { Success, Timeout, MalformedResponse, Offline, UploadNotApproved, InvalidRequest, CredentialMissing, AuthRejected, ModelUnavailable, RateLimited, ServiceUnavailable }
    public sealed class BailianMapResult { public BailianMapStatus Status { get; internal set; } public InitialMapAnnotation Annotation { get; internal set; } public string Message { get; internal set; } }
}
