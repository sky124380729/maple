using Maple.Contracts;

namespace Maple.Vision;

public enum SelfIdentityStatus { NotFound, WarmingUp, Ready, Occluded, Ambiguous }

public sealed class SelfIdentityOptions
{
    public int WarmupFrames { get; init; } = 3;
    public double MinimumConfidence { get; init; } = 0.75;
    public long OcclusionTtlMs { get; init; } = 180;
}

public sealed class SelfIdentityResult
{
    public SelfIdentityStatus Status { get; init; }
    public string Diagnostic { get; init; } = string.Empty;
    public SelfObservation? Self { get; init; }
    public List<PlayerObservation> Players { get; init; } = [];
    public bool CanDriveActions { get; init; }
}

public sealed class SelfIdentityTracker
{
    private readonly SelfIdentityOptions options;
    private readonly List<Track> tracks = [];
    private long nextTrackId;
    private long? selfTrackId;

    public SelfIdentityTracker(SelfIdentityOptions? options = null)
    {
        this.options = options ?? new SelfIdentityOptions();
        if (this.options.WarmupFrames < 1 || this.options.MinimumConfidence is < 0 or > 1 || this.options.OcclusionTtlMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public SelfIdentityResult Update(IReadOnlyList<DetectionCandidate> candidates, long nowMonoMs, bool monsterRoleAvailable)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        tracks.RemoveAll(track => nowMonoMs - track.LastSeenMonoMs > options.OcclusionTtlMs);
        foreach (Track track in tracks) track.SeenThisFrame = false;

        foreach (DetectionCandidate candidate in candidates.Where(item => item.Role == DetectionRole.CharacterCandidate && item.Confidence >= options.MinimumConfidence))
        {
            Track? match = tracks.Where(track => !track.SeenThisFrame).OrderBy(track => MatchDistance(track.Box, candidate.Box)).FirstOrDefault();
            if (match is null || MatchDistance(match.Box, candidate.Box) > 0.18)
            {
                match = new Track { Id = ++nextTrackId, Box = candidate.Box, Confidence = candidate.Confidence, StableFrames = 0 };
                tracks.Add(match);
            }
            match.Box = candidate.Box;
            match.Confidence = candidate.Confidence;
            match.LastSeenMonoMs = nowMonoMs;
            match.StableFrames++;
            match.SeenThisFrame = true;
        }

        Track? self = selfTrackId.HasValue ? tracks.FirstOrDefault(track => track.Id == selfTrackId.Value) : null;
        if (selfTrackId.HasValue && self is null) selfTrackId = null;
        List<Track> visible = tracks.Where(track => track.SeenThisFrame).ToList();

        if (self is null)
        {
            List<Track> stable = visible.Where(track => track.StableFrames >= options.WarmupFrames).ToList();
            if (stable.Count > 1) return Result(SelfIdentityStatus.Ambiguous, "SELF_AMBIGUOUS", null, visible, nowMonoMs, false);
            if (stable.Count == 1 && visible.Count == 1)
            {
                self = stable[0];
                selfTrackId = self.Id;
            }
            else
            {
                SelfIdentityStatus status = visible.Count > 1 ? SelfIdentityStatus.Ambiguous : visible.Count == 1 ? SelfIdentityStatus.WarmingUp : SelfIdentityStatus.NotFound;
                string diagnostic = status == SelfIdentityStatus.Ambiguous ? "SELF_AMBIGUOUS" : status == SelfIdentityStatus.WarmingUp ? "SELF_WARMING_UP" : "SELF_NOT_FOUND";
                return Result(status, diagnostic, null, visible, nowMonoMs, false);
            }
        }

        if (!self.SeenThisFrame)
            return Result(SelfIdentityStatus.Occluded, "SELF_OCCLUDED", self, visible, nowMonoMs, false);

        return Result(SelfIdentityStatus.Ready, "OK", self, visible, nowMonoMs, monsterRoleAvailable && self.Confidence >= options.MinimumConfidence);
    }

    public void Reset()
    {
        tracks.Clear();
        selfTrackId = null;
        nextTrackId = 0;
    }

    private SelfIdentityResult Result(SelfIdentityStatus status, string diagnostic, Track? self, IReadOnlyList<Track> visible, long nowMonoMs, bool canDrive)
    {
        long freshUntil = self?.LastSeenMonoMs + options.OcclusionTtlMs ?? nowMonoMs;
        return new SelfIdentityResult
        {
            Status = status,
            Diagnostic = diagnostic,
            Self = self is null ? null : new SelfObservation { Box = self.Box, Confidence = self.Confidence, FreshUntilMonoMs = freshUntil },
            Players = visible.Where(track => self is null || track.Id != self.Id)
                .Select(track => new PlayerObservation { TrackId = $"character-{track.Id}", Box = track.Box, Confidence = track.Confidence, FreshUntilMonoMs = track.LastSeenMonoMs + options.OcclusionTtlMs })
                .ToList(),
            CanDriveActions = canDrive,
        };
    }

    private static double MatchDistance(double[] first, double[] second)
    {
        double firstX = first[0] + first[2] / 2;
        double firstY = first[1] + first[3] / 2;
        double secondX = second[0] + second[2] / 2;
        double secondY = second[1] + second[3] / 2;
        return Math.Sqrt(Math.Pow(firstX - secondX, 2) + Math.Pow(firstY - secondY, 2));
    }

    private sealed class Track
    {
        public long Id { get; init; }
        public double[] Box { get; set; } = [];
        public double Confidence { get; set; }
        public int StableFrames { get; set; }
        public long LastSeenMonoMs { get; set; }
        public bool SeenThisFrame { get; set; }
    }
}
