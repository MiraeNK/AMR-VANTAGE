using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Vantage.Core.Paths;
using Vantage.Fleet.Catalog;
using Vantage.Fleet.Project;
using Vantage.Studio.Rendering;

namespace Vantage.Studio.Views;

public sealed record CatalogItem(RobotModelInfo Info, BitmapSource Image)
{
    public string ClassLabel => Info.Class == VehicleClass.Agv ? "AGV" : "AMR";
    public string Spec => $"{Info.Footprint.Length:F2}×{Info.Footprint.Width:F2} m · {Info.MaxSpeed:F1} m/s · {Info.PayloadKg:F0} kg";
}

public partial class AddRobotWindow : Window
{
    private readonly VantageProject _project;
    private bool _idEdited;

    public ProjectRobot? Result { get; private set; }

    public AddRobotWindow(VantageProject project)
    {
        _project = project;
        InitializeComponent();
        Catalog.ItemsSource = RobotCatalog.Models.Select(m => new CatalogItem(m, RobotRenderer.Render(m.Key, 220, 170))).ToList();
        Catalog.SelectedIndex = 0;
        RobotId.TextChanged += (_, _) => { if (RobotId.IsKeyboardFocused) _idEdited = true; };
    }

    private RobotModelInfo Model => ((CatalogItem)Catalog.SelectedItem).Info;

    private void Catalog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Catalog.SelectedItem is null) return;
        if (!_idEdited)
        {
            RobotId.Text = _project.NextRobotId(Model.Class);
            RobotName.Text = $"{Model.Family} {RobotId.Text[^2..]}";
        }
        Priority.Text = Model.Class == VehicleClass.Agv ? "1" : "2";
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (PhysicalFields is null) return;
        bool physical = ModePhysical.IsChecked == true;
        PhysicalFields.Visibility = physical ? Visibility.Visible : Visibility.Collapsed;
        StarterRoute.IsChecked = !physical;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var id = RobotId.Text.Trim();
        if (id.Length == 0 || id.Contains('/') || id.Contains(' '))
        {
            MessageBox.Show(this, "Robot ID must be non-empty and contain no spaces or '/'.", "Add robot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_project.Robots.Any(r => r.Id == id))
        {
            MessageBox.Show(this, $"{id} already exists in this project.", "Add robot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var m = Model;
        var robot = new ProjectRobot
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(RobotName.Text) ? id : RobotName.Text.Trim(),
            Model = m.Key,
            Connection = ModePhysical.IsChecked == true ? RobotConnection.Physical : RobotConnection.Simulated,
            IpAddress = ModePhysical.IsChecked == true ? Ip.Text.Trim() : "",
            Priority = int.TryParse(Priority.Text, out var p) ? p : 2,
            StartDelay = double.TryParse(Delay.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0,
            RouteKind = m.Class == VehicleClass.Agv ? RouteKind.Fixed : RouteKind.Planner,
            InitialBattery = 70 + Random.Shared.Next(0, 28),
        };

        if (StarterRoute.IsChecked == true)
        {
            // straight lane in a free band of the workspace
            var ws = _project.Workspace;
            int lane = _project.Robots.Count;
            double y = ws.MinY + 1.0 + (lane * 1.6) % Math.Max(1, ws.MaxY - ws.MinY - 2);
            bool east = lane % 2 == 0;
            double x0 = ws.MinX + 1.2, x1 = ws.MaxX - 1.6;
            robot.Route =
            [
                new() { X = east ? x0 : x1, Y = y, Node = robot.RouteKind == RouteKind.Fixed ? $"{id}.N0" : null },
                new() { X = east ? x1 : x0, Y = y, Node = robot.RouteKind == RouteKind.Fixed ? $"{id}.N1" : null },
            ];
        }

        Result = robot;
        DialogResult = true;
    }
}
