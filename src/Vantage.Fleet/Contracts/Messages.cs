using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vantage.Fleet.Contracts;

/// <summary>
/// Message contract between FMR and robot adapters. Header, state and response
/// fields follow VDA 5050 3.0 naming where a counterpart exists; the path
/// topic is an internal extension and is documented as such (docs/MESSAGE_CONTRACT.md).
/// </summary>
public static class Topics
{
    public const string Root = "vantage/v1";
    public const string ContractVersion = "1.0.0";

    public static string Path(string robotId) => $"{Root}/{robotId}/path";
    public static string State(string robotId) => $"{Root}/{robotId}/state";
    public static string Clearance(string robotId) => $"{Root}/{robotId}/clearance";
    public static string Connection(string robotId) => $"{Root}/{robotId}/connection";
    public static string Factsheet(string robotId) => $"{Root}/{robotId}/factsheet";
    public static string Command(string robotId) => $"{Root}/{robotId}/command";

    public static string All(string leaf) => $"{Root}/+/{leaf}";

    /// <summary>Robot id from "vantage/v1/{robot}/{leaf}".</summary>
    public static bool TryParse(string topic, out string robotId, out string leaf)
    {
        var parts = topic.Split('/');
        if (parts.Length == 4 && parts[0] == "vantage" && parts[1] == "v1")
        {
            robotId = parts[2];
            leaf = parts[3];
            return true;
        }
        robotId = leaf = "";
        return false;
    }
}

public sealed record Header
{
    public long HeaderId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string Version { get; init; } = Topics.ContractVersion;
    public string Manufacturer { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    /// <summary>Simulation or wall-clock time in seconds; used for logging and replay.</summary>
    public double T { get; init; }
}

public sealed record PoseDto(double X, double Y, double Theta, string? NodeId = null);

public sealed record FootprintDto(double Front, double Rear, double Left, double Right);

/// <summary>Robot capabilities (VDA 5050 factsheet subset).</summary>
public sealed record FactsheetMessage
{
    public Header Header { get; init; } = new();
    public string VehicleClass { get; init; } = "AMR";
    public string DisplayName { get; init; } = "";
    public FootprintDto Footprint { get; init; } = new(0.4, 0.4, 0.3, 0.3);
    public int Priority { get; init; } = 10;
    public double MaxSpeed { get; init; }
    /// <summary>Optional: robot network address, shown in fleet setup.</summary>
    public string? IpAddress { get; init; }
    /// <summary>Optional: catalog model key (e.g. "amr-compact").</summary>
    public string? Model { get; init; }
}

/// <summary>VDA 5050 batteryState subset.</summary>
public sealed record BatteryState(double BatteryCharge = 100, bool Charging = false);

/// <summary>Candidate path (internal extension, not a VDA 5050 topic).</summary>
public sealed record PathMessage
{
    public Header Header { get; init; } = new();
    public string MissionId { get; init; } = "";
    public int Revision { get; init; }
    public string Source { get; init; } = "AmrPlanner";
    public IReadOnlyList<PoseDto> Poses { get; init; } = [];
}

/// <summary>Robot state (VDA 5050 state subset + path progress extension).</summary>
public sealed record StateMessage
{
    public Header Header { get; init; } = new();
    /// <summary>VDA: orderId.</summary>
    public string MissionId { get; init; } = "";
    /// <summary>VDA: orderUpdateId.</summary>
    public int Revision { get; init; }
    public AgvPosition AgvPosition { get; init; } = new();
    public Velocity Velocity { get; init; } = new();
    /// <summary>Extension: arc length travelled along the active revision, metres.</summary>
    public double PathProgress { get; init; }
    public bool Driving { get; init; }
    public string OperatingMode { get; init; } = "AUTOMATIC";
    public IReadOnlyList<string> Errors { get; init; } = [];
    public BatteryState? BatteryState { get; init; }
}

public sealed record AgvPosition
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Theta { get; init; }
    public string MapId { get; init; } = "workspace";
    public bool PositionInitialized { get; init; } = true;
}

public sealed record Velocity(double Vx = 0, double Vy = 0, double Omega = 0);

/// <summary>Clearance response (mirrors VDA 5050 3.0 zone responses: GRANTED / QUEUED / REVOKED / REJECTED).</summary>
public sealed record ClearanceMessage
{
    public Header Header { get; init; } = new();
    public long DecisionNo { get; init; }
    public string MissionId { get; init; } = "";
    public int Revision { get; init; }
    public string Status { get; init; } = "QUEUED";
    public IReadOnlyList<string> Segments { get; init; } = [];
    /// <summary>Arc length on <see cref="Revision"/> the robot may not pass.</summary>
    public double LimitS { get; init; }
    public string Reason { get; init; } = "";
    /// <summary>VDA leaseExpiry. Null: valid until revoked; reservations are never released by time.</summary>
    public DateTime? LeaseExpiry { get; init; }
}

public sealed record ConnectionMessage
{
    public Header Header { get; init; } = new();
    /// <summary>ONLINE, OFFLINE, CONNECTIONBROKEN.</summary>
    public string ConnectionState { get; init; } = "ONLINE";
}

public sealed record CommandMessage
{
    public Header Header { get; init; } = new();
    /// <summary>pause, resume, cancel.</summary>
    public string Command { get; init; } = "";
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
