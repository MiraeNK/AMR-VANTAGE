using System.IO;
using System.Windows;
using System.Windows.Threading;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;
using Vantage.Fleet.Catalog;
using Vantage.Studio.Rendering;
using Vantage.Studio.ViewModels;
using Vantage.Studio.Views;

namespace Vantage.Studio;

public partial class App : Application
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VANTAGE");

    /// <summary>Command line: --scenario S2 --run --speed 2 --page monitor --tab 1 --batch --no-splash</summary>
    public static IReadOnlyDictionary<string, string> Args { get; private set; } = new Dictionary<string, string>();

    protected override async void OnStartup(StartupEventArgs e)
    {
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < e.Args.Length; i++)
        {
            if (!e.Args[i].StartsWith("--")) continue;
            string key = e.Args[i][2..];
            string value = i + 1 < e.Args.Length && !e.Args[i + 1].StartsWith("--") ? e.Args[++i] : "true";
            args[key] = value;
        }
        Args = args;
        Directory.CreateDirectory(DataDirectory);
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);

        var splash = new SplashWindow();
        splash.Show();

        // Debug aid: --render-splash out.png writes the splash to a PNG and exits.
        if (args.TryGetValue("render-splash", out var shot))
        {
            await splash.StepAsync("Loading registry…", "", 0.4);
            await Task.Delay(300);
            var root = (FrameworkElement)splash.Content;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var bg = new System.Windows.Media.DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1B, 0x1B)), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            rtb.Render(bg);
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(shot)) enc.Save(fs);
            Shutdown();
            return;
        }
        var app = new AppViewModel();
        try
        {
            await splash.StepAsync("Initializing geometry kernel…", "SAT · swept footprint · raster oracle", 0.12);
            new PathNormalizer(new NormalizerOptions()).Normalize(
                new PathCandidate("warmup", "M", 0, PathSource.StaticRoute, [new(new Pose2D(0, 0, 0)), new(new Pose2D(2, 0, 0))]),
                Footprint.Centered(0.8, 0.5));

            int i = 0;
            foreach (var m in RobotCatalog.Models)
            {
                await splash.StepAsync("Rendering robot catalogue…", $"{m.Name}  ·  {m.Family}", 0.15 + 0.4 * (++i) / RobotCatalog.Models.Count);
                RobotRenderer.Render(m.Key, 220, 170);
            }

            await splash.StepAsync("Starting MQTT broker…", "0.0.0.0:1883 · vantage/v1", 0.62);
            await app.StartBrokerAsync();

            await splash.StepAsync("Restoring project…", "", 0.78);
            app.OpenStartupProject();

            await splash.StepAsync("Loading workspace…", "", 0.9);
            var main = new MainWindow(app);
            MainWindow = main;
            await app.InitializeAsync();
            await splash.StepAsync("Ready", "", 1.0);
            if (!args.ContainsKey("no-splash")) await Task.Delay(350);
            main.Show();
            main.Activate();
        }
        finally
        {
            splash.Close();
        }
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        File.AppendAllText(Path.Combine(DataDirectory, "fault.log"), $"[{DateTime.Now:O}] {e.Exception}\n\n");
        MessageBox.Show(e.Exception.Message, "FMR Vantage · Unhandled fault", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
