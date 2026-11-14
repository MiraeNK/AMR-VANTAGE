using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vantage.Fleet.Hmi;

namespace Vantage.Studio.ViewModels;

public sealed record ToolboxItem(WidgetKind Kind, string Label, string Glyph, string Hint);

public sealed partial class WidgetItem(HmiWidget model, Action changed) : ObservableObject
{
    public HmiWidget Model { get; } = model;
    public string Id => Model.Id;
    public WidgetKind Kind => Model.Kind;

    public double X { get => Model.X; set { Model.X = Math.Round(value); OnPropertyChanged(); changed(); } }
    public double Y { get => Model.Y; set { Model.Y = Math.Round(value); OnPropertyChanged(); changed(); } }
    public double Width { get => Model.Width; set { Model.Width = Math.Max(24, Math.Round(value)); OnPropertyChanged(); changed(); } }
    public double Height { get => Model.Height; set { Model.Height = Math.Max(20, Math.Round(value)); OnPropertyChanged(); changed(); } }

    [ObservableProperty] private bool _isSelected;
    /// <summary>Bumped when a property changes so the view rebuilds the widget preview.</summary>
    [ObservableProperty] private int _revision;
}

public sealed record PortItem(string Name, PortType Type, int Index, bool IsOutput)
{
    public Brush Brush => Type switch
    {
        PortType.Number => Brushes2.Of(Color.FromRgb(0x07, 0x5B, 0xCC)),
        PortType.Bool => Brushes2.Of(Color.FromRgb(0x2E, 0x7D, 0x32)),
        PortType.Text => Brushes2.Of(Color.FromRgb(0x7B, 0x4F, 0xA0)),
        PortType.Event => Brushes2.Of(Color.FromRgb(0xC6, 0x28, 0x28)),
        _ => Brushes2.Of(Color.FromRgb(0x56, 0x60, 0x6A)),
    };
}

public sealed partial class NodeItem : ObservableObject
{
    public const double NodeWidth = 176, HeaderHeight = 26, RowHeight = 20, FooterHeight = 22;

    private readonly Action _changed;

    public NodeItem(HmiNode model, Action changed)
    {
        Model = model;
        _changed = changed;
        Def = NodeLibrary.Get(model.Type) ?? NodeLibrary.All[0];
        Inputs = Def.Inputs.Select((p, i) => new PortItem(p.Name, p.Type, i, false)).ToList();
        Outputs = Def.Outputs.Select((p, i) => new PortItem(p.Name, p.Type, i, true)).ToList();
    }

    public HmiNode Model { get; }
    public NodeDef Def { get; }
    public string Id => Model.Id;
    public string Title => Def.Title;
    public IReadOnlyList<PortItem> Inputs { get; }
    public IReadOnlyList<PortItem> Outputs { get; }
    public double Height => HeaderHeight + 6 + Math.Max(Inputs.Count, Outputs.Count) * RowHeight + FooterHeight;

    public Brush HeaderBrush => Def.Category switch
    {
        "Source" => Brushes2.Of(Color.FromRgb(0x2F, 0x5D, 0x9A)),
        "Output" => Brushes2.Of(Color.FromRgb(0xA8, 0x63, 0x00)),
        _ => Brushes2.Of(Color.FromRgb(0x4A, 0x53, 0x5C)),
    };

    public string Summary => Def.Params.Length == 0 ? Def.Category.ToLowerInvariant()
        : string.Join(" · ", Def.Params.Select(p => Model.Param(p.Name, p.Default)).Where(v => v.Length > 0).Take(3));

    public double X { get => Model.X; set { Model.X = Math.Round(value); OnPropertyChanged(); _changed(); } }
    public double Y { get => Model.Y; set { Model.Y = Math.Round(value); OnPropertyChanged(); _changed(); } }

    [ObservableProperty] private bool _isSelected;

    public void ParamsChanged() => OnPropertyChanged(nameof(Summary));

    /// <summary>Port centre in graph coordinates.</summary>
    public System.Windows.Point PortPosition(string port, bool output)
    {
        var list = output ? Outputs : Inputs;
        int i = Math.Max(0, list.ToList().FindIndex(p => p.Name == port));
        return new System.Windows.Point(X + (output ? NodeWidth : 0), Y + HeaderHeight + 6 + i * RowHeight + RowHeight / 2);
    }
}

public sealed partial class PropertyItem(string key, string value, IReadOnlyList<string>? choices, Action<string, string> apply) : ObservableObject
{
    public string Key { get; } = key;
    public IReadOnlyList<string>? Choices { get; } = choices;
    public bool HasChoices => Choices is { Count: > 0 };

    private string _value = value;
    public string Value
    {
        get => _value;
        set { if (_value == value) return; _value = value; OnPropertyChanged(); apply(Key, value); }
    }
}

