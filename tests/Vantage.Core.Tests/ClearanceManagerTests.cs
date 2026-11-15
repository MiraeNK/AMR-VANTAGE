using Vantage.Core.Clearance;
using Vantage.Core.Conflicts;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Core.Tests;

public class ClearanceManagerTests
{
    private static readonly Footprint Fp = Footprint.Centered(0.8, 0.5);

    private static ClearanceManager Create(ClearanceMode mode = ClearanceMode.Segmented)
    {
        var m = new ClearanceManager(new PathNormalizer(new NormalizerOptions { SegmentLength = 1.0, Margin = 0.1 }),
            new SweptAreaDetector(), new ClearanceOptions { Mode = mode, Horizon = 20 });
        m.Register(new RobotProfile("A", VehicleClass.Agv, Fp, 1));
        m.Register(new RobotProfile("B", VehicleClass.Amr, Fp, 2));
        return m;
    }

    private static PathCandidate Line(string robot, int rev, params (double X, double Y)[] pts) =>
        new(robot, "M1", rev, robot == "A" ? PathSource.AgvVirtualLine : PathSource.AmrPlanner,
            Polyline.WithTangentHeadings(pts.Select(p => new Vec2(p.X, p.Y)).ToList()).Select(p => new PathPoint(p)).ToList());

    private static RobotView View(ClearanceManager m, string id) => m.Snapshot().Robots.Single(r => r.Profile.RobotId == id);

    private static bool Holds(ClearanceManager m, string id) =>
        View(m, id).Segments.Any(s => s.InConflict && s.State is SegmentState.Granted or SegmentState.Occupied);

    [Fact]
    public void Concurrent_requests_get_one_exclusive_grant()
    {
        var m = Create();
        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        m.SubmitPath(Line("B", 0, (5, 0), (5, 9)), 0);

        Assert.True(Holds(m, "A"));
        Assert.False(Holds(m, "B"));
        Assert.True(View(m, "B").Waiting);
        Assert.Equal(0, m.InternalIncompatible);
    }

    [Fact]
    public void Release_requires_validated_exit_not_progress_claim()
    {
        var m = Create();
        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        m.SubmitPath(Line("B", 0, (5, 0), (5, 9)), 0);

        // A claims it is past the crossing but its real pose is still on it.
        m.ReportState(new StateReport("A", "M1", 0, 1, new Pose2D(5, 4, 0), 7.5, true, 1));
        Assert.True(Holds(m, "A"));
        Assert.False(Holds(m, "B"));

        // Real pose is now clear of the crossing.
        m.ReportState(new StateReport("A", "M1", 0, 2, new Pose2D(7.5, 4, 0), 7.5, true, 2));
        Assert.False(Holds(m, "A"));
        Assert.True(Holds(m, "B"));
    }

    [Fact]
    public void Duplicate_state_does_not_release_twice_or_change_anything()
    {
        var m = Create();
        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        var s = new StateReport("A", "M1", 0, 5, new Pose2D(3, 4, 0), 3, true, 1);
        Assert.Equal(ReportOutcome.Applied, m.ReportState(s));
        long decisions = m.DecisionCount;
        Assert.Equal(ReportOutcome.Duplicate, m.ReportState(s));
        Assert.Equal(ReportOutcome.Duplicate, m.ReportState(s with { Sequence = 4 }));
        Assert.Equal(decisions, m.DecisionCount);
    }

    [Fact]
    public void Revision_revokes_unentered_grants_and_rejects_stale_paths()
    {
        var m = Create();
        var decisions = new List<ClearanceDecision>();
        m.DecisionIssued += decisions.Add;

        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        m.SubmitPath(Line("B", 0, (5, 0), (5, 9)), 0);
        Assert.True(Holds(m, "A"));   // equal request time → fixed priority A

        // A replans before reaching the crossing → its grant is revoked and B gets the area.
        Assert.Equal(SubmitOutcome.Accepted, m.SubmitPath(Line("A", 1, (0, 4), (2, 4), (2, 1)), 1));
        Assert.Contains(decisions, d => d.RobotId == "A" && d.Revision == 0 && d.Status == ClearanceStatus.Revoked);
        Assert.All(View(m, "B").Segments, s => Assert.Equal(SegmentState.Granted, s.State));

        Assert.Equal(SubmitOutcome.Stale, m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 2));
        Assert.Equal(SubmitOutcome.Duplicate, m.SubmitPath(Line("A", 1, (0, 4), (2, 4), (2, 1)), 2));

        // Stale state from revision 0 must not move progress of revision 1.
        Assert.Equal(ReportOutcome.StaleRevision, m.ReportState(new StateReport("A", "M1", 0, 10, new Pose2D(0.5, 4, 0), 4, true, 3)));
        Assert.Equal(0, View(m, "A").Progress);
    }

    [Fact]
    public void Occupied_segment_of_old_revision_stays_reserved_until_exit()
    {
        var m = Create();
        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        m.SubmitPath(Line("B", 0, (5, 0), (5, 9)), 0);
        m.ReportState(new StateReport("A", "M1", 0, 1, new Pose2D(5, 4, 0), 5.0, true, 1)); // A inside crossing

        m.SubmitPath(Line("A", 1, (5, 4), (10, 4)), 2);
        Assert.NotEmpty(View(m, "A").Legacy);
        Assert.False(Holds(m, "B"));

        m.ReportState(new StateReport("A", "M1", 1, 2, new Pose2D(8, 4, 0), 3.0, true, 3));
        Assert.Empty(View(m, "A").Legacy);
    }

    [Fact]
    public void Entering_without_clearance_is_counted()
    {
        var m = Create();
        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        m.SubmitPath(Line("B", 0, (5, 0), (5, 9)), 0);
        var conflictStart = View(m, "B").Segments.First(s => s.InConflict).Segment.StartS;
        m.ReportState(new StateReport("B", "M1", 0, 1, new Pose2D(5, conflictStart + 0.5, Math.PI / 2), conflictStart + 0.5, true, 1));
        Assert.Equal(1, m.OrderViolations);
    }

    [Fact]
    public void Full_path_baseline_holds_whole_path()
    {
        var m = Create(ClearanceMode.FullPathBaseline);
        m.SubmitPath(Line("A", 0, (0, 4), (10, 4)), 0);
        Assert.All(View(m, "A").Segments, s => Assert.Equal(SegmentState.Granted, s.State));
    }
}
