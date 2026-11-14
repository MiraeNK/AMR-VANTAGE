using System.Globalization;

namespace Vantage.Fleet.Hmi;

public sealed record HmiRobotData(string Id, string Name, string Class, string Model, bool Online, double Battery, double Speed,
    string State, double Progress, double X, double Y, bool Holding, bool Done);

public sealed record HmiSystemData(double Time, long Decisions, int Violations, int Collisions, bool BrokerOnline, bool Running, int ConflictPairs);

/// <summary>Live data the HMI reads, and the commands it can send.</summary>
public interface IHmiDataSource
{
    IReadOnlyList<HmiRobotData> Robots { get; }
    HmiSystemData System { get; }
    void SendCommand(string robotId, string command);
}

public enum PortType { Any, Number, Bool, Text, Event }

public sealed record PortDef(string Name, PortType Type);

public sealed record ParamDef(string Name, string Default, IReadOnlyList<string>? Choices = null);

public sealed record NodeDef(string Type, string Title, string Category, PortDef[] Inputs, PortDef[] Outputs, ParamDef[] Params, string Help);

/// <summary>Node catalogue of the visual script.</summary>
public static class NodeLibrary
{
    private static readonly PortDef[] None = [];

    public static IReadOnlyList<NodeDef> All { get; } =
    [
        new("robot", "Robot", "Source", None,
            [new("battery", PortType.Number), new("speed", PortType.Number), new("progress", PortType.Number), new("state", PortType.Text),
             new("online", PortType.Bool), new("holding", PortType.Bool), new("done", PortType.Bool), new("name", PortType.Text)],
            [new("robot", "AMR-01")], "Live values of one robot."),
        new("fleet", "Fleet", "Source", None,
            [new("count", PortType.Number), new("online", PortType.Number), new("holding", PortType.Number),
             new("avgBattery", PortType.Number), new("minBattery", PortType.Number)],
            [], "Aggregates over all robots."),
        new("system", "System", "Source", None,
            [new("time", PortType.Number), new("decisions", PortType.Number), new("violations", PortType.Number),
             new("collisions", PortType.Number), new("conflicts", PortType.Number), new("broker", PortType.Bool), new("running", PortType.Bool)],
            [], "FMR and transport status."),
        new("const", "Constant", "Source", None, [new("value", PortType.Any)], [new("value", "0")], "Fixed value."),
        new("button", "Button press", "Source", None, [new("pressed", PortType.Event)], [new("widget", "")], "Fires when an operator presses the button widget."),

        new("compare", "Compare", "Logic", [new("a", PortType.Number), new("b", PortType.Number)], [new("out", PortType.Bool)],
            [new("op", ">", [">", "<", ">=", "<=", "==", "!="])], "a op b."),
        new("math", "Math", "Logic", [new("a", PortType.Number), new("b", PortType.Number)], [new("out", PortType.Number)],
            [new("op", "+", ["+", "-", "*", "/", "min", "max"])], "Arithmetic."),
        new("and", "AND", "Logic", [new("a", PortType.Bool), new("b", PortType.Bool)], [new("out", PortType.Bool)], [], "Both true."),
        new("or", "OR", "Logic", [new("a", PortType.Bool), new("b", PortType.Bool)], [new("out", PortType.Bool)], [], "Any true."),
        new("not", "NOT", "Logic", [new("in", PortType.Bool)], [new("out", PortType.Bool)], [], "Invert."),
        new("select", "Select", "Logic", [new("cond", PortType.Bool), new("whenTrue", PortType.Any), new("whenFalse", PortType.Any)],
            [new("out", PortType.Any)], [], "If / else."),
        new("threshold", "Threshold colour", "Logic", [new("in", PortType.Number)], [new("color", PortType.Text)],
            [new("low", "30"), new("high", "60"), new("lowColor", "#C62828"), new("midColor", "#B76E00"), new("highColor", "#2E7D32")],
            "Value below low / between / above high → colour."),
        new("format", "Format text", "Logic", [new("in", PortType.Any)], [new("text", PortType.Text)], [new("format", "{0:F1}")], ".NET composite format."),

        new("set", "Set widget property", "Output", [new("value", PortType.Any)], None,
            [new("widget", ""), new("property", "Value")], "Writes the input into a widget property."),
        new("command", "Robot command", "Output", [new("trigger", PortType.Event)], None,
            [new("robot", "AMR-01"), new("command", "pause", ["pause", "resume"])], "Publishes vantage/v1/{robot}/command."),
    ];

    public static NodeDef? Get(string type) => All.FirstOrDefault(d => d.Type == type);
}

/// <summary>
/// Evaluates a screen's node graph once per tick: pulls values from sources through
/// logic nodes and returns the widget property overrides. Events (button presses)
/// live for exactly one evaluation.
/// </summary>
public sealed class HmiEngine
{
    private readonly HashSet<string> _pressed = new();

    public void Press(string widgetId)
    {
        lock (_pressed) _pressed.Add(widgetId);
    }

