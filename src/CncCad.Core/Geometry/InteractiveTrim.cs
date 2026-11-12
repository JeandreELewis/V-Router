using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public sealed record TrimCandidate(Guid TargetId,IReadOnlyList<Point2D> RemovePath,IReadOnlyList<Point2D> Boundaries,IReadOnlyList<CadEntity> Replacements,string? Error=null)
{
    public bool Success=>Error is null&&RemovePath.Count>=2;
    public static TrimCandidate Invalid(Guid id,string error)=>new(id,[],[],[],error);
}

public interface IInteractiveTrimService
{
    TrimCandidate FindCandidate(CadEntity target,IEnumerable<CadEntity> cutters,Point2D click,double toleranceMm);
}

public sealed class InteractiveTrimService:IInteractiveTrimService
{
    private sealed record FlatPath(List<Point2D> Points,bool Closed);
    public TrimCandidate FindCandidate(CadEntity target,IEnumerable<CadEntity> cutters,Point2D click,double toleranceMm)
    {
        if(!double.IsFinite(toleranceMm)||toleranceMm<=0||!double.IsFinite(click.X)||!double.IsFinite(click.Y))return TrimCandidate.Invalid(target.Id,"Trim requires a finite click position and a positive finite tolerance.");
        var targetPaths=Paths(target,toleranceMm);var ranked=targetPaths.Where(x=>x.Points.Count>=2).Select(x=>(Path:x,Distance:DistanceToPath(click,x.Points))).OrderBy(x=>x.Distance).ToArray();var selected=ranked.FirstOrDefault().Path;if(selected is null)return TrimCandidate.Invalid(target.Id,"The target has no trimmable edge.");if(ranked[0].Distance>toleranceMm)return TrimCandidate.Invalid(target.Id,"Click directly on the segment to trim it.");if(ranked.Length>1&&ranked[0].Distance<=toleranceMm&&Math.Abs(ranked[0].Distance-ranked[1].Distance)<=Math.Max(GeometryTolerancePolicy.DefaultEndpointEqualityMm,toleranceMm*.01))return TrimCandidate.Invalid(target.Id,"Move away from the corner to choose one edge.");var closed=selected.Closed;var path=selected.Points;
        if(closed&&path[^1].DistanceTo(path[0])>toleranceMm)path.Add(path[0]);
        var cumulative=Cumulative(path);var total=cumulative[^1];if(total<=toleranceMm)return TrimCandidate.Invalid(target.Id,"The target is too small to trim.");
        var clickDistance=NearestDistance(path,cumulative,click);var hits=new List<(double Distance,Point2D Point)>();var overlap=false;
        foreach(var cutterPathInfo in cutters.Where(x=>x.Id!=target.Id).SelectMany(x=>Paths(x,toleranceMm)))
        {
            var cutterPath=cutterPathInfo.Points;if(cutterPathInfo.Closed&&cutterPath.Count>2&&cutterPath[^1].DistanceTo(cutterPath[0])>toleranceMm)cutterPath.Add(cutterPath[0]);
            for(var i=1;i<path.Count;i++)for(var j=1;j<cutterPath.Count;j++)
            {
                var result=Intersect(path[i-1],path[i],cutterPath[j-1],cutterPath[j],toleranceMm);overlap|=result.Overlap;
                if(result.Point is{} point){var distance=cumulative[i-1]+path[i-1].DistanceTo(point);if(closed){if(distance<=toleranceMm||distance>=total-toleranceMm)distance=0;hits.Add((distance,point));}else if(distance>toleranceMm&&distance<total-toleranceMm)hits.Add((distance,point));}
            }
        }
        if(overlap)return TrimCandidate.Invalid(target.Id,"Coincident or overlapping vectors cannot be trimmed unambiguously.");
        hits=DistinctHits(hits,toleranceMm);
        if(closed&&hits.Count<2)return TrimCandidate.Invalid(target.Id,"A closed vector needs two crossing points.");
        if(!closed&&hits.Count==0)return TrimCandidate.Invalid(target.Id,"Move over a portion bounded by a crossing.");
        double start,end;
        if(closed)
        {
            var before=hits.LastOrDefault(x=>x.Distance<clickDistance);var after=hits.FirstOrDefault(x=>x.Distance>clickDistance);
            start=before==default?hits[^1].Distance:before.Distance;end=after==default?hits[0].Distance+total:after.Distance;
        }
        else
        {
            var boundaries=new[]{0d}.Concat(hits.Select(x=>x.Distance)).Append(total).ToArray();var interval=Enumerable.Range(0,boundaries.Length-1).First(i=>clickDistance>=boundaries[i]-toleranceMm&&clickDistance<=boundaries[i+1]+toleranceMm);start=boundaries[interval];end=boundaries[interval+1];
        }
        var remove=Slice(path,cumulative,start,end,closed);if(Length(remove)<=toleranceMm)return TrimCandidate.Invalid(target.Id,"The selected portion is too small to trim.");
        var replacements=new List<CadEntity>();var preserveIdentity=true;
        void AddReplacement(IReadOnlyList<Point2D> points,bool isClosed){if(Length(points)<=toleranceMm)return;replacements.Add(CreateFragment(target,points,isClosed,preserveIdentity));preserveIdentity=false;}
        foreach(var untouched in targetPaths.Where(x=>!ReferenceEquals(x,selected)))AddReplacement(untouched.Points,untouched.Closed);
        if(closed)
        {
            var keep=Slice(path,cumulative,end,start+total,true);AddReplacement(keep,false);
        }
        else
        {
            var left=Slice(path,cumulative,0,start,false);var right=Slice(path,cumulative,end,total,false);AddReplacement(left,false);AddReplacement(right,false);
        }
        if(replacements.Count==0)return TrimCandidate.Invalid(target.Id,"Trimming would remove the complete vector.");
        var boundaryPoints=new[]{remove[0],remove[^1]}.Distinct().ToArray();return new(target.Id,remove,boundaryPoints,replacements);
    }
    private static List<FlatPath> Paths(CadEntity entity,double tolerance)
    {
        var curveTolerance=Math.Max(tolerance/4,.002);
        return entity switch
        {
            RectangleEntity rectangle=>EdgePaths(EntityGeometry.WorldPoints(rectangle),true),
            PolylineEntity polyline=>EdgePaths(EntityGeometry.WorldPoints(polyline),polyline.IsClosed),
            VectorPathEntity vector=>FigureEdgePaths(vector.Figures,vector.Transform,curveTolerance),
            TextEntity text=>FigureEdgePaths(text.Outlines,text.Transform,curveTolerance),
            CircleEntity or EllipseEntity=>[new([..EntityGeometry.WorldPoints(entity)],true)],
            _=>[new([..EntityGeometry.WorldPoints(entity)],false)]
        };
    }
    private static List<FlatPath> EdgePaths(IReadOnlyList<Point2D> points,bool closed)
    {
        var output=new List<FlatPath>();for(var i=1;i<points.Count;i++)if(points[i-1].DistanceTo(points[i])>GeometryTolerancePolicy.DefaultEndpointEqualityMm)output.Add(new([points[i-1],points[i]],false));if(closed&&points.Count>2&&points[^1].DistanceTo(points[0])>GeometryTolerancePolicy.DefaultEndpointEqualityMm)output.Add(new([points[^1],points[0]],false));return output;
    }
    private static List<FlatPath> FigureEdgePaths(IEnumerable<VectorPathFigure> figures,Matrix2D transform,double tolerance)
    {
        var output=new List<FlatPath>();foreach(var figure in figures){var current=figure.Start;foreach(var segment in figure.Segments){var edge=new VectorPathFigure{Start=current,Segments=[segment]};var points=VectorPathGeometry.Flatten(VectorPathGeometry.Transform([edge],transform),tolerance);if(points.Count>=2)output.Add(new([..points],false));current=segment.End;}if(figure.IsClosed&&current.DistanceTo(figure.Start)>GeometryTolerancePolicy.DefaultEndpointEqualityMm)output.Add(new([transform.Transform(current),transform.Transform(figure.Start)],false));}return output;
    }
    private static double[] Cumulative(IReadOnlyList<Point2D> p){var c=new double[p.Count];for(var i=1;i<p.Count;i++)c[i]=c[i-1]+p[i-1].DistanceTo(p[i]);return c;}
    private static double NearestDistance(IReadOnlyList<Point2D> p,IReadOnlyList<double> c,Point2D click){var best=double.MaxValue;var result=0d;for(var i=1;i<p.Count;i++){var d=p[i]-p[i-1];var l=d.X*d.X+d.Y*d.Y;var t=l<1e-12?0:Math.Clamp(((click.X-p[i-1].X)*d.X+(click.Y-p[i-1].Y)*d.Y)/l,0,1);var q=new Point2D(p[i-1].X+d.X*t,p[i-1].Y+d.Y*t);var distance=q.DistanceTo(click);if(distance<best){best=distance;result=c[i-1]+p[i-1].DistanceTo(q);}}return result;}
    private static List<Point2D> Slice(IReadOnlyList<Point2D> path,IReadOnlyList<double> c,double start,double end,bool wrap){var total=c[^1];if(wrap){while(start>=total)start-=total;while(end<=start)end+=total;}var output=new List<Point2D>{At(path,c,wrap?start%total:start)};for(var lap=(int)Math.Floor(start/total);lap<=(int)Math.Floor(end/total);lap++)for(var i=1;i<c.Count-1;i++){var d=c[i]+lap*total;if(d>start&&d<end)output.Add(path[i]);}output.Add(At(path,c,wrap?end%total:end));for(var i=output.Count-1;i>0;i--)if(output[i].DistanceTo(output[i-1])<GeometryTolerancePolicy.DefaultEndpointEqualityMm)output.RemoveAt(i);return output;}
    private static Point2D At(IReadOnlyList<Point2D> p,IReadOnlyList<double> c,double distance){if(distance<=0)return p[0];if(distance>=c[^1])return p[^1];var i=1;while(c[i]<distance)i++;var span=c[i]-c[i-1];var t=span<1e-12?0:(distance-c[i-1])/span;return new(p[i-1].X+(p[i].X-p[i-1].X)*t,p[i-1].Y+(p[i].Y-p[i-1].Y)*t);}
    private static CadEntity CreateFragment(CadEntity source,IReadOnlyList<Point2D> points,bool closed,bool preserveIdentity){CadEntity result=source is LineEntity&&points.Count==2?new LineEntity{Id=preserveIdentity?source.Id:Guid.NewGuid(),Start=points[0],End=points[1]}:new PolylineEntity{Id=preserveIdentity?source.Id:Guid.NewGuid(),Points=[..points],IsClosed=closed};result.LayerId=source.LayerId;result.MaterialSheetId=source.MaterialSheetId;result.GroupId=source.GroupId;result.StrokeColor=source.StrokeColor;result.IsVisible=source.IsVisible;result.IsLocked=source.IsLocked;return result;}
    private static double Length(IReadOnlyList<Point2D> p)=>p.Zip(p.Skip(1)).Sum(x=>x.First.DistanceTo(x.Second));
    private static double DistanceToPath(Point2D point,IReadOnlyList<Point2D> path){var best=double.MaxValue;for(var i=1;i<path.Count;i++){var d=path[i]-path[i-1];var l=d.X*d.X+d.Y*d.Y;var t=l<1e-12?0:Math.Clamp(((point.X-path[i-1].X)*d.X+(point.Y-path[i-1].Y)*d.Y)/l,0,1);best=Math.Min(best,point.DistanceTo(new(path[i-1].X+d.X*t,path[i-1].Y+d.Y*t)));}return best;}
    private static List<(double Distance,Point2D Point)> DistinctHits(IEnumerable<(double Distance,Point2D Point)> hits,double tolerance)
    {
        var result=new List<(double Distance,Point2D Point)>();
        foreach(var hit in hits.OrderBy(value=>value.Distance))if(result.Count==0||hit.Distance-result[^1].Distance>tolerance)result.Add(hit);
        return result;
    }
    private static (Point2D? Point,bool Overlap) Intersect(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance){static double Cross(Vector2D x,Vector2D y)=>x.X*y.Y-x.Y*y.X;var r=b-a;var s=d-c;var den=Cross(r,s);var scale=Math.Max(1,r.Length+s.Length);if(Math.Abs(den)<=tolerance*scale)return(null,Math.Abs(Cross(c-a,r))<=tolerance*Math.Max(1,r.Length));var t=Cross(c-a,s)/den;var u=Cross(c-a,r)/den;var parameterTolerance=tolerance/Math.Max(1,Math.Min(r.Length,s.Length));return t>=-parameterTolerance&&t<=1+parameterTolerance&&u>=-parameterTolerance&&u<=1+parameterTolerance?(new(a.X+r.X*t,a.Y+r.Y*t),false):(null,false);}
}
