using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Vantage.Studio.Rendering;

/// <summary>
/// Key-visual scene for the splash: a mixed fleet on a dark shop floor with what the
/// software actually manages — a fixed AGV line, an AMR planner path, the conflict area
/// where they cross, granted segments and the clearance stop line.
/// </summary>
public static class FleetScene
{
    private static readonly Color Floor = Color.FromRgb(0xF1, 0xF3, 0xF6);
    private static readonly Color GridLine = Color.FromRgb(0xD3, 0xD9, 0xE0);
    private static readonly Color AgvLine = Color.FromRgb(0x07, 0x1D, 0x39);
    private static readonly Color AmrLine = Color.FromRgb(0x1E, 0x9B, 0xE8);
    private static readonly Color Conflict = Color.FromRgb(0xD9, 0x5A, 0x00);
    private static readonly Color Stop = Color.FromRgb(0xE5, 0x39, 0x35);

    /// <param name="heroOnly">
    /// Renders only the hero robot (same camera, transparent background). Laid over the
    /// clipped scene without clipping, it "steps out" of the splash card.
    /// </param>
    public static BitmapSource Render(int width, int height, bool heroOnly = false)
    {
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(0x6A, 0x6E, 0x74)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(0xFF, 0xFB, 0xF4), new Vector3D(-0.5, -1.0, -0.7)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(0x5E, 0x6E, 0x82), new Vector3D(0.9, -0.4, 0.6)));

        if (heroOnly)
        {
            Place(scene, "agv-pallet", HeroX, HeroZ, 0, shadow: false);
            return Snapshot(scene, width, height);
        }

        // floor 12 × 8 m with 1 m grid
        Box(scene, Floor, 0, -0.01, 0, 12, 0.02, 8, false);
        for (int i = -6; i <= 6; i++) Box(scene, GridLine, i, 0.001, 0, 0.012, 0.002, 8, false);
        for (int j = -4; j <= 4; j++) Box(scene, GridLine, 0, 0.001, j, 12, 0.002, 0.012, false);

        // second, background AGV lane
        Box(scene, AgvLine, 0.8, 0.004, -1.6, 8.4, 0.004, 0.04, true);

        // AGV virtual line along X (z = 0.6), segment ticks every metre
        Box(scene, AgvLine, 0, 0.004, 0.6, 12, 0.004, 0.05, true);
        for (int i = -5; i <= 5; i++) Box(scene, AgvLine, i, 0.005, 0.6, 0.03, 0.005, 0.22, true);

        // AMR planner path: diagonal dashes crossing the AGV line
        var a = new Point3D(-1.2, 0, -3.8);
        var b = new Point3D(2.6, 0, 3.8);
        int dashes = 34;
        for (int k = 0; k < dashes; k += 2)
        {
            var p = a + (b - a) * ((k + 0.5) / dashes);
            Strip(scene, AmrLine, p, b - a, 0.18, 0.05);
        }

        // conflict area where the swept envelopes overlap (translucent pad + outline)
        var c = Intersect();
        Pad(scene, Color.FromArgb(0x55, Conflict.R, Conflict.G, Conflict.B), c.X, c.Z, 1.9, 1.5);
        foreach (var (dx, dz, sx, sz) in new[] { (0.0, -0.75, 1.9, 0.03), (0.0, 0.75, 1.9, 0.03), (-0.95, 0.0, 0.03, 1.5), (0.95, 0.0, 0.03, 1.5) })
            Box(scene, Conflict, c.X + dx, 0.006, c.Z + dz, sx, 0.004, sz, true);

        // granted segments of the stacker (behind it) as soft amber pads
        for (int i = 0; i < 3; i++) Pad(scene, Color.FromArgb(0x38, 0x07, 0x5B, 0xCC), -3.2 + i, 0.6, 0.94, 1.0);

        // clearance stop line in front of the waiting AMR
        var dir = b - a;
        dir.Normalize();
        var stopAt = c - dir * 1.55;
        Strip(scene, Stop, stopAt, new Vector3D(-dir.Z, 0, dir.X), 1.25, 0.09, height: 0.012);

        // robots
        Place(scene, "agv-pallet", HeroX, HeroZ, 0);                                  // driving +X on the virtual line
        double amrYaw = Math.Atan2(-dir.Z, dir.X) * 180 / Math.PI;
        var amrPos = stopAt - dir * 0.75;
        Place(scene, "amr-heavy", amrPos.X, amrPos.Z, amrYaw);                         // holding before the stop line
        Place(scene, "agv-tugger", 2.3, -1.6, 180);                                    // background, second lane
        Place(scene, "amr-compact", 3.9, -3.0, 200);                                   // background right

        return Snapshot(scene, width, height);
    }

    private const double HeroX = -1.75, HeroZ = 0.6;

    private static BitmapSource Snapshot(Model3DGroup scene, int width, int height)
    {
        var vp = new Viewport3D { Width = width, Height = height };
        vp.Children.Add(new ModelVisual3D { Content = scene });
        var target = new Point3D(0.2, 0.3, 0.2);
        var eye = new Point3D(5.6, 5.0, 8.6);
        vp.Camera = new PerspectiveCamera(eye, target - eye, new Vector3D(0, 1, 0), 34);
        vp.Measure(new Size(width, height));
        vp.Arrange(new Rect(0, 0, width, height));
        vp.UpdateLayout();

        var rtb = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(vp);
        rtb.Freeze();
        return rtb;
    }

    private static Point3D Intersect()
    {
        // AMR path a→b crosses z = 0.6
        double t = (0.6 - -3.8) / (3.8 - -3.8);
        return new Point3D(-1.2 + (2.6 - -1.2) * t, 0, 0.6);
    }

    private static void Place(Model3DGroup scene, string key, double x, double z, double yawDeg, bool shadow = true)
    {
        var m = RobotModels.Build(key);
        var t = new Transform3DGroup();
        t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), yawDeg)));
        t.Children.Add(new TranslateTransform3D(x, 0, z));
        m.Transform = t;
        if (shadow) scene.Children.Add(RobotModels.Shadow(new Rect3D(x - 0.6, 0, z - 0.45, 1.2, 0.5, 0.9)));
        scene.Children.Add(m);
    }

    private static Material Mat(Color c, bool glow)
    {
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
        if (glow) g.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, c.A * 0.9 + 20), c.R, c.G, c.B))));
        return g;
    }

    private static void Box(Model3DGroup m, Color c, double cx, double cy, double cz, double sx, double sy, double sz, bool glow)
    {
        var mesh = new MeshGeometry3D();
        double x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
        void F(Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(p0); mesh.Positions.Add(p1); mesh.Positions.Add(p2); mesh.Positions.Add(p3);
            foreach (var k in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i + k);
        }
        F(new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), new(x0, y1, z0));
        F(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1));
        F(new(x1, y0, z0), new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0));
        F(new(x1, y0, z1), new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1));
        F(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0));
        mesh.Freeze();
        m.Children.Add(new GeometryModel3D(mesh, Mat(c, glow)));
    }

    /// <summary>Flat strip centred at p along direction d (length × width).</summary>
    private static void Strip(Model3DGroup m, Color c, Point3D p, Vector3D d, double length, double width, double height = 0.006)
    {
        d.Normalize();
        var n = new Vector3D(-d.Z, 0, d.X);
        var h = new Vector3D(0, height, 0);
        var corners = new[] { p - d * length / 2 - n * width / 2, p + d * length / 2 - n * width / 2, p + d * length / 2 + n * width / 2, p - d * length / 2 + n * width / 2 };
        var mesh = new MeshGeometry3D();
        foreach (var q in corners) mesh.Positions.Add(q + h);
        foreach (var k in new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(k);
        mesh.Freeze();
        m.Children.Add(new GeometryModel3D(mesh, Mat(c, true)));
    }

    private static void Pad(Model3DGroup m, Color c, double cx, double cz, double sx, double sz)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(cx - sx / 2, 0.003, cz - sz / 2));
        mesh.Positions.Add(new Point3D(cx + sx / 2, 0.003, cz - sz / 2));
        mesh.Positions.Add(new Point3D(cx + sx / 2, 0.003, cz + sz / 2));
        mesh.Positions.Add(new Point3D(cx - sx / 2, 0.003, cz + sz / 2));
        foreach (var k in new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(k);
        mesh.Freeze();
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
        g.Children.Add(new EmissiveMaterial(new SolidColorBrush(c)));
        m.Children.Add(new GeometryModel3D(mesh, g));
    }
}
