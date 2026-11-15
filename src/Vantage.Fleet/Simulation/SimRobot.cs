using Vantage.Core.Clearance;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;
using Vantage.Fleet.Contracts;
using Vantage.Fleet.Transport;

namespace Vantage.Fleet.Simulation;

public sealed record RobotSpec(
    string RobotId,
    string DisplayName,
    VehicleClass Class,
    PathSource Source,
    Footprint Footprint,
    int Priority,
    double MaxSpeed,
    double Acceleration,
    IReadOnlyList<PathPoint> Route,
    double StartDelay = 0,
    string MissionId = "M1",
    string Model = "",
    double InitialBattery = 92);

/// <summary>
/// Simulated robot plus its adapter. The adapter enforces the clearance limit:
/// the reference point never passes LimitS of the current revision, braking at
/// constant deceleration so it stops before the boundary.
/// </summary>
public sealed class SimRobot
{
    private readonly object _gate = new();
    private readonly IMessageBus _bus;
    private List<PathPoint> _points = [];
    private List<Pose2D> _poses = [];
    private double _length;
    private double _limit;
    private long _lastDecision;
    private long _seq;
    private double _nextState;
    private int _duplicateFrames;
    private int _prematureFrames;
    private bool _paused;
    private PathMessage? _previousPathMessage;

    public SimRobot(RobotSpec spec, IMessageBus bus)
    {
        Spec = spec;
        _bus = bus;
        Pose = spec.Route[0].Pose;
        Battery = spec.InitialBattery;
    }

    /// <summary>State of charge, percent. Drains with distance and time.</summary>
    public double Battery { get; private set; }

    public RobotSpec Spec { get; }
    public string RobotId => Spec.RobotId;
    public Pose2D Pose { get; private set; }
    public double S { get; private set; }
    public double V { get; private set; }
    public int Revision { get; private set; } = -1;
    public string MissionId => Spec.MissionId;
    public bool Started { get; private set; }
    public bool Done { get; private set; }
    public double? CompletedAt { get; private set; }
    public double WaitTime { get; private set; }
    public int HardStops { get; private set; }
    public double Limit { get { lock (_gate) return _limit; } }
    public double PathLength => _length;
    public IReadOnlyList<Pose2D> PathPoses => _poses;
    public string LastStatus { get; private set; } = "IDLE";
    public double StateInterval { get; set; } = 0.1;

    public async Task ConnectAsync(double now)
    {
        await _bus.SubscribeAsync(Topics.Clearance(RobotId), OnClearance);
        await _bus.SubscribeAsync(Topics.Command(RobotId), OnCommand);
        var fp = Spec.Footprint;
        await _bus.PublishAsync(Topics.Factsheet(RobotId), Json.Write(new FactsheetMessage
        {
            Header = H(now),
            VehicleClass = Spec.Class == VehicleClass.Agv ? "AGV" : "AMR",
            DisplayName = Spec.DisplayName,
            Footprint = new FootprintDto(fp.Front, fp.Rear, fp.Left, fp.Right),
            Priority = Spec.Priority,
            MaxSpeed = Spec.MaxSpeed,
            IpAddress = "sim://" + RobotId.ToLowerInvariant(),
            Model = Spec.Model,
        }), retain: true);
        await _bus.PublishAsync(Topics.Connection(RobotId), Json.Write(new ConnectionMessage { Header = H(now), ConnectionState = "ONLINE" }), retain: true);
    }

    public void BeginMission(double now)
    {
        Started = true;
        Publish(now, Spec.Route, 0);
    }

    /// <summary>Planner replans from the current pose (AMR), new revision.</summary>
    public void Revise(double now, IReadOnlyList<PathPoint> tail)
    {
        var start = new PathPoint(Pose);
        Publish(now, [start, .. tail], Revision + 1);
    }

    /// <summary>Same geometry, new revision number (remaining part of the current path).</summary>
    public void ReviseSameGeometry(double now)
    {
        var remaining = new List<PathPoint> { new(Pose) };
        double acc = 0;
        for (int i = 1; i < _points.Count; i++)
        {
            acc += Vec2.Distance(_points[i - 1].Pose.Position, _points[i].Pose.Position);
            if (acc > S + 1e-3) remaining.Add(_points[i]);
        }
        Publish(now, remaining, Revision + 1);
    }

