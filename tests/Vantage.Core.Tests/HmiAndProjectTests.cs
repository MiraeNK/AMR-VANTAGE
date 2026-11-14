using Vantage.Fleet.Hmi;
using Vantage.Fleet.Project;
using Vantage.Fleet.Simulation;

namespace Vantage.Core.Tests;

public class HmiAndProjectTests
{
    private sealed class FakeData : IHmiDataSource
    {
        public List<(string Robot, string Cmd)> Sent { get; } = new();
        public IReadOnlyList<HmiRobotData> Robots { get; set; } =
        [
            new("AMR-01", "Polebot", "AMR", "amr-compact", true, 25, 0.5, "DRIVING", 0.5, 1, 2, false, false),
            new("AGV-01", "Stacker", "AGV", "agv-pallet", true, 80, 0, "HOLD", 0.2, 3, 4, true, false),
        ];
        public HmiSystemData System { get; set; } = new(12.5, 40, 0, 0, true, true, 3);
        public void SendCommand(string robotId, string command) => Sent.Add((robotId, command));
    }

    [Fact]
    public void Default_screen_graph_drives_widgets()
    {
        var screen = HmiScreen.CreateDefault();
        var data = new FakeData();
        var engine = new HmiEngine();
        var v = engine.Evaluate(screen, data);

        Assert.Equal(true, v[("lampBroker", "On")]);
        Assert.Equal(true, v[("lampSafe", "On")]);              // violations == 0
        Assert.Equal(25.0, v[("barAmr", "Value")]);
        Assert.Equal("#C62828", v[("barAmr", "Color")]);         // 25 % < low threshold
        Assert.Equal(25.0, v[("trendBat", "Value")]);            // fleet min battery
        Assert.Equal(1.0, v[("valHold", "Value")]);

        data.System = data.System with { Violations = 2 };
        Assert.Equal(false, engine.Evaluate(screen, data)[("lampSafe", "On")]);
    }

    [Fact]
    public void Button_press_fires_command_exactly_once()
    {
        var screen = HmiScreen.CreateDefault();
        var data = new FakeData();
        var engine = new HmiEngine();
        engine.Press("btnPause");
        engine.Evaluate(screen, data);
        engine.Evaluate(screen, data);
        Assert.Equal([("AMR-01", "pause")], data.Sent);
    }

    [Fact]
    public void Graph_cycles_do_not_hang()
    {
        var s = new HmiScreen();
        s.Nodes.Add(new HmiNode { Id = "a", Type = "math" });
        s.Nodes.Add(new HmiNode { Id = "b", Type = "math" });
        s.Nodes.Add(new HmiNode { Id = "c", Type = "set", Params = new() { ["widget"] = "w", ["property"] = "Value" } });
        s.Links.Add(new HmiLink { FromNode = "a", FromPort = "out", ToNode = "b", ToPort = "a" });
        s.Links.Add(new HmiLink { FromNode = "b", FromPort = "out", ToNode = "a", ToPort = "a" });
        s.Links.Add(new HmiLink { FromNode = "b", FromPort = "out", ToNode = "c", ToPort = "value" });
        var v = new HmiEngine().Evaluate(s, new FakeData());
        Assert.True(v.ContainsKey(("w", "Value")));
    }

    [Fact]
    public async Task Demo_project_runs_safely()
    {
        var p = VantageProject.CreateDemo();
        var scenario = p.ToScenario();
        Assert.Equal(3, scenario.Robots.Count);
        var r = await SimulationEngine.RunHeadlessAsync(scenario, new EngineOptions());
        Assert.Equal(0, r.OracleIncompatible);
        Assert.Equal(0, r.OracleOrderViolations);
        Assert.Equal(0, r.Collisions);
        Assert.True(r.AllCompleted, string.Join(", ", r.Robots.Select(x => $"{x.RobotId}:{x.Completed}")));
    }

    [Fact]
    public void Project_round_trips_through_json()
    {
        var p = VantageProject.CreateDemo();
        var path = Path.Combine(Path.GetTempPath(), $"vantage_{Guid.NewGuid():N}.vantage");
        p.Save(path);
        var q = VantageProject.Load(path);
        File.Delete(path);
        Assert.Equal(p.Robots.Count, q.Robots.Count);
        Assert.Equal(p.Robots[0].RouteKind, q.Robots[0].RouteKind);
        Assert.Equal(p.Screens[0].Widgets.Count, q.Screens[0].Widgets.Count);
        Assert.Equal(p.Screens[0].Links.Count, q.Screens[0].Links.Count);
    }
}
