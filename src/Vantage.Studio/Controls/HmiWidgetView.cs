using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Vantage.Fleet.Hmi;
using Vantage.Studio.Rendering;

namespace Vantage.Studio.Controls;

/// <summary>
/// Renders one HMI widget. Built once per widget; <see cref="Refresh"/> pushes live values
/// (graph overrides + fleet data). Visual language follows high-performance HMI:
/// grey surfaces, colour only for state.
/// </summary>
public sealed class HmiWidgetView : Border
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private static readonly Brush Surface = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
    private static readonly Brush Edge = Freeze(new SolidColorBrush(Color.FromRgb(0xBF, 0xC5, 0xCC)));
    private static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0x60, 0x6A)));
    private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x21, 0x26)));
    private static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromRgb(0xE3, 0xE7, 0xEB)));

    private readonly Queue<double> _trend = new();
    private TextBlock? _text, _value, _caption;
    private Ellipse? _lamp;
    private Rectangle? _barFill;
    private Grid? _barTrack;
    private Polyline? _trendLine;
    private Canvas? _trendCanvas;
    private StackPanel? _table;
    private WorkspaceView? _map;
    private Image? _cardImage;
    private TextBlock? _cardState;
    private Rectangle? _cardBattery;

    public HmiWidgetView(HmiWidget widget, bool design)
    {
        Widget = widget;
        Design = design;
        Build();
    }

    public HmiWidget Widget { get; }
    public bool Design { get; }
    public event Action<string>? Pressed;

    private string P(IReadOnlyDictionary<(string, string), object?>? o, string key, string fallback = "")
    {
        if (o is not null && o.TryGetValue((Widget.Id, key), out var v) && v is not null)
            return v is double d ? d.ToString(CultureInfo.InvariantCulture) : v.ToString() ?? fallback;
        return Widget.Prop(key, fallback);
    }

    private void Build()
    {
        var w = Widget;
        Background = Brushes.Transparent;
        switch (w.Kind)
        {
            case WidgetKind.Label:
                _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                Child = _text;
                break;

            case WidgetKind.Value:
                Card();
                _caption = Caption();
                _value = new TextBlock { FontFamily = Mono, FontSize = 18, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Right };
                Child = Stack(_caption, _value);
                break;

            case WidgetKind.Lamp:
                Card();
                _lamp = new Ellipse { Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0), Stroke = Edge, StrokeThickness = 1 };
                _caption = Caption();
                _caption.FontSize = 12;
                _caption.Foreground = Ink;
                _caption.VerticalAlignment = VerticalAlignment.Center;
                var lp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                lp.Children.Add(_lamp);
                lp.Children.Add(_caption);
                Child = lp;
                break;

            case WidgetKind.Bar:
                Card();
                _caption = Caption();
                _value = new TextBlock { FontFamily = Mono, FontSize = 12, Foreground = Ink };
                var head = new DockPanel();
                DockPanel.SetDock(_value, Dock.Right);
                head.Children.Add(_value);
                head.Children.Add(_caption);
                _barFill = new Rectangle { HorizontalAlignment = HorizontalAlignment.Left };
                _barTrack = new Grid { Height = 10, Background = Track, Margin = new Thickness(0, 6, 0, 0) };
                _barTrack.Children.Add(_barFill);
                Child = Stack(head, _barTrack);
                break;

            case WidgetKind.Button:
                var b = new Button
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x31, 0x38)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Design ? null : System.Windows.Input.Cursors.Hand,
                    IsHitTestVisible = !Design,
                    Template = ButtonTemplate(),
                };
                b.Click += (_, _) => Pressed?.Invoke(w.Id);
                _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                b.Content = _text;
                Child = b;
                break;

            case WidgetKind.Trend:
                Card();
                _caption = Caption();
                _value = new TextBlock { FontFamily = Mono, FontSize = 12, Foreground = Ink };
                var th = new DockPanel();
                DockPanel.SetDock(_value, Dock.Right);
                th.Children.Add(_value);
                th.Children.Add(_caption);
                _trendCanvas = new Canvas { ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF9)), Margin = new Thickness(0, 6, 0, 0) };
                _trendLine = new Polyline { Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0x5B, 0xCC)), StrokeThickness = 1.6 };
                _trendCanvas.Children.Add(_trendLine);
                var tg = new DockPanel();
                DockPanel.SetDock(th, Dock.Top);
                tg.Children.Add(th);
                tg.Children.Add(_trendCanvas);
                Child = tg;
                break;

            case WidgetKind.Panel:
                Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
                BorderBrush = Edge;
                BorderThickness = new Thickness(1);
                _text = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 11.5, Foreground = Dim, Margin = new Thickness(8, 4, 8, 4) };
                Child = new DockPanel { LastChildFill = false, Children = { new Border { Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE1, 0xE6)), Child = _text } } };
                DockPanel.SetDock((UIElement)((DockPanel)Child).Children[0], Dock.Top);
                break;

            case WidgetKind.FleetTable:
                Card(padding: 0);
                _table = new StackPanel();
                Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _table };
                break;

            case WidgetKind.RobotCard:
                Card(padding: 0);
                _cardImage = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(6) };
                _text = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 4, 8, 0) };
                _cardState = new TextBlock { FontFamily = Mono, FontSize = 10.5, Foreground = Dim, Margin = new Thickness(8, 0, 8, 4) };
                _cardBattery = new Rectangle { Height = 5, HorizontalAlignment = HorizontalAlignment.Left };
                var bt = new Grid { Height = 5, Background = Track, Margin = new Thickness(8, 0, 8, 8) };
                bt.Children.Add(_cardBattery);
                var cg = new DockPanel();
                foreach (var el in new UIElement[] { bt, _cardState, _text }) { DockPanel.SetDock(el, Dock.Bottom); cg.Children.Add(el); }
                cg.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xF4)), Child = _cardImage });
                Child = cg;
                break;

            case WidgetKind.Map:
                Card(padding: 0);
                _map = new WorkspaceView { ShowHud = false, IsHitTestVisible = !Design };
                Child = _map;
                break;
        }
    }

    /// <summary>Pushes values. <paramref name="o"/> are graph outputs keyed by (widget, property).</summary>
    public void Refresh(IReadOnlyDictionary<(string, string), object?>? o, IHmiDataSource? data, Scene? scene)
    {
        var w = Widget;
        switch (w.Kind)
        {
            case WidgetKind.Label:
                _text!.Text = P(o, "Text", w.Id);
                _text.FontSize = Num(P(o, "FontSize", "14"), 14);
                _text.FontWeight = P(o, "Bold") == "true" ? FontWeights.Bold : FontWeights.Normal;
                _text.Foreground = Hex(P(o, "Color", "#1C2126"));
                break;

            case WidgetKind.Value:
            {
                _caption!.Text = P(o, "Caption", w.Id).ToUpperInvariant();
                var raw = P(o, "Value", Design ? "0" : "—");
                var fmt = P(o, "Format", "F1");
                var txt = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d.ToString(fmt, CultureInfo.InvariantCulture) : raw;
                _value!.Text = $"{txt} {P(o, "Unit")}".TrimEnd();
                break;
            }

            case WidgetKind.Lamp:
            {
                _caption!.Text = P(o, "Caption", w.Id);
                bool on = HmiEngine.Bool(P(o, "On", Design ? "true" : "false"));
                _lamp!.Fill = Hex(on ? P(o, "OnColor", "#2E7D32") : P(o, "OffColor", "#8A939C"));
                break;
            }

            case WidgetKind.Bar:
            {
                _caption!.Text = P(o, "Caption", w.Id).ToUpperInvariant();
                double v = Num(P(o, "Value", Design ? "65" : "0"), 0), min = Num(P(o, "Min", "0"), 0), max = Num(P(o, "Max", "100"), 100);
                _value!.Text = $"{v:F0} {P(o, "Unit")}".TrimEnd();
                double ratio = Math.Clamp((v - min) / Math.Max(1e-9, max - min), 0, 1);
                _barFill!.Fill = Hex(P(o, "Color", "#075BCC"));
                _barFill.Width = Math.Max(0, (_barTrack!.ActualWidth > 0 ? _barTrack.ActualWidth : w.Width - 22) * ratio);
                break;
            }

            case WidgetKind.Button:
                _text!.Text = P(o, "Text", w.Id);
                break;

            case WidgetKind.Trend:
            {
                _caption!.Text = P(o, "Caption", w.Id).ToUpperInvariant();
                double v = Num(P(o, "Value", Design ? "50" : "0"), 0), max = Num(P(o, "Max", "100"), 100);
                _value!.Text = v.ToString("F1", CultureInfo.InvariantCulture);
                if (!Design || _trend.Count == 0)
                {
                    if (Design) for (int i = 0; i < 60; i++) _trend.Enqueue(50 + 25 * Math.Sin(i / 6.0));
                    else _trend.Enqueue(v);
                    while (_trend.Count > 150) _trend.Dequeue();
                }
                double cw = _trendCanvas!.ActualWidth > 0 ? _trendCanvas.ActualWidth : w.Width - 20;
                double ch = _trendCanvas.ActualHeight > 0 ? _trendCanvas.ActualHeight : w.Height - 40;
                var pts = new PointCollection();
                int n = 0;
                foreach (var s in _trend)
                {
                    pts.Add(new Point(cw * n / 149.0, ch - Math.Clamp(s / Math.Max(1e-9, max), 0, 1) * (ch - 4) - 2));
                    n++;
                }
                _trendLine!.Points = pts;
                break;
            }

            case WidgetKind.Panel:
                _text!.Text = P(o, "Title", w.Id).ToUpperInvariant();
                break;

            case WidgetKind.FleetTable:
                FillTable(data);
                break;

            case WidgetKind.RobotCard:
            {
                var id = P(o, "Robot", "AMR-01");
                var r = data?.Robots.FirstOrDefault(x => x.Id == id);
                _text!.Text = r is null ? id : $"{r.Id} · {r.Name}";
                _cardState!.Text = r is null ? (Design ? "DRIVING · 0.62 m/s" : "OFFLINE") : $"{r.State} · {r.Speed:F2} m/s · {r.Battery:F0} %";
                _cardImage!.Source ??= RobotRenderer.Render(string.IsNullOrEmpty(r?.Model) ? "amr-compact" : r!.Model, 220, 170);
                double bat = r?.Battery ?? (Design ? 72 : 0);
                _cardBattery!.Width = Math.Max(0, (w.Width - 18) * bat / 100);
                _cardBattery.Fill = Hex(bat < 30 ? "#C62828" : bat < 60 ? "#B76E00" : "#2E7D32");
                break;
            }

            case WidgetKind.Map:
                if (scene is not null) _map!.Scene = scene;
                break;
        }
    }

    private void FillTable(IHmiDataSource? data)
    {
        _table!.Children.Clear();
        _table.Children.Add(Row(true, "ROBOT", "STATE", "BATT", "SPEED"));
        var robots = data?.Robots ?? [];
        if (robots.Count == 0 && Design)
            robots = [new("AGV-01", "", "AGV", "", true, 88, 0.6, "DRIVING", 0.4, 0, 0, false, false), new("AMR-01", "", "AMR", "", true, 76, 0, "HOLD", 0.2, 0, 0, true, false)];
        foreach (var r in robots)
            _table.Children.Add(Row(false, r.Id, r.State, $"{r.Battery:F0} %", $"{r.Speed:F2}", r.Holding ? "#B76E00" : r.State == "OFFLINE" ? "#8A939C" : r.Done ? "#2E7D32" : "#1C2126"));
    }

    private static Grid Row(bool header, string a, string b, string c, string d, string color = "#1C2126")
    {
        var g = new Grid { Background = header ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0xF5, 0xF7)) : Brushes.Transparent };
        foreach (var star in new[] { 1.1, 1.3, 0.8, 0.8 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(star, GridUnitType.Star) });
        var cells = new[] { a, b, c, d };
        for (int i = 0; i < 4; i++)
        {
            var t = new TextBlock
            {
                Text = cells[i], FontFamily = Mono, FontSize = header ? 9.5 : 11, Margin = new Thickness(8, 4, 8, 4),
                Foreground = header ? Dim : i == 1 ? Hex(color) : Ink, FontWeight = header || i == 1 ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = i >= 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return new Grid { Children = { g, new Rectangle { Height = 1, Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xE6, 0xEA)), VerticalAlignment = VerticalAlignment.Bottom } } };
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private void Card(double padding = 10)
    {
        Background = Surface;
        BorderBrush = Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(padding, padding * 0.7, padding, padding * 0.7);
    }

    private static TextBlock Caption() => new() { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis };

    private static StackPanel Stack(params UIElement[] els)
    {
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var e in els) s.Children.Add(e);
        return s;
    }

    private static ControlTemplate ButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var press = new Trigger { Property = Button.IsPressedProperty, Value = true };
        press.Setters.Add(new Setter(OpacityProperty, 0.8));
        t.Triggers.Add(press);
        return t;
    }

    private static double Num(string s, double fallback) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    private static readonly Dictionary<string, Brush> BrushCache = new();

    private static Brush Hex(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var b)) return b;
        try { b = (Brush)new BrushConverter().ConvertFromString(hex)!; }
        catch { b = Brushes.Gray; }
        b.Freeze();
        BrushCache[hex] = b;
        return b;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
