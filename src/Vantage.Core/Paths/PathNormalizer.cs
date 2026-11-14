using Vantage.Core.Geometry;

namespace Vantage.Core.Paths;

public sealed record NormalizerOptions
{
    /// <summary>Target segment length along the path, metres.</summary>
    public double SegmentLength { get; init; } = 1.0;
    /// <summary>Arc-length step used to build swept areas, metres.</summary>
    public double SweepStep { get; init; } = 0.05;
    /// <summary>Spatial margin ε, metres.</summary>
    public double Margin { get; init; } = 0.10;
    /// <summary>Consecutive points closer than this are treated as duplicates, metres.</summary>
    public double DuplicateTolerance { get; init; } = 1e-4;
    /// <summary>Workspace limits; points outside are rejected (map validation).</summary>
    public Aabb? Workspace { get; init; }
}

public sealed class PathValidationException(string message) : Exception(message);

/// <summary>
/// Turns AGV fixed routes and AMR planner paths into the same segment
/// contract. Original vertices are kept, so curvature is never simplified
/// away; segment boundaries are inserted on top of them.
/// </summary>
public sealed class PathNormalizer(NormalizerOptions options)
{
    public NormalizerOptions Options { get; } = options;

    public NormalizedPath Normalize(PathCandidate candidate, Footprint footprint)
    {
        var points = Clean(candidate);
        var poses = points.Select(p => p.Pose).ToList();
        double total = Polyline.Length(poses);

        // Cumulative arc length at each original vertex.
        var s = new double[poses.Count];
        for (int i = 1; i < poses.Count; i++) s[i] = s[i - 1] + Vec2.Distance(poses[i - 1].Position, poses[i].Position);

        // Breakpoints: route nodes (fixed routes) plus every SegmentLength.
        var breaks = new SortedSet<double> { 0, total };
        for (int i = 0; i < points.Count; i++)
            if (points[i].NodeId is not null) breaks.Add(s[i]);
        var withNodes = breaks.ToList();
        for (int b = 0; b < withNodes.Count - 1; b++)
        {
            double a = withNodes[b], e = withNodes[b + 1];
            int n = Math.Max(1, (int)Math.Round((e - a) / Options.SegmentLength));
            for (int k = 1; k < n; k++) breaks.Add(a + (e - a) * k / n);
        }

        var bounds = breaks.ToList();
        var segments = new List<Segment>(bounds.Count - 1);
        for (int k = 0; k < bounds.Count - 1; k++)
        {
            double a = bounds[k], e = bounds[k + 1];
            if (e - a < Options.DuplicateTolerance) continue;
            var geom = SubPath(poses, s, a, e);
            segments.Add(new Segment
            {
                RobotId = candidate.RobotId,
                MissionId = candidate.MissionId,
                Revision = candidate.Revision,
                Index = segments.Count,
                Source = candidate.Source,
                Geometry = geom,
                Swept = SweptArea.Build(geom, footprint, Options.Margin, Options.SweepStep),
                StartS = a,
                EndS = e,
                StartNode = NodeAt(points, s, a),
                EndNode = NodeAt(points, s, e),
            });
        }

        return new NormalizedPath { Candidate = candidate, Poses = poses, Segments = segments, Length = total };
    }

    private List<PathPoint> Clean(PathCandidate c)
    {
        if (string.IsNullOrWhiteSpace(c.RobotId)) throw new PathValidationException("Robot id is empty.");
        if (c.Revision < 0) throw new PathValidationException("Revision must be non-negative.");
        var result = new List<PathPoint>(c.Points.Count);
        foreach (var p in c.Points)
        {
            var q = p.Pose;
            if (!double.IsFinite(q.X) || !double.IsFinite(q.Y) || !double.IsFinite(q.Theta))
                throw new PathValidationException("Path contains a non-finite pose.");
            if (Options.Workspace is { } ws && (q.X < ws.MinX || q.X > ws.MaxX || q.Y < ws.MinY || q.Y > ws.MaxY))
                throw new PathValidationException($"Pose {q} lies outside the map.");
            if (result.Count > 0 && Vec2.Distance(result[^1].Pose.Position, q.Position) < Options.DuplicateTolerance)
            {
                if (p.NodeId is not null && result[^1].NodeId is null) result[^1] = p;
                continue;
            }
            result.Add(p with { Pose = q with { Theta = Angle.Normalize(q.Theta) } });
        }
        if (result.Count < 2) throw new PathValidationException("Path needs at least two distinct poses.");
        return result;
    }

    private static List<Pose2D> SubPath(List<Pose2D> poses, double[] s, double a, double e)
    {
        var geom = new List<Pose2D> { Polyline.At(poses, a) };
        for (int i = 0; i < poses.Count; i++)
            if (s[i] > a + 1e-9 && s[i] < e - 1e-9) geom.Add(poses[i]);
        geom.Add(Polyline.At(poses, e));
        return geom;
    }

    /// <summary>Route node id at arc length s, or a quantized coordinate key (used by the node-id ablation only).</summary>
    private static string NodeAt(List<PathPoint> pts, double[] s, double at)
    {
        for (int i = 0; i < pts.Count; i++)
            if (Math.Abs(s[i] - at) < 1e-6 && pts[i].NodeId is { } id) return id;
        var p = Polyline.At(pts.Select(x => x.Pose).ToList(), at);
        return $"g{Math.Round(p.X * 2) / 2:F1}_{Math.Round(p.Y * 2) / 2:F1}";
    }
}
