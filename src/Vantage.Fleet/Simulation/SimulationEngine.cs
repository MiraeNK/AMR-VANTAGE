using System.Diagnostics;
using Vantage.Core.Clearance;
using Vantage.Core.Conflicts;
using Vantage.Core.Paths;
using Vantage.Fleet.Transport;

namespace Vantage.Fleet.Simulation;

public enum DetectorKind { SweptFootprint, NodeIdAblation }

public sealed record EngineOptions
{
    public ClearanceMode Mode { get; init; } = ClearanceMode.Segmented;
    public PriorityPolicy Priority { get; init; } = PriorityPolicy.FirstComeFirstServed;
    public DetectorKind Detector { get; init; } = DetectorKind.SweptFootprint;
    public double SegmentLength { get; init; } = 1.0;
    public double Margin { get; init; } = 0.10;
    public double Horizon { get; init; } = 3.0;
    public double TimeStep { get; init; } = 0.05;
}

/// <summary>
/// Owns one FMR instance (manager + coordinator) and the simulated robots of a
/// scenario. A scenario with no robots is the live mode for real adapters.
/// </summary>
public sealed class SimulationEngine : IAsyncDisposable
{
    private readonly Func<string, Task<IMessageBus>> _connect;
    private readonly List<SimRobot> _robots = new();
    private readonly HashSet<ScenarioEvent> _fired = new();
    private readonly Stopwatch _wall = new();
    private readonly object _stepGate = new();

    public SimulationEngine(ScenarioDefinition? scenario, EngineOptions options, Func<string, Task<IMessageBus>> connect)
    {
        Scenario = scenario;
        Options = options;
        _connect = connect;

        var normalizer = new PathNormalizer(new NormalizerOptions
        {
            SegmentLength = options.SegmentLength,
            Margin = options.Margin,
            Workspace = scenario?.Workspace.Inflate(0.5),
        });
        IConflictDetector detector = options.Detector == DetectorKind.NodeIdAblation ? new NodeIdDetector() : new SweptAreaDetector();
        Manager = new ClearanceManager(normalizer, detector, new ClearanceOptions
        {
            Mode = options.Mode,
            Priority = options.Priority,
            Horizon = options.Horizon,
            ExitMargin = options.Margin,
        });
        Oracle = new OccupancyOracle(new RasterOracle(Manager.FootprintOf, options.Margin));
    }

    public ScenarioDefinition? Scenario { get; }
    public EngineOptions Options { get; }
    public ClearanceManager Manager { get; }
    public FleetCoordinator? Coordinator { get; private set; }
    public OccupancyOracle Oracle { get; }
    public IReadOnlyList<SimRobot> Robots => _robots;
    public bool IsLive => Scenario is null;

    /// <summary>Simulation time in seconds (wall clock in live mode).</summary>
    public double Time { get; private set; }

    /// <summary>All robots reached their goal or the time limit expired. An empty scenario never finishes by itself.</summary>
    public bool Finished => !IsLive && ((_robots.Count > 0 && _robots.All(r => r.Done)) || Time >= Scenario!.TimeLimit);

    public async Task InitializeAsync()
    {
        var fmrBus = await _connect("vantage-fmr");
        Coordinator = new FleetCoordinator(fmrBus, Manager, () => IsLive ? _wall.Elapsed.TotalSeconds : Time);
        await Coordinator.StartAsync();
        _wall.Start();

        if (Scenario is null) return;
        foreach (var spec in Scenario.Robots)
        {
            var bus = await _connect($"vantage-sim-{spec.RobotId}");
            var robot = new SimRobot(spec, bus);
            await robot.ConnectAsync(Time);
            _robots.Add(robot);
        }
    }

