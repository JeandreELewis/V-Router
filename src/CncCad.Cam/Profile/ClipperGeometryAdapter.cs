using Clipper2Lib;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

internal static class ClipperGeometryAdapter
{
    internal const double CircleToleranceMm = 0.02;
    internal const int DecimalPrecision = 6;
    public static IReadOnlyList<Point2D> ToContour(CadEntity entity) => entity switch
    {
        RectangleEntity rectangle => Normalize(rectangle.Corners.Select(rectangle.Transform.Transform)),
        PolylineEntity { IsClosed: true, Points.Count: >= 3 } polyline => Normalize(polyline.Points.Select(polyline.Transform.Transform)),
        CircleEntity circle when circle.Radius > 0 => Normalize(FlattenCircle(circle, LocalTolerance(circle.Transform))),
        VectorPathEntity { Figures.Count: 1 } path when path.Figures[0].IsClosed => Normalize(VectorPathGeometry.Flatten(path.Figures,LocalTolerance(path.Transform)).Select(path.Transform.Transform)),
        VectorPathEntity=>throw new ArgumentException("Vector path must contain one closed figure for profiling."),
        PolylineEntity => throw new ArgumentException("Polyline must be closed and contain at least three points."),
        _ => throw new ArgumentException($"{entity.GetType().Name.Replace("Entity", string.Empty)} is not supported by profile operations.")
    };

