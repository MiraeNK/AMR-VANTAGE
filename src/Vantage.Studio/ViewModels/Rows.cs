using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Vantage.Core.Clearance;
using Vantage.Core.Paths;
using Vantage.Fleet;
using Vantage.Fleet.Simulation;
using Vantage.Studio.Controls;

namespace Vantage.Studio.ViewModels;

internal static class Brushes2
{
    private static readonly Dictionary<Color, SolidColorBrush> Cache = new();

    public static SolidColorBrush Of(Color c)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(c, out var b)) return b;
            b = new SolidColorBrush(c);
            b.Freeze();
            Cache[c] = b;
            return b;
        }
    }

    public static SolidColorBrush State(SegmentState s) => s switch
    {
        SegmentState.Granted => Of(Palette.Ok),
        SegmentState.Occupied => Of(Palette.Accent),
        SegmentState.Waiting => Of(Palette.Hold),
        _ => Of(Palette.Off),
    };
}

public sealed partial class RobotItemViewModel(string id) : ObservableObject
{
    public string Id { get; } = id;

    [ObservableProperty] private string _classLabel = "";
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private Brush _identity = Brushes.Gray;
    [ObservableProperty] private string _stateText = "IDLE";
    [ObservableProperty] private Brush _stateBrush = Brushes.Gray;
    [ObservableProperty] private string _mission = "—";
    [ObservableProperty] private string _progressText = "—";
    [ObservableProperty] private double _progressRatio;
    [ObservableProperty] private double _limitRatio;
    [ObservableProperty] private string _limitText = "—";
    [ObservableProperty] private string _speedText = "—";
    [ObservableProperty] private string _poseText = "—";
    [ObservableProperty] private string _footprintText = "—";
    [ObservableProperty] private string _priorityText = "—";
    [ObservableProperty] private string _decisionText = "—";
    [ObservableProperty] private string _reasonText = "";
    [ObservableProperty] private string _waitText = "—";
    [ObservableProperty] private string _segmentsText = "—";
    [ObservableProperty] private string _linkText = "—";
}

public sealed partial class SegmentRowViewModel(string key) : ObservableObject
{
    public string Key { get; } = key;

    [ObservableProperty] private string _robot = "";
    [ObservableProperty] private int _revision;
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private double _startS;
    [ObservableProperty] private double _endS;
    [ObservableProperty] private double _length;
    [ObservableProperty] private string _conflict = "";
    [ObservableProperty] private string _state = "";
    [ObservableProperty] private Brush _stateBrush = Brushes.Gray;
    [ObservableProperty] private string _decision = "";
    [ObservableProperty] private string _granted = "";
    [ObservableProperty] private string _entered = "";
    [ObservableProperty] private string _released = "";
    [ObservableProperty] private string _startNode = "";
    [ObservableProperty] private string _endNode = "";

    public void Update(SegmentView v, bool legacy)
    {
        var s = v.Segment;
        Robot = s.RobotId;
        Revision = s.Revision;
        Index = s.Index;
        Source = s.Source switch { PathSource.AgvVirtualLine => "AGV line", PathSource.AmrPlanner => "Planner", _ => "Static" };
        StartS = s.StartS;
        EndS = s.EndS;
        Length = s.Length;
        Conflict = v.InConflict ? "CONFLICT" : "";
        State = (legacy ? "OLD·" : "") + v.State.ToString().ToUpperInvariant();
        StateBrush = Brushes2.State(v.State);
        Decision = v.DecisionNo > 0 ? $"#{v.DecisionNo}" : "";
        Granted = v.GrantedAt is { } g ? g.ToString("F2") : "";
        Entered = v.EnteredAt is { } en ? en.ToString("F2") : "";
        Released = v.ReleasedAt is { } r ? r.ToString("F2") : "";
        StartNode = s.StartNode;
        EndNode = s.EndNode;
    }
}

public sealed record JournalRow(double Time, string Kind, string Robot, string Message, Brush KindBrush)
{
    public string TimeText => Time.ToString("F2");

    public static JournalRow From(JournalEntry e) => new(e.Time, e.Kind switch
    {
        JournalKind.Decision => "DECISION",
        JournalKind.StateChange => "STATE",
        JournalKind.Violation => "VIOLATION",
        JournalKind.Warning => "WARNING",
        JournalKind.Path => "PATH",
        _ => "INFO",
    }, e.RobotId, e.Message, e.Kind switch
    {
        JournalKind.Violation => Brushes2.Of(Palette.Alarm),
        JournalKind.Warning => Brushes2.Of(Palette.Hold),
        JournalKind.Decision => Brushes2.Of(Palette.Accent),
        _ => Brushes2.Of(Color.FromRgb(0x56, 0x60, 0x6A)),
    });
}

public sealed record TraceRow(double Time, string Direction, string Topic, string Kind, int Bytes, string Summary, string Payload)
{
    public string TimeText => Time.ToString("F2");
    public static TraceRow From(TraceEntry t) => new(t.Time, t.Direction == TraceDirection.In ? "RX" : "TX", t.Topic, t.Kind, t.Bytes, t.Summary, t.Payload);
}

public sealed record EvaluationRow(
    string Scenario,
    string Pairing,
    string Mode,
    string Detector,
    int Repetition,
    double Duration,
    int TP, int FP, int TN, int FN,
    double Fnr, double Fpr,
    int Incompatible,
    int OrderViolations,
    int Collisions,
    double Completion,
    double TotalWait,
    long Decisions,
    long Duplicates,
    bool Passed)
{
    public string Verdict => Passed ? "PASS" : "FAIL";

    public static EvaluationRow From(ScenarioResult r, string pairing, int rep) => new(
        r.ScenarioId, pairing,
        r.Options.Mode == ClearanceMode.Segmented ? "Segmented" : "Full-path baseline",
        r.Options.Detector == DetectorKind.SweptFootprint ? "Swept footprint" : "Node-ID ablation",
        rep, r.Duration,
        r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.TP : r.NodeIdDetection.TP,
        r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.FP : r.NodeIdDetection.FP,
        r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.TN : r.NodeIdDetection.TN,
        r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.FN : r.NodeIdDetection.FN,
        r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.Fnr : r.NodeIdDetection.Fnr,
        r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.Fpr : r.NodeIdDetection.Fpr,
        r.OracleIncompatible, r.OracleOrderViolations, r.Collisions,
        r.CompletionRate, r.Robots.Sum(x => x.WaitTime), r.Decisions, r.DuplicatesDropped,
        r.OracleIncompatible == 0 && r.OracleOrderViolations == 0 && r.Collisions == 0 && r.AllCompleted &&
        (r.Options.Detector == DetectorKind.SweptFootprint ? r.SweptDetection.FN : r.NodeIdDetection.FN) == 0);
}

public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
