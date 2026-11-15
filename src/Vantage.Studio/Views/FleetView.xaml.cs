using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vantage.Studio.ViewModels;

namespace Vantage.Studio.Views;

public partial class FleetView : UserControl
{
    public FleetView() => InitializeComponent();

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RobotCardViewModel card } && DataContext is FleetViewModel vm) vm.Selected = card;
    }
}
