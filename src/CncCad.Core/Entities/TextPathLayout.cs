using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public static class TextPathLayout
{
    public static IReadOnlyList<VectorPathFigure> Fit(IEnumerable<VectorPathFigure> source,IReadOnlyList<Point2D> guide,TextPathPlacement placement)
    {
        var figures=VectorPathGeometry.Clone(source);if(guide.Count<2||figures.Count==0)return figures;var path=placement.ReverseDirection?guide.Reverse().ToArray():guide.ToArray();var lengths=new double[path.Length];for(var i=1;i<path.Length;i++)lengths[i]=lengths[i-1]+path[i-1].DistanceTo(path[i]);var total=lengths[^1];if(total<1e-9)return figures;var bounds=VectorPathGeometry.Bounds(figures,Matrix2D.Identity);var textWidth=Math.Max(1e-9,bounds.Width);var scale=placement.FitMode==TextFitMode.FitSpacing?total/textWidth:1;var alignmentShift=placement.StartOffset*total+(placement.FitMode==TextFitMode.Natural?(total-textWidth)/2:0);var output=new List<VectorPathFigure>();foreach(var figure in figures){var fb=VectorPathGeometry.Bounds([figure],Matrix2D.Identity);var anchorX=(fb.MinX+fb.MaxX)/2;var distance=Math.Clamp(alignmentShift+(anchorX-bounds.MinX)*scale,0,total);var (point,tangent)=At(path,lengths,distance);var angle=Math.Atan2(tangent.Y,tangent.X)*180/Math.PI;if(placement.KeepUpright&&(angle>90||angle<-90))angle+=180;var normal=new Vector2D(-tangent.Y,tangent.X);if(placement.FlipSide)normal=new(-normal.X,-normal.Y);var target=point+new Vector2D(normal.X*placement.BaselineOffsetMm,normal.Y*placement.BaselineOffsetMm);var localAnchor=new Point2D(anchorX,bounds.MinY);var matrix=Matrix2D.Then(Matrix2D.Translation(-localAnchor.X,-localAnchor.Y),Matrix2D.Then(Matrix2D.Rotation(angle),Matrix2D.Translation(target.X,target.Y)));output.AddRange(VectorPathGeometry.Transform([figure],matrix));}return output;
    }
    private static (Point2D Point,Vector2D Tangent) At(IReadOnlyList<Point2D> path,IReadOnlyList<double> lengths,double distance){var i=1;while(i<lengths.Count-1&&lengths[i]<distance)i++;var a=path[i-1];var b=path[i];var d=b-a;var length=Math.Max(d.Length,1e-12);var t=Math.Clamp((distance-lengths[i-1])/length,0,1);return(new(a.X+d.X*t,a.Y+d.Y*t),new(d.X/length,d.Y/length));}
}
