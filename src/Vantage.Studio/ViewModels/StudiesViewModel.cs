using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vantage.Core.Clearance;
using Vantage.Core.Paths;
using Vantage.Fleet.Simulation;

namespace Vantage.Studio.ViewModels;

public sealed record StudyType(string Key, string Title, string Glyph, string Description, string Question);

public sealed record ChartBar(string Label, double Value, double Ratio, string ValueText, bool Highlight);

/// <summary>SolidWorks-style study manager: pick a study type, configure, run headless, inspect results.</summary>
public sealed partial class StudiesViewModel : ObservableObject
{
    private readonly AppViewModel _app;

    public StudiesViewModel(AppViewModel app)
    {
        _app = app;
        Types =
        [
            new("clearance", "Clearance study", "",
                "Proposed mechanism (segmented clearance + swept footprint) on every Tabel III.2 scenario and pairing.",
                "Does the mechanism avoid incompatible grants and order violations while missions complete?"),
            new("baseline", "Baseline comparison", "",
                "Segmented clearance vs. full-path reservation with the same conflict model, priority and release rule.",
                "Does segmentation reduce waiting time compared to reserving the whole path?"),
            new("ablation", "Detector ablation", "",
                "Swept-footprint detector vs. node-ID equality, scored against the raster oracle.",
                "How many conflicts does node-ID matching miss (FN) and what does that cause?"),
            new("robustness", "Robustness study", "",
                "Revision before conflict, duplicate / stale messages and premature exit reports (S5, S6).",
                "Are grants and releases idempotent under message faults?"),
            new("project", "Project fleet study", "",
                "Runs the robots and routes of the open project repeatedly with start-time jitter.",
                "Is the project layout feasible and how long do robots wait?"),
            new("full", "Full evaluation", "",
                "All scenarios × pairings × {segmented, full-path baseline, node-ID ablation}.",
                "Complete evidence table for Bab IV."),
        ];
        _selectedType = Types[0];
    }

    public IReadOnlyList<StudyType> Types { get; }
    [ObservableProperty] private StudyType _selectedType;
    [ObservableProperty] private int _repetitions = 1;
    [ObservableProperty] private double _startJitter = 0.5;
    [ObservableProperty] private bool _pairAgvAmr = true;
    [ObservableProperty] private bool _pairAgvAgv = true;
    [ObservableProperty] private bool _pairAmrAmr = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _verdict = "—";
    [ObservableProperty] private string _chartTitle = "";

    public ObservableCollection<EvaluationRow> Results { get; } = new();
    public ObservableCollection<ChartBar> Chart { get; } = new();

    partial void OnSelectedTypeChanged(StudyType value) => OnPropertyChanged(nameof(UsesPairings));
    public bool UsesPairings => SelectedType.Key != "project";

    [RelayCommand]
    private void SelectType(StudyType t) => SelectedType = t;

