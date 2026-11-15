using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Vantage.Studio.Rendering;

/// <summary>
/// Procedural CAD-style robot models (Y up, X forward, Z left; metres).
/// Built from boxes and cylinders so the catalogue needs no external mesh files.
/// </summary>
public static class RobotModels
{
    private static readonly Color Body = Color.FromRgb(0x0B, 0x63, 0xD6);
    private static readonly Color BodyDark = Color.FromRgb(0x08, 0x4C, 0xA8);
    private static readonly Color Graphite = Color.FromRgb(0x3A, 0x40, 0x47);
    private static readonly Color Steel = Color.FromRgb(0x7A, 0x82, 0x8A);
    private static readonly Color LightSteel = Color.FromRgb(0xB7, 0xBD, 0xC3);
    private static readonly Color Rubber = Color.FromRgb(0x1D, 0x20, 0x23);
    private static readonly Color Red = Color.FromRgb(0xD3, 0x2F, 0x2F);
    private static readonly Color Glass = Color.FromRgb(0x2A, 0x3A, 0x4A);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xB3, 0x2E);
    private static readonly Color Cyan = Color.FromRgb(0x4F, 0xC3, 0xF7);

    public static Model3DGroup Build(string key) => key switch
    {
        "amr-heavy" => AmrHeavy(),
        "agv-pallet" => AgvPallet(),
        "agv-tugger" => AgvTugger(),
        _ => AmrCompact(),
    };

    // ───────────────────────────── models ─────────────────────────────

    private static Model3DGroup AmrCompact()
    {
        var m = new Model3DGroup();
        // chassis + shell
        Box(m, Graphite, 0, 0.10, 0, 0.80, 0.14, 0.55);
        Box(m, Body, 0, 0.215, 0, 0.78, 0.09, 0.53);
        Box(m, BodyDark, 0, 0.215, 0.266, 0.66, 0.05, 0.006);
        Box(m, BodyDark, 0, 0.215, -0.266, 0.66, 0.05, 0.006);
        Box(m, LightSteel, 0, 0.268, 0, 0.70, 0.016, 0.46);
        Box(m, Steel, 0, 0.283, 0, 0.60, 0.014, 0.38);
        // bumpers + light strips
        Box(m, Rubber, 0.405, 0.08, 0, 0.02, 0.07, 0.5);
        Box(m, Rubber, -0.405, 0.08, 0, 0.02, 0.07, 0.5);
        Box(m, Cyan, 0.401, 0.155, 0, 0.012, 0.012, 0.44, emissive: true);
        Box(m, Cyan, -0.401, 0.155, 0, 0.012, 0.012, 0.44, emissive: true);
        // scanners
        Cylinder(m, Rubber, new Point3D(0.33, 0.15, 0.21), 0.045, 0.07);
        Cylinder(m, Rubber, new Point3D(-0.33, 0.15, -0.21), 0.045, 0.07);
        Box(m, Glass, 0.37, 0.18, 0.21, 0.02, 0.03, 0.06);
        // e-stop + HMI
        Cylinder(m, Red, new Point3D(-0.30, 0.26, 0.2), 0.025, 0.04);
        Box(m, Glass, -0.36, 0.22, 0, 0.012, 0.05, 0.14);
        Wheels(m, 0.28, 0.24, 0.05, 0.035);
        return m;
    }

    private static Model3DGroup AmrHeavy()
    {
        var m = new Model3DGroup();
        Box(m, Graphite, 0, 0.12, 0, 1.20, 0.18, 0.85);
        Box(m, Body, 0, 0.255, 0, 1.18, 0.09, 0.83);
        Box(m, Graphite, 0, 0.31, 0, 1.0, 0.02, 0.68);
        Box(m, Steel, 0, 0.335, 0, 0.95, 0.03, 0.64);
        Box(m, LightSteel, 0, 0.356, 0, 0.90, 0.012, 0.58);
        for (int i = -1; i <= 1; i += 2)
        {
            Box(m, Rubber, 0.605 * i, 0.09, 0, 0.02, 0.09, 0.8);
            Box(m, Cyan, 0.601 * i, 0.19, 0, 0.012, 0.012, 0.7, emissive: true);
        }
        Cylinder(m, Rubber, new Point3D(0.54, 0.17, 0.36), 0.05, 0.08);
        Cylinder(m, Rubber, new Point3D(-0.54, 0.17, -0.36), 0.05, 0.08);
        Cylinder(m, Red, new Point3D(0.5, 0.30, -0.36), 0.03, 0.04);
        Box(m, Glass, 0.58, 0.25, -0.2, 0.012, 0.06, 0.18);
        Wheels(m, 0.42, 0.38, 0.065, 0.045);
        return m;
    }

    private static Model3DGroup AgvPallet()
    {
        var m = new Model3DGroup();
        // drive body (rear), robot frame: forks point to +X
        Box(m, Graphite, -0.15, 0.24, 0, 0.60, 0.42, 0.88);
        Box(m, Body, -0.17, 0.62, 0, 0.56, 0.34, 0.86);
        Box(m, BodyDark, -0.17, 0.80, 0, 0.50, 0.02, 0.80);
        Box(m, Glass, -0.44, 0.60, 0.15, 0.012, 0.12, 0.22);
        Cylinder(m, Red, new Point3D(-0.36, 0.80, -0.3), 0.03, 0.04);
        // mast
        for (int s = -1; s <= 1; s += 2)
        {
            Box(m, Steel, 0.16, 1.15, 0.30 * s, 0.07, 2.2, 0.07);
            Box(m, LightSteel, 0.19, 1.15, 0.22 * s, 0.04, 2.2, 0.05);
        }
        Box(m, Steel, 0.16, 2.24, 0, 0.08, 0.06, 0.68);
        Box(m, Steel, 0.16, 1.40, 0, 0.06, 0.05, 0.62);
        // carriage + display
        Box(m, Body, 0.24, 0.92, 0, 0.10, 0.30, 0.66);
        Box(m, Glass, 0.295, 0.94, 0, 0.012, 0.10, 0.22);
        Box(m, Graphite, 0.23, 0.42, 0, 0.08, 0.40, 0.70);
        // forks
        for (int s = -1; s <= 1; s += 2)
        {
            Box(m, BodyDark, 0.78, 0.045, 0.24 * s, 1.0, 0.06, 0.16);
            Box(m, Rubber, 1.22, 0.03, 0.24 * s, 0.06, 0.04, 0.12);
        }
        // scanners + beacon
        Cylinder(m, Rubber, new Point3D(0.16, 2.31, 0), 0.05, 0.08);
        Cylinder(m, Amber, new Point3D(-0.36, 0.86, 0.32), 0.035, 0.06, emissive: true);
        Cylinder(m, Rubber, new Point3D(-0.42, 0.12, -0.33), 0.045, 0.06);
        Wheels(m, 0.0, 0.34, 0.07, 0.05, single: true);
        return m;
    }

    private static Model3DGroup AgvTugger()
    {
        var m = new Model3DGroup();
        Box(m, Graphite, 0, 0.13, 0, 1.0, 0.18, 0.60);
        Box(m, Body, 0.05, 0.30, 0, 0.90, 0.18, 0.58);
        Box(m, BodyDark, 0.30, 0.40, 0, 0.36, 0.02, 0.5);
        Box(m, Graphite, -0.22, 0.48, 0, 0.36, 0.18, 0.52);
        Box(m, Glass, -0.05, 0.50, 0, 0.012, 0.10, 0.30);
        // tow hitch
        Box(m, Steel, -0.53, 0.14, 0, 0.08, 0.04, 0.12);
        Cylinder(m, LightSteel, new Point3D(-0.56, 0.12, 0), 0.02, 0.08);
        // sensor mast
        Box(m, Steel, 0.42, 0.50, 0, 0.04, 0.22, 0.04);
        Cylinder(m, Rubber, new Point3D(0.42, 0.62, 0), 0.045, 0.07);
        Cylinder(m, Amber, new Point3D(-0.32, 0.60, 0.2), 0.03, 0.05, emissive: true);
        Cylinder(m, Red, new Point3D(-0.32, 0.58, -0.2), 0.026, 0.04);
        Box(m, Rubber, 0.505, 0.11, 0, 0.02, 0.09, 0.56);
        Box(m, Cyan, 0.501, 0.21, 0, 0.012, 0.012, 0.48, emissive: true);
        Wheels(m, 0.34, 0.26, 0.07, 0.05);
        return m;
    }

    // ───────────────────────────── primitives ─────────────────────────────

    private static void Wheels(Model3DGroup m, double x, double z, double r, double w, bool single = false)
    {
        var xs = single ? new[] { -0.32, 0.9 } : new[] { -x, x };
        foreach (var px in xs)
        foreach (var pz in new[] { -z, z })
        {
            Cylinder(m, Rubber, new Point3D(px, r, pz - w / 2 * Math.Sign(pz)), r, w, axisZ: true);
            Cylinder(m, Steel, new Point3D(px, r, pz + (w / 2 + 0.001) * Math.Sign(pz)), r * 0.55, 0.004, axisZ: true);
        }
    }

    private static Material Mat(Color c, bool emissive = false)
    {
        var g = new MaterialGroup();
        g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
        if (emissive) g.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0xC0, c.R, c.G, c.B))));
        else g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 28));
        return g;
    }

    private static void Box(Model3DGroup m, Color c, double cx, double cy, double cz, double sx, double sy, double sz, bool emissive = false)
    {
        double x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
        var mesh = new MeshGeometry3D();
        void Face(Point3D a, Point3D b, Point3D cc, Point3D d)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(cc); mesh.Positions.Add(d);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
        }
        Face(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1)); // +z
        Face(new(x1, y0, z0), new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0)); // -z
        Face(new(x1, y0, z1), new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1)); // +x
        Face(new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), new(x0, y1, z0)); // -x
        Face(new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), new(x0, y1, z0)); // +y
        Face(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1)); // -y
        mesh.Freeze();
        m.Children.Add(new GeometryModel3D(mesh, Mat(c, emissive)));
    }

    /// <summary>Cylinder standing on <paramref name="b"/> (vertical), or centred on it along Z when <paramref name="axisZ"/>.</summary>
    private static void Cylinder(Model3DGroup m, Color c, Point3D b, double r, double h, bool axisZ = false, bool emissive = false, int seg = 28)
    {
        var mesh = new MeshGeometry3D();
        Point3D P(double ang, double t) => axisZ
            ? new Point3D(b.X + r * Math.Cos(ang), b.Y + r * Math.Sin(ang), b.Z - h / 2 + h * t)
            : new Point3D(b.X + r * Math.Cos(ang), b.Y + h * t, b.Z + r * Math.Sin(ang));
        var c0 = axisZ ? new Point3D(b.X, b.Y, b.Z - h / 2) : b;
        var c1 = axisZ ? new Point3D(b.X, b.Y, b.Z + h / 2) : new Point3D(b.X, b.Y + h, b.Z);
        for (int i = 0; i < seg; i++)
        {
            double a0 = 2 * Math.PI * i / seg, a1 = 2 * Math.PI * (i + 1) / seg;
            int k = mesh.Positions.Count;
            mesh.Positions.Add(P(a0, 0)); mesh.Positions.Add(P(a1, 0)); mesh.Positions.Add(P(a1, 1)); mesh.Positions.Add(P(a0, 1));
            mesh.Positions.Add(c0); mesh.Positions.Add(c1);
            // side (both windings so orientation never hides it)
            mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 2); mesh.TriangleIndices.Add(k + 1);
            mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 3); mesh.TriangleIndices.Add(k + 2);
            mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 1); mesh.TriangleIndices.Add(k + 2);
            mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 2); mesh.TriangleIndices.Add(k + 3);
            // caps
            mesh.TriangleIndices.Add(k + 4); mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 1);
            mesh.TriangleIndices.Add(k + 4); mesh.TriangleIndices.Add(k + 1); mesh.TriangleIndices.Add(k);
            mesh.TriangleIndices.Add(k + 5); mesh.TriangleIndices.Add(k + 3); mesh.TriangleIndices.Add(k + 2);
            mesh.TriangleIndices.Add(k + 5); mesh.TriangleIndices.Add(k + 2); mesh.TriangleIndices.Add(k + 3);
        }
        mesh.Freeze();
        m.Children.Add(new GeometryModel3D(mesh, Mat(c, emissive)));
    }

    /// <summary>Soft contact shadow under the model.</summary>
    public static GeometryModel3D Shadow(Rect3D bounds)
    {
        double cx = bounds.X + bounds.SizeX / 2, cz = bounds.Z + bounds.SizeZ / 2;
        double rx = bounds.SizeX * 0.62, rz = bounds.SizeZ * 0.70;
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(cx - rx, 0.001, cz - rz));
        mesh.Positions.Add(new Point3D(cx + rx, 0.001, cz - rz));
        mesh.Positions.Add(new Point3D(cx + rx, 0.001, cz + rz));
        mesh.Positions.Add(new Point3D(cx - rx, 0.001, cz + rz));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
        mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
        mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
        mesh.TriangleIndices = new Int32Collection([0, 2, 1, 0, 3, 2]);
        var brush = new RadialGradientBrush(Color.FromArgb(0x70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
        return new GeometryModel3D(mesh, new DiffuseMaterial(brush));
    }
}