/// <summary>Screen designer: widget canvas (like a form designer) plus node-based logic.</summary>
public sealed partial class HmiDesignerViewModel : ObservableObject
{
    private readonly AppViewModel _app;

    public HmiDesignerViewModel(AppViewModel app)
    {
        _app = app;
        Toolbox =
        [
            new(WidgetKind.Label, "Label", "", "Static or bound text"),
            new(WidgetKind.Value, "Numeric display", "", "Value with caption and unit"),
            new(WidgetKind.Lamp, "Indicator lamp", "", "On / off state colour"),
            new(WidgetKind.Bar, "Bar gauge", "", "Level, battery, progress"),
            new(WidgetKind.Trend, "Trend", "", "Value over time"),
            new(WidgetKind.Button, "Push button", "", "Fires an event into the logic"),
            new(WidgetKind.Panel, "Group panel", "", "Frame with a title"),
            new(WidgetKind.FleetTable, "Fleet table", "", "All robots, live"),
            new(WidgetKind.RobotCard, "Robot card", "", "3D thumbnail, state, battery"),
            new(WidgetKind.Map, "Fleet map", "", "Live workspace view"),
        ];
        NodeGroups = NodeLibrary.All.GroupBy(d => d.Category).Select(g => new NodeGroup(g.Key, g.ToList())).ToList();
    }

    public IReadOnlyList<ToolboxItem> Toolbox { get; }
    public IReadOnlyList<NodeGroup> NodeGroups { get; }
    public ObservableCollection<HmiScreen> Screens { get; } = new();
    public ObservableCollection<WidgetItem> Widgets { get; } = new();
    public ObservableCollection<NodeItem> Nodes { get; } = new();
    public ObservableCollection<HmiLink> Links { get; } = new();
    public ObservableCollection<PropertyItem> Properties { get; } = new();

    [ObservableProperty] private HmiScreen? _screen;
    [ObservableProperty] private object? _selection;
    [ObservableProperty] private string _selectionTitle = "Nothing selected";
    [ObservableProperty] private int _tab;

    public event Action? GraphChanged;

    partial void OnScreenChanged(HmiScreen? value) => LoadScreen();

    public void Reload()
    {
        Screens.Clear();
        foreach (var s in _app.Project.Screens) Screens.Add(s);
        Screen = Screens.FirstOrDefault();
        _app.HmiRuntime.Reload();
    }

    private void LoadScreen()
    {
        Widgets.Clear();
        Nodes.Clear();
        Links.Clear();
        Select(null);
        if (Screen is null) return;
        foreach (var w in Screen.Widgets) Widgets.Add(new WidgetItem(w, Changed));
        foreach (var n in Screen.Nodes) Nodes.Add(new NodeItem(n, () => { Changed(); GraphChanged?.Invoke(); }));
        foreach (var l in Screen.Links) Links.Add(l);
        OnPropertyChanged(nameof(ScreenWidth));
        OnPropertyChanged(nameof(ScreenHeight));
        GraphChanged?.Invoke();
    }

    public double ScreenWidth => Screen?.Width ?? 1280;
    public double ScreenHeight => Screen?.Height ?? 720;

    private void Changed() => _app.MarkDirty();

    // ───────────────────────────── selection & properties ─────────────────────────────

    public void Select(object? item)
    {
        foreach (var w in Widgets) w.IsSelected = w == item;
        foreach (var n in Nodes) n.IsSelected = n == item;
        Selection = item;
        Properties.Clear();
        switch (item)
        {
            case WidgetItem w:
                SelectionTitle = $"{w.Kind} · {w.Id}";
                foreach (var key in new[] { "X", "Y", "Width", "Height" })
                    Properties.Add(new PropertyItem(key, Geometry(w, key).ToString(CultureInfo.InvariantCulture), null, (k, v) => SetGeometry(w, k, v)));
                foreach (var key in HmiWidget.PropertyNames(w.Kind))
                    Properties.Add(new PropertyItem(key, w.Model.Prop(key), key == "Robot" ? RobotIds() : key == "Bold" ? ["true", "false"] : null,
                        (k, v) => { w.Model.Props[k] = v; w.Revision++; Changed(); }));
                break;
            case NodeItem n:
                SelectionTitle = $"{n.Title} · {n.Id}";
                foreach (var p in n.Def.Params)
                {
                    IReadOnlyList<string>? choices = p.Choices ?? p.Name switch
                    {
                        "widget" => Widgets.Select(x => x.Id).ToList(),
                        "robot" => RobotIds(),
                        "property" => Widgets.SelectMany(x => HmiWidget.PropertyNames(x.Kind)).Distinct().OrderBy(x => x).ToList(),
                        _ => null,
                    };
                    Properties.Add(new PropertyItem(p.Name, n.Model.Param(p.Name, p.Default), choices,
                        (k, v) => { n.Model.Params[k] = v; n.ParamsChanged(); Changed(); }));
                }
                break;
            default:
                SelectionTitle = Screen is null ? "Nothing selected" : $"Screen · {Screen.Name}";
                if (Screen is { } s)
                {
                    Properties.Add(new PropertyItem("Name", s.Name, null, (_, v) => { s.Name = v; Changed(); }));
                    Properties.Add(new PropertyItem("Width", s.Width.ToString(CultureInfo.InvariantCulture), null, (_, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { s.Width = d; OnPropertyChanged(nameof(ScreenWidth)); Changed(); } }));
                    Properties.Add(new PropertyItem("Height", s.Height.ToString(CultureInfo.InvariantCulture), null, (_, v) => { if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { s.Height = d; OnPropertyChanged(nameof(ScreenHeight)); Changed(); } }));
                }
                break;
        }
    }

