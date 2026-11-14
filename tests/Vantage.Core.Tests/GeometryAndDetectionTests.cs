using Vantage.Core.Conflicts;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Core.Tests;

public class GeometryAndDetectionTests
{
    private static readonly Footprint Fp = Footprint.Centered(0.8, 0.5);
    private static readonly PathNormalizer Normalizer = new(new NormalizerOptions { SegmentLength = 1.0, Margin = 0.1 });

    private static NormalizedPath Path(string robot, PathSource src, params (double X, double Y, string? Node)[] pts) =>
        Normalizer.Normalize(new PathCandidate(robot, "M1", 0, src,
            pts.Select(p => new PathPoint(new Pose2D(p.X, p.Y, 0), p.Node)).ToList()), Fp);

    [Fact]
    public void Sat_detects_overlap_and_separation()
    {
        var a = Fp.At(new Pose2D(0, 0, 0));
        Assert.True(a.Intersects(Fp.At(new Pose2D(0.5, 0.2, 0.3))));
        Assert.False(a.Intersects(Fp.At(new Pose2D(2, 0, 0))));
        Assert.True(a.Intersects(Fp.At(new Pose2D(0.8, 0, 0))), "touching edges count as intersecting");
    }

    [Fact]
    public void Normalizer_keeps_route_nodes_and_original_vertices()
    {
        var p = Path("AGV", PathSource.AgvVirtualLine, (0, 0, "A"), (2.5, 0, "B"), (2.5, 3, null));
        Assert.Equal(5.5, p.Length, 6);
        Assert.Contains(p.Segments, s => Math.Abs(s.EndS - 2.5) < 1e-9);         // node B is a boundary
        Assert.Contains(p.Segments, s => s.Geometry.Any(q => Math.Abs(q.X - 2.5) < 1e-9 && Math.Abs(q.Y) < 1e-9)); // corner kept
        Assert.Equal("A", p.Segments[0].StartNode);
        Assert.All(p.Segments.Zip(p.Segments.Skip(1)), t => Assert.Equal(t.First.EndS, t.Second.StartS, 9));
    }

    [Fact]
    public void Normalizer_rejects_degenerate_input()
    {
        Assert.Throws<PathValidationException>(() => Path("X", PathSource.AmrPlanner, (1, 1, null), (1, 1, null)));
        Assert.Throws<PathValidationException>(() => Path("X", PathSource.AmrPlanner, (double.NaN, 1, null), (2, 1, null)));
    }

    [Fact]
    public void Mid_edge_crossing_is_found_by_sweep_and_missed_by_node_ids()
    {
        var agv = Path("AGV", PathSource.AgvVirtualLine, (0.7, 4.2, "N1"), (13.3, 4.2, "N2"));
        var amr = Path("AMR", PathSource.AmrPlanner, (6, 0.6, null), (6, 7.4, null));
        var all = agv.Segments.Concat(amr.Segments).ToList();

        var sweep = ConflictGraph.Build(all, new SweptAreaDetector().Conflicts);
        var nodes = ConflictGraph.Build(all, new NodeIdDetector().Conflicts);
        Assert.True(sweep.Count > 0);
        Assert.Equal(0, nodes.Count);

        var oracle = new RasterOracle(_ => Fp, 0.1);
        var score = DetectionScore.Evaluate(new SweptAreaDetector(), oracle, all);
        Assert.Equal(0, score.FN);
        var ablation = DetectionScore.Evaluate(new NodeIdDetector(), oracle, all);
        Assert.True(ablation.FN > 0);
    }

    [Fact]
    public void Close_but_disjoint_paths_do_not_conflict()
    {
        // centre distance 1.0 m; grown half widths 0.35 + 0.35 → 0.30 m clearance
        var a = Path("A", PathSource.StaticRoute, (0, 0, null), (8, 0, null));
        var b = Path("B", PathSource.AmrPlanner, (8, 1.0, null), (0, 1.0, null));
        var all = a.Segments.Concat(b.Segments).ToList();
        Assert.Equal(0, ConflictGraph.Build(all, new SweptAreaDetector().Conflicts).Count);
        var score = DetectionScore.Evaluate(new SweptAreaDetector(), new RasterOracle(_ => Fp, 0.1), all);
        Assert.Equal(0, score.FP + score.FN);
    }
}
