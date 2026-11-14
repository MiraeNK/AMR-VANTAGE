using Vantage.Core.Clearance;
using Vantage.Core.Paths;
using Vantage.Fleet.Simulation;
using Xunit.Abstractions;

namespace Vantage.Core.Tests;

/// <summary>Directed tests: every critical case of Tabel III.2 must pass as its own case.</summary>
public class ScenarioTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Scenarios() => ScenarioLibrary.All.Select(s => new object[] { s.Id });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Scenario_passes_hypothesis_criteria(string id)
    {
        var scenario = ScenarioLibrary.All.Single(s => s.Id == id);
        var r = await SimulationEngine.RunHeadlessAsync(scenario, new EngineOptions());
        output.WriteLine($"{r.ScenarioId} t={r.Duration:F1}s decisions={r.Decisions} FN={r.SweptDetection.FN} FP={r.SweptDetection.FP} " +
                         $"incompat={r.OracleIncompatible} order={r.OracleOrderViolations} coll={r.Collisions} " +
                         string.Join(" ", r.Robots.Select(x => $"{x.RobotId}:{(x.Completed ? "done" : "NOT")}@{x.CompletedAt:F1} wait={x.WaitTime:F1} hs={x.HardStops}")));

        Assert.Equal(0, r.SweptDetection.FN);
        Assert.Equal(0, r.OracleIncompatible);
        Assert.Equal(0, r.OracleOrderViolations);
        Assert.Equal(0, r.FmrIncompatible);
        Assert.Equal(0, r.Collisions);
        Assert.True(r.AllCompleted, "all feasible missions must complete");
        if (!scenario.ConflictExpected) Assert.All(r.Robots, x => Assert.True(x.WaitTime < 0.5, $"{x.RobotId} held without geometric reason"));
    }

    [Theory]
    [InlineData(VehicleClass.Agv, VehicleClass.Agv)]
    [InlineData(VehicleClass.Amr, VehicleClass.Amr)]
    public async Task Pairings_pass_crossing_and_corridor(VehicleClass a, VehicleClass b)
    {
        foreach (var s in new[] { ScenarioLibrary.CrossingMidEdge(), ScenarioLibrary.OpposingCorridor() })
        {
            var r = await SimulationEngine.RunHeadlessAsync(ScenarioLibrary.WithPairing(s, a, b), new EngineOptions());
            Assert.True(r.Passed, $"{r.ScenarioId} failed");
        }
    }

    [Fact]
    public async Task Duplicate_and_stale_messages_are_dropped()
    {
        var r = await SimulationEngine.RunHeadlessAsync(ScenarioLibrary.DuplicateAndStale(), new EngineOptions());
        Assert.True(r.DuplicatesDropped >= 10);
        Assert.True(r.Passed);
    }

    [Fact]
    public async Task Full_path_baseline_is_safe_but_waits_longer()
    {
        var s = ScenarioLibrary.Merge();
        var seg = await SimulationEngine.RunHeadlessAsync(s, new EngineOptions());
        var full = await SimulationEngine.RunHeadlessAsync(s, new EngineOptions { Mode = ClearanceMode.FullPathBaseline });
        output.WriteLine($"segmented wait={seg.Robots.Sum(x => x.WaitTime):F1}s  full-path wait={full.Robots.Sum(x => x.WaitTime):F1}s");
        Assert.True(full.Passed);
        Assert.True(full.Robots.Sum(x => x.WaitTime) >= seg.Robots.Sum(x => x.WaitTime));
    }

    [Fact]
    public async Task Node_id_ablation_misses_mid_edge_crossing()
    {
        var r = await SimulationEngine.RunHeadlessAsync(ScenarioLibrary.CrossingMidEdge(), new EngineOptions { Detector = DetectorKind.NodeIdAblation });
        Assert.True(r.NodeIdDetection.FN > 0);
    }
}
