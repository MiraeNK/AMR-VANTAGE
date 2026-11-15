namespace Vantage.Core.Geometry;

/// <summary>
/// Convex polygon with counter-clockwise vertices. Intersection uses the
/// separating axis theorem; touching edges count as intersecting so the
/// detector errs on the conservative side.
/// </summary>
public sealed class ConvexPolygon
{
    private const double Eps = 1e-9;

    public IReadOnlyList<Vec2> Vertices { get; }
    public Aabb Bounds { get; }

    public ConvexPolygon(IReadOnlyList<Vec2> vertices)
    {
        if (vertices.Count < 3) throw new ArgumentException("Polygon needs at least 3 vertices.", nameof(vertices));
        Vertices = vertices;
        Bounds = Aabb.Of(vertices);
    }

    public bool Intersects(ConvexPolygon other)
    {
        if (!Bounds.Intersects(other.Bounds)) return false;
        return !HasSeparatingAxis(this, other) && !HasSeparatingAxis(other, this);
    }

    public bool Contains(Vec2 p)
    {
        int n = Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % n];
            if ((b - a).Cross(p - a) < -Eps) return false;
        }
        return true;
    }

    public double Area()
    {
        double s = 0;
        int n = Vertices.Count;
        for (int i = 0; i < n; i++) s += Vertices[i].Cross(Vertices[(i + 1) % n]);
        return Math.Abs(s) * 0.5;
    }

    private static bool HasSeparatingAxis(ConvexPolygon a, ConvexPolygon b)
    {
        int n = a.Vertices.Count;
        for (int i = 0; i < n; i++)
        {
            var edge = a.Vertices[(i + 1) % n] - a.Vertices[i];
            var axis = edge.Perp;
            if (axis.Length < Eps) continue;
            Project(a, axis, out double minA, out double maxA);
            Project(b, axis, out double minB, out double maxB);
            if (maxA < minB - Eps || maxB < minA - Eps) return true;
        }
        return false;
    }

    private static void Project(ConvexPolygon p, Vec2 axis, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        foreach (var v in p.Vertices)
        {
            double d = v.Dot(axis);
            if (d < min) min = d;
            if (d > max) max = d;
        }
    }

    /// <summary>Andrew's monotone chain. Returns CCW hull.</summary>
    public static ConvexPolygon Hull(IEnumerable<Vec2> points)
    {
        var pts = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (pts.Count < 3) throw new ArgumentException("Hull needs at least 3 distinct points.");

        var hull = new Vec2[pts.Count * 2];
        int k = 0;
        foreach (var p in pts)
        {
            while (k >= 2 && (hull[k - 1] - hull[k - 2]).Cross(p - hull[k - 2]) <= 0) k--;
            hull[k++] = p;
        }
        for (int i = pts.Count - 2, t = k + 1; i >= 0; i--)
        {
            var p = pts[i];
            while (k >= t && (hull[k - 1] - hull[k - 2]).Cross(p - hull[k - 2]) <= 0) k--;
            hull[k++] = p;
        }
        return new ConvexPolygon(hull.Take(k - 1).ToArray());
    }
}
