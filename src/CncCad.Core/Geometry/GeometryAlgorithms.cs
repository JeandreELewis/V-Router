using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public static class GeometryAlgorithms
{
    public static ArcEntity CreateThreePointArc(Point2D start, Point2D pointOnArc, Point2D end, Guid layerId = default)
    {
        if(!Finite(start)||!Finite(pointOnArc)||!Finite(end))throw new ArgumentException("The three arc points must contain finite coordinates.");
        var middle=pointOnArc-start;var last=end-start;var middleLengthSquared=middle.X*middle.X+middle.Y*middle.Y;var lastLengthSquared=last.X*last.X+last.Y*last.Y;var endSpan=end-pointOnArc;
        var minimumLength=GeometryTolerancePolicy.DefaultMinimumLengthMm;if(middleLengthSquared<=minimumLength*minimumLength||lastLengthSquared<=minimumLength*minimumLength||endSpan.Length<=minimumLength)throw new ArgumentException("The three arc points must be distinct.");
        var cross=middle.X*last.Y-middle.Y*last.X;var scale=Math.Max(middleLengthSquared,lastLengthSquared);
        if (Math.Abs(cross) <= 1e-10*Math.Max(1,scale)) throw new ArgumentException("The three arc points cannot be collinear.");
        var determinant=2*cross;
        var center = start+new Vector2D((last.Y*middleLengthSquared-middle.Y*lastLengthSquared)/determinant,(middle.X*lastLengthSquared-last.X*middleLengthSquared)/determinant);
        if(!Finite(center))throw new ArgumentException("The three arc points do not define a finite circle.");
        var startAngle = Angle(center, start);
        var middleAngle = Angle(center, pointOnArc);
        var endAngle = Angle(center, end);
        var ccwSweep = Normalize(endAngle - startAngle);
        var middleCcw = Normalize(middleAngle - startAngle);
        var sweep = middleCcw <= ccwSweep ? ccwSweep : ccwSweep - 360;
        var radius=center.DistanceTo(start);if(!double.IsFinite(radius)||radius<=GeometryTolerancePolicy.DefaultMinimumLengthMm)throw new ArgumentException("The three arc points do not define a usable radius.");
        return new ArcEntity { LayerId = layerId, Center = center, Radius = radius, StartAngleDegrees = startAngle, SweepAngleDegrees = sweep };
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
    private static bool Finite(Point2D point)=>double.IsFinite(point.X)&&double.IsFinite(point.Y);
}
