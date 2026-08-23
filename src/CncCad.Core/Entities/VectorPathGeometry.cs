using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public static class VectorPathGeometry
{
    public static List<VectorPathFigure> Clone(IEnumerable<VectorPathFigure> figures)=>figures.Select(f=>new VectorPathFigure{Start=f.Start,IsClosed=f.IsClosed,Segments=f.Segments.Select(Clone).ToList()}).ToList();
    public static List<VectorPathFigure> Transform(IEnumerable<VectorPathFigure> figures,Matrix2D matrix)=>figures.Select(f=>new VectorPathFigure{Start=matrix.Transform(f.Start),IsClosed=f.IsClosed,Segments=f.Segments.Select(s=>(VectorPathSegment)(s switch{LinePathSegment l=>new LinePathSegment(matrix.Transform(l.End)),QuadraticPathSegment q=>new QuadraticPathSegment(matrix.Transform(q.Control),matrix.Transform(q.End)),CubicPathSegment c=>new CubicPathSegment(matrix.Transform(c.Control1),matrix.Transform(c.Control2),matrix.Transform(c.End)),_=>throw new NotSupportedException()})).ToList()}).ToList();
    private static VectorPathSegment Clone(VectorPathSegment s)=>s switch{LinePathSegment l=>new LinePathSegment(l.End),QuadraticPathSegment q=>new QuadraticPathSegment(q.Control,q.End),CubicPathSegment c=>new CubicPathSegment(c.Control1,c.Control2,c.End),_=>throw new NotSupportedException()};
    public static IReadOnlyList<Point2D> Flatten(IEnumerable<VectorPathFigure> figures,double tolerance)
    {
        var result=new List<Point2D>();foreach(var f in figures){var current=f.Start;result.Add(current);foreach(var segment in f.Segments){switch(segment){case LinePathSegment l:result.Add(l.End);break;case QuadraticPathSegment q:FlattenQuadratic(current,q.Control,q.End,tolerance,result);break;case CubicPathSegment c:FlattenCubic(current,c.Control1,c.Control2,c.End,tolerance,result);break;}current=segment.End;}if(f.IsClosed&&result.Count>0&&result[^1].DistanceTo(f.Start)>1e-9)result.Add(f.Start);}return result;
    }
    public static BoundingBox Bounds(IEnumerable<VectorPathFigure> figures,Matrix2D transform)
    {
        var points=new List<Point2D>();foreach(var f in figures){var p=f.Start;points.Add(transform.Transform(p));foreach(var s in f.Segments){points.Add(transform.Transform(s.End));switch(s){case QuadraticPathSegment q:AddQuadraticExtrema(p,q,transform,points);break;case CubicPathSegment c:AddCubicExtrema(p,c,transform,points);break;}p=s.End;}}return points.Count==0?new(0,0,0,0):BoundingBox.FromPoints(points);
    }
    public static (CubicPathSegment Left,CubicPathSegment Right,Point2D Mid) Split(Point2D start,CubicPathSegment c,double t)
    {var a=Lerp(start,c.Control1,t);var b=Lerp(c.Control1,c.Control2,t);var d=Lerp(c.Control2,c.End,t);var e=Lerp(a,b,t);var f=Lerp(b,d,t);var m=Lerp(e,f,t);return(new(a,e,m),new(f,d,c.End),m);}
    private static void FlattenQuadratic(Point2D a,Point2D b,Point2D c,double tolerance,List<Point2D> output,int depth=0){if(depth>16||DistanceToLine(b,a,c)<=tolerance){output.Add(c);return;}var ab=Lerp(a,b,.5);var bc=Lerp(b,c,.5);var m=Lerp(ab,bc,.5);FlattenQuadratic(a,ab,m,tolerance,output,depth+1);FlattenQuadratic(m,bc,c,tolerance,output,depth+1);}
    private static void FlattenCubic(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance,List<Point2D> output,int depth=0){if(depth>16||Math.Max(DistanceToLine(b,a,d),DistanceToLine(c,a,d))<=tolerance){output.Add(d);return;}var ab=Lerp(a,b,.5);var bc=Lerp(b,c,.5);var cd=Lerp(c,d,.5);var e=Lerp(ab,bc,.5);var f=Lerp(bc,cd,.5);var m=Lerp(e,f,.5);FlattenCubic(a,ab,e,m,tolerance,output,depth+1);FlattenCubic(m,f,cd,d,tolerance,output,depth+1);}
    private static void AddQuadraticExtrema(Point2D p,QuadraticPathSegment q,Matrix2D transform,List<Point2D> output){foreach(var t in new[]{Extremum(p.X,q.Control.X,q.End.X),Extremum(p.Y,q.Control.Y,q.End.Y)}.Where(x=>x is>0 and<1))output.Add(transform.Transform(Quadratic(p,q.Control,q.End,t)));}
    private static void AddCubicExtrema(Point2D p,CubicPathSegment c,Matrix2D transform,List<Point2D> output){foreach(var t in CubicRoots(p.X,c.Control1.X,c.Control2.X,c.End.X).Concat(CubicRoots(p.Y,c.Control1.Y,c.Control2.Y,c.End.Y)).Where(x=>x is>0 and<1))output.Add(transform.Transform(Cubic(p,c.Control1,c.Control2,c.End,t)));}
    private static double Extremum(double a,double b,double c)=>Math.Abs(a-2*b+c)<1e-12?-1:(a-b)/(a-2*b+c);
    private static IEnumerable<double> CubicRoots(double p0,double p1,double p2,double p3){var a=-p0+3*p1-3*p2+p3;var b=2*(p0-2*p1+p2);var c=p1-p0;if(Math.Abs(a)<1e-12){if(Math.Abs(b)>1e-12)yield return-c/b;yield break;}var d=b*b-4*a*c;if(d<0)yield break;yield return(-b+Math.Sqrt(d))/(2*a);yield return(-b-Math.Sqrt(d))/(2*a);}
    private static Point2D Quadratic(Point2D a,Point2D b,Point2D c,double t)=>Lerp(Lerp(a,b,t),Lerp(b,c,t),t);
    private static Point2D Cubic(Point2D a,Point2D b,Point2D c,Point2D d,double t)=>Lerp(Lerp(Lerp(a,b,t),Lerp(b,c,t),t),Lerp(Lerp(b,c,t),Lerp(c,d,t),t),t);
    private static Point2D Lerp(Point2D a,Point2D b,double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
    private static double DistanceToLine(Point2D p,Point2D a,Point2D b){var d=b-a;if(d.Length<1e-12)return p.DistanceTo(a);return Math.Abs(d.X*(a.Y-p.Y)-(a.X-p.X)*d.Y)/d.Length;}
}