    public Dictionary<(string Widget, string Property), object?> Evaluate(HmiScreen screen, IHmiDataSource data)
    {
        HashSet<string> pressed;
        lock (_pressed)
        {
            pressed = new HashSet<string>(_pressed);
            _pressed.Clear();
        }

        var cache = new Dictionary<(string, string), object?>();
        var visiting = new HashSet<string>();
        var nodes = screen.Nodes.ToDictionary(n => n.Id);
        var result = new Dictionary<(string, string), object?>();

        object? Input(HmiNode node, string port)
        {
            var link = screen.Links.FirstOrDefault(l => l.ToNode == node.Id && l.ToPort == port);
            if (link is null || !nodes.TryGetValue(link.FromNode, out var src)) return null;
            return Output(src, link.FromPort);
        }

        object? Output(HmiNode node, string port)
        {
            if (cache.TryGetValue((node.Id, port), out var v)) return v;
            if (!visiting.Add(node.Id)) return null; // cycle guard
            try
            {
                v = Compute(node, port);
            }
            finally
            {
                visiting.Remove(node.Id);
            }
            cache[(node.Id, port)] = v;
            return v;
        }

        object? Compute(HmiNode n, string port)
        {
            switch (n.Type)
            {
                case "robot":
                {
                    var r = data.Robots.FirstOrDefault(x => x.Id == n.Param("robot"));
                    if (r is null) return port switch { "online" or "holding" or "done" => false, "state" or "name" => "—", _ => 0.0 };
                    return port switch
                    {
                        "battery" => r.Battery, "speed" => r.Speed, "progress" => r.Progress * 100, "state" => r.State,
                        "online" => r.Online, "holding" => r.Holding, "done" => r.Done, "name" => r.Name, _ => null,
                    };
                }
                case "fleet":
                {
                    var rs = data.Robots;
                    return port switch
                    {
                        "count" => (double)rs.Count,
                        "online" => (double)rs.Count(r => r.Online),
                        "holding" => (double)rs.Count(r => r.Holding),
                        "avgBattery" => rs.Count == 0 ? 0.0 : rs.Average(r => r.Battery),
                        "minBattery" => rs.Count == 0 ? 0.0 : rs.Min(r => r.Battery),
                        _ => null,
                    };
                }
                case "system":
                {
                    var s = data.System;
                    return port switch
                    {
                        "time" => s.Time, "decisions" => (double)s.Decisions, "violations" => (double)s.Violations,
                        "collisions" => (double)s.Collisions, "conflicts" => (double)s.ConflictPairs, "broker" => s.BrokerOnline,
                        "running" => s.Running, _ => null,
                    };
                }
                case "const":
                {
                    var raw = n.Param("value");
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
                    if (bool.TryParse(raw, out var b)) return b;
                    return raw;
                }
                case "button": return pressed.Contains(n.Param("widget"));
                case "compare":
                {
                    double a = Num(Input(n, "a")), b = Num(Input(n, "b"));
                    return n.Param("op", ">") switch
                    {
                        ">" => a > b, "<" => a < b, ">=" => a >= b, "<=" => a <= b,
                        "==" => Math.Abs(a - b) < 1e-9, "!=" => Math.Abs(a - b) >= 1e-9, _ => false,
                    };
                }
                case "math":
                {
                    double a = Num(Input(n, "a")), b = Num(Input(n, "b"));
                    return n.Param("op", "+") switch
                    {
                        "+" => a + b, "-" => a - b, "*" => a * b, "/" => b == 0 ? 0 : a / b,
                        "min" => Math.Min(a, b), "max" => Math.Max(a, b), _ => 0.0,
                    };
                }
                case "and": return Bool(Input(n, "a")) && Bool(Input(n, "b"));
                case "or": return Bool(Input(n, "a")) || Bool(Input(n, "b"));
                case "not": return !Bool(Input(n, "in"));
                case "select": return Bool(Input(n, "cond")) ? Input(n, "whenTrue") : Input(n, "whenFalse");
                case "threshold":
                {
                    double v = Num(Input(n, "in"));
                    double lo = Num(n.Param("low", "30")), hi = Num(n.Param("high", "60"));
                    return v < lo ? n.Param("lowColor", "#C62828") : v < hi ? n.Param("midColor", "#B76E00") : n.Param("highColor", "#2E7D32");
                }
                case "format":
                {
                    var v = Input(n, "in");
                    try { return string.Format(CultureInfo.InvariantCulture, n.Param("format", "{0}"), v); }
                    catch (FormatException) { return v?.ToString(); }
                }
                default: return null;
            }
        }

        foreach (var n in screen.Nodes)
        {
            if (n.Type == "set")
            {
                var w = n.Param("widget");
                if (w.Length > 0) result[(w, n.Param("property", "Value"))] = Input(n, "value");
            }
            else if (n.Type == "command" && Bool(Input(n, "trigger")))
            {
                data.SendCommand(n.Param("robot"), n.Param("command", "pause"));
            }
        }
        return result;
    }

    public static double Num(object? v) => v switch
    {
        double d => d,
        int i => i,
        long l => l,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => 0,
    };

    public static bool Bool(object? v) => v switch
    {
        bool b => b,
        double d => Math.Abs(d) > 1e-9,
        string s => bool.TryParse(s, out var b) ? b : s.Length > 0 && s != "0",
        _ => false,
    };
}
