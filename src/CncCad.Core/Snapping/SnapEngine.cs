using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Core.Snapping;

public enum SnapType { None, Endpoint, Center, Midpoint, Grid }
public sealed class SnapSettings
{
    public bool Grid { get; set; } = true;
    public bool Endpoint { get; set; } = true;
    public bool Midpoint { get; set; } = true;
    public bool Center { get; set; } = true;
    public double GridSpacingMm { get; set; } = 10;
}
public readonly record struct SnapResult(Point2D Point, SnapType Type, double Distance)
{
    public static SnapResult None(Point2D point) => new(point, SnapType.None, 0);
}

public sealed class SnapEngine
{
    public SnapResult Snap(Point2D cursor, IEnumerable<CadEntity> entities, SnapSettings settings, double toleranceMm)
    {
        var candidates = new List<SnapResult>();
        foreach (var entity in entities.Where(e => e.IsVisible))
        {
            if (settings.Endpoint) Add(candidates, cursor, EntityGeometry.Endpoints(entity), SnapType.Endpoint, toleranceMm);
            if (settings.Center) Add(candidates, cursor, EntityGeometry.Centers(entity), SnapType.Center, toleranceMm);
            if (settings.Midpoint) Add(candidates, cursor, EntityGeometry.Midpoints(entity), SnapType.Midpoint, toleranceMm);
        }
        if (settings.Grid && settings.GridSpacingMm > 0)
        {
            var grid = new Point2D(Math.Round(cursor.X / settings.GridSpacingMm) * settings.GridSpacingMm, Math.Round(cursor.Y / settings.GridSpacingMm) * settings.GridSpacingMm);
            if (cursor.DistanceTo(grid) <= toleranceMm) candidates.Add(new(grid, SnapType.Grid, cursor.DistanceTo(grid)));
        }
        return candidates.OrderBy(c => Priority(c.Type)).ThenBy(c => c.Distance).FirstOrDefault(SnapResult.None(cursor));
    }
    private static void Add(List<SnapResult> results, Point2D cursor, IEnumerable<Point2D> points, SnapType type, double tolerance)
    {
        foreach (var point in points) { var distance = cursor.DistanceTo(point); if (distance <= tolerance) results.Add(new(point, type, distance)); }
    }
    private static int Priority(SnapType type) => type switch { SnapType.Endpoint => 0, SnapType.Center => 1, SnapType.Midpoint => 2, SnapType.Grid => 3, _ => 4 };
}
