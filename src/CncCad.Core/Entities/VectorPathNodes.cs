using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public static class VectorPathNodes
{
    public static IReadOnlyList<Point2D> Points(VectorPathEntity path)=>path.Figures.SelectMany(Points).ToArray();
    public static Point2D Get(VectorPathEntity path,int index)=>Points(path)[index];
    public static void Set(VectorPathEntity path,int index,Point2D value)
    {
        var cursor=0;foreach(var figure in path.Figures){if(cursor++==index){figure.Start=value;return;}foreach(var segment in figure.Segments){switch(segment){case LinePathSegment line:if(cursor++==index){line.End=value;return;}break;case QuadraticPathSegment quadratic:if(cursor++==index){quadratic.Control=value;return;}if(cursor++==index){quadratic.End=value;return;}break;case CubicPathSegment cubic:if(cursor++==index){cubic.Control1=value;return;}if(cursor++==index){cubic.Control2=value;return;}if(cursor++==index){cubic.End=value;return;}break;}}}throw new ArgumentOutOfRangeException(nameof(index));
    }
    private static IEnumerable<Point2D> Points(VectorPathFigure figure){yield return figure.Start;foreach(var segment in figure.Segments)switch(segment){case LinePathSegment line:yield return line.End;break;case QuadraticPathSegment quadratic:yield return quadratic.Control;yield return quadratic.End;break;case CubicPathSegment cubic:yield return cubic.Control1;yield return cubic.Control2;yield return cubic.End;break;}}
}
