namespace Vantage.Core.Geometry;

/// <summary>
/// Rectangular robot footprint F (fixed per robot, Batasan Masalah 3).
/// Front/Rear are distances from the reference point along +x/-x of the
/// robot frame, Left/Right along +y/-y.
/// </summary>
public sealed record Footprint(double Front, double Rear, double Left, double Right)
{
    public static Footprint Centered(double length, double width) =>
        new(length / 2, length / 2, width / 2, width / 2);

    public double Length => Front + Rear;
    public double Width => Left + Right;

    /// <summary>
    /// Footprint placed at <paramref name="pose"/>, grown by margin ε on every side.
    /// Growing the rectangle is a superset of the Minkowski sum F ⊕ B(ε), so the
    /// approximation can only add false positives, never false negatives.
    /// </summary>
    public ConvexPolygon At(Pose2D pose, double margin = 0)
    {
        var c = pose.Position;
        double f = Front + margin, r = Rear + margin, l = Left + margin, rt = Right + margin;
        var corners = new[]
        {
            new Vec2(-r, -rt), new Vec2(f, -rt), new Vec2(f, l), new Vec2(-r, l)
        };
        return new ConvexPolygon(corners.Select(v => c + v.Rotate(pose.Theta)).ToArray());
    }

    public IEnumerable<Vec2> CornersAt(Pose2D pose, double margin = 0) => At(pose, margin).Vertices;
}
