using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Vantage.Fleet.Hmi;
using Vantage.Studio.Controls;
using Vantage.Studio.ViewModels;

namespace Vantage.Studio.Views;

public partial class HmiDesignerView : UserControl
{
    private const double Snap = 8;
    private enum DragMode { None, MoveWidget, ResizeWidget, MoveNode, Wire }

    private readonly Dictionary<WidgetItem, Grid> _widgetHosts = new();
    private readonly Dictionary<NodeItem, Canvas> _nodeHosts = new();
    private readonly LinkLayer _links = new();
    private HmiDesignerViewModel? _vm;

    private DragMode _mode;
    private Point _dragStart;
    private Point _origin;
    private Size _originSize;
    private object? _dragItem;
    private (string Node, string Port)? _wireFrom;
    private (object Item, Point At)? _toolboxPress;

    public HmiDesignerView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as HmiDesignerViewModel);
        ScreenCanvas.MouseMove += ScreenCanvas_MouseMove;
        ScreenCanvas.MouseLeftButtonUp += (_, _) => EndDrag(ScreenCanvas);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not ComboBox)
            {
                _vm?.DeleteSelectionCommand.Execute(null);
                e.Handled = true;
            }
        };
        _links.Provider = LinkSegments;
    }

    private void Attach(HmiDesignerViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.Widgets.CollectionChanged -= OnWidgetsChanged;
            _vm.Nodes.CollectionChanged -= OnNodesChanged;
            _vm.GraphChanged -= OnGraphChanged;
        }
        _vm = vm;
        if (vm is null) return;
        vm.Widgets.CollectionChanged += OnWidgetsChanged;
        vm.Nodes.CollectionChanged += OnNodesChanged;
        vm.GraphChanged += OnGraphChanged;
        RebuildScreen();
        RebuildLogic();
    }

    private void OnWidgetsChanged(object? s, NotifyCollectionChangedEventArgs e) => RebuildScreen();
    private void OnNodesChanged(object? s, NotifyCollectionChangedEventArgs e) => RebuildLogic();
    private void OnGraphChanged() => _links.InvalidateVisual();

    // ═══════════════════════════════ Screen surface ═══════════════════════════════

    private void RebuildScreen()
    {
        foreach (var w in _widgetHosts.Keys) w.PropertyChanged -= OnWidgetPropertyChanged;
        _widgetHosts.Clear();
        ScreenCanvas.Children.Clear();
        if (_vm is null) return;
        foreach (var w in _vm.Widgets)
        {
            var host = MakeWidgetHost(w);
            _widgetHosts[w] = host;
            ScreenCanvas.Children.Add(host);
            w.PropertyChanged += OnWidgetPropertyChanged;
        }
    }

    private Grid MakeWidgetHost(WidgetItem w)
    {
        var host = new Grid { Width = w.Width, Height = w.Height, Background = Brushes.Transparent, Tag = w };
        Canvas.SetLeft(host, w.X);
        Canvas.SetTop(host, w.Y);
        Panel.SetZIndex(host, w.Kind == WidgetKind.Panel ? 0 : 1);

        var view = new HmiWidgetView(w.Model, design: true) { IsHitTestVisible = false };
        host.Children.Add(view);
        host.Loaded += (_, _) => view.Refresh(null, null, MapPreview());

        var sel = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x07, 0x5B, 0xCC)), StrokeThickness = 1.5, StrokeDashArray = [4, 2],
            Margin = new Thickness(-3), IsHitTestVisible = false, Visibility = w.IsSelected ? Visibility.Visible : Visibility.Collapsed,
        };
        host.Children.Add(sel);

        var handle = new Rectangle
        {
            Width = 9, Height = 9, Fill = Brushes.White, Stroke = sel.Stroke, StrokeThickness = 1.5, Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -6, -6),
            Visibility = sel.Visibility,
        };
        handle.MouseLeftButtonDown += (_, e) =>
        {
            _vm?.Select(w);
            Begin(DragMode.ResizeWidget, w, e.GetPosition(ScreenCanvas), new Point(w.X, w.Y), new Size(w.Width, w.Height), ScreenCanvas);
            e.Handled = true;
        };
        host.Children.Add(handle);

        host.MouseLeftButtonDown += (_, e) =>
        {
            _vm?.Select(w);
            Focus();
            Begin(DragMode.MoveWidget, w, e.GetPosition(ScreenCanvas), new Point(w.X, w.Y), new Size(w.Width, w.Height), ScreenCanvas);
            e.Handled = true;
        };
        return host;
    }

    private Scene? MapPreview() => (Application.Current.MainWindow?.DataContext as AppViewModel)?.Monitor.Scene;

    private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not WidgetItem w || !_widgetHosts.TryGetValue(w, out var host)) return;
        switch (e.PropertyName)
        {
            case nameof(WidgetItem.X): Canvas.SetLeft(host, w.X); break;
            case nameof(WidgetItem.Y): Canvas.SetTop(host, w.Y); break;
            case nameof(WidgetItem.Width): host.Width = w.Width; RefreshWidget(host); break;
            case nameof(WidgetItem.Height): host.Height = w.Height; RefreshWidget(host); break;
            case nameof(WidgetItem.IsSelected):
                var vis = w.IsSelected ? Visibility.Visible : Visibility.Collapsed;
                host.Children[1].Visibility = vis;
                host.Children[2].Visibility = vis;
                break;
            case nameof(WidgetItem.Revision):
                host.Children.RemoveAt(0);
                var view = new HmiWidgetView(w.Model, design: true) { IsHitTestVisible = false };
                host.Children.Insert(0, view);
                view.Loaded += (_, _) => view.Refresh(null, null, MapPreview());
                break;
        }
    }

    private void RefreshWidget(Grid host)
    {
        if (host.Children[0] is HmiWidgetView v) Dispatcher.BeginInvoke(() => v.Refresh(null, null, MapPreview()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScreenCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm?.Select(null);
        Focus();
    }

    private void ScreenCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mode is not (DragMode.MoveWidget or DragMode.ResizeWidget) || _dragItem is not WidgetItem w) return;
        var d = e.GetPosition(ScreenCanvas) - _dragStart;
        if (_mode == DragMode.MoveWidget)
        {
            w.X = Math.Clamp(SnapTo(_origin.X + d.X), 0, Math.Max(0, ScreenCanvas.Width - w.Width));
            w.Y = Math.Clamp(SnapTo(_origin.Y + d.Y), 0, Math.Max(0, ScreenCanvas.Height - w.Height));
        }
        else
        {
            w.Width = Math.Max(24, SnapTo(_originSize.Width + d.X));
            w.Height = Math.Max(20, SnapTo(_originSize.Height + d.Y));
        }
    }

    private void ScreenCanvas_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null || e.Data.GetData(typeof(ToolboxItem)) is not ToolboxItem t) return;
        var p = e.GetPosition(ScreenCanvas);
        _vm.Tab = 0;
        _vm.AddWidgetAt(t.Kind, SnapTo(p.X), SnapTo(p.Y));
    }

    // ═══════════════════════════════ Logic surface ═══════════════════════════════

    private void RebuildLogic()
    {
        foreach (var n in _nodeHosts.Keys) n.PropertyChanged -= OnNodePropertyChanged;
        _nodeHosts.Clear();
        LogicCanvas.Children.Clear();
        _links.Width = LogicCanvas.Width;
        _links.Height = LogicCanvas.Height;
        _links.IsHitTestVisible = false;
        LogicCanvas.Children.Add(_links);
        if (_vm is null) return;
        foreach (var n in _vm.Nodes)
        {
            var host = MakeNodeHost(n);
            _nodeHosts[n] = host;
            LogicCanvas.Children.Add(host);
            n.PropertyChanged += OnNodePropertyChanged;
        }
        _links.InvalidateVisual();
    }

    private static readonly Brush NodeBody = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x4B)));
    private static readonly Brush NodeEdge = Frozen(new SolidColorBrush(Color.FromRgb(0x55, 0x5F, 0x69)));
    private static readonly Brush NodeSelected = Frozen(new SolidColorBrush(Color.FromRgb(0x5A, 0xA2, 0xFF)));
    private static readonly Brush NodeText = Frozen(new SolidColorBrush(Color.FromRgb(0xD7, 0xDC, 0xE1)));
    private static readonly Brush NodeFaint = Frozen(new SolidColorBrush(Color.FromRgb(0x95, 0x9F, 0xA9)));

    private Canvas MakeNodeHost(NodeItem n)
    {
        double w = NodeItem.NodeWidth, h = n.Height;
        var host = new Canvas { Width = w, Height = h, Tag = n };
        Canvas.SetLeft(host, n.X);
        Canvas.SetTop(host, n.Y);

        var body = new Border
        {
            Width = w, Height = h, Background = NodeBody, BorderBrush = n.IsSelected ? NodeSelected : NodeEdge,
            BorderThickness = new Thickness(n.IsSelected ? 2 : 1), CornerRadius = new CornerRadius(3),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.4 },
        };
        host.Children.Add(body);

        var header = new Border
        {
            Width = w, Height = NodeItem.HeaderHeight, Background = n.HeaderBrush, CornerRadius = new CornerRadius(3, 3, 0, 0), Cursor = Cursors.SizeAll,
            Child = new DockPanel
            {
                Margin = new Thickness(8, 0, 8, 0),
                Children =
                {
                    new TextBlock { Text = n.Id, Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), FontSize = 9.5, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = n.Title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
        DockPanel.SetDock((UIElement)((DockPanel)header.Child).Children[0], Dock.Right);
        header.MouseLeftButtonDown += (_, e) =>
        {
            _vm?.Select(n);
            Focus();
            Begin(DragMode.MoveNode, n, e.GetPosition(LogicCanvas), new Point(n.X, n.Y), default, LogicCanvas);
            e.Handled = true;
        };
        host.Children.Add(header);

        foreach (var p in n.Inputs) AddPort(host, n, p);
        foreach (var p in n.Outputs) AddPort(host, n, p);

        var footer = new TextBlock
        {
            Text = n.Summary, Foreground = NodeFaint, FontSize = 10, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Width = w - 16, TextTrimming = TextTrimming.CharacterEllipsis, Tag = "summary",
        };
        Canvas.SetLeft(footer, 8);
        Canvas.SetTop(footer, h - NodeItem.FooterHeight + 3);
        host.Children.Add(footer);

        host.MouseLeftButtonDown += (_, e) => { _vm?.Select(n); Focus(); e.Handled = true; };
        return host;
    }

    private void AddPort(Canvas host, NodeItem n, PortItem p)
    {
        double y = NodeItem.HeaderHeight + 6 + p.Index * NodeItem.RowHeight + NodeItem.RowHeight / 2;
        var dot = new Ellipse
        {
            Width = 11, Height = 11, Fill = p.Brush, Stroke = Brushes.White, StrokeThickness = 1.2, Cursor = Cursors.Cross,
            Tag = (n.Id, p.Name, p.IsOutput), ToolTip = $"{p.Name} : {p.Type}",
        };
        Canvas.SetLeft(dot, (p.IsOutput ? NodeItem.NodeWidth : 0) - 5.5);
        Canvas.SetTop(dot, y - 5.5);
        var label = new TextBlock { Text = p.Name, Foreground = NodeText, FontSize = 10.5 };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, p.IsOutput ? NodeItem.NodeWidth - 12 - label.DesiredSize.Width : 12);
        Canvas.SetTop(label, y - 8);
        host.Children.Add(label);
        host.Children.Add(dot);

        if (p.IsOutput)
        {
            dot.MouseLeftButtonDown += (_, e) =>
            {
                _wireFrom = (n.Id, p.Name);
                Begin(DragMode.Wire, n, e.GetPosition(LogicCanvas), default, default, LogicCanvas);
                _links.Pending = (n.PortPosition(p.Name, true), e.GetPosition(LogicCanvas));
                e.Handled = true;
            };
        }
        else
        {
            dot.MouseRightButtonUp += (_, e) => { _vm?.Disconnect(n.Id, p.Name); e.Handled = true; };
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NodeItem n || !_nodeHosts.TryGetValue(n, out var host)) return;
        switch (e.PropertyName)
        {
            case nameof(NodeItem.X): Canvas.SetLeft(host, n.X); _links.InvalidateVisual(); break;
            case nameof(NodeItem.Y): Canvas.SetTop(host, n.Y); _links.InvalidateVisual(); break;
            case nameof(NodeItem.IsSelected):
                var body = (Border)host.Children[0];
                body.BorderBrush = n.IsSelected ? NodeSelected : NodeEdge;
                body.BorderThickness = new Thickness(n.IsSelected ? 2 : 1);
                break;
            case nameof(NodeItem.Summary):
                foreach (var t in host.Children.OfType<TextBlock>().Where(t => Equals(t.Tag, "summary"))) t.Text = n.Summary;
                break;
        }
    }

    private void LogicCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _vm?.Select(null);
        Focus();
    }

    private void LogicCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(LogicCanvas);
        if (_mode == DragMode.MoveNode && _dragItem is NodeItem n)
        {
            var d = p - _dragStart;
            n.X = Math.Clamp(Math.Round((_origin.X + d.X) / 4) * 4, 0, LogicCanvas.Width - NodeItem.NodeWidth);
            n.Y = Math.Clamp(Math.Round((_origin.Y + d.Y) / 4) * 4, 0, LogicCanvas.Height - n.Height);
        }
        else if (_mode == DragMode.Wire && _links.Pending is { } pending)
        {
            _links.Pending = (pending.From, p);
            _links.InvalidateVisual();
        }
    }

    private void LogicCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.Wire && _wireFrom is { } from)
        {
            var hit = LogicCanvas.InputHitTest(e.GetPosition(LogicCanvas)) as Ellipse;
            if (hit?.Tag is ValueTuple<string, string, bool> { Item3: false } target)
                _vm?.Connect(from.Node, from.Port, target.Item1, target.Item2);
        }
        _wireFrom = null;
        _links.Pending = null;
        _links.InvalidateVisual();
        EndDrag(LogicCanvas);
    }

    private void LogicCanvas_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null || e.Data.GetData(typeof(NodeDef)) is not NodeDef def) return;
        var p = e.GetPosition(LogicCanvas);
        _vm.AddNodeAt(def.Type, p.X - NodeItem.NodeWidth / 2, p.Y - 12);
    }

    private IEnumerable<(Point A, Point B, Brush Brush)> LinkSegments()
    {
        if (_vm is null) yield break;
        var byId = _vm.Nodes.ToDictionary(n => n.Id);
        foreach (var l in _vm.Links)
        {
            if (!byId.TryGetValue(l.FromNode, out var a) || !byId.TryGetValue(l.ToNode, out var b)) continue;
            var port = a.Outputs.FirstOrDefault(p => p.Name == l.FromPort);
            yield return (a.PortPosition(l.FromPort, true), b.PortPosition(l.ToPort, false), port?.Brush ?? Brushes.Gray);
        }
    }

    // ═══════════════════════════════ Toolbox drag ═══════════════════════════════

    private void ToolboxWidget_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = (ToolboxItem)((FrameworkElement)sender).DataContext;
        if (e.ClickCount == 2) { _vm!.Tab = 0; _vm.AddWidgetCommand.Execute(item); return; }
        _toolboxPress = (item, e.GetPosition(this));
    }

    private void ToolboxNode_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var def = (NodeDef)((FrameworkElement)sender).DataContext;
        if (e.ClickCount == 2) { _vm!.Tab = 1; _vm.AddNodeCommand.Execute(def); return; }
        _toolboxPress = (def, e.GetPosition(this));
    }

    private void Toolbox_MouseMove(object sender, MouseEventArgs e)
    {
        if (_toolboxPress is not { } press || e.LeftButton != MouseButtonState.Pressed) { _toolboxPress = null; return; }
        if ((e.GetPosition(this) - press.At).Length < 5) return;
        _toolboxPress = null;
        if (press.Item is ToolboxItem) _vm!.Tab = 0;
        if (press.Item is NodeDef) _vm!.Tab = 1;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(press.Item.GetType(), press.Item), DragDropEffects.Copy);
    }

    // ═══════════════════════════════ helpers ═══════════════════════════════

    private void Begin(DragMode mode, object item, Point start, Point origin, Size size, IInputElement capture)
    {
        _mode = mode;
        _dragItem = item;
        _dragStart = start;
        _origin = origin;
        _originSize = size;
        Mouse.Capture(capture);
    }

    private void EndDrag(IInputElement surface)
    {
        _mode = DragMode.None;
        _dragItem = null;
        if (Mouse.Captured == surface) Mouse.Capture(null);
    }

    private static double SnapTo(double v) => Math.Round(v / Snap) * Snap;

    private void PropertyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}

/// <summary>Draws node links as bezier wires, plus the wire being dragged.</summary>
public sealed class LinkLayer : FrameworkElement
{
    public Func<IEnumerable<(Point A, Point B, Brush Brush)>>? Provider { get; set; }
    public (Point From, Point To)? Pending { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Provider is not null)
            foreach (var (a, b, brush) in Provider()) Wire(dc, a, b, new Pen(brush, 2.2));
        if (Pending is { } p) Wire(dc, p.From, p.To, new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0xA2, 0xFF)), 2) { DashStyle = new DashStyle([4, 2], 0) });
    }

    private static void Wire(DrawingContext dc, Point a, Point b, Pen pen)
    {
        double dx = Math.Max(40, Math.Abs(b.X - a.X) * 0.5);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, false, false);
            ctx.BezierTo(new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