    public static IReadOnlyList<IReadOnlyList<Point2D>> Offset(IReadOnlyList<Point2D> contour, double delta)
    {
        if (Math.Abs(delta) < 1e-9) return [contour];
        var paths = new PathsD { new(contour.Select(point => new PointD(point.X, point.Y))) };
        var result = Clipper.InflatePaths(paths, delta, JoinType.Miter, EndType.Polygon, 6, DecimalPrecision);
        return result.Select(path => (IReadOnlyList<Point2D>)path.Select(point => new Point2D(point.x, point.y)).ToArray()).Where(path => path.Count >= 3 && Math.Abs(SignedArea(path)) > 1e-8).ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<Point2D>> PocketOffsets(IEnumerable<IReadOnlyList<Point2D>> contours, double initialInset, double stepOver)
    {
        if (initialInset < 0 || stepOver <= 0) throw new ArgumentOutOfRangeException(nameof(stepOver));
        var source = new PathsD(contours.Select(contour => new PathD(contour.Select(point => new PointD(point.X, point.Y)))));
        if (source.Count == 0) return [];
        var region = Clipper.Union(source, [], FillRule.EvenOdd, DecimalPrecision);
        var result = new List<IReadOnlyList<Point2D>>();
        var inset = Math.Max(initialInset, 1e-6);
        for (var guard = 0; guard < 10000; guard++, inset += stepOver)
        {
            var level = Clipper.InflatePaths(region, -inset, JoinType.Miter, EndType.Polygon, 6, DecimalPrecision);
            if (level.Count == 0) break;
            foreach (var path in level)
            {
                var points = path.Select(point => new Point2D(point.x, point.y)).ToArray();
                if (points.Length >= 3 && Math.Abs(SignedArea(points)) > 1e-8) result.Add(points);
            }
        }
        return result;
    }
    public static IReadOnlyList<IReadOnlyList<Point2D>> InsetRegion(IEnumerable<IReadOnlyList<Point2D>> contours,double inset)
    {
        if(!double.IsFinite(inset)||inset<0)throw new ArgumentOutOfRangeException(nameof(inset));var source=new PathsD(contours.Select(contour=>new PathD(contour.Select(point=>new PointD(point.X,point.Y)))));if(source.Count==0)return[];var region=Clipper.Union(source,[],FillRule.EvenOdd,DecimalPrecision);var result=inset<=1e-9?region:Clipper.InflatePaths(region,-inset,JoinType.Miter,EndType.Polygon,6,DecimalPrecision);return result.Where(path=>path.Count>=3&&Math.Abs(Clipper.Area(path))>1e-8).Select(path=>(IReadOnlyList<Point2D>)path.Select(point=>new Point2D(point.x,point.y)).ToArray()).ToArray();
    }

    public static double SignedArea(IReadOnlyList<Point2D> path)
    {
        var area = 0d; for (var i = 0; i < path.Count; i++) { var next = path[(i + 1) % path.Count]; area += path[i].X * next.Y - next.X * path[i].Y; } return area / 2;
    }
    public static bool Contains(IReadOnlyList<Point2D> polygon, Point2D point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    public static bool Overlaps(IReadOnlyList<Point2D> a,IReadOnlyList<Point2D> b,double spacing)
    {
        var pa=new PathsD{new(a.Select(x=>new PointD(x.X,x.Y)))};if(spacing>0)pa=Clipper.InflatePaths(pa,spacing/2,JoinType.Miter,EndType.Polygon,6,DecimalPrecision);var pb=new PathsD{new(b.Select(x=>new PointD(x.X,x.Y)))};if(spacing>0)pb=Clipper.InflatePaths(pb,spacing/2,JoinType.Miter,EndType.Polygon,6,DecimalPrecision);return Clipper.Intersect(pa,pb,FillRule.NonZero,DecimalPrecision).Sum(x=>Math.Abs(Clipper.Area(x)))>1e-8;
    }
    private static IReadOnlyList<Point2D> Normalize(IEnumerable<Point2D> points)
    {
        var result = points.ToList();
        if (result.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y))) throw new ArgumentException("Contour contains a non-finite coordinate.");
        if (result.Count > 1 && result[0].DistanceTo(result[^1]) < 1e-9) result.RemoveAt(result.Count - 1);
        if (result.Count < 3 || Math.Abs(SignedArea(result)) < 1e-8) throw new ArgumentException("Contour is degenerate.");
        if (!IsSimple(result)) throw new ArgumentException("Contour intersects itself.");
        if (SignedArea(result) < 0) result.Reverse(); return result;
    }
    private static bool IsSimple(IReadOnlyList<Point2D> points)
    {
        for (var i = 0; i < points.Count; i++)
        for (var j = i + 1; j < points.Count; j++)
        {
            if (j == i || j == i + 1 || i == 0 && j == points.Count - 1) continue;
            if (SegmentsIntersect(points[i], points[(i + 1) % points.Count], points[j], points[(j + 1) % points.Count])) return false;
        }
        return true;
    }
    private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d)
    {
        static double Cross(Point2D p, Point2D q, Point2D r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        var abC = Cross(a, b, c); var abD = Cross(a, b, d); var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        return abC * abD < -1e-12 && cdA * cdB < -1e-12;
    }
    private static double LocalTolerance(Matrix2D transform)
    {
        var trace=transform.M11*transform.M11+transform.M12*transform.M12+transform.M21*transform.M21+transform.M22*transform.M22;
        var determinant=transform.M11*transform.M22-transform.M12*transform.M21;
        var discriminant=Math.Max(0,trace*trace-4*determinant*determinant);
        var maximumScale=Math.Sqrt(Math.Max(0,(trace+Math.Sqrt(discriminant))/2));
        if(!double.IsFinite(maximumScale)||maximumScale<=1e-12)return CircleToleranceMm;
        return CircleToleranceMm/maximumScale;
    }
    private static IEnumerable<Point2D> FlattenCircle(CircleEntity circle,double localTolerance)
    {
        var count = Math.Clamp((int)Math.Ceiling(Math.PI / Math.Acos(Math.Max(-1, 1 - localTolerance / circle.Radius))), 12, 16384);
        for (var i = 0; i < count; i++) { var angle = i * Math.PI * 2 / count; yield return circle.Transform.Transform(new(circle.Center.X + circle.Radius * Math.Cos(angle), circle.Center.Y + circle.Radius * Math.Sin(angle))); }
    }
}
