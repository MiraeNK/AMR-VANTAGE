using Vantage.Core.Geometry;

namespace Vantage.Core.Paths;

/// <summary>Where a candidate path came from. All sources go through the same normalizer and the same clearance rules.</summary>
public enum PathSource
{
    /// <summary>Pre-designed route.</summary>
    StaticRoute,
    /// <summary>Fixed guided line of an AGV (virtual line); treated like a static route.</summary>
    AgvVirtualLine,
    /// <summary>Planner output of a free-navigating AMR (e.g. Nav2); may be revised.</summary>
    AmrPlanner,
}

public enum VehicleClass { Agv, Amr }

/// <summary>Optional route node attached to a pose (only fixed routes have them).</summary>
public sealed record PathPoint(Pose2D Pose, string? NodeId = null);

/// <summary>Candidate path P_i = (q_i0 … q_im) as submitted by a robot adapter.</summary>
public sealed record PathCandidate(
    string RobotId,
    string MissionId,
    int Revision,
    PathSource Source,
    IReadOnlyList<PathPoint> Points)
{
    public string Key => $"{RobotId}/{MissionId}/r{Revision}";
}

/// <summary>
/// Normalized, directed segment E_ik with its original sub-path geometry,
/// revision identity, and swept envelope S_ik.
/// </summary>
public sealed class Segment
{
    public required string RobotId { get; init; }
    public required string MissionId { get; init; }
    public required int Revision { get; init; }
    public required int Index { get; init; }
    public required PathSource Source { get; init; }
    public required IReadOnlyList<Pose2D> Geometry { get; init; }
    public required SweptArea Swept { get; init; }
    public required double StartS { get; init; }
    public required double EndS { get; init; }
    public string StartNode { get; init; } = "";
    public string EndNode { get; init; } = "";

    /// <summary>Unique key: robot, mission, revision, index.</summary>
    public string Id => $"{RobotId}:{MissionId}:r{Revision}:s{Index:D3}";
    /// <summary>Operator-facing label.</summary>
    public string Label => $"{RobotId}·r{Revision}·s{Index:D2}";
    public double Length => EndS - StartS;
    public Pose2D Start => Geometry[0];
    public Pose2D End => Geometry[^1];

    public override string ToString() => Id;
}

/// <summary>Result of normalizing one candidate.</summary>
public sealed class NormalizedPath
{
    public required PathCandidate Candidate { get; init; }
    public required IReadOnlyList<Pose2D> Poses { get; init; }
    public required IReadOnlyList<Segment> Segments { get; init; }
    public required double Length { get; init; }

    public string RobotId => Candidate.RobotId;
    public int Revision => Candidate.Revision;
}
