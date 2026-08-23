namespace CncCad.Core.Geometry;

public readonly record struct Point2D(double X, double Y)
{
    public static Vector2D operator -(Point2D a, Point2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2D operator +(Point2D p, Vector2D v) => new(p.X + v.X, p.Y + v.Y);
    public double DistanceTo(Point2D other) => Math.Sqrt(Math.Pow(X - other.X, 2) + Math.Pow(Y - other.Y, 2));
}

public readonly record struct Vector2D(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
}

public readonly record struct BoundingBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public bool Contains(Point2D p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    public static BoundingBox FromPoints(IEnumerable<Point2D> points)
    {
        var values = points.ToArray();
        if (values.Length == 0) return new(0, 0, 0, 0);
        return new(values.Min(p => p.X), values.Min(p => p.Y), values.Max(p => p.X), values.Max(p => p.Y));
    }
}

public readonly record struct Matrix2D(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static Matrix2D Identity => new(1, 0, 0, 1, 0, 0);
    public static Matrix2D Translation(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Matrix2D Scale(double x, double y) => new(x, 0, 0, y, 0, 0);
    public static Matrix2D Rotation(double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
    }
    public Point2D Transform(Point2D p) => new(p.X * M11 + p.Y * M21 + OffsetX, p.X * M12 + p.Y * M22 + OffsetY);
    public static Matrix2D Then(Matrix2D first, Matrix2D second) => new(
        first.M11 * second.M11 + first.M12 * second.M21,
        first.M11 * second.M12 + first.M12 * second.M22,
        first.M21 * second.M11 + first.M22 * second.M21,
        first.M21 * second.M12 + first.M22 * second.M22,
        first.OffsetX * second.M11 + first.OffsetY * second.M21 + second.OffsetX,
        first.OffsetX * second.M12 + first.OffsetY * second.M22 + second.OffsetY);
    public static Matrix2D Around(Point2D pivot, Matrix2D transform) => Then(Then(Translation(-pivot.X, -pivot.Y), transform), Translation(pivot.X, pivot.Y));
    public Matrix2D Inverse()
    {
        var determinant = M11 * M22 - M12 * M21;
        if (Math.Abs(determinant) < 1e-12) throw new InvalidOperationException("Transform is not invertible.");
        var inverse = new Matrix2D(M22 / determinant, -M12 / determinant, -M21 / determinant, M11 / determinant, 0, 0);
        return inverse with { OffsetX = -(OffsetX * inverse.M11 + OffsetY * inverse.M21), OffsetY = -(OffsetX * inverse.M12 + OffsetY * inverse.M22) };
    }
}

public readonly record struct LineSegment(Point2D Start, Point2D End) { public double Length => Start.DistanceTo(End); }
public readonly record struct ArcSegment(Point2D Center, double Radius, double StartAngleDegrees, double SweepAngleDegrees);
