using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Core.Conflicts;

/// <summary>Labels C(i,k,j,l) for a pair of segments from different robots.</summary>
public interface IConflictDetector
{
    string Name { get; }
    bool Conflicts(Segment a, Segment b);
}

/// <summary>Primary detector: swept footprint areas intersect (Eq. II.3).</summary>
public sealed class SweptAreaDetector : IConflictDetector
{
    public string Name => "Swept footprint (SAT)";
    public bool Conflicts(Segment a, Segment b) => a.RobotId != b.RobotId && a.Swept.Intersects(b.Swept);
}

/// <summary>
/// Ablation: conflict only when two segments share a node identity.
/// Expected to miss mid-edge crossings — that is the point of running it.
/// </summary>
public sealed class NodeIdDetector : IConflictDetector
{
    public string Name => "Node-ID equality (ablation)";

    public bool Conflicts(Segment a, Segment b)
    {
        if (a.RobotId == b.RobotId) return false;
        return a.StartNode == b.StartNode || a.StartNode == b.EndNode || a.EndNode == b.StartNode || a.EndNode == b.EndNode;
    }
}

/// <summary>
/// Independent reference ("oracle"). Rasterizes the footprint + margin at a fine
/// arc-length step onto a grid and compares occupied cells. It shares the inputs
/// (original geometry, footprint, margin) but none of the code path of the SAT
/// detector, so both cannot inherit the same bug.
/// </summary>
public sealed class RasterOracle(Func<string, Footprint> footprintOf, double margin, double resolution = 0.02, double step = 0.01)
{
    private readonly Dictionary<string, HashSet<long>> _cache = new();

    public string Name => $"Raster oracle ({resolution * 100:F0} mm)";
    public double Resolution => resolution;

    private readonly Dictionary<(string, string), bool> _pairs = new();

    public bool Conflicts(Segment a, Segment b)
    {
        if (a.RobotId == b.RobotId) return false;
        var key = string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
        lock (_pairs)
            if (_pairs.TryGetValue(key, out var known)) return known;

        bool result = false;
        var ca = Cells(a);
        var cb = Cells(b);
        var (small, large) = ca.Count < cb.Count ? (ca, cb) : (cb, ca);
        foreach (var c in small)
            if (large.Contains(c)) { result = true; break; }

        lock (_pairs) _pairs[key] = result;
        return result;
    }

    /// <summary>True when the footprint at <paramref name="pose"/> overlaps any cell of the segment.</summary>
    public bool Occupies(Segment seg, Pose2D pose)
    {
        var cells = Cells(seg);
        foreach (var c in Rasterize(footprintOf(seg.RobotId), pose))
            if (cells.Contains(c)) return true;
        return false;
    }

    private HashSet<long> Cells(Segment seg)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(seg.Id, out var hit)) return hit;
            var set = new HashSet<long>();
            var fp = footprintOf(seg.RobotId);
            foreach (var pose in Polyline.Sample(seg.Geometry, step))
                foreach (var c in Rasterize(fp, pose)) set.Add(c);
            _cache[seg.Id] = set;
            return set;
        }
    }

    private IEnumerable<long> Rasterize(Footprint fp, Pose2D pose)
    {
        // Cell (i,j) is occupied when its centre lies inside the footprint grown by ε.
        // Boundary disagreement with the SAT detector is bounded by one cell diagonal
        // (√2·resolution); that value is the declared numeric tolerance.
        double f = fp.Front + margin, r = fp.Rear + margin, l = fp.Left + margin, rt = fp.Right + margin;
        double c = Math.Cos(pose.Theta), s = Math.Sin(pose.Theta);
        var box = fp.At(pose, margin).Bounds.Inflate(resolution);
        long i0 = (long)Math.Floor(box.MinX / resolution), i1 = (long)Math.Ceiling(box.MaxX / resolution);
        long j0 = (long)Math.Floor(box.MinY / resolution), j1 = (long)Math.Ceiling(box.MaxY / resolution);
        for (long i = i0; i <= i1; i++)
        {
            double cx = (i + 0.5) * resolution - pose.X;
            for (long j = j0; j <= j1; j++)
            {
                double cy = (j + 0.5) * resolution - pose.Y;
                double lx = c * cx + s * cy;
                double ly = -s * cx + c * cy;
                if (lx >= -r && lx <= f && ly >= -rt && ly <= l) yield return (i << 32) ^ (j & 0xFFFFFFFF);
            }
        }
    }
}

/// <summary>Conflict labels among the currently active paths, keyed by segment id.</summary>
public sealed class ConflictGraph
{
    private readonly Dictionary<string, HashSet<string>> _edges = new();

    public IEnumerable<(string A, string B)> Pairs =>
        _edges.SelectMany(kv => kv.Value.Where(v => string.CompareOrdinal(kv.Key, v) < 0).Select(v => (kv.Key, v)));

    public int Count => Pairs.Count();

    public bool HasConflict(string segmentId) => _edges.TryGetValue(segmentId, out var s) && s.Count > 0;

    public IReadOnlySet<string> ConflictsOf(string segmentId) =>
        _edges.TryGetValue(segmentId, out var s) ? s : EmptySet;

    public bool Conflicts(string a, string b) => _edges.TryGetValue(a, out var s) && s.Contains(b);

    private static readonly HashSet<string> EmptySet = new();

    public static ConflictGraph Build(IReadOnlyList<Segment> segments, Func<Segment, Segment, bool> conflicts)
    {
        var g = new ConflictGraph();
        for (int i = 0; i < segments.Count; i++)
        for (int j = i + 1; j < segments.Count; j++)
        {
            var a = segments[i];
            var b = segments[j];
            if (a.RobotId == b.RobotId) continue;
            if (!conflicts(a, b)) continue;
            g.Add(a.Id, b.Id);
        }
        return g;
    }

    private void Add(string a, string b)
    {
        if (!_edges.TryGetValue(a, out var sa)) _edges[a] = sa = new HashSet<string>();
        if (!_edges.TryGetValue(b, out var sb)) _edges[b] = sb = new HashSet<string>();
        sa.Add(b);
        sb.Add(a);
    }
}

/// <summary>Confusion matrix of a detector against the oracle (Eq. II.4).</summary>
public sealed record DetectionScore(string Detector, int TP, int FP, int TN, int FN)
{
    public double Fnr => TP + FN == 0 ? 0 : (double)FN / (TP + FN);
    public double Fpr => FP + TN == 0 ? 0 : (double)FP / (FP + TN);
    public int Total => TP + FP + TN + FN;

    public static DetectionScore Evaluate(IConflictDetector detector, RasterOracle oracle, IReadOnlyList<Segment> segments)
    {
        int tp = 0, fp = 0, tn = 0, fn = 0;
        for (int i = 0; i < segments.Count; i++)
        for (int j = i + 1; j < segments.Count; j++)
        {
            var a = segments[i];
            var b = segments[j];
            if (a.RobotId == b.RobotId) continue;
            bool truth = oracle.Conflicts(a, b);
            bool said = detector.Conflicts(a, b);
            if (truth && said) tp++;
            else if (!truth && said) fp++;
            else if (truth) fn++;
            else tn++;
        }
        return new DetectionScore(detector.Name, tp, fp, tn, fn);
    }
}
