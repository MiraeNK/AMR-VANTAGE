using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vantage.Studio.ViewModels;

namespace Vantage.Studio.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    private void Recent_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentGrid.SelectedItem is RecentProject r && DataContext is AppViewModel app) app.OpenRecentCommand.Execute(r);
    }

    private void Doc_Click(object sender, RoutedEventArgs e)
    {
        var rel = (string)((FrameworkElement)sender).Tag;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, rel))) dir = dir.Parent;
        if (dir is null)
        {
            MessageBox.Show($"{rel} was not found next to the application.", "FMR Vantage", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(Path.Combine(dir.FullName, rel)) { UseShellExecute = true });
    }

    private void DataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(App.DataDirectory) { UseShellExecute = true });
}
