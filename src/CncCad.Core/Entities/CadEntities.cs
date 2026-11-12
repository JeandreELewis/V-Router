using CncCad.Core.Geometry;
using System.Text.Json.Serialization;

namespace CncCad.Core.Entities;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LineEntity), "line")]
[JsonDerivedType(typeof(PolylineEntity), "polyline")]
[JsonDerivedType(typeof(RectangleEntity), "rectangle")]
[JsonDerivedType(typeof(CircleEntity), "circle")]
[JsonDerivedType(typeof(ArcEntity), "arc")]
[JsonDerivedType(typeof(EllipseEntity), "ellipse")]
[JsonDerivedType(typeof(VectorPathEntity), "vectorPath")]
[JsonDerivedType(typeof(TextEntity), "text")]
[JsonDerivedType(typeof(DrillPointEntity), "drillPoint")]
public abstract class CadEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid LayerId { get; set; }
    public Guid? MaterialSheetId { get; set; }
    public Guid? GroupId { get; set; }
    public string? StrokeColor { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public Matrix2D Transform { get; set; } = Matrix2D.Identity;
    public abstract BoundingBox GetBounds();
}

public sealed class LineEntity : CadEntity
{
    public Point2D Start { get; set; }
    public Point2D End { get; set; }
    public override BoundingBox GetBounds() => BoundingBox.FromPoints([Transform.Transform(Start), Transform.Transform(End)]);
}

public sealed class PolylineEntity : CadEntity
{
    public List<Point2D> Points { get; set; } = [];
    public bool IsClosed { get; set; }
    public override BoundingBox GetBounds() => BoundingBox.FromPoints(Points.Select(Transform.Transform));
}

public sealed class RectangleEntity : CadEntity
{
    public Point2D Origin { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public IReadOnlyList<Point2D> Corners => [Origin, new(Origin.X + Width, Origin.Y), new(Origin.X + Width, Origin.Y + Height), new(Origin.X, Origin.Y + Height)];
    public override BoundingBox GetBounds() => BoundingBox.FromPoints(Corners.Select(Transform.Transform));
}

public sealed class CircleEntity : CadEntity
{
    public Point2D Center { get; set; }
    public double Radius { get; set; }
    public override BoundingBox GetBounds() => EntityGeometry.WorldBounds(this);
}

public sealed class DrillPointEntity : CadEntity
{
    public Point2D Position { get; set; }
    public override BoundingBox GetBounds()
    {
        var point = Transform.Transform(Position);
        return new(point.X, point.Y, point.X, point.Y);
    }
}

public sealed class ArcEntity : CadEntity
{
    public Point2D Center { get; set; }
    public double Radius { get; set; }
    public double StartAngleDegrees { get; set; }
    public double SweepAngleDegrees { get; set; }
    public override BoundingBox GetBounds() => EntityGeometry.WorldBounds(this);
}

public sealed class EllipseEntity : CadEntity
{
    public Point2D Center { get; set; }
    public double RadiusX { get; set; }
    public double RadiusY { get; set; }
    public override BoundingBox GetBounds() => EntityGeometry.WorldBounds(this);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LinePathSegment), "line")]
[JsonDerivedType(typeof(QuadraticPathSegment), "quadratic")]
[JsonDerivedType(typeof(CubicPathSegment), "cubic")]
[JsonDerivedType(typeof(ArcPathSegment), "arc")]
public abstract record VectorPathSegment { protected VectorPathSegment(Point2D end)=>End=end; public Point2D End { get; set; } }
public sealed record LinePathSegment : VectorPathSegment { public LinePathSegment(Point2D end):base(end){} }
public sealed record QuadraticPathSegment : VectorPathSegment { public QuadraticPathSegment(Point2D control,Point2D end):base(end)=>Control=control; public Point2D Control { get; set; } }
public sealed record CubicPathSegment : VectorPathSegment { public CubicPathSegment(Point2D control1,Point2D control2,Point2D end):base(end){Control1=control1;Control2=control2;} public Point2D Control1 { get; set; } public Point2D Control2 { get; set; } }
public sealed record ArcPathSegment : VectorPathSegment
{
    public ArcPathSegment(Point2D center,double sweepAngleDegrees,Point2D end):base(end){Center=center;SweepAngleDegrees=sweepAngleDegrees;}
    public Point2D Center { get; set; }
    public double SweepAngleDegrees { get; set; }
}
public sealed class VectorPathFigure
{
    public Point2D Start { get; set; }
    public List<VectorPathSegment> Segments { get; set; } = [];
    public bool IsClosed { get; set; }
}
public sealed class VectorPathEntity : CadEntity
{
    public List<VectorPathFigure> Figures { get; set; } = [];
    public override BoundingBox GetBounds() => EntityGeometry.WorldBounds(this);
}
public enum TextAlignment { Left, Center, Right }
public enum FontKind { Outline, SingleLine, Stencil }
public enum TextLayoutMode { Straight, FollowPath }
public enum TextFitMode { Natural, FitSpacing }
public sealed class TextPathPlacement
{
    public Guid? GuideEntityId { get; set; }
    public double StartOffset { get; set; }
    public double BaselineOffsetMm { get; set; }
    public bool ReverseDirection { get; set; }
    public bool FlipSide { get; set; }
    public bool KeepUpright { get; set; } = true;
    public TextFitMode FitMode { get; set; }
    public List<Point2D> GuideSnapshot { get; set; } = [];
}
public sealed class TextGlyphGeometry
{
    public string Cluster{get;set;}="";public double AdvanceMm{get;set;}public int Order{get;set;}public List<VectorPathFigure> SourceFigures{get;set;}=[];public List<VectorPathFigure> PlacedFigures{get;set;}=[];
}
public sealed class TextEntity : CadEntity
{
    public string Text { get; set; } = "Text";
    public string FontFamily { get; set; } = "Inter";
    public string FontStyle { get; set; } = "Regular";
    public double SizeMm { get; set; } = 12;
    public double LetterSpacingMm { get; set; }
    public double LineSpacing { get; set; } = 1.2;
    public TextAlignment Alignment { get; set; }
    public FontKind FontKind { get; set; }
    public TextLayoutMode LayoutMode { get; set; }
    public TextPathPlacement PathPlacement { get; set; } = new();
    public List<double> GlyphAdvancesMm { get; set; } = [];
    public List<VectorPathFigure> SourceOutlines { get; set; } = [];
    public List<VectorPathFigure> Outlines { get; set; } = [];
    public List<TextGlyphGeometry> Glyphs { get; set; } = [];
    public override BoundingBox GetBounds() => EntityGeometry.WorldBounds(this);
}
