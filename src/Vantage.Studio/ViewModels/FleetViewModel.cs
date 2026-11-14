using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vantage.Core.Paths;
using Vantage.Fleet.Catalog;
using Vantage.Fleet.Project;
using Vantage.Studio.Controls;
using Vantage.Studio.Rendering;
using Vantage.Studio.Views;

namespace Vantage.Studio.ViewModels;

/// <summary>"+" tile at the start of the card grid.</summary>
public sealed class AddRobotCard;

public sealed partial class RobotCardViewModel(ProjectRobot robot, AppViewModel app) : ObservableObject
{
    public ProjectRobot Robot { get; } = robot;
    public RobotModelInfo Info => Robot.Info;
    public BitmapSource Image => RobotRenderer.Render(Robot.Model, 220, 170);
    public string ClassLabel => Info.Class == VehicleClass.Agv ? "AGV" : "AMR";
    public Brush Identity => Brushes2.Of(Palette.Identity(Info.Class, 0));
    public string FootprintText => $"{Info.Footprint.Length:F2} × {Info.Footprint.Width:F2} m";
    public string SpecText => $"{Info.MaxSpeed:F1} m/s · {Info.PayloadKg:F0} kg";
    public bool IsSimulated => Robot.Connection == RobotConnection.Simulated;
    public string ConnectionLabel => IsSimulated ? "Simulated" : "Physical";
    public string RouteText => Robot.Route.Count < 2 ? "No route" : $"{(Robot.RouteKind == RouteKind.Fixed ? "Virtual line" : "Planner")} · {Robot.Route.Count} pts";

    public string Id => Robot.Id;

    public string Name
    {
        get => Robot.Name;
        set { Robot.Name = value; app.MarkDirty(); OnPropertyChanged(); }
    }

    public string IpAddress
    {
        get => Robot.IpAddress;
        set { Robot.IpAddress = value; app.MarkDirty(); OnPropertyChanged(); OnPropertyChanged(nameof(AddressText)); }
    }

    public int Priority
    {
        get => Robot.Priority;
        set { Robot.Priority = value; app.MarkDirty(); OnPropertyChanged(); }
    }

    public double StartDelay
    {
        get => Robot.StartDelay;
        set { Robot.StartDelay = value; app.MarkDirty(); OnPropertyChanged(); }
    }

    public double InitialBattery
    {
        get => Robot.InitialBattery;
        set { Robot.InitialBattery = Math.Clamp(value, 0, 100); app.MarkDirty(); OnPropertyChanged(); }
    }

    public string AddressText => IsSimulated ? $"sim://{Robot.Id.ToLowerInvariant()}" : string.IsNullOrWhiteSpace(Robot.IpAddress) ? "no address" : Robot.IpAddress;

    [ObservableProperty] private string _status = "OFFLINE";
    [ObservableProperty] private Brush _statusBrush = Brushes2.Of(Palette.Off);
    [ObservableProperty] private double _battery;
    [ObservableProperty] private string _batteryText = "—";
    [ObservableProperty] private Brush _batteryBrush = Brushes2.Of(Palette.Off);
    [ObservableProperty] private string _lastSeen = "never";

    public void RefreshRoute() => OnPropertyChanged(nameof(RouteText));
}

public sealed record DiscoveredRobot(string Id, string Address, string Model, string State, string Battery);

public sealed partial class FleetViewModel : ObservableObject
{
    private readonly AppViewModel _app;
    private readonly DispatcherTimer _timer;

    public FleetViewModel(AppViewModel app)
    {
        _app = app;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => RefreshLive();
        _timer.Start();
    }

    public ObservableCollection<object> Cards { get; } = new();
    public ObservableCollection<DiscoveredRobot> Discovered { get; } = new();
    [ObservableProperty] private RobotCardViewModel? _selected;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _filter = "All";
    public IReadOnlyList<string> Filters { get; } = ["All", "AMR", "AGV", "Simulated", "Physical"];

    partial void OnFilterChanged(string value) => Reload();

    public void Reload()
    {
        var keep = Selected?.Id;
        Cards.Clear();
        Cards.Add(new AddRobotCard());
        foreach (var r in _app.Project.Robots.Where(Pass))
            Cards.Add(new RobotCardViewModel(r, _app));
        Selected = Cards.OfType<RobotCardViewModel>().FirstOrDefault(c => c.Id == keep) ?? Cards.OfType<RobotCardViewModel>().FirstOrDefault();
        RefreshLive();
    }

