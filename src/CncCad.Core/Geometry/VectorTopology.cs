using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public enum VectorEditOutcome { Applied, NoChange, Rejected }
public sealed record VectorEditDiagnostic(string Code,string Message);
public sealed record VectorEditResult
{
    public VectorEditResult(IReadOnlyList<CadEntity> entities,string? error=null)
    {
        Entities=entities;
        Outcome=error is not null?VectorEditOutcome.Rejected:entities.Count>0?VectorEditOutcome.Applied:VectorEditOutcome.NoChange;
        Diagnostics=error is null?[]:[new("rejected",error)];
    }
    private VectorEditResult(VectorEditOutcome outcome,IReadOnlyList<CadEntity> entities,IReadOnlyList<Guid> affectedEntityIds,IReadOnlyList<VectorEditDiagnostic> diagnostics)
    {
        Outcome=outcome;Entities=entities;AffectedEntityIds=affectedEntityIds;Diagnostics=diagnostics;
    }
    public VectorEditOutcome Outcome{get;}
    public IReadOnlyList<CadEntity> Entities{get;}
    public IReadOnlyList<Guid> AffectedEntityIds{get;init;}=[];
    public IReadOnlyList<VectorEditDiagnostic> Diagnostics{get;}
    public string? Error=>Diagnostics.FirstOrDefault()?.Message;
    public bool Success=>Outcome==VectorEditOutcome.Applied;
    public static VectorEditResult Applied(IReadOnlyList<CadEntity> entities,IEnumerable<Guid> affectedEntityIds,params VectorEditDiagnostic[] diagnostics)=>new(VectorEditOutcome.Applied,entities,affectedEntityIds.Distinct().ToArray(),diagnostics);
    public static VectorEditResult NoChange(string code,string message,IEnumerable<Guid>? affectedEntityIds=null)=>new(VectorEditOutcome.NoChange,[],affectedEntityIds?.Distinct().ToArray()??[],[new(code,message)]);
    public static VectorEditResult Fail(string message,string code="rejected")=>new(VectorEditOutcome.Rejected,[],[],[new(code,message)]);
}
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
        var boundaries=new[]{a}.Concat(hits).Append(b).ToArray();var remove=Enumerable.Range(0,boundaries.Length-1).OrderBy(i=>DistanceToSegment(click,boundaries[i],boundaries[i+1])).First();var inverse=line.Transform.Inverse();var result=new List<CadEntity>();if(remove>0)result.Add(Copy(line,inverse.Transform(a),inverse.Transform(boundaries[remove]),true));if(remove<boundaries.Length-2)result.Add(Copy(line,inverse.Transform(boundaries[remove+1]),inverse.Transform(b),result.Count==0));return result.Count==0?VectorEditResult.Fail("Trim would remove the complete target."):VectorEditResult.Applied(result,[line.Id]);
    }
    public VectorEditResult Chamfer(PolylineEntity path,int cornerIndex,double distanceMm)
    {
        if(!double.IsFinite(distanceMm)||distanceMm<=0||path.Points.Count<3)return VectorEditResult.Fail("Chamfer needs a positive finite distance and a path corner.");if(cornerIndex<0||cornerIndex>=path.Points.Count)return VectorEditResult.Fail("Corner index is outside the selected path.","invalid-corner");var world=path.Points.Select(path.Transform.Transform).ToList();var i=cornerIndex;if(!path.IsClosed&&(i==0||i==world.Count-1))return VectorEditResult.Fail("Choose an interior corner.");var prior=world[(i-1+world.Count)%world.Count];var corner=world[i];var next=world[(i+1)%world.Count];var priorLength=prior.DistanceTo(corner);var nextLength=next.DistanceTo(corner);if(priorLength<=GeometryTolerancePolicy.DefaultMinimumLengthMm||nextLength<=GeometryTolerancePolicy.DefaultMinimumLengthMm)return VectorEditResult.Fail("The selected corner has a collapsed adjacent segment.","degenerate-corner");if(distanceMm>=Math.Min(priorLength,nextLength)-GeometryTolerancePolicy.DefaultEndpointEqualityMm)return VectorEditResult.Fail("Chamfer distance exceeds an adjacent segment.");world[i]=Along(corner,prior,distanceMm);world.Insert(i+1,Along(corner,next,distanceMm));Matrix2D inverse;try{inverse=path.Transform.Inverse();}catch(InvalidOperationException){return VectorEditResult.Fail("The selected path has a non-invertible transform.","invalid-transform");}var replacement=new PolylineEntity{Id=path.Id,LayerId=path.LayerId,MaterialSheetId=path.MaterialSheetId,GroupId=path.GroupId,StrokeColor=path.StrokeColor,IsVisible=path.IsVisible,IsLocked=path.IsLocked,Transform=path.Transform,Points=world.Select(inverse.Transform).ToList(),IsClosed=path.IsClosed};return VectorEditResult.Applied([replacement],[path.Id]);
    }
    public VectorEditResult Fillet(PolylineEntity path,int cornerIndex,double radiusMm)
    {
        if(!double.IsFinite(radiusMm)||radiusMm<=0||path.Points.Count<3)return VectorEditResult.Fail("Fillet needs a positive finite radius and a path corner.");if(cornerIndex<0||cornerIndex>=path.Points.Count)return VectorEditResult.Fail("Corner index is outside the selected path.","invalid-corner");if(!path.IsClosed&&(cornerIndex==0||cornerIndex==path.Points.Count-1))return VectorEditResult.Fail("Choose an interior corner.");
        var world=path.Points.Select(path.Transform.Transform).ToArray();var prior=world[(cornerIndex-1+world.Length)%world.Length];var corner=world[cornerIndex];var next=world[(cornerIndex+1)%world.Length];var priorVector=prior-corner;var nextVector=next-corner;var priorLength=priorVector.Length;var nextLength=nextVector.Length;if(priorLength<=GeometryTolerancePolicy.DefaultMinimumLengthMm||nextLength<=GeometryTolerancePolicy.DefaultMinimumLengthMm)return VectorEditResult.Fail("The selected corner has a collapsed adjacent segment.","degenerate-corner");var priorDirection=new Vector2D(priorVector.X/priorLength,priorVector.Y/priorLength);var nextDirection=new Vector2D(nextVector.X/nextLength,nextVector.Y/nextLength);var dot=Math.Clamp(priorDirection.X*nextDirection.X+priorDirection.Y*nextDirection.Y,-1,1);var angle=Math.Acos(dot);if(angle<=1e-7||Math.PI-angle<=1e-7)return VectorEditResult.Fail("A fillet requires two non-collinear adjacent segments.","degenerate-corner");var tangentDistance=radiusMm/Math.Tan(angle/2);if(!double.IsFinite(tangentDistance)||tangentDistance>=Math.Min(priorLength,nextLength)-GeometryTolerancePolicy.DefaultEndpointEqualityMm)return VectorEditResult.Fail("The requested radius does not fit on the adjacent segments.","radius-too-large");var bisector=new Vector2D(priorDirection.X+nextDirection.X,priorDirection.Y+nextDirection.Y);var bisectorLength=bisector.Length;if(bisectorLength<=GeometryTolerancePolicy.DefaultMinimumLengthMm)return VectorEditResult.Fail("A fillet requires a finite corner angle.","degenerate-corner");var centerDistance=radiusMm/Math.Sin(angle/2);var center=new Point2D(corner.X+bisector.X/bisectorLength*centerDistance,corner.Y+bisector.Y/bisectorLength*centerDistance);var tangentStart=new Point2D(corner.X+priorDirection.X*tangentDistance,corner.Y+priorDirection.Y*tangentDistance);var tangentEnd=new Point2D(corner.X+nextDirection.X*tangentDistance,corner.Y+nextDirection.Y*tangentDistance);var startRadius=tangentStart-center;var endRadius=tangentEnd-center;var sweep=Math.Atan2(startRadius.X*endRadius.Y-startRadius.Y*endRadius.X,startRadius.X*endRadius.X+startRadius.Y*endRadius.Y)*180/Math.PI;if(Math.Abs(sweep)<=1e-7||Math.Abs(sweep)>=180-1e-7)return VectorEditResult.Fail("The selected corner cannot produce a stable circular fillet.","degenerate-corner");
        var figure=path.IsClosed?ClosedFilletFigure(world,cornerIndex,tangentStart,tangentEnd,center,sweep):OpenFilletFigure(world,cornerIndex,tangentStart,tangentEnd,center,sweep);var replacement=new VectorPathEntity{Id=path.Id,LayerId=path.LayerId,MaterialSheetId=path.MaterialSheetId,GroupId=path.GroupId,StrokeColor=path.StrokeColor,IsVisible=path.IsVisible,IsLocked=path.IsLocked,Transform=Matrix2D.Identity,Figures=[figure]};return VectorEditResult.Applied([replacement],[path.Id]);
    }
    private static VectorPathFigure OpenFilletFigure(IReadOnlyList<Point2D> points,int cornerIndex,Point2D tangentStart,Point2D tangentEnd,Point2D center,double sweep)
    {
        var figure=new VectorPathFigure{Start=points[0]};for(var index=1;index<cornerIndex;index++)figure.Segments.Add(new LinePathSegment(points[index]));figure.Segments.Add(new LinePathSegment(tangentStart));figure.Segments.Add(new ArcPathSegment(center,sweep,tangentEnd));for(var index=cornerIndex+1;index<points.Count;index++)figure.Segments.Add(new LinePathSegment(points[index]));return figure;
    }
    private static VectorPathFigure ClosedFilletFigure(IReadOnlyList<Point2D> points,int cornerIndex,Point2D tangentStart,Point2D tangentEnd,Point2D center,double sweep)
    {
        var figure=new VectorPathFigure{Start=tangentEnd,IsClosed=true};for(var offset=1;offset<points.Count;offset++)figure.Segments.Add(new LinePathSegment(points[(cornerIndex+offset)%points.Count]));figure.Segments.Add(new LinePathSegment(tangentStart));figure.Segments.Add(new ArcPathSegment(center,sweep,tangentEnd));return figure;
    }
    private static LineEntity Copy(LineEntity source,Point2D start,Point2D end,bool preserveId)=>new(){Id=preserveId?source.Id:Guid.NewGuid(),LayerId=source.LayerId,MaterialSheetId=source.MaterialSheetId,GroupId=source.GroupId,StrokeColor=source.StrokeColor,IsVisible=source.IsVisible,IsLocked=source.IsLocked,Transform=source.Transform,Start=start,End=end};
    private static Point2D Along(Point2D from,Point2D to,double distance){var d=to-from;var scale=distance/d.Length;return new(from.X+d.X*scale,from.Y+d.Y*scale);}
    private static double Parameter(Point2D a,Point2D b,Point2D p){var d=b-a;return Math.Abs(d.X)>=Math.Abs(d.Y)?(p.X-a.X)/d.X:(p.Y-a.Y)/d.Y;}
    private static Point2D? Intersection(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance){static double Cross(Vector2D u,Vector2D v)=>u.X*v.Y-u.Y*v.X;var r=b-a;var s=d-c;var den=Cross(r,s);if(Math.Abs(den)<=tolerance)return null;var t=Cross(c-a,s)/den;var u=Cross(c-a,r)/den;return t>=-tolerance&&t<=1+tolerance&&u>=-tolerance&&u<=1+tolerance?new(a.X+r.X*t,a.Y+r.Y*t):null;}
    private static double DistanceToSegment(Point2D p,Point2D a,Point2D b){var d=b-a;var lengthSquared=d.X*d.X+d.Y*d.Y;var q=p-a;var t=lengthSquared<1e-12?0:Math.Clamp((q.X*d.X+q.Y*d.Y)/lengthSquared,0,1);return p.DistanceTo(new(a.X+d.X*t,a.Y+d.Y*t));}
}
