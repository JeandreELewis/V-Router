using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public static class TextPathLayout
{
    public static IReadOnlyList<VectorPathFigure> Fit(IEnumerable<VectorPathFigure> source,IReadOnlyList<Point2D> guide,TextPathPlacement placement)
    {
        return FitGlyphs(source.Select(figure=>(IEnumerable<VectorPathFigure>)[figure]),guide,placement).SelectMany(group=>group).ToArray();
    }

    public static IReadOnlyList<IReadOnlyList<VectorPathFigure>> FitGlyphs(IEnumerable<IEnumerable<VectorPathFigure>> sourceGlyphs,IReadOnlyList<Point2D> guide,TextPathPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(sourceGlyphs);ArgumentNullException.ThrowIfNull(guide);ArgumentNullException.ThrowIfNull(placement);
        if(!double.IsFinite(placement.StartOffset)||!double.IsFinite(placement.BaselineOffsetMm))throw new ArgumentException("Text path placement must contain finite values.",nameof(placement));
        var groups=sourceGlyphs.Select(VectorPathGeometry.Clone).ToArray();var figures=groups.SelectMany(group=>group).ToArray();if(figures.Length==0)return groups;
        if(guide.Any(point=>!GeometryValidation.IsFinite(point)))throw new ArgumentException("Text path guide must contain finite coordinates.",nameof(guide));
        var clean=new List<Point2D>();foreach(var point in guide)if(clean.Count==0||clean[^1].DistanceTo(point)>GeometryValidation.CoordinateToleranceMm)clean.Add(point);if(clean.Count<2)return groups;
        var path=placement.ReverseDirection?clean.AsEnumerable().Reverse().ToArray():clean.ToArray();var lengths=new double[path.Length];for(var i=1;i<path.Length;i++)lengths[i]=lengths[i-1]+path[i-1].DistanceTo(path[i]);var total=lengths[^1];if(total<=GeometryValidation.CoordinateToleranceMm)return groups;
        var bounds=VectorPathGeometry.Bounds(figures,Matrix2D.Identity);var textWidth=Math.Max(GeometryValidation.CoordinateToleranceMm,bounds.Width);var spacingScale=placement.FitMode==TextFitMode.FitSpacing?total/textWidth:1;var firstDistance=placement.StartOffset*total-textWidth*spacingScale/2;var output=new List<IReadOnlyList<VectorPathFigure>>(groups.Length);
        foreach(var group in groups)
        {
            if(group.Count==0){output.Add(group);continue;}var glyphBounds=VectorPathGeometry.Bounds(group,Matrix2D.Identity);var anchorX=(glyphBounds.MinX+glyphBounds.MaxX)/2;var distance=Math.Clamp(firstDistance+(anchorX-bounds.MinX)*spacingScale,0,total);var (point,tangent)=At(path,lengths,distance);var angle=Math.Atan2(tangent.Y,tangent.X)*180/Math.PI;if(placement.KeepUpright&&(angle>90||angle<-90))angle+=180;var normal=new Vector2D(-tangent.Y,tangent.X);if(placement.FlipSide)normal=new(-normal.X,-normal.Y);var target=point+new Vector2D(normal.X*placement.BaselineOffsetMm,normal.Y*placement.BaselineOffsetMm);var localAnchor=new Point2D(anchorX,bounds.MinY);var matrix=Matrix2D.Then(Matrix2D.Translation(-localAnchor.X,-localAnchor.Y),Matrix2D.Then(Matrix2D.Rotation(angle),Matrix2D.Translation(target.X,target.Y)));output.Add(VectorPathGeometry.Transform(group,matrix));
        }
        return output;
    }

    private static (Point2D Point,Vector2D Tangent) At(IReadOnlyList<Point2D> path,IReadOnlyList<double> lengths,double distance){var i=1;while(i<lengths.Count-1&&lengths[i]<distance)i++;var a=path[i-1];var b=path[i];var d=b-a;var length=d.Length;var t=Math.Clamp((distance-lengths[i-1])/length,0,1);return(new(a.X+d.X*t,a.Y+d.Y*t),new(d.X/length,d.Y/length));}
}
