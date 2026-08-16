using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Maple.Contracts
{
    public static class ContractConstants
    {
        public const int SchemaVersion = 2;
        public const long MaxObservationTtlMs = 5000;
        public const int MaxActionDurationMs = 5000;
    }

    [DataContract]
    public sealed class TargetBinding
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "hwnd", IsRequired = true)] public string Hwnd { get; set; }
        [DataMember(Name = "pid", IsRequired = true)] public int Pid { get; set; }
        [DataMember(Name = "startedAtUtc", EmitDefaultValue = false)] public DateTimeOffset? StartedAtUtc { get; set; }
        [DataMember(Name = "executablePath", EmitDefaultValue = false)] public string ExecutablePath { get; set; }
        [DataMember(Name = "clientWidth", IsRequired = true)] public int ClientWidth { get; set; }
        [DataMember(Name = "clientHeight", IsRequired = true)] public int ClientHeight { get; set; }
        [DataMember(Name = "dpi", IsRequired = true)] public int Dpi { get; set; }
    }

    [DataContract]
    public sealed class CaptureFrameMetadata
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "frameId", IsRequired = true)] public long FrameId { get; set; }
        [DataMember(Name = "capturedAtMonoMs", IsRequired = true)] public long CapturedAtMonoMs { get; set; }
        [DataMember(Name = "clientWidth", IsRequired = true)] public int ClientWidth { get; set; }
        [DataMember(Name = "clientHeight", IsRequired = true)] public int ClientHeight { get; set; }
        [DataMember(Name = "dpi", IsRequired = true)] public int Dpi { get; set; }
        [DataMember(Name = "captureBackend", IsRequired = true)] public CaptureBackend CaptureBackend { get; set; }
        [DataMember(Name = "captureDurationMs", IsRequired = true)] public double CaptureDurationMs { get; set; }
        [DataMember(Name = "droppedReason", IsRequired = true)] public DroppedFrameReason DroppedReason { get; set; }
    }

    [DataContract]
    public sealed class SelfObservation
    {
        [DataMember(Name = "box", IsRequired = true)] public double[] Box { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "freshUntilMonoMs", IsRequired = true)] public long FreshUntilMonoMs { get; set; }
    }

    [DataContract]
    public sealed class PlayerObservation
    {
        [DataMember(Name = "box", IsRequired = true)] public double[] Box { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "freshUntilMonoMs", IsRequired = true)] public long FreshUntilMonoMs { get; set; }
        [DataMember(Name = "trackId", IsRequired = true)] public string TrackId { get; set; }
    }

    [DataContract]
    public sealed class MonsterObservation
    {
        [DataMember(Name = "class", IsRequired = true)] public string Class { get; set; }
        [DataMember(Name = "box", IsRequired = true)] public double[] Box { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "freshUntilMonoMs", IsRequired = true)] public long FreshUntilMonoMs { get; set; }
        [DataMember(Name = "targetId", IsRequired = true)] public string TargetId { get; set; }
    }

    [DataContract]
    public sealed class LootObservation
    {
        [DataMember(Name = "visible", IsRequired = true)] public bool Visible { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "freshUntilMonoMs", IsRequired = true)] public long FreshUntilMonoMs { get; set; }
    }

    [DataContract]
    public sealed class ResourceObservation
    {
        [DataMember(Name = "mode", IsRequired = true)] public ResourceMode Mode { get; set; }
        [DataMember(Name = "value", IsRequired = true)] public double Value { get; set; }
        [DataMember(Name = "currentValue", EmitDefaultValue = false)] public double? CurrentValue { get; set; }
        [DataMember(Name = "maximumValue", EmitDefaultValue = false)] public double? MaximumValue { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "freshUntilMonoMs", IsRequired = true)] public long FreshUntilMonoMs { get; set; }
    }

    [DataContract]
    public sealed class MapObservation
    {
        [DataMember(Name = "mapId", IsRequired = true)] public string MapId { get; set; }
        [DataMember(Name = "state", IsRequired = true)] public MapArchiveState State { get; set; }
        [DataMember(Name = "confidence", IsRequired = true)] public double Confidence { get; set; }
        [DataMember(Name = "freshUntilMonoMs", IsRequired = true)] public long FreshUntilMonoMs { get; set; }
    }

    [DataContract]
    public sealed class ObservationSnapshot
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "frameId", IsRequired = true)] public long FrameId { get; set; }
        [DataMember(Name = "capturedAtMonoMs", IsRequired = true)] public long CapturedAtMonoMs { get; set; }
        [DataMember(Name = "target", IsRequired = true)] public TargetBinding Target { get; set; }
        [DataMember(Name = "self", IsRequired = true)] public SelfObservation Self { get; set; }
        [DataMember(Name = "players", IsRequired = true)] public List<PlayerObservation> Players { get; set; }
        [DataMember(Name = "monsters", IsRequired = true)] public List<MonsterObservation> Monsters { get; set; }
        [DataMember(Name = "loot", IsRequired = true)] public LootObservation Loot { get; set; }
        [DataMember(Name = "hp", IsRequired = true)] public ResourceObservation Hp { get; set; }
        [DataMember(Name = "mp", IsRequired = true)] public ResourceObservation Mp { get; set; }
        [DataMember(Name = "map", IsRequired = true)] public MapObservation Map { get; set; }
        [DataMember(Name = "state", IsRequired = true)] public SessionState State { get; set; }
    }

    [DataContract]
    public sealed class OverlaySnapshot
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "frameId", IsRequired = true)] public long FrameId { get; set; }
        [DataMember(Name = "generatedAtMonoMs", IsRequired = true)] public long GeneratedAtMonoMs { get; set; }
        [DataMember(Name = "self", EmitDefaultValue = false)] public SelfObservation Self { get; set; }
        [DataMember(Name = "players", IsRequired = true)] public List<PlayerObservation> Players { get; set; }
        [DataMember(Name = "monsters", IsRequired = true)] public List<MonsterObservation> Monsters { get; set; }
        [DataMember(Name = "selectedTargetId", EmitDefaultValue = false)] public string SelectedTargetId { get; set; }
        [DataMember(Name = "modelVersion", EmitDefaultValue = false)] public string ModelVersion { get; set; }
    }

    [DataContract]
    public sealed class TelemetrySnapshot
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "timestamp", IsRequired = true)] public DateTimeOffset Timestamp { get; set; }
        [DataMember(Name = "captureFps", IsRequired = true)] public double CaptureFps { get; set; }
        [DataMember(Name = "renderFps", IsRequired = true)] public double RenderFps { get; set; }
        [DataMember(Name = "recognitionFps", IsRequired = true)] public double RecognitionFps { get; set; }
        [DataMember(Name = "frameLatencyMs", IsRequired = true)] public double FrameLatencyMs { get; set; }
        [DataMember(Name = "detectorLatencyMs", IsRequired = true)] public double DetectorLatencyMs { get; set; }
        [DataMember(Name = "droppedFrames", IsRequired = true)] public long DroppedFrames { get; set; }
        [DataMember(Name = "queueAgeMs", IsRequired = true)] public double QueueAgeMs { get; set; }
        [DataMember(Name = "processMemoryMb", IsRequired = true)] public double ProcessMemoryMb { get; set; }
        [DataMember(Name = "inferenceProvider", IsRequired = true)] public InferenceProvider InferenceProvider { get; set; }
        [DataMember(Name = "captureBackend", IsRequired = true)] public CaptureBackend CaptureBackend { get; set; }
        [DataMember(Name = "lastAction", IsRequired = true)] public string LastAction { get; set; }
        [DataMember(Name = "warningCode", IsRequired = true)] public string WarningCode { get; set; }
        [DataMember(Name = "state", IsRequired = true)] public SessionState State { get; set; }
        [DataMember(Name = "pauseReason", IsRequired = true)] public PauseReason PauseReason { get; set; }
    }

    [DataContract]
    public sealed class AbstractAction
    {
        [DataMember(Name = "actionId", IsRequired = true)] public string ActionId { get; set; }
        [DataMember(Name = "type", IsRequired = true)] public ActionType Type { get; set; }
        [DataMember(Name = "profileId", EmitDefaultValue = false)] public ActionProfileId? ProfileId { get; set; }
        [DataMember(Name = "issuedAtMonoMs", IsRequired = true)] public long IssuedAtMonoMs { get; set; }
        [DataMember(Name = "holdMs", IsRequired = true)] public int HoldMs { get; set; }
        [DataMember(Name = "maxDurationMs", IsRequired = true)] public int MaxDurationMs { get; set; }
    }

    [DataContract]
    public sealed class ActionPlan
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "planId", IsRequired = true)] public string PlanId { get; set; }
        [DataMember(Name = "createdAtMonoMs", IsRequired = true)] public long CreatedAtMonoMs { get; set; }
        [DataMember(Name = "actions", IsRequired = true)] public List<AbstractAction> Actions { get; set; }
    }

    [DataContract]
    public sealed class InputResult
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "actionId", IsRequired = true)] public string ActionId { get; set; }
        [DataMember(Name = "status", IsRequired = true)] public InputStatus Status { get; set; }
        [DataMember(Name = "startedAtMonoMs", EmitDefaultValue = false)] public long? StartedAtMonoMs { get; set; }
        [DataMember(Name = "endedAtMonoMs", EmitDefaultValue = false)] public long? EndedAtMonoMs { get; set; }
        [DataMember(Name = "releasedKeys", IsRequired = true)] public List<string> ReleasedKeys { get; set; }
        [DataMember(Name = "message", EmitDefaultValue = false)] public string Message { get; set; }
    }

    [DataContract]
    public sealed class MapRuntimeStatusPayload
    {
        [DataMember(Name = "mapId", IsRequired = true)] public string MapId { get; set; }
        [DataMember(Name = "state", IsRequired = true)] public MapArchiveState State { get; set; }
        [DataMember(Name = "coverage", IsRequired = true)] public double Coverage { get; set; }
        [DataMember(Name = "calibrationErrorPx", IsRequired = true)] public double CalibrationErrorPx { get; set; }
        [DataMember(Name = "platformCount", IsRequired = true)] public int PlatformCount { get; set; }
        [DataMember(Name = "ladderCount", IsRequired = true)] public int LadderCount { get; set; }
        [DataMember(Name = "errors", IsRequired = true)] public List<string> Errors { get; set; }
        [DataMember(Name = "canProduceActions", IsRequired = true)] public bool CanProduceActions { get; set; }
    }

    [DataContract]
    public sealed class MapScanStatusPayload
    {
        [DataMember(Name = "scanning", IsRequired = true)] public bool Scanning { get; set; }
        [DataMember(Name = "frameIds", IsRequired = true)] public List<long> FrameIds { get; set; }
    }

    [DataContract]
    public sealed class InputHotKeyBindings
    {
        [DataMember(Name = "pauseResume", IsRequired = true)] public string PauseResume { get; set; }
        [DataMember(Name = "emergencyStop", IsRequired = true)] public string EmergencyStop { get; set; }
    }

    [DataContract]
    public sealed class InputBrokerStatusPayload
    {
        [DataMember(Name = "provider", IsRequired = true)] public string Provider { get; set; }
        [DataMember(Name = "status", IsRequired = true)] public InputBrokerStatus Status { get; set; }
        [DataMember(Name = "integrity", IsRequired = true)] public InputBrokerIntegrity Integrity { get; set; }
        [DataMember(Name = "activeKeys", IsRequired = true)] public List<string> ActiveKeys { get; set; }
        [DataMember(Name = "lastReleaseSucceeded", IsRequired = true)] public bool LastReleaseSucceeded { get; set; }
        [DataMember(Name = "hotkeys", IsRequired = true)] public InputHotKeyBindings Hotkeys { get; set; }
        [DataMember(Name = "errorCode", IsRequired = true)] public string ErrorCode { get; set; }
    }

    [DataContract]
    public sealed class HostEvent
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "type", IsRequired = true)] public HostEventType Type { get; set; }
        [DataMember(Name = "timestamp", EmitDefaultValue = false)] public DateTimeOffset? Timestamp { get; set; }
        [DataMember(Name = "payload", IsRequired = true)] public object Payload { get; set; }
    }

    [DataContract]
    public sealed class UiCommand
    {
        [DataMember(Name = "schemaVersion", IsRequired = true)] public int SchemaVersion { get; set; }
        [DataMember(Name = "type", IsRequired = true)] public UiCommandType Type { get; set; }
        [DataMember(Name = "timestamp", EmitDefaultValue = false)] public DateTimeOffset? Timestamp { get; set; }
        [DataMember(Name = "payload", IsRequired = true)] public object Payload { get; set; }
    }

    [DataContract]
    public sealed class EmergencyStopPayload
    {
        [DataMember(Name = "message", IsRequired = true)] public string Message { get; set; }
    }

    [DataContract]
    public sealed class PreviewBoundsPayload
    {
        [DataMember(Name = "left", IsRequired = true)] public double Left { get; set; }
        [DataMember(Name = "top", IsRequired = true)] public double Top { get; set; }
        [DataMember(Name = "width", IsRequired = true)] public double Width { get; set; }
        [DataMember(Name = "height", IsRequired = true)] public double Height { get; set; }
        [DataMember(Name = "devicePixelRatio", IsRequired = true)] public double DevicePixelRatio { get; set; }
    }

    [DataContract]
    public sealed class VisionStatusPayload
    {
        [DataMember(Name = "status", IsRequired = true)] public VisionModelStatus Status { get; set; }
        [DataMember(Name = "modelId", IsRequired = true)] public string ModelId { get; set; }
        [DataMember(Name = "provider", IsRequired = true)] public InferenceProvider Provider { get; set; }
        [DataMember(Name = "diagnostic", IsRequired = true)] public string Diagnostic { get; set; }
    }

    [DataContract]
    public sealed class ContractValidationResult
    {
        private ContractValidationResult(bool isValid, string error)
        {
            IsValid = isValid;
            Error = error;
        }

        public bool IsValid { get; private set; }
        public string Error { get; private set; }
        public static ContractValidationResult Valid() { return new ContractValidationResult(true, null); }
        public static ContractValidationResult Invalid(string error) { return new ContractValidationResult(false, error); }
    }

    public static class ContractValidation
    {
        public static ContractValidationResult ValidateObservation(ObservationSnapshot observation)
        {
            if (observation == null || observation.SchemaVersion != ContractConstants.SchemaVersion) return ContractValidationResult.Invalid("schemaVersion");
            if (observation.Target == null || observation.Self == null) return ContractValidationResult.Invalid("target/self");
            if (!ValidBox(observation.Self.Box) || !ValidConfidence(observation.Self.Confidence) || !ValidFreshness(observation.Self.FreshUntilMonoMs, observation.CapturedAtMonoMs)) return ContractValidationResult.Invalid("self");
            if (observation.Players == null || observation.Monsters == null) return ContractValidationResult.Invalid("detections");
            if (observation.Players.Any(player => !ValidBox(player.Box) || !ValidConfidence(player.Confidence) || !ValidFreshness(player.FreshUntilMonoMs, observation.CapturedAtMonoMs) || string.IsNullOrWhiteSpace(player.TrackId))) return ContractValidationResult.Invalid("players");
            if (observation.Monsters.Any(monster => !ValidBox(monster.Box) || !ValidConfidence(monster.Confidence) || !ValidFreshness(monster.FreshUntilMonoMs, observation.CapturedAtMonoMs) || string.IsNullOrWhiteSpace(monster.TargetId))) return ContractValidationResult.Invalid("monsters");
            return ContractValidationResult.Valid();
        }

        public static ContractValidationResult ValidateAction(AbstractAction action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.ActionId)) return ContractValidationResult.Invalid("actionId");
            if (action.HoldMs < 0 || action.MaxDurationMs <= 0 || action.HoldMs > action.MaxDurationMs || action.MaxDurationMs > ContractConstants.MaxActionDurationMs) return ContractValidationResult.Invalid("duration");
            if (action.Type == ActionType.Attack && action.ProfileId != ActionProfileId.SingleAttack && action.ProfileId != ActionProfileId.AreaAttack) return ContractValidationResult.Invalid("attack.profileId");
            if (action.Type == ActionType.UsePotion && action.ProfileId != ActionProfileId.HpPotion && action.ProfileId != ActionProfileId.MpPotion) return ContractValidationResult.Invalid("potion.profileId");
            if (action.Type != ActionType.Attack && action.Type != ActionType.UsePotion && action.ProfileId.HasValue) return ContractValidationResult.Invalid("profileId");
            return ContractValidationResult.Valid();
        }

        public static ContractValidationResult ValidateUiCommand(UiCommand command)
        {
            if (command == null || command.SchemaVersion != ContractConstants.SchemaVersion) return ContractValidationResult.Invalid("schemaVersion");
            if (command.Type == UiCommandType.SessionEmergencyStop)
            {
                var payload = command.Payload as EmergencyStopPayload;
                if (payload == null || string.IsNullOrWhiteSpace(payload.Message) || payload.Message.Length > 200) return ContractValidationResult.Invalid("emergencyStop.message");
            }
            if (command.Type == UiCommandType.PreviewBoundsChanged)
            {
                var payload = command.Payload as PreviewBoundsPayload;
                if (payload == null
                    || !ValidFiniteRange(payload.Left, 0, 10000)
                    || !ValidFiniteRange(payload.Top, 0, 10000)
                    || !ValidFiniteRange(payload.Width, 320, 10000)
                    || !ValidFiniteRange(payload.Height, 180, 10000)
                    || !ValidFiniteRange(payload.DevicePixelRatio, 0.5, 4)) return ContractValidationResult.Invalid("preview.bounds");
            }
            return ContractValidationResult.Valid();
        }

        private static bool ValidConfidence(double value) { return value >= 0 && value <= 1; }
        private static bool ValidFiniteRange(double value, double minimum, double maximum) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= minimum && value <= maximum; }
        private static bool ValidFreshness(long freshUntil, long capturedAt) { return freshUntil >= capturedAt && freshUntil - capturedAt <= ContractConstants.MaxObservationTtlMs; }
        private static bool ValidBox(double[] box)
        {
            return box != null && box.Length == 4 && box.All(value => value >= 0 && value <= 1) && box[0] + box[2] <= 1 && box[1] + box[3] <= 1;
        }
    }

    [DataContract]
    public enum CaptureBackend { [EnumMember(Value = "WGC")] Wgc, [EnumMember(Value = "BitBlt")] BitBlt, [EnumMember(Value = "PrintWindow")] PrintWindow }
    [DataContract]
    public enum InferenceProvider
    {
        [EnumMember(Value = "none")] None,
        [EnumMember(Value = "cpu")] Cpu,
        [EnumMember(Value = "directml")] DirectMl,
        [EnumMember(Value = "cuda")] Cuda
    }
    [DataContract]
    public enum VisionModelStatus
    {
        [EnumMember(Value = "notConfigured")] NotConfigured,
        [EnumMember(Value = "inspecting")] Inspecting,
        [EnumMember(Value = "ready")] Ready,
        [EnumMember(Value = "repairing")] Repairing,
        [EnumMember(Value = "faulted")] Faulted
    }
    [DataContract]
    public enum DroppedFrameReason { [EnumMember(Value = "backpressure")] Backpressure, [EnumMember(Value = "occluded")] Occluded, [EnumMember(Value = "invalid")] Invalid, [EnumMember(Value = "none")] None }
    [DataContract]
    public enum ResourceMode { [EnumMember(Value = "percent")] Percent, [EnumMember(Value = "absolute")] Absolute }
    [DataContract]
    public enum MapArchiveState { [EnumMember(Value = "candidate")] Candidate, [EnumMember(Value = "validated")] Validated, [EnumMember(Value = "archived")] Archived }
    [DataContract]
    public enum SessionState { Stopped, Arming, Observing, MapScanning, MapCalibrating, Navigating, Attacking, Looting, UsingPotion, Paused, ManualIntervention, EmergencyStop }
    [DataContract]
    public enum PauseReason { None, CalibrationRequired, StaleFrame, TargetLost, WindowNotForeground, BlackFrame, MapNotValidated, InputUnavailable, HealthUnknown, UnknownPopup, WatchdogTimeout, OperatorRequested, SafetyViolation }
    [DataContract]
    public enum ActionType { MoveLeft, MoveRight, Jump, ClimbUp, ClimbDown, Attack, Pickup, UsePotion, Pause, Replan }
    [DataContract]
    public enum ActionProfileId
    {
        [EnumMember(Value = "singleAttack")] SingleAttack,
        [EnumMember(Value = "areaAttack")] AreaAttack,
        [EnumMember(Value = "hpPotion")] HpPotion,
        [EnumMember(Value = "mpPotion")] MpPotion
    }
    [DataContract]
    public enum InputStatus { Accepted, Rejected, Completed, Cancelled, Failed }
    [DataContract]
    public enum InputBrokerStatus
    {
        [EnumMember(Value = "disconnected")] Disconnected,
        [EnumMember(Value = "starting")] Starting,
        [EnumMember(Value = "ready")] Ready,
        [EnumMember(Value = "paused")] Paused,
        [EnumMember(Value = "faulted")] Faulted
    }
    [DataContract]
    public enum InputBrokerIntegrity
    {
        [EnumMember(Value = "unknown")] Unknown,
        [EnumMember(Value = "medium")] Medium,
        [EnumMember(Value = "high")] High
    }
    [DataContract]
    public enum HostEventType
    {
        [EnumMember(Value = "target.updated")] TargetUpdated,
        [EnumMember(Value = "capture.frameMetadata")] CaptureFrameMetadata,
        [EnumMember(Value = "overlay.updated")] OverlayUpdated,
        [EnumMember(Value = "observation.updated")] ObservationUpdated,
        [EnumMember(Value = "telemetry.updated")] TelemetryUpdated,
        [EnumMember(Value = "session.stateChanged")] SessionStateChanged,
        [EnumMember(Value = "input.result")] InputResult,
        [EnumMember(Value = "input.status.updated")] InputStatusUpdated,
        [EnumMember(Value = "log.appended")] LogAppended,
        [EnumMember(Value = "preview.availabilityChanged")] PreviewAvailabilityChanged,
        [EnumMember(Value = "cloud.status.updated")] CloudStatusUpdated,
        [EnumMember(Value = "vision.status.updated")] VisionStatusUpdated,
        [EnumMember(Value = "config.updated")] ConfigUpdated,
        [EnumMember(Value = "map.status.updated")] MapStatusUpdated,
        [EnumMember(Value = "map.scan.updated")] MapScanUpdated
    }
    [DataContract]
    public enum UiCommandType
    {
        [EnumMember(Value = "snapshot.request")] SnapshotRequest,
        [EnumMember(Value = "session.arm")] SessionArm,
        [EnumMember(Value = "session.pause")] SessionPause,
        [EnumMember(Value = "session.resume")] SessionResume,
        [EnumMember(Value = "session.emergencyStop")] SessionEmergencyStop,
        [EnumMember(Value = "combat.trial.start")] CombatTrialStart,
        [EnumMember(Value = "map.scan.start")] MapScanStart,
        [EnumMember(Value = "map.calibration.start")] MapCalibrationStart,
        [EnumMember(Value = "map.calibration.confirm")] MapCalibrationConfirm,
        [EnumMember(Value = "preview.boundsChanged")] PreviewBoundsChanged,
        [EnumMember(Value = "input.test")] InputTest,
        [EnumMember(Value = "config.update")] ConfigUpdate,
        [EnumMember(Value = "cloud.credential.set")] CloudCredentialSet,
        [EnumMember(Value = "cloud.credential.clear")] CloudCredentialClear,
        [EnumMember(Value = "cloud.config.update")] CloudConfigUpdate,
        [EnumMember(Value = "cloud.connection.test")] CloudConnectionTest,
        [EnumMember(Value = "cloud.map.annotate")] CloudMapAnnotate
    }
}