    private bool Pass(ProjectRobot r) => Filter switch
    {
        "AMR" => r.Info.Class == VehicleClass.Amr,
        "AGV" => r.Info.Class == VehicleClass.Agv,
        "Simulated" => r.Connection == RobotConnection.Simulated,
        "Physical" => r.Connection == RobotConnection.Physical,
        _ => true,
    };

    private void RefreshLive()
    {
        var data = _app.Data;
        var live = data.Robots.ToDictionary(r => r.Id);
        int online = 0;
        foreach (var c in Cards.OfType<RobotCardViewModel>())
        {
            live.TryGetValue(c.Id, out var d);
            var link = data.Link(c.Id);
            double? battery = d?.Battery ?? link?.Battery;
            if (d is { Online: true })
            {
                online++;
                c.Status = c.IsSimulated ? $"SIM · {d.State}" : $"ONLINE · {d.State}";
                c.StatusBrush = Brushes2.Of(d.Holding ? Palette.Hold : Palette.Ok);
                c.LastSeen = "now";
            }
            else
            {
                c.Status = c.IsSimulated ? "SIMULATED · idle" : "OFFLINE";
                c.StatusBrush = Brushes2.Of(c.IsSimulated ? Palette.Accent : Palette.Off);
                c.LastSeen = link is null ? "never" : link.LastSeen.ToString("HH:mm:ss");
                battery ??= c.IsSimulated ? c.InitialBattery : null;
            }
            c.Battery = battery ?? 0;
            c.BatteryText = battery is { } b ? $"{b:F0} %" : "—";
            c.BatteryBrush = Brushes2.Of(battery is null ? Palette.Off : battery < 30 ? Palette.Alarm : battery < 60 ? Palette.Hold : Palette.Ok);
        }

        var known = _app.Project.Robots.Select(r => r.Id).ToHashSet();
        var disc = (_app.Host.Engine?.Coordinator?.Links ?? [])
            .Where(l => !known.Contains(l.RobotId) && !(l.IpAddress ?? "").StartsWith("sim://"))
            .Select(l => new DiscoveredRobot(l.RobotId, l.IpAddress ?? "—", RobotCatalog.Get(l.Model).Name, l.ConnectionState, l.Battery is { } b ? $"{b:F0} %" : "—"))
            .ToList();
        if (!disc.SequenceEqual(Discovered))
        {
            Discovered.Clear();
            foreach (var d in disc) Discovered.Add(d);
        }

        int total = _app.Project.Robots.Count;
        Summary = $"{total} robots · {_app.Project.Robots.Count(r => r.Info.Class == VehicleClass.Amr)} AMR · {_app.Project.Robots.Count(r => r.Info.Class == VehicleClass.Agv)} AGV · {online} active";
    }

    [RelayCommand]
    private void AddRobot()
    {
        var dlg = new AddRobotWindow(_app.Project) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        _app.Project.Robots.Add(dlg.Result);
        _app.MarkDirty();
        Reload();
        Selected = Cards.OfType<RobotCardViewModel>().FirstOrDefault(c => c.Id == dlg.Result.Id);
        _app.Monitor.RefreshRouteRobots();
    }

    [RelayCommand]
    private void Adopt(DiscoveredRobot d)
    {
        _app.Project.Robots.Add(new ProjectRobot
        {
            Id = d.Id, Name = d.Id, Model = RobotCatalog.Models.FirstOrDefault(m => m.Name == d.Model)?.Key ?? "amr-compact",
            Connection = RobotConnection.Physical, IpAddress = d.Address,
        });
        _app.MarkDirty();
        Reload();
    }

    [RelayCommand]
    private void RemoveRobot()
    {
        if (Selected is null) return;
        if (MessageBox.Show($"Remove {Selected.Id} from the project?", "Fleet setup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _app.Project.Robots.Remove(Selected.Robot);
        _app.MarkDirty();
        Reload();
        _app.Monitor.RefreshRouteRobots();
    }

    [RelayCommand]
    private void EditRoute()
    {
        if (Selected is null) return;
        _app.Monitor.OpenRouteFor(Selected.Id);
        _app.Page = Page.Monitor;
    }

    [RelayCommand]
    private void Select(RobotCardViewModel card) => Selected = card;
}
