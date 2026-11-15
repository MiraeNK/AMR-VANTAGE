using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vantage.Core.Clearance;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;
using Vantage.Fleet.Project;
using Vantage.Fleet.Simulation;
using Vantage.Studio.Controls;
using Vantage.Studio.Services;

namespace Vantage.Studio.ViewModels;

/// <summary>Engineering workspace: live clearance monitoring, segment table, message trace, route editor.</summary>
public sealed partial class MonitorViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppViewModel _app;
    private readonly RuntimeHost _host;
    private readonly DispatcherTimer _ui;
    private readonly Dictionary<string, SegmentRowViewModel> _segmentIndex = new();
    private readonly List<JournalRow> _journalAll = new();
    private int _tick;
    private MapLayer? _map;

    /// <summary>Placeholder for "run the project's own fleet and routes".</summary>
    private static readonly ScenarioDefinition ProjectSentinel = new("PRJ", "Project fleet", "", "", true, ScenarioLibrary.Workspace, [], []);

    public MonitorViewModel(AppViewModel app)
    {
        _app = app;
        _host = app.Host;
        Pairings =
        [
            new("AGV – AMR", (VehicleClass.Agv, VehicleClass.Amr)),
            new("AGV – AGV", (VehicleClass.Agv, VehicleClass.Agv)),
            new("AMR – AMR", (VehicleClass.Amr, VehicleClass.Amr)),
        ];
        Modes = [new("Segmented clearance", ClearanceMode.Segmented), new("Full-path reservation (baseline)", ClearanceMode.FullPathBaseline)];
        Detectors = [new("Swept footprint (SAT)", DetectorKind.SweptFootprint), new("Node-ID equality (ablation)", DetectorKind.NodeIdAblation)];
        Priorities = [new("First come, first served", PriorityPolicy.FirstComeFirstServed), new("Fixed priority", PriorityPolicy.FixedPriority)];
        Transports = [new("MQTT · embedded broker", TransportKind.Mqtt), new("In-process (deterministic)", TransportKind.InProcess)];
        Speeds = [new("0.25×", 0.25), new("0.5×", 0.5), new("1×", 1.0), new("2×", 2.0), new("4×", 4.0), new("8×", 8.0)];
        JournalFilters = ["All", "Decisions", "State", "Warnings", "Violations"];
        BuildScenarioList();

        _selectedScenario = Scenarios[0];
        _selectedPairing = Pairings[0];
        _selectedMode = Modes[0];
        _selectedDetector = Detectors[0];
        _selectedPriority = Priorities[0];
        _selectedTransport = Transports[0];
        _selectedSpeed = Speeds[2];

        _ui = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        _ui.Tick += (_, _) => Refresh();
    }

    // ───────────────────────────── options ─────────────────────────────

    public ObservableCollection<Option<ScenarioDefinition?>> Scenarios { get; } = new();
    public IReadOnlyList<Option<(VehicleClass, VehicleClass)>> Pairings { get; }
    public IReadOnlyList<Option<ClearanceMode>> Modes { get; }
    public IReadOnlyList<Option<DetectorKind>> Detectors { get; }
    public IReadOnlyList<Option<PriorityPolicy>> Priorities { get; }
    public IReadOnlyList<Option<TransportKind>> Transports { get; }
    public IReadOnlyList<Option<double>> Speeds { get; }
    public IReadOnlyList<string> JournalFilters { get; }

    [ObservableProperty] private Option<ScenarioDefinition?> _selectedScenario;
    [ObservableProperty] private Option<(VehicleClass, VehicleClass)> _selectedPairing;
    [ObservableProperty] private Option<ClearanceMode> _selectedMode;
    [ObservableProperty] private Option<DetectorKind> _selectedDetector;
    [ObservableProperty] private Option<PriorityPolicy> _selectedPriority;
    [ObservableProperty] private Option<TransportKind> _selectedTransport;
    [ObservableProperty] private Option<double> _selectedSpeed;
    [ObservableProperty] private string _journalFilter = "All";
    [ObservableProperty] private double _segmentLength = 1.0;
    [ObservableProperty] private double _margin = 0.10;
    [ObservableProperty] private double _horizon = 3.0;

    partial void OnSelectedScenarioChanged(Option<ScenarioDefinition?> value) { if (value is not null) _ = ReloadAsync(); }
    partial void OnSelectedPairingChanged(Option<(VehicleClass, VehicleClass)> value) => _ = ReloadAsync();
    partial void OnSelectedModeChanged(Option<ClearanceMode> value) => _ = ReloadAsync();
    partial void OnSelectedDetectorChanged(Option<DetectorKind> value) => _ = ReloadAsync();
    partial void OnSelectedPriorityChanged(Option<PriorityPolicy> value) => _ = ReloadAsync();
    partial void OnSelectedTransportChanged(Option<TransportKind> value) => _ = ReloadAsync();
    partial void OnSelectedSpeedChanged(Option<double> value) => _host.Speed = value.Value;
    partial void OnJournalFilterChanged(string value) => RebuildJournalView();

    private void BuildScenarioList()
    {
#pragma warning disable MVVMTK0034 // rebuilding the list must not trigger a reload
        var keep = _selectedScenario?.Value?.Id;
        Scenarios.Clear();
        Scenarios.Add(new($"PRJ  Project fleet · {_app.Project.Name}", ProjectSentinel));
        foreach (var s in ScenarioLibrary.All) Scenarios.Add(new($"{s.Id}  {s.Title}", s));
        Scenarios.Add(new("LIVE  External robot adapters (MQTT)", null));
        _selectedScenario = Scenarios.FirstOrDefault(s => s.Value?.Id == keep) ?? Scenarios[0];
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SelectedScenario));
    }

    public bool IsProjectScenario => SelectedScenario?.Value?.Id == "PRJ";

    // ───────────────────────────── layers ─────────────────────────────

    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _showMap = true;
    [ObservableProperty] private bool _showPaths = true;
    [ObservableProperty] private bool _showSweeps = true;
    [ObservableProperty] private bool _showConflicts = true;
    [ObservableProperty] private bool _showReservations = true;
    [ObservableProperty] private bool _showLabels = true;
    [ObservableProperty] private bool _showLimits = true;

    private SceneLayers Layers =>
        (ShowGrid ? SceneLayers.Grid : 0) | (ShowMap ? SceneLayers.Map : 0) | (ShowPaths ? SceneLayers.Paths : 0) |
        (ShowSweeps ? SceneLayers.Sweeps : 0) | (ShowConflicts ? SceneLayers.Conflicts : 0) |
        (ShowReservations ? SceneLayers.Reservations : 0) | (ShowLabels ? SceneLayers.Labels : 0) | (ShowLimits ? SceneLayers.Limits : 0);

    // ───────────────────────────── live state ─────────────────────────────

    [ObservableProperty] private Scene _scene = Scene.Empty;
    [ObservableProperty] private RobotItemViewModel? _selectedRobot;
    [ObservableProperty] private string _runState = "STOPPED";
    [ObservableProperty] private string _timeText = "0.00 s";
    [ObservableProperty] private string _brokerText = "Broker starting…";
    [ObservableProperty] private bool _brokerOk;
    [ObservableProperty] private string _decisionsText = "0";
    [ObservableProperty] private string _violationsText = "0 / 0";
    [ObservableProperty] private bool _hasViolation;
    [ObservableProperty] private string _collisionsText = "0";
    [ObservableProperty] private string _cursorText = "x —  y —";
    [ObservableProperty] private string _conflictText = "0";
    [ObservableProperty] private string _scenarioTitle = "";
    [ObservableProperty] private string _scenarioExpected = "";
    [ObservableProperty] private string _scenarioBehaviour = "";
    [ObservableProperty] private string _verdictText = "—";
    [ObservableProperty] private string _detectionText = "—";
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<RobotItemViewModel> Robots { get; } = new();
    public ObservableCollection<SegmentRowViewModel> Segments { get; } = new();
    public ObservableCollection<JournalRow> Journal { get; } = new();
    public ObservableCollection<TraceRow> Trace { get; } = new();

    [ObservableProperty] private TraceRow? _selectedTrace;
    [ObservableProperty] private string _selectedTracePayload = "";
    partial void OnSelectedTraceChanged(TraceRow? value) => SelectedTracePayload = value is null ? "" : Pretty(value.Payload);

    // ───────────────────────────── lifecycle ─────────────────────────────

    public async Task InitializeAsync()
    {
        BrokerOk = _host.Broker.IsRunning;
        BuildScenarioList();

#pragma warning disable MVVMTK0034 // startup options go to the backing fields so the engine loads once
        if (App.Args.TryGetValue("scenario", out var sid))
            _selectedScenario = Scenarios.FirstOrDefault(s => (s.Value?.Id ?? "LIVE").Equals(sid, StringComparison.OrdinalIgnoreCase)) ?? _selectedScenario;
        if (App.Args.TryGetValue("speed", out var sp) && double.TryParse(sp, CultureInfo.InvariantCulture, out var spv))
            _selectedSpeed = Speeds.FirstOrDefault(s => Math.Abs(s.Value - spv) < 1e-6) ?? _selectedSpeed;
        if (App.Args.TryGetValue("transport", out var tr) && tr.Equals("inproc", StringComparison.OrdinalIgnoreCase))
            _selectedTransport = Transports[1];
#pragma warning restore MVVMTK0034
        OnPropertyChanged(string.Empty);

        LoadProjectMap();
        RefreshRouteRobots();
        await ReloadAsync();
        _ui.Start();
        if (App.Args.ContainsKey("run")) Play();
        if (App.Args.ContainsKey("routes")) IsRouteEditorOpen = true;
    }

    public async Task OnProjectSwitchedAsync()
    {
        BuildScenarioList();
        OnPropertyChanged(nameof(SelectedScenario));
        LoadProjectMap();
        RefreshRouteRobots();
        if (IsProjectScenario) await ReloadAsync();
    }

    private bool _reloading;

    private async Task ReloadAsync()
    {
        if (_reloading) return;
        _reloading = true;
        try
        {
            IsRunning = false;
            var scenario = SelectedScenario.Value;
            if (scenario?.Id == "PRJ") scenario = _app.Project.ToScenario();
            else if (scenario is not null && SelectedPairing.Value is var (a, b) && (a, b) != (VehicleClass.Agv, VehicleClass.Amr))
                scenario = ScenarioLibrary.WithPairing(scenario, a, b);

            await _host.LoadAsync(scenario, Options(), SelectedTransport.Value);
            _host.Speed = SelectedSpeed.Value;

            Robots.Clear();
            Segments.Clear();
            _segmentIndex.Clear();
            Journal.Clear();
            _journalAll.Clear();
            Trace.Clear();
            ScenarioTitle = scenario is null ? "LIVE · external adapters" : scenario.Id == "PRJ" ? $"Project · {_app.Project.Name}" : scenario.ToString();
            ScenarioExpected = scenario?.ExpectedConflict ?? "Robots register with a factsheet on vantage/v1/{id}/factsheet";
            ScenarioBehaviour = scenario?.CheckedBehaviour ?? "Paths and state from real adapters";
            if (scenario?.Id == "PRJ" && scenario.Robots.Count == 0) ScenarioExpected = "No simulated robot with a route yet — add robots in Fleet setup and teach routes in the route editor.";
            RunState = scenario is null ? "LIVE" : "READY";
            VerdictText = "—";
            DetectionText = "—";
            if (scenario is null) Play();
            Refresh();
            OnPropertyChanged(nameof(IsProjectScenario));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FMR Vantage · Load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _reloading = false;
        }
    }

    public EngineOptions Options() => new()
    {
        Mode = SelectedMode.Value,
        Detector = SelectedDetector.Value,
        Priority = SelectedPriority.Value,
        SegmentLength = SegmentLength,
        Margin = Margin,
        Horizon = Horizon,
    };

    // ───────────────────────────── run control ─────────────────────────────

    [RelayCommand]
    private void Play()
    {
        IsDrawing = false;
        _host.Play();
        IsRunning = true;
        RunState = _host.Engine?.IsLive == true ? "LIVE" : "RUNNING";
    }

    [RelayCommand]
    private void Pause()
    {
        _host.Pause();
        IsRunning = false;
        RunState = "PAUSED";
    }

    [RelayCommand]
    private void Step()
    {
        if (IsRunning) return;
        for (int i = 0; i < 2; i++) _host.StepOnce();
        RunState = "PAUSED";
    }

    [RelayCommand]
    private Task Reset() => ReloadAsync();

    [RelayCommand]
    private Task ApplyParameters() => ReloadAsync();

    [RelayCommand]
    private void SelectRobot(string? id) => SelectedRobot = Robots.FirstOrDefault(r => r.Id == id);

    public event Action? FitRequested;

    [RelayCommand]
    private void FitView() => FitRequested?.Invoke();

    // ───────────────────────────── map & export ─────────────────────────────

    private void LoadProjectMap()
    {
        _map = null;
        var path = _app.Project.MapPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try { _map = MapLoader.Load(path); } catch { _map = null; }
    }

    [RelayCommand]
    private void LoadMap()
    {
        var dlg = new OpenFileDialog { Filter = "ROS map (*.yaml)|*.yaml;*.yml|Image (*.pgm;*.png)|*.pgm;*.png;*.bmp|All files|*.*", Title = "Load occupancy map" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _map = MapLoader.Load(dlg.FileName);
            _app.Project.MapPath = dlg.FileName;
            _app.MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FMR Vantage · Map", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ClearMap()
    {
        _map = null;
        _app.Project.MapPath = null;
        _app.MarkDirty();
    }

    [RelayCommand]
    private void ExportJournal()
    {
        var e = _host.Engine;
        if (e is null) return;
        var path = Save("journal", "JSON Lines (*.jsonl)|*.jsonl");
        if (path is null) return;
        var sb = new StringBuilder();
        sb.AppendLine(JsonSerializer.Serialize(new
        {
            manifest = new
            {
                tool = "FMR Vantage",
                version = typeof(MonitorViewModel).Assembly.GetName().Version?.ToString(),
                project = _app.Project.Name,
                scenario = e.Scenario?.Id ?? "LIVE",
                pairing = SelectedPairing.Label,
                options = e.Options,
                exported = DateTime.Now,
            },
        }));
        foreach (var j in e.Manager.Journal)
            sb.AppendLine(JsonSerializer.Serialize(new { t = j.Time, kind = j.Kind.ToString(), robot = j.RobotId, decision = j.DecisionNo, segment = j.SegmentId, message = j.Message }));
        File.WriteAllText(path, sb.ToString());
    }

    [RelayCommand]
    private void ExportTrace()
    {
        var path = Save("trace", "JSON Lines (*.jsonl)|*.jsonl");
        if (path is null) return;
        File.WriteAllLines(path, Trace.Reverse().Select(t => JsonSerializer.Serialize(new { t = t.Time, dir = t.Direction, topic = t.Topic, payload = t.Payload })));
    }

    public static string? Save(string kind, string filter)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            InitialDirectory = App.DataDirectory,
            FileName = $"vantage_{kind}_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    // ───────────────────────────── route editor ─────────────────────────────

    [ObservableProperty] private bool _isRouteEditorOpen;
    [ObservableProperty] private bool _isDrawing;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private ProjectRobot? _routeRobot;
    public ObservableCollection<ProjectRobot> RouteRobots { get; } = new();

    partial void OnIsRouteEditorOpenChanged(bool value) { if (!value) IsDrawing = false; }
    partial void OnRouteRobotChanged(ProjectRobot? value) => RouteChanged(false);

    public bool RouteIsFixed
    {
        get => RouteRobot?.RouteKind == RouteKind.Fixed;
        set { if (RouteRobot is null || !value) return; RouteRobot.RouteKind = RouteKind.Fixed; RouteChanged(true); }
    }

    public bool RouteIsPlanner
    {
        get => RouteRobot?.RouteKind == RouteKind.Planner;
        set { if (RouteRobot is null || !value) return; RouteRobot.RouteKind = RouteKind.Planner; RouteChanged(true); }
    }

    public string RouteInfo
    {
        get
        {
            if (RouteRobot is not { } r) return "Select a robot to edit its route.";
            double len = 0;
            for (int i = 1; i < r.Route.Count; i++) len += Math.Sqrt(Math.Pow(r.Route[i].X - r.Route[i - 1].X, 2) + Math.Pow(r.Route[i].Y - r.Route[i - 1].Y, 2));
            return $"{r.Route.Count} way points · {len:F2} m · {(r.RouteKind == RouteKind.Fixed ? "virtual line, nodes kept" : "planner path, smoothed")}";
        }
    }

    public string RouteRobotModel => RouteRobot?.Info.Name ?? "";

    public void RefreshRouteRobots()
    {
        var keep = RouteRobot?.Id;
        RouteRobots.Clear();
        foreach (var r in _app.Project.Robots) RouteRobots.Add(r);
        RouteRobot = RouteRobots.FirstOrDefault(r => r.Id == keep) ?? RouteRobots.FirstOrDefault();
    }

    private void RouteChanged(bool dirty)
    {
        if (dirty) _app.MarkDirty();
        OnPropertyChanged(nameof(RouteIsFixed));
        OnPropertyChanged(nameof(RouteIsPlanner));
        OnPropertyChanged(nameof(RouteInfo));
        OnPropertyChanged(nameof(RouteRobotModel));
    }

    public void OpenRouteFor(string robotId)
    {
        RefreshRouteRobots();
        RouteRobot = RouteRobots.FirstOrDefault(r => r.Id == robotId) ?? RouteRobot;
        IsRouteEditorOpen = true;
    }

    [RelayCommand]
    private void ToggleDrawing()
    {
        if (RouteRobot is null) return;
        if (IsRunning) Pause();
        IsDrawing = !IsDrawing;
    }

    [RelayCommand]
    private void NewRoute()
    {
        if (RouteRobot is null) return;
        if (IsRunning) Pause();
        RouteRobot.Route.Clear();
        IsDrawing = true;
        RouteChanged(true);
    }

    [RelayCommand]
    private void UndoRoutePoint()
    {
        if (RouteRobot is not { Route.Count: > 0 } r) return;
        r.Route.RemoveAt(r.Route.Count - 1);
        RouteChanged(true);
    }

    [RelayCommand]
    private void ReverseRoute()
    {
        if (RouteRobot is null) return;
        RouteRobot.Route.Reverse();
        RouteChanged(true);
    }

    [RelayCommand]
    private async Task ApplyRoutes()
    {
        IsDrawing = false;
        _app.SaveProject();
        var prj = Scenarios.First(s => s.Value?.Id == "PRJ");
        if (SelectedScenario != prj) SelectedScenario = prj;
        else await ReloadAsync();
    }

    public void AddRoutePoint(double x, double y)
    {
        if (!IsDrawing || RouteRobot is null) return;
        if (SnapToGrid) { x = Math.Round(x * 10) / 10; y = Math.Round(y * 10) / 10; }
        var r = RouteRobot;
        if (r.Route.Count > 0 && Math.Abs(r.Route[^1].X - x) < 1e-6 && Math.Abs(r.Route[^1].Y - y) < 1e-6) return;
        r.Route.Add(new RoutePoint { X = x, Y = y, Node = r.RouteKind == RouteKind.Fixed ? $"{r.Id}.N{r.Route.Count}" : null });
        RouteChanged(true);
    }

    private IReadOnlyList<RouteOverlayItem>? BuildOverlay()
    {
        if (!IsRouteEditorOpen) return null;
        var list = new List<RouteOverlayItem>();
        int agv = 0, amr = 0;
        foreach (var r in _app.Project.Robots.OrderBy(r => r.Id))
        {
            var cls = r.Info.Class;
            var color = Palette.Identity(cls, cls == VehicleClass.Agv ? agv++ : amr++);
            var pts = r.Route.Select(p => new Vec2(p.X, p.Y)).ToList();
            var preview = r.RouteKind == RouteKind.Planner && pts.Count > 2
                ? RouteTools.PlannerPath(pts).Select(p => p.Pose.Position).ToList()
                : pts;
            list.Add(new RouteOverlayItem(r.Id, color, pts, preview, r.RouteKind == RouteKind.Fixed, r == RouteRobot, r.Info.Footprint));
        }
        return list;
    }

    // ───────────────────────────── refresh ─────────────────────────────

    private void Refresh()
    {
        var e = _host.Engine;
        BrokerOk = _host.Broker.IsRunning;
        BrokerText = _host.Broker.IsRunning
            ? $"MQTT 0.0.0.0:{_host.Broker.Port} · {_host.Broker.Clients.Count} clients · {_host.Broker.MessageCount} msgs"
            : _host.BrokerFault is null ? "MQTT broker offline" : "External broker on :1883";
        if (e is null) return;

        DrainQueues();

        var snap = e.Manager.Snapshot();
        var sceneRobots = BuildSceneRobots(e, snap);
        var ws = e.Scenario?.Workspace ?? _app.Project.Workspace.Box;
        Scene = new Scene(snap, sceneRobots, ws, _map, Layers, SelectedRobot?.Id, BuildOverlay(), IsDrawing);

        TimeText = $"{e.Time:F2} s";
        DecisionsText = snap.Decisions.ToString();
        ConflictText = snap.ConflictPairs.Count.ToString();
        int fmrV = snap.OrderViolations + snap.InternalIncompatible;
        int oracleV = e.Oracle.OrderViolations + e.Oracle.IncompatibleReservations;
        ViolationsText = $"{fmrV} / {oracleV}";
        HasViolation = fmrV + oracleV + e.Oracle.Collisions > 0;
        CollisionsText = e.Oracle.Collisions.ToString();

        UpdateRobots(e, snap, sceneRobots);
        if (_tick++ % 8 == 0) UpdateSegments(snap);

        if (!e.IsLive && e.Finished && RunState != "FINISHED")
        {
            RunState = "FINISHED";
            IsRunning = false;
            var r = e.Result();
            VerdictText = r.Passed ? "PASS" : "FAIL";
            DetectionText = $"TP {r.SweptDetection.TP} · FP {r.SweptDetection.FP} · TN {r.SweptDetection.TN} · FN {r.SweptDetection.FN}";
        }
    }

    private void DrainQueues()
    {
        bool journalChanged = false;
        while (_host.JournalQueue.TryDequeue(out var j))
        {
            var row = JournalRow.From(j);
            _journalAll.Add(row);
            if (Matches(row)) { Journal.Insert(0, row); journalChanged = true; }
        }
        if (journalChanged) while (Journal.Count > 1500) Journal.RemoveAt(Journal.Count - 1);
        if (_journalAll.Count > 20000) _journalAll.RemoveRange(0, 5000);

        while (_host.TraceQueue.TryDequeue(out var t))
        {
            Trace.Insert(0, TraceRow.From(t));
            if (Trace.Count > 800) Trace.RemoveAt(Trace.Count - 1);
        }
    }

    private bool Matches(JournalRow r) => JournalFilter switch
    {
        "Decisions" => r.Kind == "DECISION",
        "State" => r.Kind == "STATE",
        "Warnings" => r.Kind is "WARNING" or "VIOLATION",
        "Violations" => r.Kind == "VIOLATION",
        _ => true,
    };

    private void RebuildJournalView()
    {
        Journal.Clear();
        foreach (var r in _journalAll.Where(Matches).Reverse().Take(1500)) Journal.Add(r);
    }

    public static List<SceneRobot> BuildSceneRobots(SimulationEngine e, ClearanceSnapshot snap)
    {
        var list = new List<SceneRobot>();
        int agv = 0, amr = 0;
        foreach (var v in snap.Robots.OrderBy(r => r.Profile.RobotId))
        {
            var cls = v.Profile.Class;
            var color = Palette.Identity(cls, cls == VehicleClass.Agv ? agv++ : amr++);
            var sim = e.Robots.FirstOrDefault(r => r.RobotId == v.Profile.RobotId);
            if (sim is not null)
            {
                bool holding = sim.Started && !sim.Done && sim.V < 1e-3 && sim.S >= sim.Limit - 0.03;
                list.Add(new SceneRobot(sim.RobotId, cls, sim.Spec.Footprint, sim.Pose, sim.V, holding, sim.Done, color));
            }
            else if (v.Pose is { } pose)
            {
                list.Add(new SceneRobot(v.Profile.RobotId, cls, v.Profile.Footprint, pose, 0, v.Waiting, v.MissionDone, color));
            }
        }
        return list;
    }

    private void UpdateRobots(SimulationEngine e, ClearanceSnapshot snap, List<SceneRobot> scene)
    {
        var links = e.Coordinator?.Links ?? [];
        foreach (var v in snap.Robots.OrderBy(r => r.Profile.RobotId))
        {
            var id = v.Profile.RobotId;
            var item = Robots.FirstOrDefault(r => r.Id == id);
            if (item is null)
            {
                item = new RobotItemViewModel(id);
                Robots.Add(item);
                SelectedRobot ??= item;
            }
            var sr = scene.FirstOrDefault(x => x.Id == id);
            var sim = e.Robots.FirstOrDefault(r => r.RobotId == id);
            var fp = v.Profile.Footprint;

            item.ClassLabel = v.Profile.Class == VehicleClass.Agv ? "AGV" : "AMR";
            item.Source = v.Source switch { PathSource.AgvVirtualLine => "Virtual line", PathSource.AmrPlanner => "Planner (Nav2)", PathSource.StaticRoute => "Static route", _ => "—" };
            item.Identity = Brushes2.Of(sr?.Identity ?? Palette.Off);
            item.Mission = v.Revision >= 0 ? $"{v.MissionId} · r{v.Revision}" : "—";
            double progress = sim?.S ?? v.Progress;
            item.ProgressText = $"{progress,6:F2} / {v.PathLength:F2} m";
            item.ProgressRatio = v.PathLength > 0 ? Math.Clamp(progress / v.PathLength, 0, 1) : 0;
            item.LimitRatio = v.PathLength > 0 ? Math.Clamp(v.LimitS / v.PathLength, 0, 1) : 0;
            item.LimitText = v.PathLength > 0 ? $"{v.LimitS:F2} m" : "—";
            item.SpeedText = sim is not null ? $"{sim.V:F2} m/s" : "—";
            item.PoseText = sr is null ? "—" : $"{sr.Pose.X:F2}, {sr.Pose.Y:F2}, {sr.Pose.Theta * 180 / Math.PI:F0}°";
            item.FootprintText = $"{fp.Length:F2} × {fp.Width:F2} m";
            item.PriorityText = v.Profile.Priority.ToString();
            item.DecisionText = v.LastDecision is { } d ? $"#{d.DecisionNo} {d.Status.ToString().ToUpperInvariant()}" : "—";
            item.ReasonText = v.LastDecision?.Reason ?? "";
            item.WaitText = sim is not null ? $"{sim.WaitTime:F1} s" : "—";
            int reserved = v.Segments.Count(s => s.State is SegmentState.Granted or SegmentState.Occupied) + v.Legacy.Count;
            item.SegmentsText = $"{v.Segments.Count} total · {reserved} reserved · {v.Segments.Count(s => s.InConflict)} in conflict";
            var link = links.FirstOrDefault(l => l.RobotId == id);
            double? battery = sim?.Battery ?? link?.Battery;
            item.LinkText = link is null ? "—" : $"{link.ConnectionState} · {link.StatesReceived} st · {link.DuplicatesDropped} dup · {link.StaleDropped} stale";
            if (battery is { } b) item.LinkText += $" · {b:F0} %";

            (item.StateText, item.StateBrush) =
                sr is null ? ("NO POSE", Brushes2.Of(Palette.Off)) :
                sr.Done ? ("GOAL REACHED", Brushes2.Of(Palette.Ok)) :
                sr.Holding ? ("HOLD · CLEARANCE", Brushes2.Of(Palette.Hold)) :
                sim is { Started: false } ? ("STANDBY", Brushes2.Of(Palette.Off)) :
                ("DRIVING", Brushes2.Of(Palette.Accent));
        }
    }

    private void UpdateSegments(ClearanceSnapshot snap)
    {
        var seen = new HashSet<string>();
        foreach (var r in snap.Robots)
        {
            foreach (var (v, legacy) in r.Segments.Select(s => (s, false)).Concat(r.Legacy.Select(s => (s, true))))
            {
                var key = v.Segment.Id;
                seen.Add(key);
                if (!_segmentIndex.TryGetValue(key, out var row))
                {
                    row = new SegmentRowViewModel(key);
                    _segmentIndex[key] = row;
                    Segments.Add(row);
                }
                row.Update(v, legacy);
            }
        }
        foreach (var gone in _segmentIndex.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            Segments.Remove(_segmentIndex[gone]);
            _segmentIndex.Remove(gone);
        }
    }

    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ui.Stop();
        await _host.DisposeAsync();
    }
}
