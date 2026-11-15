using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Fleet.Simulation;

public enum ScenarioAction { Revise, ReviseSameGeometry, DuplicateState, StalePath, PrematureExit }

public sealed record ScenarioEvent(double AtTime, string RobotId, ScenarioAction Action, IReadOnlyList<PathPoint>? Tail = null, int Frames = 3);

public sealed record ScenarioDefinition(
    string Id,
    string Title,
    string ExpectedConflict,
    string CheckedBehaviour,
    bool ConflictExpected,
    Aabb Workspace,
    IReadOnlyList<RobotSpec> Robots,
    IReadOnlyList<ScenarioEvent> Events,
    double TimeLimit = 90)
{
    public override string ToString() => $"{Id} · {Title}";
}

/// <summary>Test geometries of Tabel III.2 / Gambar III.5. One AGV (fixed virtual line) and one AMR (planner path).</summary>
public static class ScenarioLibrary
{
    public static readonly Aabb Workspace = new(0, 0, 14, 8);

    public static Footprint AgvFootprint { get; } = Footprint.Centered(1.00, 0.60);
    public static Footprint AmrFootprint { get; } = Footprint.Centered(0.80, 0.55);

    public static IReadOnlyList<ScenarioDefinition> All { get; } =
    [
        CrossingMidEdge(),
        OpposingCorridor(),
        Merge(),
        NearParallel(),
        RevisionBeforeConflict(),
        DuplicateAndStale(),
    ];

    /// <summary>Use a different AMR as the second vehicle to obtain the AMR–AMR combination.</summary>
    public static ScenarioDefinition WithPairing(ScenarioDefinition s, VehicleClass first, VehicleClass second)
    {
        var robots = s.Robots.ToList();
        robots[0] = Convert(robots[0], first, "01");
        robots[1] = Convert(robots[1], second, "02");
        var map = s.Robots.Select((r, i) => (r.RobotId, robots[i].RobotId)).ToDictionary(x => x.Item1, x => x.Item2);
        var events = s.Events.Select(e => e with { RobotId = map[e.RobotId] }).ToList();
        return s with { Id = $"{s.Id}-{Tag(first)}{Tag(second)}", Robots = robots, Events = events };

        static string Tag(VehicleClass c) => c == VehicleClass.Agv ? "G" : "M";
    }

    private static RobotSpec Convert(RobotSpec r, VehicleClass c, string n) => c == VehicleClass.Agv
        ? r with { RobotId = $"AGV-{n}", DisplayName = $"AGV {n}", Class = c, Source = PathSource.AgvVirtualLine, Footprint = AgvFootprint, MaxSpeed = 0.6, Priority = 1, Model = "agv-tugger" }
        : r with { RobotId = $"AMR-{n}", DisplayName = $"AMR {n}", Class = c, Source = PathSource.AmrPlanner, Footprint = AmrFootprint, MaxSpeed = 0.8, Priority = 2, Model = "amr-compact" };

    private static RobotSpec Agv(IReadOnlyList<PathPoint> route, double delay = 0) =>
        new("AGV-01", "AGV 01", VehicleClass.Agv, PathSource.AgvVirtualLine, AgvFootprint, 1, 0.6, 0.5, route, delay, Model: "agv-tugger");

    private static RobotSpec Amr(IReadOnlyList<PathPoint> route, double delay = 0) =>
        new("AMR-01", "AMR 01", VehicleClass.Amr, PathSource.AmrPlanner, AmrFootprint, 2, 0.8, 0.6, route, delay, Model: "amr-compact", InitialBattery: 78);

    /// <summary>Virtual line: route nodes only at the given vertices.</summary>
    private static List<PathPoint> Line(params (double X, double Y, string Node)[] nodes) =>
        nodes.Select(n => new PathPoint(new Pose2D(n.X, n.Y, 0), n.Node)).ToList();

