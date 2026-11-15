using Vantage.Core.Clearance;
using Vantage.Core.Conflicts;
using Vantage.Core.Paths;

namespace Vantage.Fleet.Simulation;

/// <summary>
/// Occupancy oracle: judges the run from the simulator's true poses and the
/// raster geometry oracle, never from the FMR's own conflict labels.
/// </summary>
public sealed class OccupancyOracle(RasterOracle geometry, double progressTolerance = 0.02)
{
    private readonly HashSet<string> _incompatible = new();
    private readonly HashSet<string> _orderViolations = new();
    private readonly Dictionary<string, Segment> _seen = new();
    private readonly HashSet<string> _inCollision = new();

    public RasterOracle Geometry => geometry;
    public int IncompatibleReservations => _incompatible.Count;
    public int OrderViolations => _orderViolations.Count;
    public int Collisions { get; private set; }
    public double CollisionTime { get; private set; }
    public IReadOnlyCollection<string> IncompatiblePairs => _incompatible;
    public IReadOnlyCollection<Segment> SeenSegments => _seen.Values;

    public void Observe(double dt, IReadOnlyList<SimRobot> robots, ClearanceManager manager, ClearanceSnapshot snap)
    {
        foreach (var s in manager.ActiveSegments()) _seen.TryAdd(s.Id, s);

        // 1. Two robots holding reservations that the oracle says cannot be used together.
        var reserved = manager.ReservedSegments();
        for (int i = 0; i < reserved.Count; i++)
        for (int j = i + 1; j < reserved.Count; j++)
        {
            var a = reserved[i];
            var b = reserved[j];
            if (a.RobotId == b.RobotId) continue;
            if (!geometry.Conflicts(a, b)) continue;
            var key = string.CompareOrdinal(a.Id, b.Id) < 0 ? $"{a.Id}|{b.Id}" : $"{b.Id}|{a.Id}";
            _incompatible.Add(key);
        }

        // 2. Robot body (true pose) has passed the start of a segment the FMR had not granted.
        foreach (var r in robots)
        {
            var view = snap.Robots.FirstOrDefault(v => v.Profile.RobotId == r.RobotId);
            if (view is null || view.Revision != r.Revision) continue;
            foreach (var seg in view.Segments)
            {
                if (r.S <= seg.Segment.StartS + progressTolerance) break;
                if (seg.State is SegmentState.Waiting or SegmentState.Revised) _orderViolations.Add(seg.Segment.Id);
            }
        }

        // 3. Physical overlap of bare footprints.
        for (int i = 0; i < robots.Count; i++)
        for (int j = i + 1; j < robots.Count; j++)
        {
            var a = robots[i];
            var b = robots[j];
            string key = a.RobotId + "|" + b.RobotId;
            bool hit = a.Spec.Footprint.At(a.Pose).Intersects(b.Spec.Footprint.At(b.Pose));
            if (hit)
            {
                CollisionTime += dt;
                if (_inCollision.Add(key)) Collisions++;
            }
            else _inCollision.Remove(key);
        }
    }
}
