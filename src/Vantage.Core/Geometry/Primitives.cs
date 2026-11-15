namespace Vantage.Core.Geometry;

/// <summary>2D vector in map frame, metres.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);

    public double Dot(Vec2 o) => X * o.X + Y * o.Y;
    public double Cross(Vec2 o) => X * o.Y - Y * o.X;
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec2 Perp => new(-Y, X);

    public Vec2 Rotate(double theta)
    {
        double c = Math.Cos(theta), s = Math.Sin(theta);
        return new Vec2(c * X - s * Y, s * X + c * Y);
    }

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public override string ToString() => $"({X:F3}, {Y:F3})";
}

/// <summary>Pose q = (x, y, θ) in the shared map frame (Eq. II.1).</summary>
public readonly record struct Pose2D(double X, double Y, double Theta)
{
    public Vec2 Position => new(X, Y);

    public static Pose2D Lerp(Pose2D a, Pose2D b, double t)
    {
        double dTheta = Angle.Normalize(b.Theta - a.Theta);
        return new Pose2D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Angle.Normalize(a.Theta + dTheta * t));
    }

    public override string ToString() => $"({X:F3}, {Y:F3}, {Angle.ToDeg(Theta):F1}°)";
}

public static class Angle
{
    public static double Normalize(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a <= -Math.PI) a += 2 * Math.PI;
        return a;
    }

    public static double ToDeg(double rad) => rad * 180.0 / Math.PI;
    public static double ToRad(double deg) => deg * Math.PI / 180.0;
}

/// <summary>Axis-aligned bounding box, used as broad phase.</summary>
public readonly record struct Aabb(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly Aabb Empty = new(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);

    public bool IsEmpty => MinX > MaxX;
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;

    public bool Intersects(Aabb o) =>
        !IsEmpty && !o.IsEmpty && MinX <= o.MaxX && o.MinX <= MaxX && MinY <= o.MaxY && o.MinY <= MaxY;

    public Aabb Union(Aabb o) => IsEmpty ? o : o.IsEmpty ? this :
        new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    public Aabb Include(Vec2 p) => IsEmpty ? new(p.X, p.Y, p.X, p.Y) :
        new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Aabb Inflate(double d) => IsEmpty ? this : new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public static Aabb Of(IEnumerable<Vec2> points)
    {
        var box = Empty;
        foreach (var p in points) box = box.Include(p);
        return box;
    }
}
