using Vantage.Core.Conflicts;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Core.Clearance;

/// <summary>
/// Central reservation manager. Implements Grant(i,k,r) = Valid(i,r) ∧ Compatible(S_ik, R_active)
/// (Eq. III.1). Every public call runs under one lock, so the final compatibility
/// check and the reservation write are a single atomic step. Events are raised
/// after the lock is released.
/// </summary>
public sealed class ClearanceManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, RobotRecord> _robots = new();
    private readonly Dictionary<string, (RobotRecord Robot, SegmentRecord Record)> _index = new();
    private readonly List<JournalEntry> _journal = new();
    private readonly List<Action> _outbox = new();
    private ConflictGraph _graph = ConflictGraph.Build([], (_, _) => false);
    private long _decisionNo;
    private double _now;

    public ClearanceManager(PathNormalizer normalizer, IConflictDetector detector, ClearanceOptions options)
    {
        Normalizer = normalizer;
        Detector = detector;
        Options = options;
    }

    public PathNormalizer Normalizer { get; }
    public IConflictDetector Detector { get; }
    public ClearanceOptions Options { get; }

    public event Action<ClearanceDecision>? DecisionIssued;
    public event Action<JournalEntry>? Journaled;

    public long DecisionCount { get { lock (_gate) return _decisionNo; } }
    public int OrderViolations { get; private set; }
    /// <summary>Self-check: two robots holding reservations the manager's own detector labels as conflicting. Must stay 0.</summary>
    public int InternalIncompatible { get; private set; }

    // ───────────────────────────── Registration ─────────────────────────────

    public void Register(RobotProfile profile)
    {
        lock (_gate) _robots[profile.RobotId] = new RobotRecord(profile);
    }

    public IReadOnlyList<RobotProfile> Profiles
    {
        get { lock (_gate) return _robots.Values.Select(r => r.Profile).ToList(); }
    }

    public Footprint FootprintOf(string robotId)
    {
        lock (_gate) return _robots[robotId].Profile.Footprint;
    }

    // ───────────────────────────── Path input ─────────────────────────────

    public SubmitOutcome SubmitPath(PathCandidate candidate, double now)
    {
        SubmitOutcome outcome;
        lock (_gate)
        {
            _now = now;
            outcome = SubmitLocked(candidate);
        }
        Flush();
        return outcome;
    }

    private SubmitOutcome SubmitLocked(PathCandidate c)
    {
        if (!_robots.TryGetValue(c.RobotId, out var r))
        {
            Log(JournalKind.Warning, c.RobotId, "Path from unregistered robot rejected.");
            return SubmitOutcome.UnknownRobot;
        }

        if (r.Path is { } active && active.Candidate.MissionId == c.MissionId)
        {
            if (c.Revision == active.Revision)
            {
                Log(JournalKind.Info, c.RobotId, $"Duplicate path {c.Key} ignored.");
                return SubmitOutcome.Duplicate;
            }
            if (c.Revision < active.Revision)
            {
                Log(JournalKind.Warning, c.RobotId, $"Stale path {c.Key} rejected (active r{active.Revision}).");
                return SubmitOutcome.Stale;
            }
        }

        NormalizedPath path;
        try
        {
            path = Normalizer.Normalize(c, r.Profile.Footprint);
        }
        catch (PathValidationException ex)
        {
            Log(JournalKind.Warning, c.RobotId, $"Path {c.Key} invalid: {ex.Message}");
            return SubmitOutcome.Invalid;
        }

        if (r.Path is not null) RetireRevision(r);

        r.Path = path;
        r.Records = path.Segments.Select(s => new SegmentRecord(s)).ToList();
        r.Progress = 0;
        r.ProgressS = 0;
        r.MissionDone = false;
        r.PendingBlockStart = null;
        r.LastQueuedBlock = null;
        foreach (var rec in r.Records) _index[rec.Segment.Id] = (r, rec);

        RebuildGraph();
        Log(JournalKind.Path, c.RobotId,
            $"Path {c.Key} accepted · {c.Source} · {path.Segments.Count} segments · {path.Length:F2} m");
        RevokeGrantsThatBecameConflicting();

        if (r.Records.Count > 0 && _graph.HasConflict(r.Records[0].Segment.Id))
            Log(JournalKind.Warning, c.RobotId, "Feasibility: robot starts inside a conflict area; start pose should be outside shared space.");

        Evaluate();
        return SubmitOutcome.Accepted;
    }

    /// <summary>
    /// A new path can turn a previously free, granted-but-unentered segment into a
    /// conflicting one. Such grants are withdrawn (from that segment onward) when the
    /// robot can still stop; the area is then requested again as a proper block.
    /// </summary>
    private void RevokeGrantsThatBecameConflicting()
    {
        foreach (var r in _robots.Values)
        {
            if (r.Path is null) continue;
            int first = -1;
            for (int i = r.Progress; i < r.Records.Count; i++)
            {
                var rec = r.Records[i];
                if (rec.State != SegmentState.Granted) continue;
                if (!_graph.HasConflict(rec.Segment.Id)) continue;
                if (rec.Segment.StartS < r.ProgressS + Options.RevokeDistance) continue;
                if (BlockAlreadyHeld(r, i)) continue;
                first = i;
                break;
            }
            if (first < 0) continue;

            var ids = new List<string>();
            for (int i = first; i < r.Records.Count; i++)
            {
                var rec = r.Records[i];
                if (rec.State != SegmentState.Granted) continue;
                rec.State = SegmentState.Waiting;
                rec.GrantedAt = null;
                rec.DecisionNo = 0;
                ids.Add(rec.Segment.Id);
            }
            var p = r.Path;
            Issue(r, p.Candidate.MissionId, p.Revision, ClearanceStatus.Revoked, ids, LimitOf(r), "Area became shared by a new path");
        }
    }

    /// <summary>True when the conflict run containing record i was granted as one block together with an earlier, already reserved conflicting segment.</summary>
    private bool BlockAlreadyHeld(RobotRecord r, int i) =>
        i > 0 && r.Records[i - 1].State == SegmentState.Occupied && _graph.HasConflict(r.Records[i - 1].Segment.Id);

    /// <summary>Old revision: unentered grants are revoked, occupied areas stay reserved until exit.</summary>
    private void RetireRevision(RobotRecord r)
    {
        var revoked = new List<string>();
        foreach (var rec in r.Records)
        {
            switch (rec.State)
            {
                case SegmentState.Occupied:
                    r.Legacy.Add(rec);
                    break;
                case SegmentState.Granted:
                    rec.State = SegmentState.Revised;
                    rec.ReleasedAt = _now;
                    revoked.Add(rec.Segment.Id);
                    _index.Remove(rec.Segment.Id);
                    break;
                case SegmentState.Waiting:
                    rec.State = SegmentState.Revised;
                    _index.Remove(rec.Segment.Id);
                    break;
                default:
                    _index.Remove(rec.Segment.Id);
                    break;
            }
        }
        var old = r.Path!;
        if (revoked.Count > 0)
            Issue(r, old.Candidate.MissionId, old.Revision, ClearanceStatus.Revoked, revoked, 0, "Superseded by new revision");
        if (r.Legacy.Count > 0)
            Log(JournalKind.Info, r.Profile.RobotId, $"r{old.Revision}: {r.Legacy.Count} occupied segment(s) kept reserved until exit.");
    }

    public void CancelMission(string robotId, double now)
    {
        lock (_gate)
        {
            _now = now;
            if (!_robots.TryGetValue(robotId, out var r) || r.Path is null) return;
            RetireRevision(r);
            r.Path = null;
            r.Records = [];
            RebuildGraph();
            Log(JournalKind.Info, robotId, "Mission cancelled by operator.");
            Evaluate();
        }
        Flush();
    }

    // ───────────────────────────── State input ─────────────────────────────

    public ReportOutcome ReportState(StateReport s)
    {
        ReportOutcome outcome;
        lock (_gate)
        {
            _now = s.Time;
            outcome = ReportLocked(s);
        }
        Flush();
        return outcome;
    }

    private ReportOutcome ReportLocked(StateReport s)
    {
        if (!_robots.TryGetValue(s.RobotId, out var r)) return ReportOutcome.UnknownRobot;
        if (s.Sequence <= r.LastSequence) return ReportOutcome.Duplicate;
        r.LastSequence = s.Sequence;
        r.Pose = s.Pose;

        var body = r.Profile.Footprint.At(s.Pose, Options.ExitMargin);
        bool changed = ReleaseLegacy(r, body);

        bool current = r.Path is { } p && p.Revision == s.Revision && p.Candidate.MissionId == s.MissionId;
        if (current)
        {
            r.ProgressS = s.PathProgress;
            changed |= ApplyProgress(r, body);
        }

        if (changed) Evaluate();
        return current ? ReportOutcome.Applied : ReportOutcome.StaleRevision;
    }

    private bool ApplyProgress(RobotRecord r, ConvexPolygon body)
    {
        bool changed = false;
        double tol = Options.ProgressTolerance;
        foreach (var rec in r.Records)
        {
            var seg = rec.Segment;
            bool entered = r.ProgressS > seg.StartS + tol;
            if (!entered) break;

            if (rec.State == SegmentState.Granted)
            {
                rec.State = SegmentState.Occupied;
                rec.EnteredAt = _now;
                Log(JournalKind.StateChange, seg.RobotId, $"{seg.Id} occupied", rec.DecisionNo, seg.Id);
                changed = true;
            }
            else if (rec.State == SegmentState.Waiting && !rec.ViolationRaised)
            {
                rec.ViolationRaised = true;
                OrderViolations++;
                Log(JournalKind.Violation, seg.RobotId, $"ORDER VIOLATION: entered {seg.Id} without valid clearance", 0, seg.Id);
            }

            // Release needs both: the reference point has strictly passed the segment end
            // (so the segment holding the body, incl. the final one, is never released) AND
            // the grown footprint at the actual pose is out of the conflict area, i.e. it
            // touches none of the other robots' swept areas that conflict with this segment.
            // A progress claim alone (premature exit report) never releases.
            if (rec.State == SegmentState.Occupied && r.ProgressS > seg.EndS + tol && OutOfConflictArea(seg, body))
            {
                rec.State = SegmentState.Completed;
                rec.ReleasedAt = _now;
                Log(JournalKind.StateChange, seg.RobotId, $"{seg.Id} released (exit validated)", rec.DecisionNo, seg.Id);
                changed = true;
            }
        }

        while (r.Progress < r.Records.Count && r.Records[r.Progress].State == SegmentState.Completed) r.Progress++;

        if (!r.MissionDone && r.Path is { } path && r.ProgressS >= path.Length - tol)
        {
            r.MissionDone = true;
            Log(JournalKind.Info, r.Profile.RobotId, $"Mission {path.Candidate.MissionId} reached goal.");
            changed = true;
        }
        return changed;
    }

    private bool OutOfConflictArea(Segment seg, ConvexPolygon body)
    {
        foreach (var other in _graph.ConflictsOf(seg.Id))
            if (_index.TryGetValue(other, out var hit) && hit.Record.Segment.Swept.Intersects(body)) return false;
        return true;
    }

    private bool ReleaseLegacy(RobotRecord r, ConvexPolygon body)
    {
        bool changed = false;
        for (int i = r.Legacy.Count - 1; i >= 0; i--)
        {
            var rec = r.Legacy[i];
            if (rec.Segment.Swept.Intersects(body)) continue;
            rec.State = SegmentState.Completed;
            rec.ReleasedAt = _now;
            r.Legacy.RemoveAt(i);
            _index.Remove(rec.Segment.Id);
            Log(JournalKind.StateChange, rec.Segment.RobotId, $"{rec.Segment.Id} (old revision) released", rec.DecisionNo, rec.Segment.Id);
            changed = true;
        }
        return changed;
    }

    // ───────────────────────────── Decision ─────────────────────────────

    private sealed record Pending(RobotRecord Robot, List<SegmentRecord> Block, double Since, bool Continuation);

    private void Evaluate()
    {
        // Pass 1: grant conflict-free segments inside the horizon; collect pending conflict blocks.
        var pending = new List<Pending>();
        foreach (var r in _robots.Values)
        {
            if (r.Path is null) continue;
            while (true)
            {
                var next = NextRequest(r);
                if (next is null) break;
                var block = BlockFrom(r, next.Value);
                bool free = block.All(b => !_graph.HasConflict(b.Segment.Id));
                if (free)
                {
                    Grant(r, block, "No conflict");
                    continue;
                }
                if (r.PendingBlockStart != block[0].Segment.Id)
                {
                    r.PendingBlockStart = block[0].Segment.Id;
                    r.PendingSince = _now;
                }
                // A robot already holding the shared area directly behind this block
                // must be able to finish it; it goes first and never yields.
                bool continuation = next.Value > 0 && r.Records[next.Value - 1].IsReserved && _graph.HasConflict(r.Records[next.Value - 1].Segment.Id);
                pending.Add(new Pending(r, block, continuation ? double.NegativeInfinity : r.PendingSince, continuation));
                break;
            }
        }

        // Pass 2: conflict blocks in fixed order; a later request may not overtake an
        // earlier one that wants overlapping space.
        var ordered = Options.Priority == PriorityPolicy.FixedPriority
            ? pending.OrderByDescending(p => p.Continuation).ThenBy(p => p.Robot.Profile.Priority).ThenBy(p => p.Since)
            : pending.OrderBy(p => p.Since).ThenBy(p => p.Robot.Profile.Priority);

        var blocked = new List<Pending>();
        foreach (var p in ordered)
        {
            var holder = FirstIncompatibleHolder(p.Block);
            var yieldTo = holder is null && !p.Continuation ? blocked.FirstOrDefault(b => BlocksConflict(b.Block, p.Block)) : null;
            if (holder is null && yieldTo is null)
            {
                Grant(p.Robot, p.Block, "Compatible with active reservations");
                p.Robot.PendingBlockStart = null;
                continue;
            }

            blocked.Add(p);
            string startId = p.Block[0].Segment.Id;
            if (p.Robot.LastQueuedBlock != startId)
            {
                p.Robot.LastQueuedBlock = startId;
                string reason = holder is not null
                    ? $"Area reserved by {holder.Value.Robot.Profile.RobotId} ({holder.Value.Record.Segment.Id})"
                    : $"Yield to earlier request of {yieldTo!.Robot.Profile.RobotId}";
                var path = p.Robot.Path!;
                Issue(p.Robot, path.Candidate.MissionId, path.Revision, ClearanceStatus.Queued,
                    p.Block.Select(b => b.Segment.Id).ToList(), LimitOf(p.Robot), reason);
            }
        }

        SelfCheck();
    }

    /// <summary>First waiting segment inside the horizon, or null.</summary>
    private int? NextRequest(RobotRecord r)
    {
        for (int i = r.Progress; i < r.Records.Count; i++)
        {
            var rec = r.Records[i];
            if (rec.State != SegmentState.Waiting) continue;
            if (Options.Mode == ClearanceMode.Segmented && rec.Segment.StartS > r.ProgressS + Options.Horizon) return null;
            return i;
        }
        return null;
    }

    /// <summary>
    /// A conflict block is the contiguous run of conflicting segments, extended by
    /// conflict-free segments until the robot has room to leave the shared area
    /// completely (footprint length + 2ε). Granting it atomically means a robot
    /// never holds part of a corridor while waiting for the rest.
    /// </summary>
    private List<SegmentRecord> BlockFrom(RobotRecord r, int start)
    {
        var recs = r.Records;
        if (Options.Mode == ClearanceMode.FullPathBaseline)
            return recs.Skip(start).Where(x => x.State == SegmentState.Waiting).ToList();

        if (!_graph.HasConflict(recs[start].Segment.Id)) return [recs[start]];

        var fp = r.Profile.Footprint;
        double exitRoom = fp.Length + 2 * Options.ExitMargin;
        var block = new List<SegmentRecord>();
        double clear = 0;
        for (int i = start; i < recs.Count && recs[i].State == SegmentState.Waiting; i++)
        {
            block.Add(recs[i]);
            clear = _graph.HasConflict(recs[i].Segment.Id) ? 0 : clear + recs[i].Segment.Length;
            if (clear >= exitRoom) break;
        }
        return block;
    }

    private (RobotRecord Robot, SegmentRecord Record)? FirstIncompatibleHolder(List<SegmentRecord> block)
    {
        foreach (var rec in block)
        foreach (var other in _graph.ConflictsOf(rec.Segment.Id))
            if (_index.TryGetValue(other, out var hit) && hit.Record.IsReserved && hit.Robot.Profile.RobotId != rec.Segment.RobotId)
                return hit;
        return null;
    }

    private bool BlocksConflict(List<SegmentRecord> a, List<SegmentRecord> b)
    {
        foreach (var x in a)
        foreach (var y in b)
            if (_graph.Conflicts(x.Segment.Id, y.Segment.Id)) return true;
        return false;
    }

    private void Grant(RobotRecord r, List<SegmentRecord> block, string reason)
    {
        long no = ++_decisionNo;
        foreach (var rec in block)
        {
            rec.State = SegmentState.Granted;
            rec.DecisionNo = no;
            rec.GrantedAt = _now;
        }
        r.LastQueuedBlock = null;
        var path = r.Path!;
        var ids = block.Select(b => b.Segment.Id).ToList();
        IssueNumbered(no, r, path.Candidate.MissionId, path.Revision, ClearanceStatus.Granted, ids, LimitOf(r), reason);
    }

    /// <summary>Reference point may advance up to the start of the first segment without clearance.</summary>
    private static double LimitOf(RobotRecord r)
    {
        if (r.Path is null) return 0;
        for (int i = r.Progress; i < r.Records.Count; i++)
            if (r.Records[i].State is SegmentState.Waiting or SegmentState.Revised) return r.Records[i].Segment.StartS;
        return r.Path.Length;
    }

    private void SelfCheck()
    {
        foreach (var (a, b) in _graph.Pairs)
        {
            if (_index.TryGetValue(a, out var ha) && _index.TryGetValue(b, out var hb) &&
                ha.Record.IsReserved && hb.Record.IsReserved && ha.Robot != hb.Robot)
            {
                InternalIncompatible++;
                Log(JournalKind.Violation, ha.Robot.Profile.RobotId, $"INCOMPATIBLE RESERVATION {a} ↔ {b}");
            }
        }
    }

    private void RebuildGraph()
    {
        var segs = _robots.Values
            .SelectMany(r => r.Records.Where(x => x.State != SegmentState.Revised).Concat(r.Legacy))
            .Select(x => x.Segment)
            .ToList();
        _graph = ConflictGraph.Build(segs, Detector.Conflicts);
    }

    private void Issue(RobotRecord r, string mission, int rev, ClearanceStatus status, IReadOnlyList<string> ids, double limit, string reason)
        => IssueNumbered(++_decisionNo, r, mission, rev, status, ids, limit, reason);

    private void IssueNumbered(long no, RobotRecord r, string mission, int rev, ClearanceStatus status, IReadOnlyList<string> ids, double limit, string reason)
    {
        var d = new ClearanceDecision(no, r.Profile.RobotId, mission, rev, status, ids, limit, reason, _now);
        r.LastDecision = d;
        string span = ids.Count == 0 ? "-" : ids.Count == 1 ? ids[0] : $"{ids[0]} … {ids[^1]} ({ids.Count})";
        Log(JournalKind.Decision, r.Profile.RobotId, $"#{no} {status.ToString().ToUpperInvariant()} {span} · limit {limit:F2} m · {reason}", no);
        _outbox.Add(() => DecisionIssued?.Invoke(d));
    }

    private void Log(JournalKind kind, string robot, string msg, long decision = 0, string seg = "")
    {
        var e = new JournalEntry(_now, kind, robot, msg, decision, seg);
        _journal.Add(e);
        _outbox.Add(() => Journaled?.Invoke(e));
    }

    private void Flush()
    {
        Action[] items;
        lock (_gate)
        {
            items = _outbox.ToArray();
            _outbox.Clear();
        }
        foreach (var a in items) a();
    }

    // ───────────────────────────── Read side ─────────────────────────────

    public IReadOnlyList<JournalEntry> Journal
    {
        get { lock (_gate) return _journal.ToList(); }
    }

    public ClearanceSnapshot Snapshot()
    {
        lock (_gate)
        {
            var robots = _robots.Values.Select(r => new RobotView(
                r.Profile,
                r.Path?.Candidate.MissionId ?? "",
                r.Path?.Revision ?? -1,
                r.Path?.Candidate.Source,
                r.Path?.Poses ?? [],
                r.Path?.Length ?? 0,
                r.ProgressS,
                r.Pose,
                LimitOf(r),
                r.MissionDone,
                r.PendingBlockStart is not null,
                r.LastDecision,
                r.Records.Select(x => View(x)).ToList(),
                r.Legacy.Select(x => View(x)).ToList())).ToList();

            return new ClearanceSnapshot(_now, robots, _graph.Pairs.ToList(), _decisionNo, OrderViolations, InternalIncompatible);
        }

        SegmentView View(SegmentRecord x) => new(x.Segment, x.State, x.DecisionNo, _graph.HasConflict(x.Segment.Id),
            x.GrantedAt, x.EnteredAt, x.ReleasedAt);
    }

    /// <summary>All current (non-revised) segments, for offline detection scoring.</summary>
    public IReadOnlyList<Segment> ActiveSegments()
    {
        lock (_gate)
            return _robots.Values.SelectMany(r => r.Records.Where(x => x.State != SegmentState.Revised).Concat(r.Legacy))
                .Select(x => x.Segment).ToList();
    }

    /// <summary>Reserved segments right now (for the occupancy oracle).</summary>
    public IReadOnlyList<Segment> ReservedSegments()
    {
        lock (_gate)
            return _index.Values.Where(v => v.Record.IsReserved).Select(v => v.Record.Segment).ToList();
    }

    private sealed class RobotRecord(RobotProfile profile)
    {
        public RobotProfile Profile { get; } = profile;
        public NormalizedPath? Path { get; set; }
        public List<SegmentRecord> Records { get; set; } = [];
        public List<SegmentRecord> Legacy { get; } = [];
        public int Progress { get; set; }
        public double ProgressS { get; set; }
        public Pose2D? Pose { get; set; }
        public long LastSequence { get; set; } = -1;
        public bool MissionDone { get; set; }
        public string? PendingBlockStart { get; set; }
        public double PendingSince { get; set; }
        public string? LastQueuedBlock { get; set; }
        public ClearanceDecision? LastDecision { get; set; }
    }
}

public sealed record SegmentView(Segment Segment, SegmentState State, long DecisionNo, bool InConflict,
    double? GrantedAt, double? EnteredAt, double? ReleasedAt);

public sealed record RobotView(
    RobotProfile Profile,
    string MissionId,
    int Revision,
    PathSource? Source,
    IReadOnlyList<Pose2D> Path,
    double PathLength,
    double Progress,
    Pose2D? Pose,
    double LimitS,
    bool MissionDone,
    bool Waiting,
    ClearanceDecision? LastDecision,
    IReadOnlyList<SegmentView> Segments,
    IReadOnlyList<SegmentView> Legacy);

public sealed record ClearanceSnapshot(
    double Time,
    IReadOnlyList<RobotView> Robots,
    IReadOnlyList<(string A, string B)> ConflictPairs,
    long Decisions,
    int OrderViolations,
    int InternalIncompatible)
{
    public static readonly ClearanceSnapshot Empty = new(0, [], [], 0, 0, 0);
}