    /// <summary>Advances the simulation by one fixed step.</summary>
    public void Step()
    {
        if (IsLive)
        {
            Time = _wall.Elapsed.TotalSeconds;
            return;
        }
        lock (_stepGate)
        {
            if (Finished) return;
            double dt = Options.TimeStep;
            Time += dt;

            foreach (var r in _robots)
                if (!r.Started && Time >= r.Spec.StartDelay) r.BeginMission(Time);

            foreach (var e in Scenario!.Events)
            {
                if (_fired.Contains(e) || Time < e.AtTime) continue;
                var r = _robots.FirstOrDefault(x => x.RobotId == e.RobotId);
                if (r is null || !r.Started) continue;
                _fired.Add(e);
                switch (e.Action)
                {
                    case ScenarioAction.Revise: r.Revise(Time, e.Tail!); break;
                    case ScenarioAction.ReviseSameGeometry: r.ReviseSameGeometry(Time); break;
                    case ScenarioAction.DuplicateState: r.InjectDuplicateStates(e.Frames); break;
                    case ScenarioAction.StalePath: r.InjectStalePath(); break;
                    case ScenarioAction.PrematureExit: r.InjectPrematureExit(e.Frames); break;
                }
            }

            foreach (var r in _robots) r.Step(Time, dt);
            Oracle.Observe(dt, _robots, Manager, Manager.Snapshot());
        }
    }

    public ScenarioResult Result()
    {
        var snap = Manager.Snapshot();
        var segments = Oracle.SeenSegments.ToList();
        var sat = DetectionScore.Evaluate(new SweptAreaDetector(), Oracle.Geometry, segments);
        var node = DetectionScore.Evaluate(new NodeIdDetector(), Oracle.Geometry, segments);
        var links = Coordinator?.Links ?? [];
        return new ScenarioResult(
            Scenario?.Id ?? "LIVE",
            Scenario?.Title ?? "Live",
            Options,
            Time,
            _robots.Select(r => new RobotResult(r.RobotId, r.Spec.Class, r.Done, r.CompletedAt, r.WaitTime, r.HardStops)).ToList(),
            snap.Decisions,
            snap.OrderViolations,
            snap.InternalIncompatible,
            Oracle.OrderViolations,
            Oracle.IncompatibleReservations,
            Oracle.Collisions,
            sat,
            node,
            links.Sum(l => l.DuplicatesDropped),
            links.Sum(l => l.StaleDropped));
    }

    /// <summary>Runs a scenario to completion on the deterministic in-process bus.</summary>
    public static async Task<ScenarioResult> RunHeadlessAsync(ScenarioDefinition scenario, EngineOptions options)
    {
        var hub = new InProcessHub();
        await using var engine = new SimulationEngine(scenario, options, id => Task.FromResult(hub.Connect(id)));
        await engine.InitializeAsync();
        while (!engine.Finished) engine.Step();
        return engine.Result();
    }

    public async ValueTask DisposeAsync()
    {
        if (Coordinator is not null) await Coordinator.DisposeAsync();
    }
}

public sealed record RobotResult(string RobotId, Vantage.Core.Paths.VehicleClass Class, bool Completed, double? CompletedAt, double WaitTime, int HardStops);

public sealed record ScenarioResult(
    string ScenarioId,
    string Title,
    EngineOptions Options,
    double Duration,
    IReadOnlyList<RobotResult> Robots,
    long Decisions,
    int FmrOrderViolations,
    int FmrIncompatible,
    int OracleOrderViolations,
    int OracleIncompatible,
    int Collisions,
    DetectionScore SweptDetection,
    DetectionScore NodeIdDetection,
    long DuplicatesDropped,
    long StaleDropped)
{
    public bool AllCompleted => Robots.All(r => r.Completed);
    public double CompletionRate => Robots.Count == 0 ? 0 : (double)Robots.Count(r => r.Completed) / Robots.Count;

    /// <summary>Hypothesis criteria (III.3.5): no FN, no incompatible grant, no entry without clearance, feasible missions complete.</summary>
    public bool Passed => SweptDetection.FN == 0 && OracleIncompatible == 0 && OracleOrderViolations == 0 && Collisions == 0 && AllCompleted;
}
