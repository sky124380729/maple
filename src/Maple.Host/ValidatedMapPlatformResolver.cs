using Maple.Contracts;
using Maple.Core;
using Maple.Map;

namespace Maple.Host;

public sealed record PlatformResolution(bool Resolved, string Code, PlatformContext Context);

public sealed class ValidatedMapPlatformResolver(double verticalTolerancePx = 28)
{
    private readonly double verticalTolerancePx = verticalTolerancePx is >= 4 and <= 200
        ? verticalTolerancePx
        : throw new ArgumentOutOfRangeException(nameof(verticalTolerancePx));

    public PlatformResolution Resolve(ObservationSnapshot snapshot, MapWorld? world, CameraTransform? transform)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Map?.State != MapArchiveState.Validated || world is null || !world.CanProduceActions)
            return Unresolved("MAP_NOT_VALIDATED");
        if (!string.Equals(snapshot.Map.MapId, world.MapId, StringComparison.Ordinal))
            return Unresolved("MAP_IDENTITY_MISMATCH");
        if (transform is null || transform.FrameId != snapshot.FrameId || transform.Confidence < 0.8)
            return Unresolved("CAMERA_TRANSFORM_UNRESOLVED");
        if (!TryFoot(snapshot.Self?.Box, snapshot.Target, transform, out MapPoint self))
            return Unresolved("SELF_PLATFORM_UNRESOLVED");

        PlatformNode? selfPlatform = FindPlatform(world.Platforms, self);
        MonsterObservation? target = SelectNearestMonster(snapshot.Monsters, self.X, snapshot.Target.ClientWidth, snapshot.CapturedAtMonoMs);
        if (selfPlatform is null || target is null || !TryFoot(target.Box, snapshot.Target, transform, out MapPoint monster))
            return Unresolved(selfPlatform is null ? "SELF_PLATFORM_UNRESOLVED" : "TARGET_PLATFORM_UNRESOLVED");
        PlatformNode? targetPlatform = FindPlatform(world.Platforms, monster);
        if (targetPlatform is null) return Unresolved("TARGET_PLATFORM_UNRESOLVED");

        bool same = string.Equals(selfPlatform.PlatformId, targetPlatform.PlatformId, StringComparison.Ordinal);
        double left = self.X - (selfPlatform.X1 + selfPlatform.SafeMarginPx);
        double right = (selfPlatform.X2 - selfPlatform.SafeMarginPx) - self.X;
        bool canClimbUp = world.Edges.Any(edge => edge.Type == TopologyEdgeType.Climb && edge.FromPlatformId == selfPlatform.PlatformId && edge.ToPlatformId == targetPlatform.PlatformId);
        bool canClimbDown = world.Edges.Any(edge => edge.Type == TopologyEdgeType.Climb && edge.ToPlatformId == selfPlatform.PlatformId && edge.FromPlatformId == targetPlatform.PlatformId);
        bool canJump = world.Edges.Any(edge => edge.Type == TopologyEdgeType.Jump && edge.FromPlatformId == selfPlatform.PlatformId && edge.ToPlatformId == targetPlatform.PlatformId);
        return new PlatformResolution(true, "OK", new PlatformContext
        {
            CurrentPlatformId = selfPlatform.PlatformId,
            TargetPlatformId = targetPlatform.PlatformId,
            SamePlatform = same,
            CanJump = canJump,
            CanClimbUp = canClimbUp,
            CanClimbDown = canClimbDown,
            DistanceToBoundaryPx = Math.Max(0, Math.Min(left, right)),
            CameraStable = true,
            Facing = FacingDirection.Unknown,
        });
    }

    private PlatformNode? FindPlatform(IEnumerable<PlatformNode> platforms, MapPoint point) => platforms
        .Where(platform => platform is not null
            && point.X >= platform.X1 + platform.SafeMarginPx
            && point.X <= platform.X2 - platform.SafeMarginPx
            && Math.Abs(point.Y - platform.Y) <= verticalTolerancePx)
        .OrderBy(platform => Math.Abs(point.Y - platform.Y))
        .FirstOrDefault();

    private static MonsterObservation? SelectNearestMonster(IEnumerable<MonsterObservation>? monsters, double selfWorldX, int clientWidth, long nowMonoMs) =>
        monsters?
            .Where(monster => monster is not null && monster.Confidence > 0 && monster.FreshUntilMonoMs >= nowMonoMs && ValidBox(monster.Box))
            .OrderBy(monster => Math.Abs((monster.Box[0] + monster.Box[2] / 2) * clientWidth - selfWorldX))
            .FirstOrDefault();

    private static bool TryFoot(double[]? box, TargetBinding target, CameraTransform transform, out MapPoint point)
    {
        point = default;
        if (!ValidBox(box) || target is null || target.ClientWidth <= 0 || target.ClientHeight <= 0) return false;
        point = new MapPoint(
            (box![0] + box[2] / 2) * target.ClientWidth + transform.OffsetX,
            (box[1] + box[3]) * target.ClientHeight + transform.OffsetY);
        return true;
    }

    private static bool ValidBox(double[]? box) => box is { Length: 4 }
        && box.All(double.IsFinite)
        && box[0] >= 0 && box[1] >= 0 && box[2] > 0 && box[3] > 0
        && box[0] + box[2] <= 1 && box[1] + box[3] <= 1;

    private static PlatformResolution Unresolved(string code) => new(false, code, new PlatformContext { CameraStable = false });
    private readonly record struct MapPoint(double X, double Y);
}
