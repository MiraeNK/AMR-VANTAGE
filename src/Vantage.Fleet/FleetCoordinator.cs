using System.Collections.Concurrent;
using Vantage.Core.Clearance;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;
using Vantage.Fleet.Contracts;
using Vantage.Fleet.Transport;

namespace Vantage.Fleet;

public enum TraceDirection { In, Out }

public sealed record TraceEntry(double Time, DateTime Wall, TraceDirection Direction, string Topic, string RobotId, string Kind, int Bytes, string Summary, string Payload);

public sealed record RobotLink(string RobotId, string ConnectionState, DateTime LastSeen, long StatesReceived, long DuplicatesDropped, long StaleDropped,
    double? Battery = null, string? IpAddress = null, string? Model = null, double Speed = 0, string Manufacturer = "");

/// <summary>
/// FMR side of the message contract: turns factsheet/path/state messages into
/// calls on <see cref="ClearanceManager"/> and publishes every decision back to the robot.
/// The transport only carries messages; validity is decided by the manager.
/// </summary>
public sealed class FleetCoordinator : IAsyncDisposable
{
    private readonly IMessageBus _bus;
    private readonly Func<double> _clock;
    private readonly ConcurrentDictionary<string, LinkInfo> _links = new();
    private long _headerId;

    public FleetCoordinator(IMessageBus bus, ClearanceManager manager, Func<double> clock)
    {
        _bus = bus;
        Manager = manager;
        _clock = clock;
        Manager.DecisionIssued += OnDecision;
    }

    public ClearanceManager Manager { get; }
    public event Action<TraceEntry>? Traced;

    public IReadOnlyList<RobotLink> Links =>
        _links.Select(kv => new RobotLink(kv.Key, kv.Value.State, kv.Value.LastSeen, kv.Value.States, kv.Value.Duplicates, kv.Value.Stale,
            kv.Value.Battery, kv.Value.Ip, kv.Value.Model, kv.Value.Speed, kv.Value.Manufacturer)).ToList();

    public async Task StartAsync()
    {
        await _bus.SubscribeAsync(Topics.All("factsheet"), OnMessage);
        await _bus.SubscribeAsync(Topics.All("connection"), OnMessage);
        await _bus.SubscribeAsync(Topics.All("path"), OnMessage);
        await _bus.SubscribeAsync(Topics.All("state"), OnMessage);
    }

    private LinkInfo Link(string id) => _links.GetOrAdd(id, _ => new LinkInfo());

