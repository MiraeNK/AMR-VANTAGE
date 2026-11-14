using Vantage.Core.Paths;
using Vantage.Fleet;
using Vantage.Fleet.Hmi;

namespace Vantage.Studio.Services;

/// <summary>Live fleet values for the HMI and the fleet cards, merged from engine snapshot, simulator and MQTT links.</summary>
public sealed class FleetDataSource(RuntimeHost host) : IHmiDataSource
{
    public IReadOnlyList<HmiRobotData> Robots
    {
        get
        {
            var e = host.Engine;
            if (e is null) return [];
            var snap = e.Manager.Snapshot();
            var links = e.Coordinator?.Links ?? [];
            var list = new List<HmiRobotData>();
            foreach (var v in snap.Robots)
            {
                var id = v.Profile.RobotId;
                var sim = e.Robots.FirstOrDefault(r => r.RobotId == id);
                var link = links.FirstOrDefault(l => l.RobotId == id);
                bool online = sim is not null || link?.ConnectionState == "ONLINE";
                double battery = sim?.Battery ?? link?.Battery ?? 0;
                double speed = sim?.V ?? link?.Speed ?? 0;
                double progress = v.PathLength > 0 ? Math.Clamp((sim?.S ?? v.Progress) / v.PathLength, 0, 1) : 0;
                bool done = sim?.Done ?? v.MissionDone;
                bool holding = sim is not null ? sim.Started && !sim.Done && sim.V < 1e-3 && sim.S >= sim.Limit - 0.03 : v.Waiting;
                string state = !online ? "OFFLINE" : done ? "DONE" : holding ? "HOLD" : speed > 1e-3 ? "DRIVING" : "IDLE";
                var pose = sim?.Pose ?? v.Pose;
                list.Add(new HmiRobotData(id, string.IsNullOrEmpty(v.Profile.DisplayName) ? id : v.Profile.DisplayName,
                    v.Profile.Class == VehicleClass.Agv ? "AGV" : "AMR", sim?.Spec.Model ?? link?.Model ?? "",
                    online, battery, speed, state, progress, pose?.X ?? 0, pose?.Y ?? 0, holding, done));
            }
            return list;
        }
    }

    public HmiSystemData System
    {
        get
        {
            var e = host.Engine;
            if (e is null) return new HmiSystemData(0, 0, 0, 0, host.Broker.IsRunning, false, 0);
            var snap = e.Manager.Snapshot();
            int violations = snap.OrderViolations + snap.InternalIncompatible + e.Oracle.OrderViolations + e.Oracle.IncompatibleReservations;
            return new HmiSystemData(e.Time, snap.Decisions, violations, e.Oracle.Collisions, host.Broker.IsRunning, host.IsRunning, snap.ConflictPairs.Count);
        }
    }

    public RobotLink? Link(string robotId) => host.Engine?.Coordinator?.Links.FirstOrDefault(l => l.RobotId == robotId);

    public void SendCommand(string robotId, string command)
    {
        var c = host.Engine?.Coordinator;
        if (c is not null) _ = c.SendCommandAsync(robotId, command);
    }
}
