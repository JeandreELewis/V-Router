using CncCad.Core.Geometry;

namespace CncCad.Core.Entities;

public static class VectorPathGeometry
{
    public static List<VectorPathFigure> Clone(IEnumerable<VectorPathFigure> figures)=>figures.Select(f=>new VectorPathFigure{Start=f.Start,IsClosed=f.IsClosed,Segments=f.Segments.Select(Clone).ToList()}).ToList();
    public static List<VectorPathFigure> Transform(IEnumerable<VectorPathFigure> figures,Matrix2D matrix)
    {
        var output=new List<VectorPathFigure>();
        foreach(var source in figures)
        {
            var figure=new VectorPathFigure{Start=matrix.Transform(source.Start),IsClosed=source.IsClosed};var current=source.Start;
            foreach(var segment in source.Segments)
            {
                switch(segment)
                {
                    case LinePathSegment line:figure.Segments.Add(new LinePathSegment(matrix.Transform(line.End)));break;
                    case QuadraticPathSegment quadratic:figure.Segments.Add(new QuadraticPathSegment(matrix.Transform(quadratic.Control),matrix.Transform(quadratic.End)));break;
                    case CubicPathSegment cubic:figure.Segments.Add(new CubicPathSegment(matrix.Transform(cubic.Control1),matrix.Transform(cubic.Control2),matrix.Transform(cubic.End)));break;
                    case ArcPathSegment arc:foreach(var cubic in ArcCubics(current,arc))figure.Segments.Add(new CubicPathSegment(matrix.Transform(cubic.Control1),matrix.Transform(cubic.Control2),matrix.Transform(cubic.End)));break;
                    default:throw new NotSupportedException($"Unsupported vector segment {segment.GetType().Name}.");
                }
                current=segment.End;
            }
            output.Add(figure);
        }
        return output;
    }
    private static VectorPathSegment Clone(VectorPathSegment s)=>s switch{LinePathSegment l=>new LinePathSegment(l.End),QuadraticPathSegment q=>new QuadraticPathSegment(q.Control,q.End),CubicPathSegment c=>new CubicPathSegment(c.Control1,c.Control2,c.End),ArcPathSegment a=>new ArcPathSegment(a.Center,a.SweepAngleDegrees,a.End),_=>throw new NotSupportedException()};
    public static IReadOnlyList<Point2D> Flatten(IEnumerable<VectorPathFigure> figures,double tolerance)
    {
        if(!double.IsFinite(tolerance)||tolerance<=0)throw new ArgumentOutOfRangeException(nameof(tolerance),"Flattening tolerance must be positive and finite.");var result=new List<Point2D>();foreach(var f in figures){var current=f.Start;result.Add(current);foreach(var segment in f.Segments){switch(segment){case LinePathSegment l:result.Add(l.End);break;case QuadraticPathSegment q:FlattenQuadratic(current,q.Control,q.End,tolerance,result);break;case CubicPathSegment c:FlattenCubic(current,c.Control1,c.Control2,c.End,tolerance,result);break;case ArcPathSegment a:FlattenArc(current,a,tolerance,result);break;default:throw new NotSupportedException($"Unsupported vector segment {segment.GetType().Name}.");}current=segment.End;}if(f.IsClosed&&result.Count>0&&result[^1].DistanceTo(f.Start)>1e-9)result.Add(f.Start);}return result;
    }
    public static BoundingBox Bounds(IEnumerable<VectorPathFigure> figures,Matrix2D transform)
    {
        var points=new List<Point2D>();foreach(var f in figures){var p=f.Start;points.Add(transform.Transform(p));foreach(var s in f.Segments){points.Add(transform.Transform(s.End));switch(s){case QuadraticPathSegment q:AddQuadraticExtrema(p,q,transform,points);break;case CubicPathSegment c:AddCubicExtrema(p,c,transform,points);break;case ArcPathSegment a:AddArcExtrema(p,a,transform,points);break;}p=s.End;}}return points.Count==0?new(0,0,0,0):BoundingBox.FromPoints(points);
    }
    public static (CubicPathSegment Left,CubicPathSegment Right,Point2D Mid) Split(Point2D start,CubicPathSegment c,double t)
    {var a=Lerp(start,c.Control1,t);var b=Lerp(c.Control1,c.Control2,t);var d=Lerp(c.Control2,c.End,t);var e=Lerp(a,b,t);var f=Lerp(b,d,t);var m=Lerp(e,f,t);return(new(a,e,m),new(f,d,c.End),m);}
    private static void FlattenQuadratic(Point2D a,Point2D b,Point2D c,double tolerance,List<Point2D> output,int depth=0){if(depth>16||DistanceToLine(b,a,c)<=tolerance){output.Add(c);return;}var ab=Lerp(a,b,.5);var bc=Lerp(b,c,.5);var m=Lerp(ab,bc,.5);FlattenQuadratic(a,ab,m,tolerance,output,depth+1);FlattenQuadratic(m,bc,c,tolerance,output,depth+1);}
    private static void FlattenCubic(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance,List<Point2D> output,int depth=0){if(depth>16||Math.Max(DistanceToLine(b,a,d),DistanceToLine(c,a,d))<=tolerance){output.Add(d);return;}var ab=Lerp(a,b,.5);var bc=Lerp(b,c,.5);var cd=Lerp(c,d,.5);var e=Lerp(ab,bc,.5);var f=Lerp(bc,cd,.5);var m=Lerp(e,f,.5);FlattenCubic(a,ab,e,m,tolerance,output,depth+1);FlattenCubic(m,f,cd,d,tolerance,output,depth+1);}
    private static void FlattenArc(Point2D start,ArcPathSegment arc,double tolerance,List<Point2D> output)
    {
        var radius=start.DistanceTo(arc.Center);var sweep=arc.SweepAngleDegrees*Math.PI/180;if(!double.IsFinite(radius)||radius<=1e-12||!double.IsFinite(sweep)||Math.Abs(sweep)<=1e-12){output.Add(arc.End);return;}var startAngle=Math.Atan2(start.Y-arc.Center.Y,start.X-arc.Center.X);var cosine=Math.Clamp(1-tolerance/radius,-1,1);var maximumStep=2*Math.Acos(cosine);if(!double.IsFinite(maximumStep)||maximumStep<=1e-6)maximumStep=Math.PI/180;var steps=Math.Clamp((int)Math.Ceiling(Math.Abs(sweep)/maximumStep),1,16384);for(var index=1;index<steps;index++){var angle=startAngle+sweep*index/steps;output.Add(new(arc.Center.X+radius*Math.Cos(angle),arc.Center.Y+radius*Math.Sin(angle)));}output.Add(arc.End);
    }
    private static void AddQuadraticExtrema(Point2D p,QuadraticPathSegment q,Matrix2D transform,List<Point2D> output){foreach(var t in new[]{Extremum(p.X,q.Control.X,q.End.X),Extremum(p.Y,q.Control.Y,q.End.Y)}.Where(x=>x is>0 and<1))output.Add(transform.Transform(Quadratic(p,q.Control,q.End,t)));}
    private static void AddCubicExtrema(Point2D p,CubicPathSegment c,Matrix2D transform,List<Point2D> output){foreach(var t in CubicRoots(p.X,c.Control1.X,c.Control2.X,c.End.X).Concat(CubicRoots(p.Y,c.Control1.Y,c.Control2.Y,c.End.Y)).Where(x=>x is>0 and<1))output.Add(transform.Transform(Cubic(p,c.Control1,c.Control2,c.End,t)));}
    private static void AddArcExtrema(Point2D start,ArcPathSegment arc,Matrix2D transform,List<Point2D> output)
    {
        var radius=start.DistanceTo(arc.Center);if(radius<=1e-12||!double.IsFinite(radius)||!double.IsFinite(arc.SweepAngleDegrees))return;var startAngle=Math.Atan2(start.Y-arc.Center.Y,start.X-arc.Center.X);var sweep=arc.SweepAngleDegrees*Math.PI/180;var candidates=new[]{Math.Atan2(transform.M21,transform.M11),Math.Atan2(transform.M21,transform.M11)+Math.PI,Math.Atan2(transform.M22,transform.M12),Math.Atan2(transform.M22,transform.M12)+Math.PI};foreach(var angle in candidates)if(AngleOnSweep(angle,startAngle,sweep))output.Add(transform.Transform(new(arc.Center.X+radius*Math.Cos(angle),arc.Center.Y+radius*Math.Sin(angle))));
    }
    private static bool AngleOnSweep(double angle,double start,double sweep){static double Normalize(double value){var result=value%(Math.PI*2);return result<0?result+Math.PI*2:result;}if(sweep>=0)return Normalize(angle-start)<=sweep+1e-12;return Normalize(start-angle)<=-sweep+1e-12;}
    private static IEnumerable<CubicPathSegment> ArcCubics(Point2D start,ArcPathSegment arc)
    {
        var radius=start.DistanceTo(arc.Center);var total=arc.SweepAngleDegrees*Math.PI/180;if(radius<=1e-12||!double.IsFinite(radius)||!double.IsFinite(total)){yield return new(start,start,arc.End);yield break;}var count=Math.Max(1,(int)Math.Ceiling(Math.Abs(total)/(Math.PI/2)));var step=total/count;var angle=Math.Atan2(start.Y-arc.Center.Y,start.X-arc.Center.X);for(var index=0;index<count;index++){var next=angle+step;var alpha=4d/3*Math.Tan(step/4);var p0=new Point2D(arc.Center.X+radius*Math.Cos(angle),arc.Center.Y+radius*Math.Sin(angle));var p3=index==count-1?arc.End:new Point2D(arc.Center.X+radius*Math.Cos(next),arc.Center.Y+radius*Math.Sin(next));var p1=new Point2D(p0.X-alpha*radius*Math.Sin(angle),p0.Y+alpha*radius*Math.Cos(angle));var p2=new Point2D(p3.X+alpha*radius*Math.Sin(next),p3.Y-alpha*radius*Math.Cos(next));yield return new(p1,p2,p3);angle=next;}
    }
    private static double Extremum(double a,double b,double c)=>Math.Abs(a-2*b+c)<1e-12?-1:(a-b)/(a-2*b+c);
    private static IEnumerable<double> CubicRoots(double p0,double p1,double p2,double p3){var a=-p0+3*p1-3*p2+p3;var b=2*(p0-2*p1+p2);var c=p1-p0;if(Math.Abs(a)<1e-12){if(Math.Abs(b)>1e-12)yield return-c/b;yield break;}var d=b*b-4*a*c;if(d<0)yield break;yield return(-b+Math.Sqrt(d))/(2*a);yield return(-b-Math.Sqrt(d))/(2*a);}
    private static Point2D Quadratic(Point2D a,Point2D b,Point2D c,double t)=>Lerp(Lerp(a,b,t),Lerp(b,c,t),t);
    private static Point2D Cubic(Point2D a,Point2D b,Point2D c,Point2D d,double t)=>Lerp(Lerp(Lerp(a,b,t),Lerp(b,c,t),t),Lerp(Lerp(b,c,t),Lerp(c,d,t),t),t);
    private static Point2D Lerp(Point2D a,Point2D b,double t)=>new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);
    private static double DistanceToLine(Point2D p,Point2D a,Point2D b){var d=b-a;if(d.Length<1e-12)return p.DistanceTo(a);return Math.Abs(d.X*(a.Y-p.Y)-(a.X-p.X)*d.Y)/d.Length;}
}