    private List<string> RobotIds() => _app.Project.Robots.Select(r => r.Id).DefaultIfEmpty("AMR-01").ToList();

    private static double Geometry(WidgetItem w, string key) => key switch { "X" => w.X, "Y" => w.Y, "Width" => w.Width, _ => w.Height };

    private static void SetGeometry(WidgetItem w, string key, string v)
    {
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return;
        switch (key)
        {
            case "X": w.X = d; break;
            case "Y": w.Y = d; break;
            case "Width": w.Width = d; break;
            default: w.Height = d; break;
        }
    }

    // ───────────────────────────── editing ─────────────────────────────

    [RelayCommand]
    private void AddWidget(ToolboxItem item) => AddWidgetAt(item.Kind, 40 + Widgets.Count * 12 % 200, 40 + Widgets.Count * 12 % 200);

    public WidgetItem? AddWidgetAt(WidgetKind kind, double x, double y)
    {
        if (Screen is null) return null;
        var (w, h) = kind switch
        {
            WidgetKind.Label => (240, 32),
            WidgetKind.Value or WidgetKind.Lamp => (196, 48),
            WidgetKind.Bar => (300, 52),
            WidgetKind.Trend => (360, 160),
            WidgetKind.Button => (160, 44),
            WidgetKind.Panel => (320, 200),
            WidgetKind.FleetTable => (380, 200),
            WidgetKind.RobotCard => (200, 220),
            WidgetKind.Map => (520, 320),
            _ => (160, 48),
        };
        var model = new HmiWidget { Id = Screen.NextWidgetId(kind.ToString()), Kind = kind, X = Math.Round(x), Y = Math.Round(y), Width = w, Height = h };
        if (kind == WidgetKind.Label) model.Props["Text"] = "Label";
        if (kind == WidgetKind.Button) model.Props["Text"] = "Button";
        if (kind == WidgetKind.RobotCard) model.Props["Robot"] = RobotIds()[0];
        Screen.Widgets.Add(model);
        var item = new WidgetItem(model, Changed);
        Widgets.Add(item);
        Select(item);
        Changed();
        return item;
    }

    [RelayCommand]
    private void AddNode(NodeDef def) => AddNodeAt(def.Type, 60 + Nodes.Count * 14 % 300, 60 + Nodes.Count * 14 % 300);

    public NodeItem? AddNodeAt(string type, double x, double y)
    {
        if (Screen is null) return null;
        var def = NodeLibrary.Get(type);
        if (def is null) return null;
        var model = new HmiNode { Id = Screen.NextNodeId(), Type = type, X = Math.Round(x), Y = Math.Round(y) };
        foreach (var p in def.Params) model.Params[p.Name] = p.Default;
        if (type is "set" or "button" && Selection is WidgetItem w) model.Params["widget"] = w.Id;
        Screen.Nodes.Add(model);
        var item = new NodeItem(model, () => { Changed(); GraphChanged?.Invoke(); });
        Nodes.Add(item);
        Select(item);
        Changed();
        GraphChanged?.Invoke();
        return item;
    }

    /// <summary>Connects an output port to an input port; an input takes only one link.</summary>
    public void Connect(string fromNode, string fromPort, string toNode, string toPort)
    {
        if (Screen is null || fromNode == toNode) return;
        foreach (var old in Screen.Links.Where(l => l.ToNode == toNode && l.ToPort == toPort).ToList())
        {
            Screen.Links.Remove(old);
            Links.Remove(old);
        }
        var link = new HmiLink { FromNode = fromNode, FromPort = fromPort, ToNode = toNode, ToPort = toPort };
        Screen.Links.Add(link);
        Links.Add(link);
        Changed();
        GraphChanged?.Invoke();
    }

