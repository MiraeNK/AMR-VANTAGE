using System.Windows.Controls;
using System.Windows.Input;
using Vantage.Studio.ViewModels;

namespace Vantage.Studio.Views;

public partial class StudiesView : UserControl
{
    public StudiesView() => InitializeComponent();

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StudiesViewModel vm && ResultsGrid.SelectedItem is EvaluationRow row) vm.OpenInWorkspaceCommand.Execute(row);
    }
}
