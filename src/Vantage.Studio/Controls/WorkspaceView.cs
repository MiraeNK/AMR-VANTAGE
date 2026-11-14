using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Vantage.Core.Clearance;
using Vantage.Core.Geometry;
using Vantage.Core.Paths;

namespace Vantage.Studio.Controls;

/// <summary>
/// 2D workspace renderer: map, grid, segment sweeps by clearance state, conflict
/// areas, stop lines and robot footprints. Everything is drawn in one OnRender pass.
/// </summary>
public sealed class WorkspaceView : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(
        nameof(Scene), typeof(Scene), typeof(WorkspaceView),
        new FrameworkPropertyMetadata(Scene.Empty, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((WorkspaceView)d).OnSceneChanged((Scene)e.OldValue, (Scene)e.NewValue)));

    public Scene Scene
    {
        get => (Scene)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public event Action<double, double>? CursorMoved;

    /// <summary>Show the drawing cursor on the 10 cm grid.</summary>
    public bool SnapHover { get; set; } = true;

    /// <summary>When false (embedded in an HMI screen) the view shows no legend / HUD.</summary>
    public bool ShowHud { get; set; } = true;
    public event Action<string?>? RobotPicked;

    private double _zoom = 60;          // px per metre
    private Vec2 _center = new(7, 4);   // world point at control centre
    private Point? _dragFrom;
    private Vec2 _dragCenter;
    private bool _dragged;
    private bool _fitted;
    private readonly Dictionary<string, Geometry> _sweepCache = new();

    private static readonly Typeface UiFace = new("Segoe UI");
    private static readonly Typeface UiBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface MonoFace = new(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(0xFA, 0xFB, 0xFC)));
    private static readonly Pen GridMinor = FrozenPen(Color.FromRgb(0xEE, 0xF0, 0xF2), 1);
    private static readonly Pen GridMajor = FrozenPen(Color.FromRgb(0xDB, 0xDF, 0xE3), 1);
    private static readonly Pen Boundary = FrozenPen(Color.FromRgb(0x9A, 0xA3, 0xAC), 1);
    private static readonly Brush GridText = Frozen(new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0x9C)));
    private static readonly Brush HudText = Frozen(new SolidColorBrush(Color.FromRgb(0x38, 0x40, 0x48)));
    private static readonly Brush HudBack = Frozen(new SolidColorBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen HudBorder = FrozenPen(Color.FromRgb(0xBF, 0xC5, 0xCC), 1);
    private static readonly Brush ConflictHatch = MakeHatch(Palette.Hold);

    public WorkspaceView()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = Cursors.Cross;
    }

    // ───────────────────────────── view transform ─────────────────────────────

    private Point ToScreen(Vec2 w) => new((w.X - _center.X) * _zoom + ActualWidth / 2, ActualHeight / 2 - (w.Y - _center.Y) * _zoom);
    private Point ToScreen(Pose2D p) => ToScreen(p.Position);
    private Vec2 ToWorld(Point s) => new((s.X - ActualWidth / 2) / _zoom + _center.X, (ActualHeight / 2 - s.Y) / _zoom + _center.Y);
    private Matrix WorldMatrix => new(_zoom, 0, 0, -_zoom, ActualWidth / 2 - _center.X * _zoom, ActualHeight / 2 + _center.Y * _zoom);

    public void Fit()
    {
        var s = Scene;
        var box = s.Workspace;
        if (s.Map is { } m) box = box.Union(new Aabb(m.OriginX, m.OriginY, m.OriginX + m.WidthM, m.OriginY + m.HeightM));
        foreach (var r in s.Snapshot.Robots)
            foreach (var p in r.Path) box = box.Include(p.Position);
        if (box.IsEmpty || ActualWidth < 10) return;
        box = box.Inflate(0.6);
        _center = new Vec2((box.MinX + box.MaxX) / 2, (box.MinY + box.MaxY) / 2);
        _zoom = Math.Clamp(Math.Min((ActualWidth - 40) / box.Width, (ActualHeight - 60) / box.Height), 5, 600);
        InvalidateVisual();
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        if (!ReferenceEquals(oldScene.Snapshot, newScene.Snapshot) && _sweepCache.Count > 4000) _sweepCache.Clear();
        if (oldScene.Workspace != newScene.Workspace || oldScene.Map != newScene.Map) _fitted = false;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!_fitted) { Fit(); _fitted = true; }
    }

    // ───────────────────────────── rendering ─────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (!_fitted && ActualWidth > 10) { Fit(); _fitted = true; }
        var scene = Scene;
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Background, null, rect);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (scene.Layers.HasFlag(SceneLayers.Map) && scene.Map is { } map) DrawMap(dc, map);
        if (scene.Layers.HasFlag(SceneLayers.Grid)) DrawGrid(dc, dpi);
        DrawBoundary(dc, scene.Workspace);

        var colors = scene.Robots.ToDictionary(r => r.Id, r => r.Identity);
        foreach (var r in scene.Snapshot.Robots)
        {
            var c = colors.TryGetValue(r.Profile.RobotId, out var col) ? col : Palette.Off;
            DrawSegments(dc, scene, r, c);
        }
        foreach (var r in scene.Snapshot.Robots)
        {
            var c = colors.TryGetValue(r.Profile.RobotId, out var col) ? col : Palette.Off;
            if (scene.Layers.HasFlag(SceneLayers.Paths)) DrawPath(dc, scene, r, c, dpi);
        }
        foreach (var r in scene.Snapshot.Robots)
        {
            var c = colors.TryGetValue(r.Profile.RobotId, out var col) ? col : Palette.Off;
            var sim = scene.Robots.FirstOrDefault(x => x.Id == r.Profile.RobotId);
            if (scene.Layers.HasFlag(SceneLayers.Limits)) DrawLimit(dc, r, c, sim, dpi);
        }
        foreach (var r in scene.Robots) DrawRobot(dc, scene, r, dpi);
        DrawRoutes(dc, scene, dpi);

        DrawAxes(dc, dpi);
        DrawScaleBar(dc, dpi);
        if (!ShowHud) return;
        DrawLegend(dc, dpi);
        DrawViewInfo(dc, dpi);
        if (scene.Drawing)
        {
            var ft = Format("ROUTE TEACH · click: add point · right-click: undo · drag: pan", 10.5, Brushes.White, UiBold, dpi);
            var r = new Rect(ActualWidth / 2 - ft.Width / 2 - 10, 10, ft.Width + 20, 22);
            dc.DrawRectangle(new SolidColorBrush(Palette.Accent), null, r);
            dc.DrawText(ft, new Point(r.X + 10, r.Y + 4));
        }
    }

    private void DrawMap(DrawingContext dc, Services.MapLayer map)
    {
        dc.PushTransform(new MatrixTransform(WorldMatrix));
        var r = new Rect(map.OriginX, map.OriginY, map.WidthM, map.HeightM);
        dc.PushTransform(new ScaleTransform(1, -1, 0, r.Y + r.Height / 2));
        dc.PushOpacity(0.55);
        dc.DrawImage(map.Image, r);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private void DrawGrid(DrawingContext dc, double dpi)
    {
        double minor = _zoom > 70 ? 0.25 : _zoom > 30 ? 0.5 : _zoom > 12 ? 1 : 5;
        double major = minor < 1 ? 1 : minor * 5;
        var tl = ToWorld(new Point(0, 0));
        var br = ToWorld(new Point(ActualWidth, ActualHeight));

        for (double x = Math.Floor(tl.X / minor) * minor; x <= br.X; x += minor)
        {
            bool isMajor = Math.Abs(x / major - Math.Round(x / major)) < 1e-6;
            double sx = Snap(ToScreen(new Vec2(x, 0)).X);
            dc.DrawLine(isMajor ? GridMajor : GridMinor, new Point(sx, 0), new Point(sx, ActualHeight));
            if (isMajor) Text(dc, x.ToString("0.#", CultureInfo.InvariantCulture), new Point(sx + 3, ActualHeight - 15), 9.5, GridText, MonoFace, dpi);
        }
        for (double y = Math.Floor(br.Y / minor) * minor; y <= tl.Y; y += minor)
        {
            bool isMajor = Math.Abs(y / major - Math.Round(y / major)) < 1e-6;
            double sy = Snap(ToScreen(new Vec2(0, y)).Y);
            dc.DrawLine(isMajor ? GridMajor : GridMinor, new Point(0, sy), new Point(ActualWidth, sy));
            if (isMajor) Text(dc, y.ToString("0.#", CultureInfo.InvariantCulture), new Point(4, sy - 14), 9.5, GridText, MonoFace, dpi);
        }
    }

    private void DrawBoundary(DrawingContext dc, Aabb ws)
    {
        var a = ToScreen(new Vec2(ws.MinX, ws.MaxY));
        var b = ToScreen(new Vec2(ws.MaxX, ws.MinY));
        dc.DrawRectangle(null, Boundary, new Rect(new Point(Snap(a.X), Snap(a.Y)), new Point(Snap(b.X), Snap(b.Y))));
    }

    private void DrawSegments(DrawingContext dc, Scene scene, RobotView r, Color c)
    {
        dc.PushTransform(new MatrixTransform(WorldMatrix));
        bool sweeps = scene.Layers.HasFlag(SceneLayers.Sweeps);
        bool reserv = scene.Layers.HasFlag(SceneLayers.Reservations);
        bool conflicts = scene.Layers.HasFlag(SceneLayers.Conflicts);

        foreach (var s in r.Segments.Concat(r.Legacy))
        {
            byte alpha = s.State switch
            {
                SegmentState.Granted when reserv => 0x3A,
                SegmentState.Occupied when reserv => 0x66,
                SegmentState.Waiting when sweeps => 0x12,
                _ => 0,
            };
            bool hatch = conflicts && s.InConflict && s.State is not (SegmentState.Completed or SegmentState.Revised);
            if (alpha == 0 && !hatch) continue;
            var g = SweepGeometry(s.Segment);
            if (alpha > 0) dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)), null, g);
            if (hatch) dc.DrawGeometry(ConflictHatch, null, g);
        }
        dc.Pop();
    }

    private void DrawPath(DrawingContext dc, Scene scene, RobotView r, Color c, double dpi)
    {
        if (r.Path.Count < 2) return;
        var pen = new Pen(new SolidColorBrush(c), 1.6) { LineJoin = PenLineJoin.Round };
        if (r.Profile.Class == VehicleClass.Amr) pen.DashStyle = new DashStyle([5, 3], 0);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(ToScreen(r.Path[0]), false, false);
            ctx.PolyLineTo(r.Path.Skip(1).Select(ToScreen).ToList(), true, true);
        }
        dc.DrawGeometry(null, pen, geo);

        // segment boundary ticks
        var tick = new Pen(new SolidColorBrush(c), 1);
        foreach (var s in r.Segments)
        {
            var p = s.Segment.Start;
            var n = new Vec2(-Math.Sin(p.Theta), Math.Cos(p.Theta));
            dc.DrawLine(tick, ToScreen(p.Position + n * (4 / _zoom)), ToScreen(p.Position - n * (4 / _zoom)));
            if (scene.Layers.HasFlag(SceneLayers.Labels) && _zoom > 55)
            {
                var mid = Polyline.At(s.Segment.Geometry, s.Segment.Length / 2);
                var off = new Vec2(-Math.Sin(mid.Theta), Math.Cos(mid.Theta)) * (10 / _zoom);
                Text(dc, $"s{s.Segment.Index:D2}", ToScreen(mid.Position + off) + new Vector(-8, -6), 8.5,
                    new SolidColorBrush(Color.FromArgb(0xB0, c.R, c.G, c.B)), MonoFace, dpi);
            }
        }

        // start / goal markers
        var start = ToScreen(r.Path[0]);
        dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(c), 1.4), start, 3.5, 3.5);
        var goal = ToScreen(r.Path[^1]);
        dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(goal.X - 3.5, goal.Y - 3.5, 7, 7));
    }

    private void DrawLimit(DrawingContext dc, RobotView r, Color c, SceneRobot? sim, double dpi)
    {
        if (r.Path.Count < 2 || r.MissionDone || r.LimitS >= r.PathLength - 1e-3) return;
        var p = Polyline.At(r.Path, r.LimitS);
        var n = new Vec2(-Math.Sin(p.Theta), Math.Cos(p.Theta));
        double half = r.Profile.Footprint.Width / 2 + 0.12;
        bool holding = sim?.Holding ?? r.Waiting;
        var col = holding ? Palette.Alarm : c;
        var pen = new Pen(new SolidColorBrush(col), 3) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        var a = ToScreen(p.Position + n * half);
        var b = ToScreen(p.Position - n * half);
        dc.DrawLine(pen, a, b);
        if (_zoom > 25)
            Text(dc, $"STOP {r.LimitS:F2}", new Point(Math.Max(a.X, b.X) + 4, Math.Min(a.Y, b.Y) - 2), 9, new SolidColorBrush(col), MonoFace, dpi);
    }

    private void DrawRobot(DrawingContext dc, Scene scene, SceneRobot r, double dpi)
    {
        var c = r.Identity;
        var poly = r.Footprint.At(r.Pose);
        var geo = Polygon(poly.Vertices.Select(ToScreen).ToList());
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0xE6, c.R, c.G, c.B)),
            new Pen(new SolidColorBrush(Darken(c, 0.55)), 1.2), geo);

        // heading chevron
        var dir = new Vec2(Math.Cos(r.Pose.Theta), Math.Sin(r.Pose.Theta));
        var nose = r.Pose.Position + dir * (r.Footprint.Front * 0.9);
        var back = r.Pose.Position + dir * (r.Footprint.Front * 0.62);
        var side = new Vec2(-Math.Sin(r.Pose.Theta), Math.Cos(r.Pose.Theta)) * (r.Footprint.Left * 0.62);
        var chevron = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), 1.6) { LineJoin = PenLineJoin.Miter };
        var cg = new StreamGeometry();
        using (var ctx = cg.Open())
        {
            ctx.BeginFigure(ToScreen(back + side), false, false);
            ctx.PolyLineTo([ToScreen(nose), ToScreen(back - side)], true, true);
        }
        dc.DrawGeometry(null, chevron, cg);
        // reference point
        dc.DrawEllipse(Brushes.White, null, ToScreen(r.Pose.Position), 2, 2);

        if (r.Holding && !r.Done)
        {
            var ring = Polygon(r.Footprint.At(r.Pose, 0.07).Vertices.Select(ToScreen).ToList());
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Palette.Hold), 2) { DashStyle = new DashStyle([3, 2], 0) }, ring);
        }
        if (r.Id == scene.SelectedRobotId)
        {
            var sel = Polygon(r.Footprint.At(r.Pose, 0.14).Vertices.Select(ToScreen).ToList());
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Palette.Accent), 1.5), sel);
        }

        if (!scene.Layers.HasFlag(SceneLayers.Labels)) return;
        var anchor = ToScreen(r.Pose.Position) + new Vector(r.Footprint.Length * _zoom * 0.5 + 8, -r.Footprint.Width * _zoom * 0.5 - 22);
        string state = r.Done ? "DONE" : r.Holding ? "HOLD" : $"{r.Speed:F2} m/s";
        var t1 = Format(r.Id, 10.5, Brushes.White, UiBold, dpi);
        var t2 = Format(state, 9.5, Brushes.White, MonoFace, dpi);
        double w = Math.Max(t1.Width, t2.Width) + 10;
        var box = new Rect(anchor.X, anchor.Y, w, 30);
        dc.DrawRectangle(new SolidColorBrush(Darken(c, 0.8)), null, box);
        if (r.Holding && !r.Done) dc.DrawRectangle(new SolidColorBrush(Palette.Hold), null, new Rect(box.X, box.Bottom - 3, box.Width, 3));
        dc.DrawText(t1, new Point(box.X + 5, box.Y + 2));
        dc.DrawText(t2, new Point(box.X + 5, box.Y + 15));
    }

    private void DrawAxes(DrawingContext dc, double dpi)
    {
        var o = ToScreen(Vec2.Zero);
        if (o.X < -20 || o.Y < -20 || o.X > ActualWidth + 20 || o.Y > ActualHeight + 20) return;
        var pen = new Pen(HudText, 1.2);
        dc.DrawLine(pen, o, o + new Vector(28, 0));
        dc.DrawLine(pen, o, o + new Vector(0, -28));
        Text(dc, "x", o + new Vector(30, -7), 9.5, HudText, MonoFace, dpi);
        Text(dc, "y", o + new Vector(-3, -42), 9.5, HudText, MonoFace, dpi);
        dc.DrawEllipse(HudText, null, o, 2, 2);
    }

    private void DrawScaleBar(DrawingContext dc, double dpi)
    {
        double[] nice = [0.1, 0.25, 0.5, 1, 2, 5, 10, 20];
        double len = nice.FirstOrDefault(n => n * _zoom >= 70, 20);
        double px = len * _zoom;
        double x = 14, y = ActualHeight - 34;
        var pen = new Pen(HudText, 1.4);
        dc.DrawRectangle(HudBack, HudBorder, new Rect(x - 6, y - 16, px + 12, 28));
        dc.DrawLine(pen, new Point(x, y), new Point(x + px, y));
        dc.DrawLine(pen, new Point(x, y - 4), new Point(x, y + 4));
        dc.DrawLine(pen, new Point(x + px, y - 4), new Point(x + px, y + 4));
        Text(dc, len >= 1 ? $"{len:0} m" : $"{len * 100:0} cm", new Point(x, y - 15), 9.5, HudText, MonoFace, dpi);
    }

    private void DrawLegend(DrawingContext dc, double dpi)
    {
        var items = new (string Label, Action<DrawingContext, Rect> Swatch)[]
        {
            ("AGV path (virtual line)", (d, r) => d.DrawLine(new Pen(new SolidColorBrush(Palette.Identity(VehicleClass.Agv, 0)), 1.6), new Point(r.Left, r.Top + r.Height / 2), new Point(r.Right, r.Top + r.Height / 2))),
            ("AMR path (planner)", (d, r) => d.DrawLine(new Pen(new SolidColorBrush(Palette.Identity(VehicleClass.Amr, 0)), 1.6) { DashStyle = new DashStyle([3, 2], 0) }, new Point(r.Left, r.Top + r.Height / 2), new Point(r.Right, r.Top + r.Height / 2))),
            ("Reserved · granted", (d, r) => d.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x50, 0x55, 0x60, 0x6A)), null, r)),
            ("Reserved · occupied", (d, r) => d.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0x55, 0x60, 0x6A)), null, r)),
            ("Conflict area", (d, r) => d.DrawRectangle(MakeHatch(Palette.Hold, 6), null, r)),
            ("Clearance limit", (d, r) => d.DrawLine(new Pen(new SolidColorBrush(Palette.Alarm), 3), new Point(r.Left + r.Width / 2, r.Top), new Point(r.Left + r.Width / 2, r.Bottom))),
        };
        double w = 168, h = 14 + items.Length * 17;
        double x = ActualWidth - w - 12, y = ActualHeight - h - 12;
        dc.DrawRectangle(HudBack, HudBorder, new Rect(x, y, w, h));
        for (int i = 0; i < items.Length; i++)
        {
            var sw = new Rect(x + 8, y + 9 + i * 17, 22, 9);
            items[i].Swatch(dc, sw);
            Text(dc, items[i].Label, new Point(x + 38, y + 5 + i * 17), 10, HudText, UiFace, dpi);
        }
    }

    private void DrawViewInfo(DrawingContext dc, double dpi)
    {
        var s = Scene.Snapshot;
        string info = $"t {s.Time,7:F2} s   ·   {_zoom:F0} px/m   ·   {s.ConflictPairs.Count} conflict pairs";
        var ft = Format(info, 10, HudText, MonoFace, dpi);
        dc.DrawRectangle(HudBack, HudBorder, new Rect(10, 10, ft.Width + 14, 20));
        dc.DrawText(ft, new Point(17, 13));
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private Geometry SweepGeometry(Segment seg)
    {
        if (_sweepCache.TryGetValue(seg.Id, out var g)) return g;
        var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = sg.Open())
        {
            foreach (var cell in seg.Swept.Cells)
            {
                var v = cell.Vertices;
                ctx.BeginFigure(new Point(v[0].X, v[0].Y), true, true);
                ctx.PolyLineTo(v.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), false, false);
            }
        }
        sg.Freeze();
        _sweepCache[seg.Id] = sg;
        return sg;
    }

    private static Geometry Polygon(IReadOnlyList<Point> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    private static double Snap(double v) => Math.Round(v) + 0.5;

    private static FormattedText Format(string s, double size, Brush b, Typeface face, double dpi) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, b, dpi);

    private static void Text(DrawingContext dc, string s, Point at, double size, Brush b, Typeface face, double dpi) =>
        dc.DrawText(Format(s, size, b, face, dpi), at);

    private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static Pen FrozenPen(Color c, double t) => Frozen(new Pen(Frozen(new SolidColorBrush(c)), t));

    /// <summary>Diagonal hatch. Tile size is in world metres when used under the world transform.</summary>
    private static Brush MakeHatch(Color c, double tile = 0.14)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x78, c.R, c.G, c.B)), tile * 0.16);
        var drawing = new GeometryDrawing(null, pen, new LineGeometry(new Point(0, tile), new Point(tile, 0)));
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, tile, tile),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    // ───────────────────────────── input ─────────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var pos = e.GetPosition(this);
        var before = ToWorld(pos);
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), 5, 800);
        var after = ToWorld(pos);
        _center = _center + (before - after);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) { Fit(); return; }
        _dragFrom = e.GetPosition(this);
        _dragCenter = _center;
        _dragged = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var w = ToWorld(pos);
        CursorMoved?.Invoke(w.X, w.Y);
        if (Scene.Drawing)
        {
            _hover = SnapHover ? new Vec2(Math.Round(w.X * 10) / 10, Math.Round(w.Y * 10) / 10) : w;
            InvalidateVisual();
        }
        if (_dragFrom is { } from && IsMouseCaptured)
        {
            var d = pos - from;
            if (Math.Abs(d.X) + Math.Abs(d.Y) > 3) _dragged = true;
            _center = _dragCenter - new Vec2(d.X / _zoom, -d.Y / _zoom);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        ReleaseMouseCapture();
        bool click = !_dragged;
        _dragFrom = null;
        if (!click) return;
        var w = ToWorld(pos);
        WorldClicked?.Invoke(w.X, w.Y, e.ChangedButton);
        if (e.ChangedButton != MouseButton.Left || Scene.Drawing) return;
        var hit = Scene.Robots.FirstOrDefault(r => r.Footprint.At(r.Pose, 0.15).Contains(w));
        RobotPicked?.Invoke(hit?.Id);
    }

    /// <summary>Click (no drag) in world coordinates.</summary>
    public event Action<double, double, MouseButton>? WorldClicked;

    // ───────────────────────────── route editor overlay ─────────────────────────────

    private Vec2? _hover;

    private void DrawRoutes(DrawingContext dc, Scene scene, double dpi)
    {
        if (scene.Routes is null) return;
        foreach (var r in scene.Routes.OrderBy(r => r.Selected))
        {
            var c = r.Identity;
            byte a = r.Selected ? (byte)0xFF : (byte)0x90;
            var brush = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));

            // envelope of the path (footprint width) as a light band
            if (r.Preview.Count >= 2)
            {
                var band = new Pen(new SolidColorBrush(Color.FromArgb(r.Selected ? (byte)0x30 : (byte)0x18, c.R, c.G, c.B)), r.Footprint.Width * _zoom)
                { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, band, LineGeometryOf(r.Preview));
                var pen = new Pen(brush, r.Selected ? 2.2 : 1.4) { LineJoin = PenLineJoin.Round };
                if (!r.Fixed) pen.DashStyle = new DashStyle([5, 3], 0);
                dc.DrawGeometry(null, pen, LineGeometryOf(r.Preview));
            }
            // taught way points / nodes
            if (!r.Fixed && r.WayPoints.Count >= 2)
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x60, c.R, c.G, c.B)), 1) { DashStyle = new DashStyle([2, 2], 0) }, LineGeometryOf(r.WayPoints));
            for (int i = 0; i < r.WayPoints.Count; i++)
            {
                var p = ToScreen(r.WayPoints[i]);
                if (r.Fixed) dc.DrawRectangle(Brushes.White, new Pen(brush, 1.6), new Rect(p.X - 4.5, p.Y - 4.5, 9, 9));
                else dc.DrawEllipse(Brushes.White, new Pen(brush, 1.6), p, 4.5, 4.5);
                if (r.Selected && _zoom > 25)
                    Text(dc, r.Fixed ? $"N{i}" : $"{i}", p + new Vector(6, -16), 9.5, brush, MonoFace, dpi);
            }
            if (r.WayPoints.Count > 0 && _zoom > 20)
                Text(dc, r.RobotId, ToScreen(r.WayPoints[0]) + new Vector(-10, 8), 10, brush, UiBold, dpi);
        }

        // rubber band from the last point to the cursor while drawing
        var sel = scene.Routes.FirstOrDefault(r => r.Selected);
        if (scene.Drawing && sel is not null && _hover is { } h)
        {
            var c = sel.Identity;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, c.R, c.G, c.B)), 1.4) { DashStyle = new DashStyle([3, 3], 0) };
            if (sel.WayPoints.Count > 0) dc.DrawLine(pen, ToScreen(sel.WayPoints[^1]), ToScreen(h));
            var hp = ToScreen(h);
            dc.DrawLine(new Pen(HudText, 1), hp + new Vector(-8, 0), hp + new Vector(8, 0));
            dc.DrawLine(new Pen(HudText, 1), hp + new Vector(0, -8), hp + new Vector(0, 8));
            Text(dc, $"{h.X:F2}, {h.Y:F2}", hp + new Vector(10, 4), 9.5, HudText, MonoFace, dpi);
        }
    }

    private Geometry LineGeometryOf(IReadOnlyList<Vec2> pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(ToScreen(pts[0]), false, false);
            ctx.PolyLineTo(pts.Skip(1).Select(ToScreen).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }
}
