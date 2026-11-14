using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Fleet.Catalog;

/// <summary>A robot type that can be added to a project (demo / simulation or as template for a physical robot).</summary>
public sealed record RobotModelInfo(
    string Key,
    string Name,
    string Family,
    VehicleClass Class,
    Footprint Footprint,
    double MaxSpeed,
    double Acceleration,
    double PayloadKg,
    string Navigation,
    string Description);

public static class RobotCatalog
{
    public static IReadOnlyList<RobotModelInfo> Models { get; } =
    [
        new("amr-compact", "VT-AMR 300", "Compact AMR", VehicleClass.Amr, Footprint.Centered(0.80, 0.55), 0.8, 0.6, 300,
            "Natural navigation · Nav2 planner", "Low-profile mobile robot with top module, 2D safety lidar front and rear."),
        new("amr-heavy", "VT-AMR 1000", "Heavy-duty AMR", VehicleClass.Amr, Footprint.Centered(1.20, 0.85), 0.6, 0.4, 1000,
            "Natural navigation · Nav2 planner", "Platform AMR for pallets and carts, lift top, 360° scanner coverage."),
        new("agv-pallet", "VT-AGV PS12", "Pallet stacker AGV", VehicleClass.Agv, new Footprint(1.25, 0.45, 0.45, 0.45), 0.5, 0.4, 1200,
            "Virtual line · fixed route", "Automated pallet stacker with mast and forks; follows taught virtual lines."),
        new("agv-tugger", "VT-AGV T60", "Tugger AGV", VehicleClass.Agv, Footprint.Centered(1.00, 0.60), 0.6, 0.5, 600,
            "Virtual line · fixed route", "Tow vehicle for cart trains on fixed loops."),
    ];

    public static RobotModelInfo Get(string? key) =>
        Models.FirstOrDefault(m => m.Key == key) ?? Models[0];
}
