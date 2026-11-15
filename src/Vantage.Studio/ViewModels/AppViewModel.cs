using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Vantage.Fleet.Project;
using Vantage.Studio.Rendering;
using Vantage.Studio.Services;

namespace Vantage.Studio.ViewModels;

public enum Page { Home, Monitor, Fleet, Studies, HmiDesigner, HmiRuntime }

public sealed record RecentProject(string Name, string Path, DateTime Modified, int Robots)
{
    public string ModifiedText => Modified.ToString("dd MMM yyyy HH:mm");
}

public sealed record StartTile(string Title, string Subtitle, string Glyph, Page Target, string Group, string? Action = null);

/// <summary>Application shell: project, navigation, and the shared runtime.</summary>
public sealed partial class AppViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly string RecentFile = Path.Combine(App.DataDirectory, "recent.json");
    public static string ProjectsDirectory { get; } = Path.Combine(App.DataDirectory, "Projects");

    public AppViewModel()
    {
        Host = new RuntimeHost();
        Data = new FleetDataSource(Host);
        Monitor = new MonitorViewModel(this);
        Fleet = new FleetViewModel(this);
        Studies = new StudiesViewModel(this);
        Hmi = new HmiDesignerViewModel(this);
        HmiRuntime = new HmiRuntimeViewModel(this);

        Tiles =
        [
            new("Monitoring & clearance", "Live workspace, segment table, message trace, decision journal", "", Page.Monitor, "Engineering"),
            new("Route editor", "Teach fixed virtual lines or planner paths per robot", "", Page.Monitor, "Engineering", "routes"),
            new("Fleet setup", "Connect robots, add demo AMR / AGV, battery and link status", "", Page.Fleet, "Engineering"),
            new("Simulation studies", "Clearance, baseline, ablation and robustness studies", "", Page.Studies, "Engineering"),
            new("HMI designer", "Build operator screens with widgets and visual scripting", "", Page.HmiDesigner, "Operations"),
            new("HMI runtime", "Run the operator dashboard on live fleet data", "", Page.HmiRuntime, "Operations"),
        ];
        HeroImage = RobotRenderer.Render("agv-pallet", 160, 240, yawDeg: -50, pitchDeg: 12, zoom: 1.35);
        HeroImage2 = RobotRenderer.Render("amr-compact", 220, 150, yawDeg: -135, pitchDeg: 20);
        LoadRecent();
    }

    public RuntimeHost Host { get; }
    public FleetDataSource Data { get; }
    public MonitorViewModel Monitor { get; }
    public FleetViewModel Fleet { get; }
    public StudiesViewModel Studies { get; }
    public HmiDesignerViewModel Hmi { get; }
    public HmiRuntimeViewModel HmiRuntime { get; }

    public IReadOnlyList<StartTile> Tiles { get; }
    public IEnumerable<StartTile> EngineeringTiles => Tiles.Where(t => t.Group == "Engineering");
    public IEnumerable<StartTile> OperationsTiles => Tiles.Where(t => t.Group == "Operations");
    public BitmapSource HeroImage { get; }
    public BitmapSource HeroImage2 { get; }
    public ObservableCollection<RecentProject> Recent { get; } = new();

    [ObservableProperty] private VantageProject _project = new();
    [ObservableProperty] private Page _page = Page.Home;
    [ObservableProperty] private bool _isDirty;

    public string Title => $"{Project.Name}{(IsDirty ? " *" : "")} — FMR Vantage";
    partial void OnProjectChanged(VantageProject value) { OnPropertyChanged(nameof(Title)); ProjectChanged?.Invoke(); }
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(Title));
    partial void OnPageChanged(Page value) => PageChanged?.Invoke(value);

    public event Action? ProjectChanged;
    public event Action<Page>? PageChanged;

    public void MarkDirty() => IsDirty = true;

    // ───────────────────────────── startup ─────────────────────────────

    public Task StartBrokerAsync() => Host.StartBrokerAsync();

    public void OpenStartupProject()
    {
        // --demo: in-memory demo project, nothing saved, recent list untouched (presentations, screenshots)
        if (App.Args.ContainsKey("demo"))
        {
            Project = VantageProject.CreateDemo();
            return;
        }
        var last = Recent.FirstOrDefault(r => File.Exists(r.Path));
        try
        {
            Project = last is not null ? VantageProject.Load(last.Path) : CreateDemoProject();
        }
        catch
        {
            Project = CreateDemoProject();
        }
        if (Project.Screens.Count == 0) Project.Screens.Add(global::Vantage.Fleet.Hmi.HmiScreen.CreateDefault());
    }

    private static VantageProject CreateDemoProject()
    {
        var p = VantageProject.CreateDemo();
        p.Save(Path.Combine(ProjectsDirectory, "Demo line A.vantage"));
        return p;
    }

    public async Task InitializeAsync()
    {
        await Monitor.InitializeAsync();
        Fleet.Reload();
        Hmi.Reload();
        if (App.Args.TryGetValue("page", out var page) && Enum.TryParse<Page>(page, true, out var pg)) Page = pg;
        if (App.Args.TryGetValue("hmitab", out var ht) && int.TryParse(ht, out var hti)) Hmi.Tab = hti;
        RememberRecent();
        if (App.Args.TryGetValue("study", out var st) && Studies.Types.FirstOrDefault(t => t.Key == st) is { } type)
        {
            Studies.SelectedType = type;
            _ = Studies.RunCommand.ExecuteAsync(null);
        }
    }

    // ───────────────────────────── navigation ─────────────────────────────

    [RelayCommand]
    private void Navigate(Page page) => Page = page;

    [RelayCommand]
    private void OpenTile(StartTile tile)
    {
        Page = tile.Target;
        if (tile.Action == "routes") Monitor.IsRouteEditorOpen = true;
    }

    // ───────────────────────────── project files ─────────────────────────────

    [RelayCommand]
    private void NewProject()
    {
        var p = new VantageProject { Name = $"Project {DateTime.Now:yyyy-MM-dd HHmm}" };
        p.Screens.Add(global::Vantage.Fleet.Hmi.HmiScreen.CreateDefault());
        p.Save(Path.Combine(ProjectsDirectory, p.Name + ".vantage"));
        SwitchTo(p);
    }

    [RelayCommand]
    private void NewDemoProject() => SwitchTo(CreateDemoProject());

    [RelayCommand]
    private void OpenProject()
    {
        var dlg = new OpenFileDialog { Filter = "FMR Vantage project (*.vantage)|*.vantage", InitialDirectory = ProjectsDirectory };
        if (dlg.ShowDialog() == true) OpenPath(dlg.FileName);
    }

    [RelayCommand]
    private void OpenRecent(RecentProject r) => OpenPath(r.Path);

    private void OpenPath(string path)
    {
        try
        {
            SwitchTo(VantageProject.Load(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FMR Vantage · Open project", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SwitchTo(VantageProject p)
    {
        Project = p;
        IsDirty = false;
        RememberRecent();
        Fleet.Reload();
        Hmi.Reload();
        _ = Monitor.OnProjectSwitchedAsync();
    }

    [RelayCommand]
    public void SaveProject()
    {
        if (Project.FilePath is null) { SaveProjectAs(); return; }
        Project.Save(Project.FilePath);
        IsDirty = false;
        RememberRecent();
    }

    [RelayCommand]
    private void SaveProjectAs()
    {
        var dlg = new SaveFileDialog { Filter = "FMR Vantage project (*.vantage)|*.vantage", InitialDirectory = ProjectsDirectory, FileName = Project.Name };
        if (dlg.ShowDialog() != true) return;
        Project.Name = Path.GetFileNameWithoutExtension(dlg.FileName);
        Project.Save(dlg.FileName);
        IsDirty = false;
        OnPropertyChanged(nameof(Title));
        RememberRecent();
    }

    private void LoadRecent()
    {
        try
        {
            if (!File.Exists(RecentFile)) return;
            foreach (var r in JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(RecentFile)) ?? []) Recent.Add(r);
        }
        catch { /* ignore a corrupt list */ }
    }

    private void RememberRecent()
    {
        if (Project.FilePath is null) return;
        var item = new RecentProject(Project.Name, Project.FilePath, Project.Modified, Project.Robots.Count);
        foreach (var old in Recent.Where(r => string.Equals(r.Path, item.Path, StringComparison.OrdinalIgnoreCase)).ToList()) Recent.Remove(old);
        Recent.Insert(0, item);
        while (Recent.Count > 12) Recent.RemoveAt(Recent.Count - 1);
        try { File.WriteAllText(RecentFile, JsonSerializer.Serialize(Recent.ToList())); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        HmiRuntime.Stop();
        await Monitor.DisposeAsync();
    }
}
