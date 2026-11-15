using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Vantage.Studio.ViewModels;
using Page = Vantage.Studio.ViewModels.Page;

namespace Vantage.Studio.Views;

public partial class MainWindow : Window
{
    private readonly AppViewModel _app;
    private bool _closing;

    public MainWindow(AppViewModel app)
    {
        _app = app;
        InitializeComponent();
        DataContext = app;
        BuildRail();
        app.PageChanged += p =>
        {
            if (p == Page.HmiRuntime) app.HmiRuntime.Start();
            else app.HmiRuntime.Stop();
        };
    }

    private void BuildRail()
    {
        var items = new (Page Page, string Glyph, string Label)[]
        {
            (Page.Home, "", "Home"),
            (Page.Monitor, "", "Monitor"),
            (Page.Fleet, "", "Fleet"),
            (Page.Studies, "", "Studies"),
            (Page.HmiDesigner, "", "HMI design"),
            (Page.HmiRuntime, "", "HMI run"),
        };
        Rail.Children.Add(new Image
        {
            Source = (System.Windows.Media.ImageSource)FindResource("Brand.SymbolWhite"),
            Width = 30, Height = 30, Margin = new Thickness(0, 10, 0, 12), ToolTip = "FMR Vantage",
        });
        foreach (var (page, glyph, label) in items)
        {
            var b = new Button
            {
                Style = (Style)FindResource("RailButton"),
                Command = _app.NavigateCommand,
                CommandParameter = page,
                ToolTip = label,
            };
            b.SetBinding(TagProperty, new Binding(nameof(AppViewModel.Page)) { Converter = (IValueConverter)FindResource("Is"), ConverterParameter = page.ToString() });
            var fg = new Binding("Foreground") { Source = b };
            var g = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)FindResource("GlyphFont"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center };
            g.SetBinding(TextBlock.ForegroundProperty, fg);
            var t = new TextBlock { Text = label, FontSize = 9.5, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            t.SetBinding(TextBlock.ForegroundProperty, fg);
            b.Content = new StackPanel { Children = { g, t } };
            Rail.Children.Add(b);
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_app.IsDirty)
        {
            var r = MessageBox.Show(this, $"Save changes to {_app.Project.Name}?", "FMR Vantage", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) _app.SaveProject();
        }
        _closing = true;
        try { await _app.DisposeAsync(); } catch { /* shutting down */ }
        Application.Current.Shutdown();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Contract_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Topics (QoS 1), prefix vantage/v1/{robotId}/\n\n" +
        "  factsheet   robot → FMR   class, footprint, priority, ip (retained)\n" +
        "  connection  robot → FMR   ONLINE / OFFLINE (retained, last will)\n" +
        "  path        robot → FMR   candidate path: missionId, revision, source, poses[]\n" +
        "  state       robot → FMR   headerId (sequence), revision, agvPosition, pathProgress, batteryState\n" +
        "  clearance   FMR → robot   decisionNo, revision, status, segments[], limitS\n" +
        "  command     FMR → robot   pause / resume\n\n" +
        "status ∈ GRANTED | QUEUED | REVOKED | REJECTED (as VDA 5050 3.0 zone responses).\n" +
        "The robot must not move its reference point past limitS of its active revision.\n\n" +
        "Full description: docs/MESSAGE_CONTRACT.md",
        "Message contract", MessageBoxButton.OK, MessageBoxImage.Information);

    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "FMR Vantage\nFleet Management Robot — segment clearance for mixed AGV / AMR fleets\n\n" +
        "Tugas Akhir · Teknologi Rekayasa Informatika Industri\nPoliteknik Manufaktur Bandung\n\n" +
        $"Version {typeof(MainWindow).Assembly.GetName().Version}",
        "About", MessageBoxButton.OK, MessageBoxImage.Information);
}