    /// <summary>Planner-like dense path (10 cm spacing) through the given way points.</summary>
    private static List<PathPoint> Planned(params (double X, double Y)[] wps)
    {
        var pts = new List<PathPoint>();
        for (int i = 0; i < wps.Length - 1; i++)
        {
            var a = new Vec2(wps[i].X, wps[i].Y);
            var b = new Vec2(wps[i + 1].X, wps[i + 1].Y);
            int n = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(a, b) / 0.1));
            for (int k = 0; k < n; k++)
            {
                var p = Vec2.Lerp(a, b, (double)k / n);
                pts.Add(new PathPoint(new Pose2D(p.X, p.Y, 0)));
            }
        }
        pts.Add(new PathPoint(new Pose2D(wps[^1].X, wps[^1].Y, 0)));
        return pts;
    }

    public static ScenarioDefinition CrossingMidEdge() => new(
        "S1", "Persilangan tengah edge",
        "Area sapuan beririsan meski node tidak sama",
        "Izin tidak aktif bersamaan",
        true, Workspace,
        [
            Agv(Line((0.7, 4.2, "N10"), (13.3, 4.2, "N11"))),
            Amr(Planned((6.0, 0.6), (6.0, 7.4)), delay: 3.5),
        ],
        []);

    public static ScenarioDefinition OpposingCorridor() => new(
        "S2", "Koridor berlawanan arah",
        "Ruang koridor hanya cukup untuk satu robot",
        "Robot kedua menunggu hingga area kosong",
        true, Workspace,
        [
            Agv(Line((0.8, 4.0, "N20"), (4.0, 4.0, "N21"), (10.0, 4.0, "N22"), (13.2, 4.0, "N23"))),
            Amr(Planned((13.0, 7.2), (10.5, 4.0), (3.5, 4.0), (1.0, 7.2)), delay: 0.5),
        ],
        []);

    public static ScenarioDefinition Merge() => new(
        "S3", "Penggabungan jalur",
        "Segmen hilir digunakan bersama",
        "Urutan masuk dan pelepasan konsisten",
        true, Workspace,
        [
            Agv(Line((0.8, 4.0, "N30"), (5.0, 4.0, "N31"), (9.0, 4.0, "N32"), (13.2, 4.0, "N33"))),
            Amr(Planned((1.5, 7.4), (5.0, 4.0), (9.0, 4.0), (12.5, 7.4)), delay: 0.0),
        ],
        []);

    public static ScenarioDefinition NearParallel() => new(
        "S4", "Jalur dekat tidak beririsan",
        "Oracle menyatakan tidak konflik",
        "Tidak tertahan tanpa alasan geometri",
        false, Workspace,
        [
            Agv(Line((0.8, 3.0, "N40"), (13.2, 3.0, "N41"))),
            Amr(Planned((13.2, 4.0), (0.8, 4.0))),
        ],
        []);

    public static ScenarioDefinition RevisionBeforeConflict() => new(
        "S5", "Revisi sebelum area konflik",
        "Area sapuan kandidat berubah",
        "Izin revisi lama tidak berlaku",
        true, Workspace,
        [
            Agv(Line((0.7, 4.2, "N50"), (13.3, 4.2, "N51")), delay: 2.0),
            Amr(Planned((6.0, 0.6), (6.0, 7.4))),
        ],
        [
            new ScenarioEvent(2.4, "AMR-01", ScenarioAction.Revise, Planned((6.0, 2.4), (9.5, 2.4), (9.5, 7.4))),
        ]);

    public static ScenarioDefinition DuplicateAndStale() => new(
        "S6", "Pesan duplikat atau usang",
        "Geometri tetap atau revisi lama",
        "Tidak ada grant/release ganda",
        true, Workspace,
        [
            Agv(Line((0.7, 4.2, "N60"), (13.3, 4.2, "N61"))),
            Amr(Planned((6.0, 0.6), (6.0, 7.4)), delay: 0.5),
        ],
        [
            new ScenarioEvent(1.0, "AMR-01", ScenarioAction.ReviseSameGeometry),
            new ScenarioEvent(5.0, "AMR-01", ScenarioAction.StalePath),
            new ScenarioEvent(6.0, "AMR-01", ScenarioAction.DuplicateState, Frames: 5),
            new ScenarioEvent(7.0, "AGV-01", ScenarioAction.DuplicateState, Frames: 5),
            new ScenarioEvent(9.0, "AGV-01", ScenarioAction.PrematureExit, Frames: 8),
        ]);
}
