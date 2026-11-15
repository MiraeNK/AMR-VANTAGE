using System.Windows.Media;
using Vantage.Core.Clearance;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;
using Vantage.Studio.Services;

namespace Vantage.Studio.Controls;

[Flags]
public enum SceneLayers
{
    None = 0,
    Grid = 1,
    Map = 2,
    Paths = 4,
    Sweeps = 8,
    Conflicts = 16,
    Reservations = 32,
    Labels = 64,
    Limits = 128,
    All = Grid | Map | Paths | Sweeps | Conflicts | Reservations | Labels | Limits,
}

public sealed record SceneRobot(
    string Id,
    VehicleClass Class,
    Footprint Footprint,
    Pose2D Pose,
    double Speed,
    bool Holding,
    bool Done,
    Color Identity);

/// <summary>Route being edited: way points as taught, and the path the robot will actually follow.</summary>
public sealed record RouteOverlayItem(string RobotId, Color Identity, IReadOnlyList<Vec2> WayPoints, IReadOnlyList<Vec2> Preview,
    bool Fixed, bool Selected, Footprint Footprint);

/// <summary>Immutable frame handed to the workspace view.</summary>
public sealed record Scene(
    ClearanceSnapshot Snapshot,
    IReadOnlyList<SceneRobot> Robots,
    Aabb Workspace,
    MapLayer? Map,
    SceneLayers Layers,
    string? SelectedRobotId,
    IReadOnlyList<RouteOverlayItem>? Routes = null,
    bool Drawing = false)
{
    public static readonly Scene Empty = new(ClearanceSnapshot.Empty, [], new Aabb(0, 0, 14, 8), null, SceneLayers.All, null);
}

public static class Palette
{
    private static readonly Color[] Agv = [Color.FromRgb(0x2F, 0x5D, 0x9A), Color.FromRgb(0x1E, 0x7F, 0x86)];
    private static readonly Color[] Amr = [Color.FromRgb(0x7B, 0x4F, 0xA0), Color.FromRgb(0xA0, 0x4F, 0x6E)];

    public static Color Identity(VehicleClass c, int ordinal) => c == VehicleClass.Agv ? Agv[ordinal % Agv.Length] : Amr[ordinal % Amr.Length];

    public static readonly Color Ok = Color.FromRgb(0x2E, 0x7D, 0x32);
    public static readonly Color Hold = Color.FromRgb(0xB7, 0x6E, 0x00);
    public static readonly Color Alarm = Color.FromRgb(0xC6, 0x28, 0x28);
    public static readonly Color Off = Color.FromRgb(0x8A, 0x93, 0x9C);
    public static readonly Color Accent = Color.FromRgb(0x07, 0x5B, 0xCC);
}
