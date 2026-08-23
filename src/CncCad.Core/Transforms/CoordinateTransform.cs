using CncCad.Core.Documents;
using CncCad.Core.Geometry;

namespace CncCad.Core.Transforms;

public sealed class ViewportTransform
{
    public double Zoom { get; private set; } = 1;
    public Vector2D Pan { get; private set; } = new(40, 40);
    public Point2D DocumentToScreen(Point2D p, double viewportHeight) => new(Pan.X + p.X * Zoom, viewportHeight - Pan.Y - p.Y * Zoom);
    public Point2D ScreenToDocument(Point2D p, double viewportHeight) => new((p.X - Pan.X) / Zoom, (viewportHeight - p.Y - Pan.Y) / Zoom);
    public void SetZoom(double zoom, Point2D screenAnchor, double viewportHeight)
    {
        var before = ScreenToDocument(screenAnchor, viewportHeight);
        Zoom = Math.Clamp(zoom, 0.05, 100);
        var after = DocumentToScreen(before, viewportHeight);
        Pan = new(Pan.X + screenAnchor.X - after.X, Pan.Y - screenAnchor.Y + after.Y);
    }
    public void PanBy(Vector2D delta) => Pan = new(Pan.X + delta.X, Pan.Y - delta.Y);
    public void Fit(double widthMm, double heightMm, double viewportWidth, double viewportHeight, double padding = 40)
    {
        Zoom = Math.Max(0.05, Math.Min((viewportWidth - padding * 2) / widthMm, (viewportHeight - padding * 2) / heightMm));
        Pan = new((viewportWidth - widthMm * Zoom) / 2, (viewportHeight - heightMm * Zoom) / 2);
    }
    public void Fit(BoundingBox bounds, double viewportWidth, double viewportHeight, double padding = 60)
    {
        var width = Math.Max(bounds.Width, 1); var height = Math.Max(bounds.Height, 1);
        Zoom = Math.Max(0.05, Math.Min((viewportWidth - padding * 2) / width, (viewportHeight - padding * 2) / height));
        Pan = new((viewportWidth - width * Zoom) / 2 - bounds.MinX * Zoom, (viewportHeight - height * Zoom) / 2 - bounds.MinY * Zoom);
    }
}

public static class MachineCoordinateTransform
{
    public static Point2D OriginInDocument(JobSettings job)
    {
        var anchor=job.Origin switch
        {
            XyOrigin.BottomCentre=>new Point2D(job.WidthMm/2,0),XyOrigin.BottomRight=>new(job.WidthMm,0),
            XyOrigin.CentreLeft=>new(0,job.HeightMm/2),XyOrigin.Centre=>new(job.WidthMm/2,job.HeightMm/2),XyOrigin.CentreRight=>new(job.WidthMm,job.HeightMm/2),
            XyOrigin.TopLeft=>new(0,job.HeightMm),XyOrigin.TopCentre=>new(job.WidthMm/2,job.HeightMm),XyOrigin.TopRight=>new(job.WidthMm,job.HeightMm),
            _=>new Point2D(0,0)
        };
        return anchor+new Vector2D(job.OriginOffsetXmm,job.OriginOffsetYmm);
    }
    public static Point2D DocumentToMachine(Point2D point, JobSettings job)
    {
        var origin=OriginInDocument(job);return new(point.X-origin.X,point.Y-origin.Y);
    }
    public static Point2D MachineToDocument(Point2D point, JobSettings job)
    {
        var origin=OriginInDocument(job);return new(point.X+origin.X,point.Y+origin.Y);
    }
    public static double DocumentToMachineZ(double z,JobSettings job)=>job.ZZero==ZZeroPosition.MachineBed?z+job.ThicknessMm:z;
}
