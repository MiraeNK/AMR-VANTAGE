using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vantage.Studio.ViewModels;

namespace Vantage.Studio.Views;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MonitorViewModel old) old.FitRequested -= Fit;
            if (e.NewValue is MonitorViewModel vm) vm.FitRequested += Fit;
        };
        Workspace.CursorMoved += (x, y) => { if (DataContext is MonitorViewModel vm) vm.CursorText = $"x {x,7:F3}  y {y,7:F3} m"; };
        Workspace.RobotPicked += id => { if (id is not null && DataContext is MonitorViewModel vm) vm.SelectRobotCommand.Execute(id); };
        Workspace.WorldClicked += OnWorldClicked;
    }

    private void Fit() => Workspace.Fit();

    private void OnWorldClicked(double x, double y, MouseButton button)
    {
        if (DataContext is not MonitorViewModel { IsDrawing: true } vm) return;
        if (button == MouseButton.Left) vm.AddRoutePoint(x, y);
        else if (button == MouseButton.Right) vm.UndoRoutePointCommand.Execute(null);
    }

    private void CloseRouteEditor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MonitorViewModel vm) vm.IsRouteEditorOpen = false;
    }
}