    public void InjectStalePath()
    {
        if (_previousPathMessage is { } old) _ = _bus.PublishAsync(Topics.Path(RobotId), Json.Write(old));
    }

    public void InjectDuplicateStates(int frames) => _duplicateFrames = frames;
    public void InjectPrematureExit(int frames) => _prematureFrames = frames;

    private void Publish(double now, IReadOnlyList<PathPoint> points, int revision)
    {
        var poses = Polyline.WithTangentHeadings(points.Select(p => p.Pose.Position).ToList());
        lock (_gate)
        {
            _points = points.Select((p, i) => new PathPoint(poses[i], p.NodeId)).ToList();
            _poses = poses;
            _length = Polyline.Length(poses);
            S = 0;
            _limit = 0;
            _lastDecision = 0;
            Revision = revision;
        }
        var msg = new PathMessage
        {
            Header = H(now),
            MissionId = MissionId,
            Revision = revision,
            Source = Spec.Source.ToString(),
            Poses = _points.Select(p => new PoseDto(p.Pose.X, p.Pose.Y, p.Pose.Theta, p.NodeId)).ToList(),
        };
        var json = Json.Write(msg);
        _ = _bus.PublishAsync(Topics.Path(RobotId), json);
        _previousPathMessage = _lastPathMessage;
        _lastPathMessage = msg;
    }

    private PathMessage? _lastPathMessage;

    public void Step(double now, double dt)
    {
        if (!Started) return;

        double allowed;
        lock (_gate) allowed = Math.Min(_limit, _length);

        if (!Done)
        {
            double a = Spec.Acceleration;
            double remaining = allowed - S;
            double brake = V * V / (2 * a);
            if (_paused || remaining <= brake + 1e-4) V = Math.Max(0, V - a * dt);
            else V = Math.Min(Spec.MaxSpeed, V + a * dt);

            double ds = V * dt;
            if (S + ds > allowed)
            {
                if (S + ds - allowed > 0.01) HardStops++;
                ds = Math.Max(0, allowed - S);
                V = 0;
            }
            S += ds;
            Battery = Math.Max(0, Battery - ds * 0.12 - dt * 0.002);
            if (_poses.Count > 0) Pose = Polyline.At(_poses, S);

            if (V < 1e-3 && S < _length - 1e-3) WaitTime += dt;
            if (_length > 0 && S >= _length - 1e-3)
            {
                Done = true;
                CompletedAt = now;
                V = 0;
            }
        }

        if (now + 1e-9 >= _nextState)
        {
            _nextState = now + StateInterval;
            SendState(now);
        }
    }

    private void SendState(double now)
    {
        long id = ++_seq;
        double progress = S;
        if (_prematureFrames > 0)
        {
            _prematureFrames--;
            progress = Math.Min(Limit, S + 1.5);
        }
        var msg = new StateMessage
        {
            Header = H(now) with { HeaderId = id },
            MissionId = MissionId,
            Revision = Revision,
            AgvPosition = new AgvPosition { X = Pose.X, Y = Pose.Y, Theta = Pose.Theta },
            Velocity = new Velocity(V, 0, 0),
            PathProgress = progress,
            Driving = V > 1e-3,
            BatteryState = new BatteryState(Math.Round(Battery, 1)),
        };
        var json = Json.Write(msg);
        _ = _bus.PublishAsync(Topics.State(RobotId), json);
        while (_duplicateFrames > 0)
        {
            _duplicateFrames--;
            _ = _bus.PublishAsync(Topics.State(RobotId), json);
        }
    }

    private void OnClearance(string topic, string payload)
    {
        var m = Json.Read<ClearanceMessage>(payload);
        if (m is null) return;
        lock (_gate)
        {
            // Adapter-side validity: only the active revision, only newer decisions.
            if (m.MissionId != MissionId || m.Revision != Revision) return;
            if (m.DecisionNo <= _lastDecision) return;
            _lastDecision = m.DecisionNo;
            _limit = m.LimitS;
            LastStatus = m.Status;
        }
    }

    private void OnCommand(string topic, string payload)
    {
        var m = Json.Read<CommandMessage>(payload);
        if (m is null) return;
        if (m.Command == "pause") _paused = true;
        else if (m.Command == "resume") _paused = false;
    }

    private Header H(double now) => new() { HeaderId = ++_seq, Manufacturer = "VANTAGE-SIM", SerialNumber = RobotId, T = now };
}