    private void OnMessage(string topic, string payload)
    {
        if (!Topics.TryParse(topic, out var robot, out var leaf)) return;
        double now = _clock();
        try
        {
            switch (leaf)
            {
                case "factsheet":
                {
                    var m = Json.Read<FactsheetMessage>(payload)!;
                    var cls = m.VehicleClass.Equals("AGV", StringComparison.OrdinalIgnoreCase) ? VehicleClass.Agv : VehicleClass.Amr;
                    var fp = new Footprint(m.Footprint.Front, m.Footprint.Rear, m.Footprint.Left, m.Footprint.Right);
                    Manager.Register(new RobotProfile(robot, cls, fp, m.Priority, m.DisplayName));
                    var fl = Link(robot);
                    fl.Ip = m.IpAddress;
                    fl.Model = m.Model;
                    fl.Manufacturer = m.Header.Manufacturer;
                    Trace(now, TraceDirection.In, topic, robot, "factsheet", payload, $"{m.VehicleClass} {fp.Length:F2}×{fp.Width:F2} m prio {m.Priority}");
                    break;
                }
                case "connection":
                {
                    var m = Json.Read<ConnectionMessage>(payload)!;
                    var l = Link(robot);
                    l.State = m.ConnectionState;
                    l.LastSeen = DateTime.Now;
                    Trace(now, TraceDirection.In, topic, robot, "connection", payload, m.ConnectionState);
                    break;
                }
                case "path":
                {
                    var m = Json.Read<PathMessage>(payload)!;
                    var source = Enum.TryParse<PathSource>(m.Source, true, out var s) ? s : PathSource.AmrPlanner;
                    var candidate = new PathCandidate(robot, m.MissionId, m.Revision, source,
                        m.Poses.Select(p => new PathPoint(new Pose2D(p.X, p.Y, p.Theta), p.NodeId)).ToList());
                    var outcome = Manager.SubmitPath(candidate, now);
                    Trace(now, TraceDirection.In, topic, robot, "path", payload, $"{m.MissionId} r{m.Revision} · {m.Poses.Count} poses · {outcome}");
                    break;
                }
                case "state":
                {
                    var m = Json.Read<StateMessage>(payload)!;
                    var l = Link(robot);
                    l.LastSeen = DateTime.Now;
                    l.State = "ONLINE";
                    if (m.BatteryState is { } bs) l.Battery = bs.BatteryCharge;
                    l.Speed = m.Velocity.Vx;
                    var r = Manager.ReportState(new StateReport(robot, m.MissionId, m.Revision, m.Header.HeaderId,
                        new Pose2D(m.AgvPosition.X, m.AgvPosition.Y, m.AgvPosition.Theta), m.PathProgress, m.Driving, now));
                    switch (r)
                    {
                        case ReportOutcome.Applied: l.States++; break;
                        case ReportOutcome.Duplicate: l.Duplicates++; break;
                        case ReportOutcome.StaleRevision: l.Stale++; break;
                    }
                    if (r != ReportOutcome.Applied || l.States % 10 == 0)
                        Trace(now, TraceDirection.In, topic, robot, "state", payload,
                            $"#{m.Header.HeaderId} r{m.Revision} s={m.PathProgress:F2} m · {r}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Trace(now, TraceDirection.In, topic, robot, "error", payload, $"Rejected: {ex.Message}");
        }
    }

    private void OnDecision(ClearanceDecision d)
    {
        var msg = new ClearanceMessage
        {
            Header = new Header { HeaderId = Interlocked.Increment(ref _headerId), Manufacturer = "VANTAGE", SerialNumber = "FMR", T = d.Time },
            DecisionNo = d.DecisionNo,
            MissionId = d.MissionId,
            Revision = d.Revision,
            Status = d.Status.ToString().ToUpperInvariant(),
            Segments = d.SegmentIds,
            LimitS = d.LimitS,
            Reason = d.Reason,
        };
        var json = Json.Write(msg);
        var topic = Topics.Clearance(d.RobotId);
        Trace(d.Time, TraceDirection.Out, topic, d.RobotId, "clearance", json, $"#{d.DecisionNo} {msg.Status} limit {d.LimitS:F2} m");
        _ = _bus.PublishAsync(topic, json);
    }

    /// <summary>Operator command (pause / resume) to one robot.</summary>
    public Task SendCommandAsync(string robotId, string command)
    {
        var json = Json.Write(new CommandMessage
        {
            Header = new Header { HeaderId = Interlocked.Increment(ref _headerId), Manufacturer = "VANTAGE", SerialNumber = "FMR", T = _clock() },
            Command = command,
        });
        var topic = Topics.Command(robotId);
        Trace(_clock(), TraceDirection.Out, topic, robotId, "command", json, command.ToUpperInvariant());
        return _bus.PublishAsync(topic, json);
    }

    private void Trace(double t, TraceDirection dir, string topic, string robot, string kind, string payload, string summary) =>
        Traced?.Invoke(new TraceEntry(t, DateTime.Now, dir, topic, robot, kind, payload.Length, summary, payload));

    public async ValueTask DisposeAsync()
    {
        Manager.DecisionIssued -= OnDecision;
        await _bus.DisposeAsync();
    }

    private sealed class LinkInfo
    {
        public string State { get; set; } = "UNKNOWN";
        public DateTime LastSeen { get; set; }
        public long States { get; set; }
        public long Duplicates { get; set; }
        public long Stale { get; set; }
        public double? Battery { get; set; }
        public string? Ip { get; set; }
        public string? Model { get; set; }
        public double Speed { get; set; }
        public string Manufacturer { get; set; } = "";
    }
}
