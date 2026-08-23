using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Core.Selection;

public sealed class SelectionModel
{
    private readonly HashSet<Guid> _ids = [];
    public IReadOnlySet<Guid> Ids => _ids;
    public int Count => _ids.Count;
    public Guid? PrimaryId { get; private set; }
    public bool Contains(Guid id) => _ids.Contains(id);
    public void Clear() { _ids.Clear();PrimaryId=null; }
    public void Set(Guid id) { _ids.Clear();_ids.Add(id);PrimaryId=id; }
    public void Add(Guid id) { _ids.Add(id);PrimaryId=id; }
    public void Toggle(Guid id) { if(!_ids.Remove(id)){_ids.Add(id);PrimaryId=id;}else if(PrimaryId==id)PrimaryId=_ids.FirstOrDefault() is var next&&next!=Guid.Empty?next:null; }
    public void Set(IEnumerable<Guid> ids) { _ids.Clear();_ids.UnionWith(ids);PrimaryId=_ids.FirstOrDefault() is var next&&next!=Guid.Empty?next:null; }
}

public static class EntityHitTester
{
    public static CadEntity? HitTest(IEnumerable<CadEntity> entities, Point2D point, double toleranceMm) =>
        entities.Where(e => e.IsVisible && !e.IsLocked).Reverse().FirstOrDefault(e => IsHit(e, point, toleranceMm));
    public static IReadOnlyList<CadEntity> HitTestAll(IEnumerable<CadEntity> entities,Point2D point,double toleranceMm)=>entities.Where(e=>e.IsVisible&&!e.IsLocked&&IsHit(e,point,toleranceMm)).Reverse().ToArray();

    public static IEnumerable<Guid> Inside(IEnumerable<CadEntity> entities, BoundingBox area) =>
        entities.Where(e => e.IsVisible && !e.IsLocked).Where(e => Contains(area, e.GetBounds())).Select(e => e.Id);

    private static bool IsHit(CadEntity entity, Point2D point, double tolerance)
    {
        var points = EntityGeometry.WorldPoints(entity);
        if (points.Count < 2) return Expanded(entity.GetBounds(), tolerance).Contains(point);
        if (points.Zip(points.Skip(1)).Any(pair => DistanceToSegment(point, pair.First, pair.Second) <= tolerance)) return true;
        return entity is RectangleEntity or CircleEntity or EllipseEntity or PolylineEntity { IsClosed: true }
            && DistanceToSegment(point, points[^1], points[0]) <= tolerance;
    }

    private static bool NearRectangle(BoundingBox box, Point2D p, double tolerance) =>
        Expanded(box, tolerance).Contains(p) && (!new BoundingBox(box.MinX + tolerance, box.MinY + tolerance, box.MaxX - tolerance, box.MaxY - tolerance).Contains(p));
    private static BoundingBox Expanded(BoundingBox b, double amount) => new(b.MinX - amount, b.MinY - amount, b.MaxX + amount, b.MaxY + amount);
    private static bool Contains(BoundingBox outer, BoundingBox inner) => inner.MinX >= outer.MinX && inner.MinY >= outer.MinY && inner.MaxX <= outer.MaxX && inner.MaxY <= outer.MaxY;
    private static double DistanceToSegment(Point2D p, Point2D a, Point2D b)
    {
        var ab = b - a; var lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared == 0) return p.DistanceTo(a);
        var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared, 0, 1);
        return p.DistanceTo(new(a.X + t * ab.X, a.Y + t * ab.Y));
    }
    private static bool ArcHit(ArcEntity arc, Point2D point, double tolerance)
    {
        if (Math.Abs(arc.Center.DistanceTo(point) - arc.Radius) > tolerance) return false;
        var angle = Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X) * 180 / Math.PI;
        return GeometryAlgorithms.IsAngleOnSweep(angle, arc.StartAngleDegrees, arc.SweepAngleDegrees);
    }
}
