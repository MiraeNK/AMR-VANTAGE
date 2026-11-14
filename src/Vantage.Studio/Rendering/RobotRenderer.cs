using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Vantage.Studio.Rendering;

/// <summary>Off-screen renders of the catalogue models (cached), for cards and the splash screen.</summary>
public static class RobotRenderer
{
    private static readonly Dictionary<string, BitmapSource> Cache = new();

    public static BitmapSource Render(string key, int width, int height, double yawDeg = -38, double pitchDeg = 24, double zoom = 1.0)
    {
        string ck = $"{key}|{width}|{height}|{yawDeg}|{pitchDeg}|{zoom}";
        lock (Cache)
            if (Cache.TryGetValue(ck, out var hit)) return hit;

        var vp = new Viewport3D { Width = width, Height = height, ClipToBounds = false };
        var scene = BuildScene(key, out var bounds);
        vp.Children.Add(new ModelVisual3D { Content = scene });
        vp.Camera = Camera(bounds, yawDeg, pitchDeg, zoom, (double)width / height);
        vp.Measure(new Size(width, height));
        vp.Arrange(new Rect(0, 0, width, height));
        vp.UpdateLayout();

        const double scale = 2;
        var rtb = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(vp);
        rtb.Freeze();
        lock (Cache) Cache[ck] = rtb;
        return rtb;
    }

    public static Model3DGroup BuildScene(string key, out Rect3D bounds)
    {
        var model = RobotModels.Build(key);
        bounds = model.Bounds;
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(0x5A, 0x5E, 0x64)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(0xF4, 0xF1, 0xEA), new Vector3D(-0.6, -1.0, -0.8)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(0x70, 0x78, 0x84), new Vector3D(0.8, -0.3, 0.9)));
        scene.Children.Add(RobotModels.Shadow(bounds));
        scene.Children.Add(model);
        return scene;
    }

    public static PerspectiveCamera Camera(Rect3D b, double yawDeg, double pitchDeg, double zoom, double aspect)
    {
        var center = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY * 0.45, b.Z + b.SizeZ / 2);
        double radius = Math.Sqrt(b.SizeX * b.SizeX + b.SizeY * b.SizeY + b.SizeZ * b.SizeZ) / 2;
        double fov = 30;
        double dist = radius / Math.Sin(fov * Math.PI / 360) / zoom * (aspect < 1 ? 1.15 / aspect : 1.05);
        double yaw = yawDeg * Math.PI / 180, pitch = pitchDeg * Math.PI / 180;
        var dir = new Vector3D(Math.Cos(pitch) * Math.Cos(yaw), Math.Sin(pitch), Math.Cos(pitch) * Math.Sin(yaw));
        var pos = center + dir * dist;
        return new PerspectiveCamera(pos, center - pos, new Vector3D(0, 1, 0), fov);
    }
}

/// <summary>Interactive 3D view: drag to orbit, wheel to zoom, double-click to reset.</summary>
public sealed class RobotViewer3D : UserControl
{
    public static readonly DependencyProperty ModelKeyProperty = DependencyProperty.Register(
        nameof(ModelKey), typeof(string), typeof(RobotViewer3D), new PropertyMetadata("amr-compact", (d, _) => ((RobotViewer3D)d).Rebuild()));

    public string ModelKey
    {
        get => (string)GetValue(ModelKeyProperty);
        set => SetValue(ModelKeyProperty, value);
    }

    private readonly Viewport3D _vp = new();
    private Rect3D _bounds;
    private double _yaw = -38, _pitch = 22, _zoom = 1;
    private Point? _drag;

    public RobotViewer3D()
    {
        Content = new Border { Background = Brushes.Transparent, Child = _vp };
        Cursor = Cursors.SizeAll;
        Rebuild();
    }

    private void Rebuild()
    {
        _vp.Children.Clear();
        _vp.Children.Add(new ModelVisual3D { Content = RobotRenderer.BuildScene(ModelKey ?? "amr-compact", out _bounds) });
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double aspect = ActualWidth > 0 && ActualHeight > 0 ? ActualWidth / ActualHeight : 1.3;
        _vp.Camera = RobotRenderer.Camera(_bounds, _yaw, _pitch, _zoom, aspect);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateCamera();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { _yaw = -38; _pitch = 22; _zoom = 1; UpdateCamera(); return; }
        _drag = e.GetPosition(this);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag is not { } from || !IsMouseCaptured) return;
        var p = e.GetPosition(this);
        _yaw -= (p.X - from.X) * 0.5;
        _pitch = Math.Clamp(_pitch + (p.Y - from.Y) * 0.35, 3, 80);
        _drag = p;
        UpdateCamera();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _drag = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.5, 3);
        UpdateCamera();
    }
}
