using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public sealed record VectorEditResult(IReadOnlyList<CadEntity> Entities,string? Error=null){public bool Success=>Error is null;public static VectorEditResult Fail(string message)=>new([],message);}
public interface IVectorTopologyService
{
    VectorEditResult Trim(CadEntity target,IEnumerable<CadEntity> cutters,Point2D click,double toleranceMm);
    VectorEditResult Chamfer(PolylineEntity path,int cornerIndex,double distanceMm);
    VectorEditResult Fillet(PolylineEntity path,int cornerIndex,double radiusMm);
}

public sealed class VectorTopologyService:IVectorTopologyService
{
    public VectorEditResult Trim(CadEntity target,IEnumerable<CadEntity> cutters,Point2D click,double toleranceMm)
    {
        if(target is not LineEntity line)return VectorEditResult.Fail("This trim pass supports an exact line target; polyline, arc, ellipse, and Bézier trim follow the same service contract but need their intersection implementation.");
        var a=line.Transform.Transform(line.Start);var b=line.Transform.Transform(line.End);var hits=new List<Point2D>();
        foreach(var cutter in cutters){var p=EntityGeometry.WorldPoints(cutter);for(var i=1;i<p.Count;i++)if(Intersection(a,b,p[i-1],p[i],toleranceMm) is{} hit)hits.Add(hit);var closed=cutter is RectangleEntity or CircleEntity or EllipseEntity or PolylineEntity{IsClosed:true}||cutter is VectorPathEntity vp&&vp.Figures.Any(x=>x.IsClosed);if(closed&&p.Count>2&&Intersection(a,b,p[^1],p[0],toleranceMm) is{} closing)hits.Add(closing);}
        hits=hits.Where(x=>x.DistanceTo(a)>toleranceMm&&x.DistanceTo(b)>toleranceMm).DistinctBy(x=>(Math.Round(x.X/toleranceMm),Math.Round(x.Y/toleranceMm))).OrderBy(x=>Parameter(a,b,x)).ToList();if(hits.Count<2)return VectorEditResult.Fail("Trim needs two non-tangent intersections on the selected line.");
        var boundaries=new[]{a}.Concat(hits).Append(b).ToArray();var remove=Enumerable.Range(0,boundaries.Length-1).OrderBy(i=>DistanceToSegment(click,boundaries[i],boundaries[i+1])).First();var inverse=line.Transform.Inverse();var result=new List<CadEntity>();if(remove>0)result.Add(Copy(line,inverse.Transform(a),inverse.Transform(boundaries[remove])));if(remove<boundaries.Length-2)result.Add(Copy(line,inverse.Transform(boundaries[remove+1]),inverse.Transform(b)));return result.Count==0?VectorEditResult.Fail("Trim would remove the complete target."):new(result);
    }
    public VectorEditResult Chamfer(PolylineEntity path,int cornerIndex,double distanceMm)
    {
        if(distanceMm<=0||path.Points.Count<3)return VectorEditResult.Fail("Chamfer needs a positive distance and a path corner.");var points=path.Points.ToList();var i=Math.Clamp(cornerIndex,0,points.Count-1);if(!path.IsClosed&&(i==0||i==points.Count-1))return VectorEditResult.Fail("Choose an interior corner.");var prior=points[(i-1+points.Count)%points.Count];var corner=points[i];var next=points[(i+1)%points.Count];if(distanceMm>=Math.Min(prior.DistanceTo(corner),next.DistanceTo(corner)))return VectorEditResult.Fail("Chamfer distance exceeds an adjacent segment.");points[i]=Along(corner,prior,distanceMm);points.Insert(i+1,Along(corner,next,distanceMm));return new([new PolylineEntity{LayerId=path.LayerId,Transform=path.Transform,Points=points,IsClosed=path.IsClosed}]);
    }
    public VectorEditResult Fillet(PolylineEntity path,int cornerIndex,double radiusMm)
    {
        var chamfer=Chamfer(path,cornerIndex,radiusMm);if(!chamfer.Success)return chamfer;var poly=(PolylineEntity)chamfer.Entities[0];var i=Math.Clamp(cornerIndex,0,poly.Points.Count-2);var figure=new VectorPathFigure{Start=poly.Points[0],IsClosed=poly.IsClosed};for(var n=1;n<poly.Points.Count;n++){if(n==i+1){var original=path.Points[Math.Clamp(cornerIndex,0,path.Points.Count-1)];figure.Segments.Add(new QuadraticPathSegment(original,poly.Points[n]));}else figure.Segments.Add(new LinePathSegment(poly.Points[n]));}return new([new VectorPathEntity{LayerId=path.LayerId,Transform=path.Transform,Figures=[figure]}]);
    }
    private static LineEntity Copy(LineEntity source,Point2D start,Point2D end)=>new(){LayerId=source.LayerId,Transform=source.Transform,Start=start,End=end};
    private static Point2D Along(Point2D from,Point2D to,double distance){var d=to-from;var scale=distance/d.Length;return new(from.X+d.X*scale,from.Y+d.Y*scale);}
    private static double Parameter(Point2D a,Point2D b,Point2D p){var d=b-a;return Math.Abs(d.X)>=Math.Abs(d.Y)?(p.X-a.X)/d.X:(p.Y-a.Y)/d.Y;}
    private static Point2D? Intersection(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance){static double Cross(Vector2D u,Vector2D v)=>u.X*v.Y-u.Y*v.X;var r=b-a;var s=d-c;var den=Cross(r,s);if(Math.Abs(den)<=tolerance)return null;var t=Cross(c-a,s)/den;var u=Cross(c-a,r)/den;return t>=-tolerance&&t<=1+tolerance&&u>=-tolerance&&u<=1+tolerance?new(a.X+r.X*t,a.Y+r.Y*t):null;}
    private static double DistanceToSegment(Point2D p,Point2D a,Point2D b){var d=b-a;var lengthSquared=d.X*d.X+d.Y*d.Y;var q=p-a;var t=lengthSquared<1e-12?0:Math.Clamp((q.X*d.X+q.Y*d.Y)/lengthSquared,0,1);return p.DistanceTo(new(a.X+d.X*t,a.Y+d.Y*t));}
}