    [RelayCommand]
    private async Task Run()
    {
        if (IsRunning) return;
        IsRunning = true;
        Results.Clear();
        Chart.Clear();
        Summary = "";
        Verdict = "—";
        var opts = _app.Monitor.Options();
        var key = SelectedType.Key;

        var configs = key switch
        {
            "clearance" or "robustness" or "project" => new[] { (ClearanceMode.Segmented, DetectorKind.SweptFootprint) },
            "baseline" => new[] { (ClearanceMode.Segmented, DetectorKind.SweptFootprint), (ClearanceMode.FullPathBaseline, DetectorKind.SweptFootprint) },
            "ablation" => new[] { (ClearanceMode.Segmented, DetectorKind.SweptFootprint), (ClearanceMode.Segmented, DetectorKind.NodeIdAblation) },
            _ => new[] { (ClearanceMode.Segmented, DetectorKind.SweptFootprint), (ClearanceMode.FullPathBaseline, DetectorKind.SweptFootprint), (ClearanceMode.Segmented, DetectorKind.NodeIdAblation) },
        };

        var pairings = new List<(string Label, VehicleClass A, VehicleClass B)>();
        if (PairAgvAmr) pairings.Add(("AGV – AMR", VehicleClass.Agv, VehicleClass.Amr));
        if (PairAgvAgv) pairings.Add(("AGV – AGV", VehicleClass.Agv, VehicleClass.Agv));
        if (PairAmrAmr) pairings.Add(("AMR – AMR", VehicleClass.Amr, VehicleClass.Amr));
        if (pairings.Count == 0) pairings.Add(("AGV – AMR", VehicleClass.Agv, VehicleClass.Amr));

        List<ScenarioDefinition> scenarios = key switch
        {
            "robustness" => ScenarioLibrary.All.Where(s => s.Id is "S5" or "S6").ToList(),
            "project" => new List<ScenarioDefinition> { _app.Project.ToScenario() },
            _ => ScenarioLibrary.All.ToList(),
        };
        if (key == "project")
        {
            pairings = [("Project", VehicleClass.Agv, VehicleClass.Amr)];
            if (scenarios[0].Robots.Count == 0)
            {
                MessageBox.Show("The project has no simulated robot with a route.", "Simulation study", MessageBoxButton.OK, MessageBoxImage.Information);
                IsRunning = false;
                return;
            }
        }

        var jobs = (from s in scenarios from p in pairings from c in configs from rep in Enumerable.Range(1, Math.Max(1, Repetitions)) select (s, p, c, rep)).ToList();
        int done = 0;
        var rows = new List<EvaluationRow>();
        try
        {
            await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, async (job, ct) =>
            {
                var (s, p, c, rep) = job;
                var scenario = key == "project" || (p.A, p.B) == (VehicleClass.Agv, VehicleClass.Amr) ? s : ScenarioLibrary.WithPairing(s, p.A, p.B);
                scenario = Jitter(scenario, rep);
                var r = await SimulationEngine.RunHeadlessAsync(scenario, opts with { Mode = c.Item1, Detector = c.Item2 });
                var row = EvaluationRow.From(r, p.Label, rep) with { Scenario = s.Id };
                lock (rows) rows.Add(row);
                int n = Interlocked.Increment(ref done);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Progress = (double)n / jobs.Count;
                    Status = $"{n} / {jobs.Count} runs";
                });
            });

            foreach (var row in rows.OrderBy(r => r.Scenario).ThenBy(r => r.Pairing).ThenBy(r => r.Mode).ThenBy(r => r.Detector).ThenBy(r => r.Repetition))
                Results.Add(row);
            BuildSummary();
            Status = $"Completed {jobs.Count} runs";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private ScenarioDefinition Jitter(ScenarioDefinition s, int rep)
    {
        if (rep <= 1 || StartJitter <= 0) return s;
        var rnd = new Random(rep * 7919 + s.Id.GetHashCode(StringComparison.Ordinal));
        return s with { Robots = s.Robots.Select(r => r with { StartDelay = Math.Max(0, r.StartDelay + (rnd.NextDouble() * 2 - 1) * StartJitter) }).ToList() };
    }

    private void BuildSummary()
    {
        var groups = Results.GroupBy(r => $"{(r.Mode.StartsWith("Full") ? "Full-path" : "Segmented")} · {(r.Detector.StartsWith("Node") ? "node-ID" : "swept")}").ToList();
        var main = Results.Where(r => r.Mode == "Segmented" && r.Detector.StartsWith("Swept")).ToList();
        int pass = main.Count(r => r.Passed);
        Verdict = main.Count == 0 ? "—" : pass == main.Count ? "SUPPORTED" : "NOT SUPPORTED";
        Summary = $"Proposed mechanism: {pass}/{main.Count} runs pass · FN {main.Sum(r => r.FN)} · incompatible {main.Sum(r => r.Incompatible)} · " +
                  $"order violations {main.Sum(r => r.OrderViolations)} · collisions {main.Sum(r => r.Collisions)} · completion {(main.Count == 0 ? 0 : main.Average(r => r.Completion)):P0}";

        bool ablation = SelectedType.Key == "ablation";
        ChartTitle = ablation ? "Missed conflicts (FN) per configuration" : "Mean total waiting time per run [s]";
        var values = groups.Select(g => (g.Key, Value: ablation ? g.Sum(r => r.FN) : g.Average(r => r.TotalWait))).ToList();
        double max = Math.Max(1e-9, values.Max(v => v.Value));
        foreach (var (label, value) in values)
            Chart.Add(new ChartBar(label, value, value / max, ablation ? value.ToString("F0") : value.ToString("F1"), label.StartsWith("Segmented · swept")));
    }

    [RelayCommand]
    private void Export()
    {
        if (Results.Count == 0) return;
        var path = MonitorViewModel.Save($"study_{SelectedType.Key}", "CSV (*.csv)|*.csv");
        if (path is null) return;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("scenario,pairing,mode,detector,rep,duration_s,tp,fp,tn,fn,fnr,fpr,incompatible,order_violations,collisions,completion,total_wait_s,decisions,duplicates_dropped,verdict\n");
        foreach (var r in Results)
            sb.AppendLine(string.Join(",", r.Scenario, r.Pairing, r.Mode, r.Detector, r.Repetition, r.Duration.ToString("F2", inv),
                r.TP, r.FP, r.TN, r.FN, r.Fnr.ToString("F4", inv), r.Fpr.ToString("F4", inv), r.Incompatible, r.OrderViolations,
                r.Collisions, r.Completion.ToString("F2", inv), r.TotalWait.ToString("F2", inv), r.Decisions, r.Duplicates, r.Verdict));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Opens the selected scenario in the live workspace.</summary>
    [RelayCommand]
    private void OpenInWorkspace(EvaluationRow? row)
    {
        if (row is null) return;
        var m = _app.Monitor;
        m.SelectedScenario = m.Scenarios.FirstOrDefault(s => s.Value?.Id == row.Scenario) ?? m.SelectedScenario;
        _app.Page = Page.Monitor;
    }
}
