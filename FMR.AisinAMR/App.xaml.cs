using System.Windows;

namespace FMR.AisinAMR
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global exception handling
            DispatcherUnhandledException += (s, ex) =>
            {
                System.Console.WriteLine($"\n\n--- UNHANDLED EXCEPTION ---\n{ex.Exception.ToString()}\n---------------------------\n");
                ex.Handled = false;
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // MainViewModel.Dispose() akan memanggil _mqttServer.StopAsync()
            // via binding sehingga broker berhenti dengan bersih
            base.OnExit(e);
        }
    }
}