    public void Disconnect(string toNode, string toPort)
    {
        if (Screen is null) return;
        foreach (var old in Screen.Links.Where(l => l.ToNode == toNode && l.ToPort == toPort).ToList())
        {
            Screen.Links.Remove(old);
            Links.Remove(old);
        }
        Changed();
        GraphChanged?.Invoke();
    }

    [RelayCommand]
    private void DeleteSelection()
    {
        if (Screen is null) return;
        switch (Selection)
        {
            case WidgetItem w:
                Screen.Widgets.Remove(w.Model);
                Widgets.Remove(w);
                break;
            case NodeItem n:
                Screen.Nodes.Remove(n.Model);
                Nodes.Remove(n);
                foreach (var l in Screen.Links.Where(l => l.FromNode == n.Id || l.ToNode == n.Id).ToList())
                {
                    Screen.Links.Remove(l);
                    Links.Remove(l);
                }
                GraphChanged?.Invoke();
                break;
            default:
                return;
        }
        Select(null);
        Changed();
    }

    [RelayCommand]
    private void NewScreen()
    {
        var s = new HmiScreen { Name = $"Screen {_app.Project.Screens.Count + 1}" };
        _app.Project.Screens.Add(s);
        Screens.Add(s);
        Screen = s;
        Changed();
        _app.HmiRuntime.Reload();
    }

    [RelayCommand]
    private void AddTemplateScreen()
    {
        var s = HmiScreen.CreateDefault();
        s.Name = $"Fleet overview {_app.Project.Screens.Count + 1}";
        _app.Project.Screens.Add(s);
        Screens.Add(s);
        Screen = s;
        Changed();
        _app.HmiRuntime.Reload();
    }

    [RelayCommand]
    private void DeleteScreen()
    {
        if (Screen is null || Screens.Count <= 1) return;
        _app.Project.Screens.Remove(Screen);
        Screens.Remove(Screen);
        Screen = Screens.FirstOrDefault();
        Changed();
        _app.HmiRuntime.Reload();
    }

    [RelayCommand]
    private void RunScreen()
    {
        _app.HmiRuntime.Reload();
        _app.HmiRuntime.Screen = _app.HmiRuntime.Screens.FirstOrDefault(s => s == Screen) ?? _app.HmiRuntime.Screen;
        _app.Page = Page.HmiRuntime;
    }

    [RelayCommand]
    private void Save() => _app.SaveProject();
}

public sealed record NodeGroup(string Category, IReadOnlyList<NodeDef> Nodes);

/// <summary>Runs a screen against live data (200 ms scan, like a PLC HMI).</summary>
public sealed partial class HmiRuntimeViewModel : ObservableObject
{
    private readonly AppViewModel _app;
    private readonly DispatcherTimer _timer;

    public HmiRuntimeViewModel(AppViewModel app)
    {
        _app = app;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Scan();
    }

    public HmiEngine Engine { get; } = new();
    public ObservableCollection<HmiScreen> Screens { get; } = new();
    [ObservableProperty] private HmiScreen? _screen;
    [ObservableProperty] private string _clock = "";
    [ObservableProperty] private string _scanInfo = "";
    [ObservableProperty] private bool _isRunning;

    public IReadOnlyDictionary<(string, string), object?> Values { get; private set; } = new Dictionary<(string, string), object?>();
    public event Action? Scanned;
    public event Action? ScreenSwitched;

    partial void OnScreenChanged(HmiScreen? value) => ScreenSwitched?.Invoke();

    public IHmiDataSource Data => _app.Data;
    public Controls.Scene MapScene => _app.Monitor.Scene;

    public void Reload()
    {
        var keep = Screen;
        Screens.Clear();
        foreach (var s in _app.Project.Screens) Screens.Add(s);
        Screen = Screens.Contains(keep!) ? keep : Screens.FirstOrDefault();
        ScreenSwitched?.Invoke();
    }

    public void Start()
    {
        IsRunning = true;
        _timer.Start();
    }

    public void Stop()
    {
        IsRunning = false;
        _timer.Stop();
    }

    [RelayCommand]
    private void StartFleet() => _app.Monitor.PlayCommand.Execute(null);

    [RelayCommand]
    private void PauseFleet() => _app.Monitor.PauseCommand.Execute(null);

    [RelayCommand]
    private void EditScreen()
    {
        _app.Hmi.Screen = Screen;
        _app.Page = Page.HmiDesigner;
    }

    private int _scans;

    private void Scan()
    {
        if (Screen is null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Values = Engine.Evaluate(Screen, Data);
        Scanned?.Invoke();
        Clock = DateTime.Now.ToString("dd.MM.yyyy  HH:mm:ss");
        ScanInfo = $"scan #{++_scans} · {sw.Elapsed.TotalMilliseconds:F1} ms · {Screen.Nodes.Count} nodes · {Screen.Widgets.Count} widgets";
    }
}
