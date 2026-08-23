using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public enum RepairIssueKind { DuplicateGeometry, TinySegment, CoincidentEndpoint, RedundantNode, OpenContour, SelfIntersection, Overlap }
public sealed record RepairIssue(RepairIssueKind Kind,string Message,IReadOnlyList<Guid> EntityIds,int Count=1);
public sealed record RepairOptions(double ToleranceMm=.01,double MinimumSegmentMm=.01,bool RemoveDuplicates=true,bool RemoveTinySegments=true,bool MergeEndpoints=true,bool RemoveRedundantNodes=true);
public sealed record RepairAnalysis(IReadOnlyList<RepairIssue> Issues,IReadOnlyList<CadEntity> Replacements,IReadOnlyList<Guid> RemoveIds);
public interface IVectorRepairService{RepairAnalysis Analyze(IEnumerable<CadEntity> entities,RepairOptions options);}
public sealed class VectorRepairService:IVectorRepairService
{
    public RepairAnalysis Analyze(IEnumerable<CadEntity> entities,RepairOptions options)
    {
        var source=entities.ToArray();var issues=new List<RepairIssue>();var remove=new List<Guid>();var replacements=new List<CadEntity>();
        for(var i=0;i<source.Length;i++)for(var j=i+1;j<source.Length;j++)if(Equivalent(source[i],source[j],options.ToleranceMm)){issues.Add(new(RepairIssueKind.DuplicateGeometry,"Duplicate vectors occupy the same location.",[source[i].Id,source[j].Id]));if(options.RemoveDuplicates)remove.Add(source[j].Id);}
        foreach(var path in source.OfType<PolylineEntity>())
        {
            var points=path.Points.ToList();var before=points.Count;if(options.RemoveTinySegments)for(var i=points.Count-1;i>0;i--)if(points[i].DistanceTo(points[i-1])<options.MinimumSegmentMm)points.RemoveAt(i);
            if(options.RemoveRedundantNodes)for(var i=points.Count-2;i>0;i--)if(DistanceToLine(points[i],points[i-1],points[i+1])<=options.ToleranceMm)points.RemoveAt(i);
            if(points.Count<before){issues.Add(new(RepairIssueKind.TinySegment,$"Removed {before-points.Count} tiny or redundant node(s).",[path.Id],before-points.Count));var replacement=new PolylineEntity{LayerId=path.LayerId,Points=points,IsClosed=path.IsClosed,Transform=path.Transform};replacements.Add(replacement);remove.Add(path.Id);}
            if(!path.IsClosed)issues.Add(new(RepairIssueKind.OpenContour,"Open contour requires review before profiling.",[path.Id]));
            if(SelfIntersects(EntityGeometry.WorldPoints(path),path.IsClosed,options.ToleranceMm))issues.Add(new(RepairIssueKind.SelfIntersection,"Contour crosses itself.",[path.Id]));
        }
        return new(issues,replacements,remove.Distinct().ToArray());
    }
    private static bool Equivalent(CadEntity a,CadEntity b,double tolerance){var ap=EntityGeometry.WorldPoints(a);var bp=EntityGeometry.WorldPoints(b);if(ap.Count!=bp.Count)return false;return ap.Zip(bp).All(x=>x.First.DistanceTo(x.Second)<=tolerance)||ap.Zip(bp.Reverse()).All(x=>x.First.DistanceTo(x.Second)<=tolerance);}
    private static double DistanceToLine(Point2D p,Point2D a,Point2D b){var d=b-a;if(d.Length<1e-12)return p.DistanceTo(a);return Math.Abs(d.X*(a.Y-p.Y)-(a.X-p.X)*d.Y)/d.Length;}
    private static bool SelfIntersects(IReadOnlyList<Point2D> p,bool closed,double tolerance){var count=closed?p.Count:p.Count-1;for(var i=0;i<count;i++)for(var j=i+2;j<count;j++){if(closed&&i==0&&j==count-1)continue;if(Intersect(p[i],p[(i+1)%p.Count],p[j],p[(j+1)%p.Count],tolerance))return true;}return false;}
    private static bool Intersect(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance){static double Cross(Vector2D x,Vector2D y)=>x.X*y.Y-x.Y*y.X;var r=b-a;var s=d-c;var den=Cross(r,s);if(Math.Abs(den)<=tolerance)return false;var t=Cross(c-a,s)/den;var u=Cross(c-a,r)/den;return t>tolerance&&t<1-tolerance&&u>tolerance&&u<1-tolerance;}
}

public static class ParametricShapes
{
    public static PolylineEntity Polygon(Point2D center,double radius,int sides,Guid layer){if(sides<3||radius<=0)throw new ArgumentException("Polygon needs at least three sides and a positive radius.");return new(){LayerId=layer,Points=Enumerable.Range(0,sides).Select(i=>new Point2D(center.X+radius*Math.Cos(-Math.PI/2+i*Math.PI*2/sides),center.Y+radius*Math.Sin(-Math.PI/2+i*Math.PI*2/sides))).ToList(),IsClosed=true};}
    public static PolylineEntity Star(Point2D center,double outer,double inner,int points,Guid layer){if(points<3||inner<=0||outer<=inner)throw new ArgumentException("Star radii or point count are invalid.");return new(){LayerId=layer,Points=Enumerable.Range(0,points*2).Select(i=>{var r=i%2==0?outer:inner;var a=-Math.PI/2+i*Math.PI/points;return new Point2D(center.X+r*Math.Cos(a),center.Y+r*Math.Sin(a));}).ToList(),IsClosed=true};}
    public static PolylineEntity RoundedRectangle(Point2D origin,double width,double height,double radius,Guid layer)=>Rounded(origin,width,height,radius,layer);
    public static PolylineEntity Slot(Point2D center,double length,double width,Guid layer)=>Rounded(new(center.X-length/2,center.Y-width/2),length,width,width/2,layer);
    private static PolylineEntity Rounded(Point2D o,double w,double h,double r,Guid layer){if(w<=0||h<=0)throw new ArgumentException("Shape dimensions must be positive.");r=Math.Clamp(r,0,Math.Min(w,h)/2);var points=new List<Point2D>();foreach(var (c,start) in new[]{(new Point2D(o.X+w-r,o.Y+r),-90d),(new Point2D(o.X+w-r,o.Y+h-r),0d),(new Point2D(o.X+r,o.Y+h-r),90d),(new Point2D(o.X+r,o.Y+r),180d)})for(var i=0;i<=8;i++){var a=(start+i*90d/8)*Math.PI/180;points.Add(new(c.X+r*Math.Cos(a),c.Y+r*Math.Sin(a)));}return new(){LayerId=layer,Points=points,IsClosed=true};}
}
