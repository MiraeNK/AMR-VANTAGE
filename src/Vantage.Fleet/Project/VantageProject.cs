using System.Text.Json;
using System.Text.Json.Serialization;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;
using Vantage.Fleet.Catalog;
using Vantage.Fleet.Hmi;
using Vantage.Fleet.Simulation;

namespace Vantage.Fleet.Project;

public enum RobotConnection { Simulated, Physical }

/// <summary>Fixed: route is a virtual line through the given nodes. Planner: way points are smoothed into a planner-like path.</summary>
public enum RouteKind { Fixed, Planner }

public sealed class RoutePoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public string? Node { get; set; }
}

public sealed class ProjectRobot
{
    public string Id { get; set; } = "AMR-01";
    public string Name { get; set; } = "AMR 01";
    public string Model { get; set; } = "amr-compact";
    public RobotConnection Connection { get; set; } = RobotConnection.Simulated;
    public string IpAddress { get; set; } = "";
    public int Priority { get; set; } = 2;
    public double StartDelay { get; set; }
    public double InitialBattery { get; set; } = 90;
    public RouteKind RouteKind { get; set; } = RouteKind.Planner;
    public List<RoutePoint> Route { get; set; } = [];

    [JsonIgnore] public RobotModelInfo Info => RobotCatalog.Get(Model);
}

public sealed class WorkspaceBounds
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; } = 14;
    public double MaxY { get; set; } = 8;
    [JsonIgnore] public Aabb Box => new(MinX, MinY, MaxX, MaxY);
}

public sealed class VantageProject
{
    public string Name { get; set; } = "Untitled project";
    public string Description { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Modified { get; set; } = DateTime.Now;
    public WorkspaceBounds Workspace { get; set; } = new();
    public string? MapPath { get; set; }
    public List<ProjectRobot> Robots { get; set; } = [];
    public List<HmiScreen> Screens { get; set; } = [];

    [JsonIgnore] public string? FilePath { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Save(string path)
    {
        Modified = DateTime.Now;
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static VantageProject Load(string path)
    {
        var p = JsonSerializer.Deserialize<VantageProject>(File.ReadAllText(path), JsonOptions) ?? new VantageProject();
        p.FilePath = path;
        return p;
    }

    public string NextRobotId(VehicleClass cls)
    {
        string prefix = cls == VehicleClass.Agv ? "AGV" : "AMR";
        for (int i = 1; ; i++)
        {
            var id = $"{prefix}-{i:D2}";
            if (Robots.All(r => r.Id != id)) return id;
        }
    }

    /// <summary>Simulated robots with a route become a scenario that runs on the same engine as the test scenarios.</summary>
    public ScenarioDefinition ToScenario()
    {
        var specs = new List<RobotSpec>();
        foreach (var r in Robots.Where(r => r.Connection == RobotConnection.Simulated && r.Route.Count >= 2))
        {
            var info = r.Info;
            var points = r.RouteKind == RouteKind.Fixed
                ? r.Route.Select((p, i) => new PathPoint(new Pose2D(p.X, p.Y, 0), p.Node ?? $"{r.Id}.N{i}")).ToList()
                : RouteTools.PlannerPath(r.Route.Select(p => new Vec2(p.X, p.Y)).ToList());
            specs.Add(new RobotSpec(r.Id, r.Name, info.Class,
                r.RouteKind == RouteKind.Fixed ? PathSource.AgvVirtualLine : PathSource.AmrPlanner,
                info.Footprint, r.Priority, info.MaxSpeed, info.Acceleration, points, r.StartDelay,
                Model: r.Model, InitialBattery: r.InitialBattery));
        }
        return new ScenarioDefinition("PRJ", Name, "Derived from the project routes", "All feasible missions complete without incompatible grants",
            true, Workspace.Box, specs, [], 180);
    }

    /// <summary>Starter project: two AGV lines and one AMR crossing them.</summary>
    public static VantageProject CreateDemo()
    {
        var p = new VantageProject { Name = "Demo line A", Description = "Two virtual-line AGVs and a free-navigating AMR in a shared aisle." };
        p.Robots.Add(new ProjectRobot
        {
            Id = "AGV-01", Name = "Stacker 01", Model = "agv-pallet", Priority = 1, RouteKind = RouteKind.Fixed, InitialBattery = 88,
            Route = [new() { X = 1.0, Y = 2.2, Node = "P1" }, new() { X = 7.0, Y = 2.2, Node = "P2" }, new() { X = 12.6, Y = 2.2, Node = "P3" }],
        });
        p.Robots.Add(new ProjectRobot
        {
            Id = "AGV-02", Name = "Tugger 02", Model = "agv-tugger", Priority = 1, RouteKind = RouteKind.Fixed, StartDelay = 2, InitialBattery = 64,
            Route = [new() { X = 12.8, Y = 5.6, Node = "T1" }, new() { X = 4.0, Y = 5.6, Node = "T2" }, new() { X = 1.2, Y = 5.6, Node = "T3" }],
        });
        p.Robots.Add(new ProjectRobot
        {
            Id = "AMR-01", Name = "Polebot 01", Model = "amr-compact", Priority = 2, RouteKind = RouteKind.Planner, StartDelay = 1, InitialBattery = 76,
            Route = [new() { X = 3.0, Y = 0.6 }, new() { X = 4.2, Y = 3.9 }, new() { X = 9.0, Y = 3.9 }, new() { X = 10.0, Y = 7.4 }],
        });
        p.Screens.Add(HmiScreen.CreateDefault());
        return p;
    }
}

public static class RouteTools
{
    /// <summary>Chaikin-smoothed, 10 cm resampled path through way points (ends kept), mimicking a planner output.</summary>
    public static List<PathPoint> PlannerPath(IReadOnlyList<Vec2> wps, int iterations = 2)
    {
        var pts = wps.ToList();
        for (int it = 0; it < iterations && pts.Count > 2; it++)
        {
            var next = new List<Vec2> { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                if (i > 0) next.Add(Vec2.Lerp(a, b, 0.25));
                if (i < pts.Count - 2) next.Add(Vec2.Lerp(a, b, 0.75));
            }
            next.Add(pts[^1]);
            pts = next;
        }
        var outPts = new List<PathPoint>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            int n = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(pts[i], pts[i + 1]) / 0.1));
            for (int k = 0; k < n; k++)
            {
                var q = Vec2.Lerp(pts[i], pts[i + 1], (double)k / n);
                outPts.Add(new PathPoint(new Pose2D(q.X, q.Y, 0)));
            }
        }
        outPts.Add(new PathPoint(new Pose2D(pts[^1].X, pts[^1].Y, 0)));
        return outPts;
    }
}
