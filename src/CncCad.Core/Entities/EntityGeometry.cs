using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public static class EntityGeometry
{
    public static IReadOnlyList<Point2D> WorldPoints(CadEntity entity, int curveSegments = 96) => entity switch
    {
        LineEntity line => [line.Transform.Transform(line.Start), line.Transform.Transform(line.End)],
        PolylineEntity polyline => polyline.Points.Select(polyline.Transform.Transform).ToArray(),
        RectangleEntity rectangle => rectangle.Corners.Select(rectangle.Transform.Transform).ToArray(),
        CircleEntity circle => Enumerable.Range(0, curveSegments).Select(i => circle.Transform.Transform(new(circle.Center.X + circle.Radius * Math.Cos(i * Math.PI * 2 / curveSegments), circle.Center.Y + circle.Radius * Math.Sin(i * Math.PI * 2 / curveSegments)))).ToArray(),
        EllipseEntity ellipse => Enumerable.Range(0, curveSegments).Select(i => ellipse.Transform.Transform(new(ellipse.Center.X + ellipse.RadiusX * Math.Cos(i * Math.PI * 2 / curveSegments), ellipse.Center.Y + ellipse.RadiusY * Math.Sin(i * Math.PI * 2 / curveSegments)))).ToArray(),
        ArcEntity arc => Enumerable.Range(0, curveSegments + 1).Select(i => arc.Transform.Transform(GeometryAlgorithms.PointOnArc(arc, arc.StartAngleDegrees + arc.SweepAngleDegrees * i / curveSegments))).ToArray(),
        VectorPathEntity path => VectorPathGeometry.Flatten(path.Figures, .02).Select(path.Transform.Transform).ToArray(),
        TextEntity text => VectorPathGeometry.Flatten(text.Outlines, .02).Select(text.Transform.Transform).ToArray(),
        _ => []
    };

    public static BoundingBox WorldBounds(CadEntity entity) => entity switch
    {
        CircleEntity circle => EllipticBounds(circle.Center,circle.Radius,circle.Radius,circle.Transform),
        EllipseEntity ellipse => EllipticBounds(ellipse.Center,ellipse.RadiusX,ellipse.RadiusY,ellipse.Transform),
        ArcEntity arc => ArcWorldBounds(arc),
        VectorPathEntity path => VectorPathGeometry.Bounds(path.Figures,path.Transform),
        TextEntity text => VectorPathGeometry.Bounds(text.Outlines,text.Transform),
        _ => BoundingBox.FromPoints(WorldPoints(entity))
    };

    private static BoundingBox EllipticBounds(Point2D center,double radiusX,double radiusY,Matrix2D transform)
    {
        var c=transform.Transform(center);var xRadius=Math.Sqrt(Math.Pow(radiusX*transform.M11,2)+Math.Pow(radiusY*transform.M21,2));var yRadius=Math.Sqrt(Math.Pow(radiusX*transform.M12,2)+Math.Pow(radiusY*transform.M22,2));return new(c.X-xRadius,c.Y-yRadius,c.X+xRadius,c.Y+yRadius);
    }
    private static BoundingBox ArcWorldBounds(ArcEntity arc)
    {
        var candidates=new List<double>{arc.StartAngleDegrees,arc.StartAngleDegrees+arc.SweepAngleDegrees};
        var x=Math.Atan2(arc.Transform.M21,arc.Transform.M11)*180/Math.PI;var y=Math.Atan2(arc.Transform.M22,arc.Transform.M12)*180/Math.PI;
        foreach(var angle in new[]{x,x+180,y,y+180})if(GeometryAlgorithms.IsAngleOnSweep(angle,arc.StartAngleDegrees,arc.SweepAngleDegrees))candidates.Add(angle);
        return BoundingBox.FromPoints(candidates.Select(a=>arc.Transform.Transform(GeometryAlgorithms.PointOnArc(arc,a))));
    }

    public static void Translate(CadEntity entity, Vector2D delta)
    {
        var inverse=entity.Transform.Inverse();var localOrigin=inverse.Transform(new(0,0));var localEnd=inverse.Transform(new(delta.X,delta.Y));delta=localEnd-localOrigin;
        switch (entity)
        {
            case LineEntity line: line.Start += delta; line.End += delta; break;
            case PolylineEntity polyline: for (var i = 0; i < polyline.Points.Count; i++) polyline.Points[i] += delta; break;
            case RectangleEntity rectangle: rectangle.Origin += delta; break;
            case CircleEntity circle: circle.Center += delta; break;
            case ArcEntity arc: arc.Center += delta; break;
            case EllipseEntity ellipse: ellipse.Center += delta; break;
            case VectorPathEntity path: path.Transform=Matrix2D.Then(path.Transform,Matrix2D.Translation(delta.X,delta.Y));break;
            case TextEntity text: text.Transform=Matrix2D.Then(text.Transform,Matrix2D.Translation(delta.X,delta.Y));break;
            default: throw new NotSupportedException($"Translation is not implemented for {entity.GetType().Name}.");
        }
    }

    public static CadEntity Clone(CadEntity entity) => entity switch
    {
        LineEntity line => CopyBase(line, new LineEntity { Start = line.Start, End = line.End }),
        PolylineEntity polyline => CopyBase(polyline, new PolylineEntity { Points = [.. polyline.Points], IsClosed = polyline.IsClosed }),
        RectangleEntity rectangle => CopyBase(rectangle, new RectangleEntity { Origin = rectangle.Origin, Width = rectangle.Width, Height = rectangle.Height }),
        CircleEntity circle => CopyBase(circle, new CircleEntity { Center = circle.Center, Radius = circle.Radius }),
        ArcEntity arc => CopyBase(arc, new ArcEntity { Center = arc.Center, Radius = arc.Radius, StartAngleDegrees = arc.StartAngleDegrees, SweepAngleDegrees = arc.SweepAngleDegrees }),
        EllipseEntity ellipse => CopyBase(ellipse, new EllipseEntity { Center = ellipse.Center, RadiusX = ellipse.RadiusX, RadiusY = ellipse.RadiusY }),
        VectorPathEntity path => CopyBase(path,new VectorPathEntity{Figures=VectorPathGeometry.Clone(path.Figures)}),
        TextEntity text => CopyBase(text,new TextEntity{Text=text.Text,FontFamily=text.FontFamily,FontStyle=text.FontStyle,SizeMm=text.SizeMm,LetterSpacingMm=text.LetterSpacingMm,LineSpacing=text.LineSpacing,Alignment=text.Alignment,FontKind=text.FontKind,LayoutMode=text.LayoutMode,PathPlacement=new(){GuideEntityId=text.PathPlacement.GuideEntityId,StartOffset=text.PathPlacement.StartOffset,BaselineOffsetMm=text.PathPlacement.BaselineOffsetMm,ReverseDirection=text.PathPlacement.ReverseDirection,FlipSide=text.PathPlacement.FlipSide,KeepUpright=text.PathPlacement.KeepUpright,FitMode=text.PathPlacement.FitMode,GuideSnapshot=[..text.PathPlacement.GuideSnapshot]},GlyphAdvancesMm=[..text.GlyphAdvancesMm],SourceOutlines=VectorPathGeometry.Clone(text.SourceOutlines),Outlines=VectorPathGeometry.Clone(text.Outlines),Glyphs=text.Glyphs.Select(x=>new TextGlyphGeometry{Cluster=x.Cluster,AdvanceMm=x.AdvanceMm,Order=x.Order,SourceFigures=VectorPathGeometry.Clone(x.SourceFigures),PlacedFigures=VectorPathGeometry.Clone(x.PlacedFigures)}).ToList()}),
        _ => throw new NotSupportedException($"Cloning is not implemented for {entity.GetType().Name}.")
    };

    public static IReadOnlyList<Point2D> Endpoints(CadEntity entity) => entity switch
    {
        LineEntity line => [line.Transform.Transform(line.Start), line.Transform.Transform(line.End)],
        PolylineEntity polyline when polyline.Points.Count > 0 => (polyline.IsClosed ? polyline.Points : [polyline.Points[0], polyline.Points[^1]]).Select(polyline.Transform.Transform).ToArray(),
        RectangleEntity rectangle => rectangle.Corners.Select(rectangle.Transform.Transform).ToArray(),
        ArcEntity arc => [arc.Transform.Transform(GeometryAlgorithms.PointOnArc(arc, arc.StartAngleDegrees)), arc.Transform.Transform(GeometryAlgorithms.PointOnArc(arc, arc.StartAngleDegrees + arc.SweepAngleDegrees))],
        VectorPathEntity path=>path.Figures.Where(x=>!x.IsClosed).SelectMany(x=>new[]{path.Transform.Transform(x.Start),path.Transform.Transform(x.Segments.LastOrDefault()?.End??x.Start)}).ToArray(),
        _ => []
    };

    public static IReadOnlyList<Point2D> Midpoints(CadEntity entity) => entity switch
    {
        LineEntity line => [line.Transform.Transform(Mid(line.Start, line.End))],
        RectangleEntity rectangle => rectangle.Corners.Zip(rectangle.Corners.Skip(1).Append(rectangle.Corners[0]), Mid).Select(rectangle.Transform.Transform).ToArray(),
        PolylineEntity polyline => polyline.Points.Zip(polyline.Points.Skip(1), Mid).Select(polyline.Transform.Transform).ToArray(),
        ArcEntity arc => [arc.Transform.Transform(GeometryAlgorithms.PointOnArc(arc, arc.StartAngleDegrees + arc.SweepAngleDegrees / 2))],
        VectorPathEntity path => path.Figures.SelectMany(figure=>VectorPathGeometry.Flatten([figure],.02).Zip(VectorPathGeometry.Flatten([figure],.02).Skip(1),Mid)).Select(path.Transform.Transform).ToArray(),
        _ => []
    };

    public static IReadOnlyList<Point2D> Centers(CadEntity entity) => entity switch
    {
        CircleEntity circle => [circle.Transform.Transform(circle.Center)], ArcEntity arc => [arc.Transform.Transform(arc.Center)], EllipseEntity ellipse => [ellipse.Transform.Transform(ellipse.Center)],
        RectangleEntity rectangle => [rectangle.Transform.Transform(new(rectangle.Origin.X + rectangle.Width / 2, rectangle.Origin.Y + rectangle.Height / 2))], _ => []
    };

    private static Point2D Mid(Point2D a, Point2D b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    private static T CopyBase<T>(CadEntity source, T target) where T : CadEntity { target.LayerId = source.LayerId;target.MaterialSheetId=source.MaterialSheetId;target.GroupId=source.GroupId;target.StrokeColor=source.StrokeColor; target.IsVisible = source.IsVisible; target.IsLocked = source.IsLocked; target.Transform = source.Transform; return target; }
}
