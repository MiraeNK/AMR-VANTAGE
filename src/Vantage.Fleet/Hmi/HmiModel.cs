namespace Vantage.Fleet.Hmi;

/// <summary>Operator screen: widgets placed on a canvas, driven by a node graph.</summary>
public sealed class HmiScreen
{
    public string Name { get; set; } = "Screen 1";
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 720;
    public string Background { get; set; } = "#E9ECEF";
    public List<HmiWidget> Widgets { get; set; } = [];
    public List<HmiNode> Nodes { get; set; } = [];
    public List<HmiLink> Links { get; set; } = [];

    public string NextWidgetId(string kind)
    {
        for (int i = 1; ; i++)
        {
            var id = $"{kind.ToLowerInvariant()}{i}";
            if (Widgets.All(w => w.Id != id)) return id;
        }
    }

    public string NextNodeId()
    {
        for (int i = 1; ; i++)
        {
            var id = $"n{i}";
            if (Nodes.All(n => n.Id != id)) return id;
        }
    }

    public static HmiScreen CreateDefault()
    {
        var s = new HmiScreen { Name = "Fleet overview" };
        s.Widgets.AddRange(
        [
            W("title", WidgetKind.Label, 24, 18, 600, 36, ("Text", "LINE A · FLEET OVERVIEW"), ("FontSize", "22"), ("Bold", "true")),
            W("clock", WidgetKind.Value, 1040, 14, 216, 44, ("Caption", "Sim time"), ("Unit", "s"), ("Format", "F1")),
            W("lampBroker", WidgetKind.Lamp, 24, 70, 196, 44, ("Caption", "MQTT broker")),
            W("lampSafe", WidgetKind.Lamp, 232, 70, 196, 44, ("Caption", "No violations"), ("OffColor", "#C62828")),
            W("valDecisions", WidgetKind.Value, 440, 70, 196, 44, ("Caption", "Clearance decisions"), ("Format", "F0")),
            W("valHold", WidgetKind.Value, 648, 70, 196, 44, ("Caption", "Robots holding"), ("Format", "F0")),
            W("map", WidgetKind.Map, 24, 128, 820, 470),
            W("fleet", WidgetKind.FleetTable, 860, 128, 396, 230),
            W("barAmr", WidgetKind.Bar, 860, 372, 396, 52, ("Caption", "AMR-01 battery"), ("Unit", "%")),
            W("trendBat", WidgetKind.Trend, 860, 436, 396, 162, ("Caption", "Fleet min. battery"), ("Max", "100")),
            W("btnPause", WidgetKind.Button, 24, 612, 196, 48, ("Text", "Pause AMR-01")),
            W("btnResume", WidgetKind.Button, 232, 612, 196, 48, ("Text", "Resume AMR-01")),
        ]);

        s.Nodes.AddRange(
        [
            // three columns: sources (x 40) → logic (x 320) → outputs (x 600)
            N("n1", "system", 40, 30),
            N("n4", "const", 40, 250, ("value", "0")),
            N("n3", "compare", 320, 130, ("op", "==")),
            N("n2", "set", 600, 20, ("widget", "lampBroker"), ("property", "On")),
            N("n5", "set", 600, 110, ("widget", "lampSafe"), ("property", "On")),
            N("n6", "set", 600, 200, ("widget", "valDecisions"), ("property", "Value")),
            N("n7", "set", 600, 290, ("widget", "clock"), ("property", "Value")),
            N("n8", "robot", 40, 360, ("robot", "AMR-01")),
            N("n10", "threshold", 320, 470, ("low", "30"), ("high", "60")),
            N("n9", "set", 600, 400, ("widget", "barAmr"), ("property", "Value")),
            N("n11", "set", 600, 490, ("widget", "barAmr"), ("property", "Color")),
            N("n12", "fleet", 40, 610),
            N("n13", "set", 600, 600, ("widget", "trendBat"), ("property", "Value")),
            N("n14", "set", 600, 690, ("widget", "valHold"), ("property", "Value")),
            N("n15", "button", 40, 800, ("widget", "btnPause")),
            N("n16", "command", 320, 800, ("robot", "AMR-01"), ("command", "pause")),
            N("n17", "button", 40, 890, ("widget", "btnResume")),
            N("n18", "command", 320, 890, ("robot", "AMR-01"), ("command", "resume")),
        ]);

        s.Links.AddRange(
        [
            L("n1", "broker", "n2", "value"),
            L("n1", "violations", "n3", "a"),
            L("n4", "value", "n3", "b"),
            L("n3", "out", "n5", "value"),
            L("n1", "decisions", "n6", "value"),
            L("n1", "time", "n7", "value"),
            L("n8", "battery", "n9", "value"),
            L("n8", "battery", "n10", "in"),
            L("n10", "color", "n11", "value"),
            L("n12", "minBattery", "n13", "value"),
            L("n12", "holding", "n14", "value"),
            L("n15", "pressed", "n16", "trigger"),
            L("n17", "pressed", "n18", "trigger"),
        ]);
        return s;

        static HmiWidget W(string id, WidgetKind k, double x, double y, double w, double h, params (string, string)[] props) =>
            new() { Id = id, Kind = k, X = x, Y = y, Width = w, Height = h, Props = props.ToDictionary(p => p.Item1, p => p.Item2) };
        static HmiNode N(string id, string type, double x, double y, params (string, string)[] ps) =>
            new() { Id = id, Type = type, X = x, Y = y, Params = ps.ToDictionary(p => p.Item1, p => p.Item2) };
        static HmiLink L(string a, string ap, string b, string bp) => new() { FromNode = a, FromPort = ap, ToNode = b, ToPort = bp };
    }
}

public enum WidgetKind { Label, Value, Lamp, Bar, Button, Trend, Panel, FleetTable, RobotCard, Map }

public sealed class HmiWidget
{
    public string Id { get; set; } = "";
    public WidgetKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 48;
    public Dictionary<string, string> Props { get; set; } = new();

    public string Prop(string key, string fallback = "") => Props.TryGetValue(key, out var v) ? v : fallback;

    /// <summary>Editable properties per widget kind (shown in the designer property grid).</summary>
    public static IReadOnlyList<string> PropertyNames(WidgetKind k) => k switch
    {
        WidgetKind.Label => ["Text", "FontSize", "Bold", "Color"],
        WidgetKind.Value => ["Caption", "Value", "Unit", "Format"],
        WidgetKind.Lamp => ["Caption", "On", "OnColor", "OffColor"],
        WidgetKind.Bar => ["Caption", "Value", "Min", "Max", "Unit", "Color"],
        WidgetKind.Button => ["Text"],
        WidgetKind.Trend => ["Caption", "Value", "Max"],
        WidgetKind.Panel => ["Title"],
        WidgetKind.FleetTable => [],
        WidgetKind.RobotCard => ["Robot"],
        WidgetKind.Map => [],
        _ => [],
    };
}

public sealed class HmiNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Params { get; set; } = new();

    public string Param(string key, string fallback = "") => Params.TryGetValue(key, out var v) ? v : fallback;
}

public sealed class HmiLink
{
    public string FromNode { get; set; } = "";
    public string FromPort { get; set; } = "";
    public string ToNode { get; set; } = "";
    public string ToPort { get; set; } = "";
}
