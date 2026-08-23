using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public static class GeometryAlgorithms
{
    public static ArcEntity CreateThreePointArc(Point2D start, Point2D pointOnArc, Point2D end, Guid layerId = default)
    {
        var determinant = 2 * (start.X * (pointOnArc.Y - end.Y) + pointOnArc.X * (end.Y - start.Y) + end.X * (start.Y - pointOnArc.Y));
        if (Math.Abs(determinant) < 1e-9) throw new ArgumentException("The three arc points cannot be collinear.");
        var startSquared = start.X * start.X + start.Y * start.Y;
        var middleSquared = pointOnArc.X * pointOnArc.X + pointOnArc.Y * pointOnArc.Y;
        var endSquared = end.X * end.X + end.Y * end.Y;
        var center = new Point2D(
            (startSquared * (pointOnArc.Y - end.Y) + middleSquared * (end.Y - start.Y) + endSquared * (start.Y - pointOnArc.Y)) / determinant,
            (startSquared * (end.X - pointOnArc.X) + middleSquared * (start.X - end.X) + endSquared * (pointOnArc.X - start.X)) / determinant);
        var startAngle = Angle(center, start);
        var middleAngle = Angle(center, pointOnArc);
        var endAngle = Angle(center, end);
        var ccwSweep = Normalize(endAngle - startAngle);
        var middleCcw = Normalize(middleAngle - startAngle);
        var sweep = middleCcw <= ccwSweep ? ccwSweep : ccwSweep - 360;
        return new ArcEntity { LayerId = layerId, Center = center, Radius = center.DistanceTo(start), StartAngleDegrees = startAngle, SweepAngleDegrees = sweep };
    }

    public static Point2D PointOnArc(ArcEntity arc, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return arc.Transform.Transform(new(arc.Center.X + arc.Radius * Math.Cos(radians), arc.Center.Y + arc.Radius * Math.Sin(radians)));
    }

    public static BoundingBox GetArcBounds(ArcEntity arc)
    {
        var angles = new List<double> { arc.StartAngleDegrees, arc.StartAngleDegrees + arc.SweepAngleDegrees };
        foreach (var angle in new[] { 0d, 90d, 180d, 270d }) if (IsAngleOnSweep(angle, arc.StartAngleDegrees, arc.SweepAngleDegrees)) angles.Add(angle);
        return BoundingBox.FromPoints(angles.Select(angle => PointOnArc(arc, angle)));
    }

    public static bool IsAngleOnSweep(double angle, double start, double sweep) => sweep >= 0
        ? Normalize(angle - start) <= sweep + 1e-9
        : Normalize(start - angle) <= -sweep + 1e-9;

    private static double Angle(Point2D center, Point2D point) => Normalize(Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI);
    private static double Normalize(double angle) { angle %= 360; return angle < 0 ? angle + 360 : angle; }
}
