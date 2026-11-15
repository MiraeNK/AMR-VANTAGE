using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Vantage.Studio.Rendering;

namespace Vantage.Studio.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        Scene.Source = FleetScene.Render(600, 480);
        Hero.Source = FleetScene.Render(600, 480, heroOnly: true);
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    /// <summary>Reports a startup step; yields to the dispatcher so the splash repaints.</summary>
    public async Task StepAsync(string status, string detail, double progress)
    {
        Status.Text = status;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private void Cancel_Click(object sender, MouseButtonEventArgs e) => Application.Current.Shutdown();
}
