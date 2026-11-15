namespace Vantage.Core.Geometry;

/// <summary>
/// Discrete swept area S = sweep(F, E) ⊕ B(ε) (Eq. II.2), stored as a union of
/// convex hulls of the footprint at consecutive sample poses. Between two
/// samples the hull covers pure translation exactly; heading change between
/// samples is bounded by the sampling step.
/// </summary>
public sealed class SweptArea
{
    public IReadOnlyList<ConvexPolygon> Cells { get; }
    public Aabb Bounds { get; }

    private SweptArea(IReadOnlyList<ConvexPolygon> cells)
    {
        Cells = cells;
        Bounds = cells.Aggregate(Aabb.Empty, (b, c) => b.Union(c.Bounds));
    }

    public static SweptArea Build(IReadOnlyList<Pose2D> polyline, Footprint footprint, double margin, double sampleStep)
    {
        var samples = Polyline.Sample(polyline, sampleStep);
        var cells = new List<ConvexPolygon>(Math.Max(1, samples.Count - 1));
        if (samples.Count == 1)
        {
            cells.Add(footprint.At(samples[0], margin));
        }
        else
        {
            for (int i = 0; i < samples.Count - 1; i++)
            {
                var pts = footprint.CornersAt(samples[i], margin).Concat(footprint.CornersAt(samples[i + 1], margin));
                cells.Add(ConvexPolygon.Hull(pts));
            }
        }
        return new SweptArea(cells);
    }

    public bool Intersects(SweptArea other)
    {
        if (!Bounds.Intersects(other.Bounds)) return false;
        foreach (var a in Cells)
        {
            if (!a.Bounds.Intersects(other.Bounds)) continue;
            foreach (var b in other.Cells)
                if (a.Intersects(b)) return true;
        }
        return false;
    }

    public bool Intersects(ConvexPolygon poly)
    {
        if (!Bounds.Intersects(poly.Bounds)) return false;
        foreach (var c in Cells)
            if (c.Intersects(poly)) return true;
        return false;
    }
}

public static class Polyline
{
    public static double Length(IReadOnlyList<Pose2D> poses)
    {
        double s = 0;
        for (int i = 1; i < poses.Count; i++) s += Vec2.Distance(poses[i - 1].Position, poses[i].Position);
        return s;
    }

    /// <summary>Resamples at a fixed arc-length step; always keeps both ends and original vertices.</summary>
    public static List<Pose2D> Sample(IReadOnlyList<Pose2D> poses, double step)
    {
        var result = new List<Pose2D>();
        if (poses.Count == 0) return result;
        result.Add(poses[0]);
        for (int i = 1; i < poses.Count; i++)
        {
            var a = poses[i - 1];
            var b = poses[i];
            double len = Vec2.Distance(a.Position, b.Position);
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int k = 1; k <= n; k++) result.Add(Pose2D.Lerp(a, b, (double)k / n));
        }
        return result;
    }

    /// <summary>Pose at arc length s along the polyline (clamped).</summary>
    public static Pose2D At(IReadOnlyList<Pose2D> poses, double s)
    {
        if (poses.Count == 1 || s <= 0) return poses[0];
        double acc = 0;
        for (int i = 1; i < poses.Count; i++)
        {
            double len = Vec2.Distance(poses[i - 1].Position, poses[i].Position);
            if (acc + len >= s && len > 0) return Pose2D.Lerp(poses[i - 1], poses[i], (s - acc) / len);
            acc += len;
        }
        return poses[^1];
    }

    /// <summary>Heading of each pose taken from the direction of travel where the source gave none.</summary>
    public static List<Pose2D> WithTangentHeadings(IReadOnlyList<Vec2> points)
    {
        var poses = new List<Pose2D>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            var d = i < points.Count - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1];
            poses.Add(new Pose2D(points[i].X, points[i].Y, Math.Atan2(d.Y, d.X)));
        }
        return poses;
    }
}
