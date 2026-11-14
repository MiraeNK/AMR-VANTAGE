using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Core.Clearance;

/// <summary>Segment state contract (Tabel II.1).</summary>
public enum SegmentState
{
    /// <summary>Menunggu — no clearance yet.</summary>
    Waiting,
    /// <summary>Diizinkan — reservation recorded for this revision.</summary>
    Granted,
    /// <summary>Ditempati — robot has entered; reservation kept.</summary>
    Occupied,
    /// <summary>Selesai — footprint left, exit validated, reservation released.</summary>
    Completed,
    /// <summary>Direvisi — geometry superseded by a newer revision.</summary>
    Revised,
}

/// <summary>Response status, aligned with VDA 5050 3.0 zone responses.</summary>
public enum ClearanceStatus { Granted, Queued, Revoked, Rejected }

public enum ClearanceMode
{
    /// <summary>Per conflict block (proposed mechanism).</summary>
    Segmented,
    /// <summary>Baseline: the robot must hold its entire remaining path.</summary>
    FullPathBaseline,
}

public enum PriorityPolicy
{
    /// <summary>Earliest request first; fixed robot priority breaks ties.</summary>
    FirstComeFirstServed,
    /// <summary>Fixed robot priority first; request time breaks ties.</summary>
    FixedPriority,
}

public sealed record RobotProfile(string RobotId, VehicleClass Class, Footprint Footprint, int Priority, string DisplayName = "")
{
    public string Label => string.IsNullOrEmpty(DisplayName) ? RobotId : DisplayName;
}

public sealed record ClearanceOptions
{
    public ClearanceMode Mode { get; init; } = ClearanceMode.Segmented;
    public PriorityPolicy Priority { get; init; } = PriorityPolicy.FirstComeFirstServed;
    /// <summary>Distance ahead of the robot within which segments are requested (release horizon), metres.</summary>
    public double Horizon { get; init; } = 3.0;
    /// <summary>Tolerance on reported path progress, metres.</summary>
    public double ProgressTolerance { get; init; } = 0.02;
    /// <summary>Margin ε used when validating that the footprint has left an area.</summary>
    public double ExitMargin { get; init; } = 0.10;
    /// <summary>
    /// A grant issued while a segment was conflict-free is revoked when a new path
    /// makes it conflicting, but only if it starts at least this far ahead of the
    /// robot (the robot must be able to stop before it), metres.
    /// </summary>
    public double RevokeDistance { get; init; } = 1.0;
}

/// <summary>Robot state as reported by its adapter.</summary>
public sealed record StateReport(
    string RobotId,
    string MissionId,
    int Revision,
    long Sequence,
    Pose2D Pose,
    double PathProgress,
    bool Driving,
    double Time);

/// <summary>One clearance decision. A robot may drive its reference point up to <see cref="LimitS"/> on the given revision.</summary>
public sealed record ClearanceDecision(
    long DecisionNo,
    string RobotId,
    string MissionId,
    int Revision,
    ClearanceStatus Status,
    IReadOnlyList<string> SegmentIds,
    double LimitS,
    string Reason,
    double Time);

public enum SubmitOutcome { Accepted, Duplicate, Stale, Invalid, UnknownRobot }
public enum ReportOutcome { Applied, Duplicate, StaleRevision, UnknownRobot }

public enum JournalKind { Path, Decision, StateChange, Violation, Warning, Info }

public sealed record JournalEntry(double Time, JournalKind Kind, string RobotId, string Message, long DecisionNo = 0, string SegmentId = "");

/// <summary>Live view of one segment inside the manager.</summary>
public sealed class SegmentRecord(Segment segment)
{
    public Segment Segment { get; } = segment;
    public SegmentState State { get; internal set; } = SegmentState.Waiting;
    public long DecisionNo { get; internal set; }
    public double? GrantedAt { get; internal set; }
    public double? EnteredAt { get; internal set; }
    public double? ReleasedAt { get; internal set; }
    public bool ViolationRaised { get; internal set; }
    public bool IsReserved => State is SegmentState.Granted or SegmentState.Occupied;
}
