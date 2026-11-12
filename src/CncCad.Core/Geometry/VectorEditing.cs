using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public enum RepairIssueKind { InvalidGeometry, DuplicateGeometry, TinySegment, CoincidentEndpoint, RedundantNode, OpenContour, SelfIntersection, Overlap }
public sealed record RepairIssue(RepairIssueKind Kind,string Message,IReadOnlyList<Guid> EntityIds,int Count=1);
public sealed record RepairOptions(double ToleranceMm=.01,double MinimumSegmentMm=.01,bool RemoveDuplicates=true,bool RemoveTinySegments=true,bool MergeEndpoints=true,bool RemoveRedundantNodes=true,bool CloseEndpointGaps=true);
public sealed record RepairAnalysis(IReadOnlyList<RepairIssue> Issues,IReadOnlyList<CadEntity> Replacements,IReadOnlyList<Guid> RemoveIds);
public interface IVectorRepairService{RepairAnalysis Analyze(IEnumerable<CadEntity> entities,RepairOptions options);}
public sealed class VectorRepairService:IVectorRepairService
{
    public RepairAnalysis Analyze(IEnumerable<CadEntity> entities,RepairOptions options)
    {
        if(!double.IsFinite(options.ToleranceMm)||options.ToleranceMm<=0||!double.IsFinite(options.MinimumSegmentMm)||options.MinimumSegmentMm<=0)throw new ArgumentException("Repair tolerances must be positive finite values.");var source=entities.ToArray();var issues=new List<RepairIssue>();var remove=new HashSet<Guid>();var replacements=new Dictionary<Guid,PolylineEntity>();var additional=new List<PolylineEntity>();var invalid=new HashSet<Guid>();
        foreach(var entity in source)try{if(EntityGeometry.WorldPoints(entity).Any(point=>!double.IsFinite(point.X)||!double.IsFinite(point.Y)))invalid.Add(entity.Id);}catch{invalid.Add(entity.Id);}foreach(var id in invalid)issues.Add(new(RepairIssueKind.InvalidGeometry,"Vector contains non-finite or unreadable geometry and was not changed.",[id]));
        for(var i=0;i<source.Length;i++)for(var j=i+1;j<source.Length;j++)if(!invalid.Contains(source[i].Id)&&!invalid.Contains(source[j].Id)&&Equivalent(source[i],source[j],options.ToleranceMm)){issues.Add(new(RepairIssueKind.DuplicateGeometry,"Duplicate vectors occupy the same location.",[source[i].Id,source[j].Id]));if(options.RemoveDuplicates)remove.Add(source[j].Id);}
        foreach(var path in source.OfType<PolylineEntity>().Where(path=>!invalid.Contains(path.Id)))
        {
            var points=path.Points.Select(path.Transform.Transform).ToList();var closed=path.IsClosed;var tiny=0;var redundant=0;
            if(options.CloseEndpointGaps&&!closed&&points.Count>=4&&points[0].DistanceTo(points[^1])<=options.ToleranceMm){points.RemoveAt(points.Count-1);closed=true;issues.Add(new(RepairIssueKind.CoincidentEndpoint,"Closed an open contour whose endpoints meet within tolerance.",[path.Id]));}
            if(options.RemoveTinySegments)tiny=RemoveTiny(points,closed,options.MinimumSegmentMm);
            if(options.RemoveRedundantNodes)redundant=RemoveRedundant(points,closed,options.ToleranceMm);
            if(tiny>0)issues.Add(new(RepairIssueKind.TinySegment,$"Removed {tiny} tiny segment node(s) using world-millimetre tolerance.",[path.Id],tiny));if(redundant>0)issues.Add(new(RepairIssueKind.RedundantNode,$"Removed {redundant} redundant node(s) using world-millimetre tolerance.",[path.Id],redundant));if(closed!=path.IsClosed||tiny+redundant>0){if(IsUsable(points,closed,options.ToleranceMm)){replacements[path.Id]=Replacement(path,points,closed);remove.Add(path.Id);}else issues.Add(new(RepairIssueKind.TinySegment,"Automatic cleanup was withheld because it would collapse the contour.",[path.Id]));}
            var candidate=replacements.GetValueOrDefault(path.Id)??path;if(!candidate.IsClosed)issues.Add(new(RepairIssueKind.OpenContour,"Open contour requires review before profiling.",[path.Id]));var world=EntityGeometry.WorldPoints(candidate);var selfIntersection=SelfIntersects(world,candidate.IsClosed,options.ToleranceMm);var internalOverlap=HasInternalOverlap(world,candidate.IsClosed,options.ToleranceMm);
            if(candidate.IsClosed&&(selfIntersection||internalOverlap)&&PolygonizeClosed(world,options.ToleranceMm) is{Count:>0} loops)
            {
                replacements[path.Id]=Replacement(path,loops[0],true);remove.Add(path.Id);for(var loopIndex=1;loopIndex<loops.Count;loopIndex++)additional.Add(Replacement(path,loops[loopIndex],true,StableRepairId(path.Id,loopIndex,loops[loopIndex])));
                if(selfIntersection)issues.Add(new(RepairIssueKind.SelfIntersection,$"Split the self-intersecting contour into {loops.Count} bounded simple contour(s).",[path.Id],loops.Count));
                if(internalOverlap)issues.Add(new(RepairIssueKind.Overlap,"Removed duplicate overlap edges while rebuilding bounded simple contours.",[path.Id]));
            }
            else
            {
                if(selfIntersection)issues.Add(new(RepairIssueKind.SelfIntersection,"Contour crosses itself; automatic repair is withheld because no bounded simple contour can be recovered safely.",[path.Id]));
                if(internalOverlap)issues.Add(new(RepairIssueKind.Overlap,"Contour contains a branched or ambiguous collinear overlap that requires a topology choice.",[path.Id]));
            }
        }
        if(options.MergeEndpoints)
        {
            var open=source.OfType<PolylineEntity>().Where(path=>!invalid.Contains(path.Id)&&!(replacements.GetValueOrDefault(path.Id)?.IsClosed??path.IsClosed)&&(replacements.GetValueOrDefault(path.Id)?.Points.Count??path.Points.Count)>=2&&(!remove.Contains(path.Id)||replacements.ContainsKey(path.Id))).ToArray();var matches=new List<(double Distance,Guid First,int FirstIndex,Guid Second,int SecondIndex)>();
            for(var i=0;i<open.Length;i++)for(var j=i+1;j<open.Length;j++)
            {
                var first=replacements.GetValueOrDefault(open[i].Id)??open[i];var second=replacements.GetValueOrDefault(open[j].Id)??open[j];var firstEnds=new[]{0,first.Points.Count-1};var secondEnds=new[]{0,second.Points.Count-1};foreach(var firstIndex in firstEnds)foreach(var secondIndex in secondEnds)
                {
                    var a=first.Transform.Transform(first.Points[firstIndex]);var b=second.Transform.Transform(second.Points[secondIndex]);var distance=a.DistanceTo(b);if(distance>0&&distance<=options.ToleranceMm)matches.Add((distance,first.Id,firstIndex,second.Id,secondIndex));
                }
            }
            var used=new HashSet<(Guid,int)>();foreach(var match in matches.OrderBy(item=>item.Distance).ThenBy(item=>item.First).ThenBy(item=>item.Second)){if(!used.Add((match.First,match.FirstIndex))||!used.Add((match.Second,match.SecondIndex)))continue;var first=Current(match.First);var second=Current(match.Second);var a=first.Transform.Transform(first.Points[match.FirstIndex]);var b=second.Transform.Transform(second.Points[match.SecondIndex]);var midpoint=new Point2D((a.X+b.X)/2,(a.Y+b.Y)/2);var firstWorld=first.Points.Select(first.Transform.Transform).ToList();var secondWorld=second.Points.Select(second.Transform.Transform).ToList();firstWorld[match.FirstIndex]=midpoint;secondWorld[match.SecondIndex]=midpoint;replacements[first.Id]=Replacement(first,firstWorld,false);replacements[second.Id]=Replacement(second,secondWorld,false);remove.Add(first.Id);remove.Add(second.Id);issues.Add(new(RepairIssueKind.CoincidentEndpoint,$"Merged endpoints separated by {match.Distance:0.#####} mm.",[first.Id,second.Id]));}
        }
        var remaining=source.Where(item=>!remove.Contains(item.Id)&&!invalid.Contains(item.Id)).ToArray();var grouped=new HashSet<Guid>();var duplicateProtected=new HashSet<Guid>();for(var first=0;first<remaining.Length;first++)for(var second=first+1;second<remaining.Length;second++)if(Equivalent(remaining[first],remaining[second],options.ToleranceMm)){duplicateProtected.Add(remaining[first].Id);duplicateProtected.Add(remaining[second].Id);}
        foreach(var seed in remaining)
        {
            if(grouped.Contains(seed.Id)||duplicateProtected.Contains(seed.Id)||!TrySingleSegment(seed,out _,out _))continue;var merge=MergeOverlappingSingleSegments(seed,remaining.Where(candidate=>!grouped.Contains(candidate.Id)&&!duplicateProtected.Contains(candidate.Id)).ToArray(),options.ToleranceMm);if(merge.Members.Count<2)continue;replacements[seed.Id]=merge.Entity;foreach(var member in merge.Members){remove.Add(member);grouped.Add(member);}issues.Add(new(RepairIssueKind.Overlap,$"Merged {merge.Members.Count} overlapping collinear segments into one deterministic span.",merge.Members,merge.Members.Count));
        }
        var unresolved=source.Where(item=>!remove.Contains(item.Id)).ToArray();for(var i=0;i<unresolved.Length;i++)for(var j=i+1;j<unresolved.Length;j++)if(!Equivalent(unresolved[i],unresolved[j],options.ToleranceMm)&&HasOverlap(EntityGeometry.WorldPoints(unresolved[i]),IsClosed(unresolved[i]),EntityGeometry.WorldPoints(unresolved[j]),IsClosed(unresolved[j]),options.ToleranceMm))issues.Add(new(RepairIssueKind.Overlap,"Vectors contain a branched or ambiguous overlap that requires a topology choice.",[unresolved[i].Id,unresolved[j].Id]));
        return new(issues,replacements.Values.Cast<CadEntity>().Concat(additional).OrderBy(entity=>entity.Id).ToArray(),remove.Order().ToArray());
        PolylineEntity Current(Guid id)=>replacements.GetValueOrDefault(id)??source.OfType<PolylineEntity>().Single(path=>path.Id==id);
    }
    private static PolylineEntity Replacement(PolylineEntity path,IEnumerable<Point2D> worldPoints,bool closed,Guid? id=null)=>new(){Id=id??path.Id,LayerId=path.LayerId,MaterialSheetId=path.MaterialSheetId,GroupId=path.GroupId,StrokeColor=path.StrokeColor,IsVisible=path.IsVisible,IsLocked=path.IsLocked,Points=[..worldPoints],IsClosed=closed,Transform=Matrix2D.Identity};
    private static int RemoveTiny(List<Point2D> points,bool closed,double minimum){var removed=0;for(var changed=true;changed&&points.Count>(closed?3:2);){changed=false;var count=closed?points.Count:points.Count-1;for(var i=count-1;i>=0;i--){var next=(i+1)%points.Count;if(points[i].DistanceTo(points[next])>=minimum)continue;var remove=closed?next:i+1;if(!closed&&(remove==0||remove==points.Count-1))continue;points.RemoveAt(remove);removed++;changed=true;break;}}return removed;}
    private static int RemoveRedundant(List<Point2D> points,bool closed,double tolerance){var removed=0;for(var changed=true;changed&&points.Count>(closed?3:2);){changed=false;var start=closed?0:1;var end=closed?points.Count:points.Count-1;for(var i=end-1;i>=start;i--){var previous=(i-1+points.Count)%points.Count;var next=(i+1)%points.Count;if(DistanceToLine(points[i],points[previous],points[next])>tolerance||!Between(points[i],points[previous],points[next],tolerance))continue;points.RemoveAt(i);removed++;changed=true;break;}}return removed;}
    private static bool Between(Point2D point,Point2D a,Point2D b,double tolerance)=>point.X>=Math.Min(a.X,b.X)-tolerance&&point.X<=Math.Max(a.X,b.X)+tolerance&&point.Y>=Math.Min(a.Y,b.Y)-tolerance&&point.Y<=Math.Max(a.Y,b.Y)+tolerance;
    private static bool IsUsable(IReadOnlyList<Point2D> points,bool closed,double tolerance)=>points.Count>=(closed?3:2)&&points.All(point=>double.IsFinite(point.X)&&double.IsFinite(point.Y))&&(!closed||Math.Abs(Area(points))>tolerance*tolerance);
    private static double Area(IReadOnlyList<Point2D> points){var area=0d;for(var i=0;i<points.Count;i++){var next=points[(i+1)%points.Count];area+=points[i].X*next.Y-next.X*points[i].Y;}return area/2;}
    private static bool IsClosed(CadEntity entity)=>entity is PolylineEntity{IsClosed:true} or RectangleEntity or CircleEntity or EllipseEntity||entity is VectorPathEntity path&&path.Figures.All(figure=>figure.IsClosed);
    private static bool Equivalent(CadEntity a,CadEntity b,double tolerance){var ap=EntityGeometry.WorldPoints(a);var bp=EntityGeometry.WorldPoints(b);if(ap.Count!=bp.Count||IsClosed(a)!=IsClosed(b))return false;if(!IsClosed(a))return Sequence(ap,bp,tolerance)||Sequence(ap,bp.Reverse().ToArray(),tolerance);for(var offset=0;offset<bp.Count;offset++){if(Cyclic(ap,bp,offset,1,tolerance)||Cyclic(ap,bp,offset,-1,tolerance))return true;}return false;}
    private static bool Sequence(IReadOnlyList<Point2D> a,IReadOnlyList<Point2D> b,double tolerance)=>a.Zip(b).All(pair=>pair.First.DistanceTo(pair.Second)<=tolerance);
    private static bool Cyclic(IReadOnlyList<Point2D> a,IReadOnlyList<Point2D> b,int offset,int direction,double tolerance){for(var i=0;i<a.Count;i++){var index=(offset+direction*i)%b.Count;if(index<0)index+=b.Count;if(a[i].DistanceTo(b[index])>tolerance)return false;}return true;}
    private static double DistanceToLine(Point2D p,Point2D a,Point2D b){var d=b-a;if(d.Length<1e-12)return p.DistanceTo(a);return Math.Abs(d.X*(a.Y-p.Y)-(a.X-p.X)*d.Y)/d.Length;}
    private static bool SelfIntersects(IReadOnlyList<Point2D> p,bool closed,double tolerance){var count=closed?p.Count:p.Count-1;for(var i=0;i<count;i++)for(var j=i+2;j<count;j++){if(closed&&i==0&&j==count-1)continue;if(Intersect(p[i],p[(i+1)%p.Count],p[j],p[(j+1)%p.Count],tolerance))return true;}return false;}
    private static bool Intersect(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance){static double Cross(Vector2D x,Vector2D y)=>x.X*y.Y-x.Y*y.X;var r=b-a;var s=d-c;var den=Cross(r,s);if(Math.Abs(den)<=tolerance)return false;var t=Cross(c-a,s)/den;var u=Cross(c-a,r)/den;return t>tolerance&&t<1-tolerance&&u>tolerance&&u<1-tolerance;}
    private static bool HasInternalOverlap(IReadOnlyList<Point2D> points,bool closed,double tolerance){var count=closed?points.Count:points.Count-1;for(var i=0;i<count;i++)for(var j=i+1;j<count;j++){if(j==i+1||closed&&i==0&&j==count-1)continue;if(SegmentsOverlap(points[i],points[(i+1)%points.Count],points[j],points[(j+1)%points.Count],tolerance))return true;}return false;}
    private static bool HasOverlap(IReadOnlyList<Point2D> a,bool aClosed,IReadOnlyList<Point2D> b,bool bClosed,double tolerance){var ac=aClosed?a.Count:a.Count-1;var bc=bClosed?b.Count:b.Count-1;for(var i=0;i<ac;i++)for(var j=0;j<bc;j++)if(SegmentsOverlap(a[i],a[(i+1)%a.Count],b[j],b[(j+1)%b.Count],tolerance))return true;return false;}
    private static bool SegmentsOverlap(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance){var ab=b-a;var length=ab.Length;if(length<=tolerance||DistanceToLine(c,a,b)>tolerance||DistanceToLine(d,a,b)>tolerance)return false;var ux=ab.X/length;var uy=ab.Y/length;var cProjection=(c.X-a.X)*ux+(c.Y-a.Y)*uy;var dProjection=(d.X-a.X)*ux+(d.Y-a.Y)*uy;var overlap=Math.Min(length,Math.Max(cProjection,dProjection))-Math.Max(0,Math.Min(cProjection,dProjection));return overlap>tolerance;}
    private static IReadOnlyList<IReadOnlyList<Point2D>> PolygonizeClosed(IReadOnlyList<Point2D> source,double tolerance)
    {
        var points=source.ToList();if(points.Count>1&&points[0].DistanceTo(points[^1])<=tolerance)points.RemoveAt(points.Count-1);if(points.Count<3)return[];var segmentCount=points.Count;var splits=Enumerable.Range(0,segmentCount).Select(_=>new List<double>{0,1}).ToArray();
        for(var first=0;first<segmentCount;first++)for(var second=first+1;second<segmentCount;second++)AddIntersections(points[first],points[(first+1)%segmentCount],points[second],points[(second+1)%segmentCount],splits[first],splits[second],tolerance);
        var nodes=new List<Point2D>();var edges=new HashSet<(int First,int Second)>();
        for(var segment=0;segment<segmentCount;segment++)
        {
            var start=points[segment];var end=points[(segment+1)%segmentCount];var length=start.DistanceTo(end);var parameterTolerance=length<=tolerance?1:tolerance/length;var values=splits[segment].Select(value=>Math.Clamp(value,0,1)).Order().Aggregate(new List<double>(),(result,value)=>{if(result.Count==0||value-result[^1]>parameterTolerance)result.Add(value);return result;});
            for(var index=0;index+1<values.Count;index++){var firstPoint=At(start,end,values[index]);var secondPoint=At(start,end,values[index+1]);if(firstPoint.DistanceTo(secondPoint)<=tolerance)continue;var firstNode=FindNode(nodes,firstPoint,tolerance);var secondNode=FindNode(nodes,secondPoint,tolerance);if(firstNode==secondNode)continue;edges.Add(firstNode<secondNode?(firstNode,secondNode):(secondNode,firstNode));}
        }
        var adjacency=Enumerable.Range(0,nodes.Count).ToDictionary(index=>index,_=>new List<int>());foreach(var edge in edges){adjacency[edge.First].Add(edge.Second);adjacency[edge.Second].Add(edge.First);}foreach(var pair in adjacency)pair.Value.Sort((first,second)=>Math.Atan2(nodes[first].Y-nodes[pair.Key].Y,nodes[first].X-nodes[pair.Key].X).CompareTo(Math.Atan2(nodes[second].Y-nodes[pair.Key].Y,nodes[second].X-nodes[pair.Key].X)));
        var visited=new HashSet<(int From,int To)>();var loops=new List<IReadOnlyList<Point2D>>();var keys=new HashSet<string>(StringComparer.Ordinal);
        foreach(var edge in edges)foreach(var directed in new[]{(edge.First,edge.Second),(edge.Second,edge.First)})
        {
            if(visited.Contains(directed))continue;var current=directed;var indices=new List<int>();var valid=true;
            for(var guard=0;guard<edges.Count*2+2;guard++)
            {
                if(!visited.Add(current)){valid=current==directed;break;}indices.Add(current.Item1);var neighbors=adjacency[current.Item2];var reverseIndex=neighbors.IndexOf(current.Item1);if(reverseIndex<0||neighbors.Count<2){valid=false;break;}var next=neighbors[(reverseIndex-1+neighbors.Count)%neighbors.Count];current=(current.Item2,next);if(current==directed)break;if(guard==edges.Count*2+1)valid=false;
            }
            if(!valid||current!=directed||indices.Count<3)continue;var loop=indices.Select(index=>nodes[index]).ToList();RemoveRedundant(loop,true,tolerance);if(loop.Count<3||Area(loop)<=tolerance*tolerance)continue;var key=CanonicalLoopKey(loop,tolerance);if(keys.Add(key))loops.Add(loop);
        }
        return loops.OrderByDescending(loop=>Math.Abs(Area(loop))).ThenBy(loop=>loop.Min(point=>point.X)).ThenBy(loop=>loop.Min(point=>point.Y)).ToArray();
    }
    private static void AddIntersections(Point2D a,Point2D b,Point2D c,Point2D d,List<double> first,List<double> second,double tolerance)
    {
        static double Cross(Vector2D first,Vector2D second)=>first.X*second.Y-first.Y*second.X;var ab=b-a;var cd=d-c;var denominator=Cross(ab,cd);var scale=Math.Max(1,ab.Length*cd.Length);
        if(Math.Abs(denominator)>tolerance*scale)
        {
            var t=Cross(c-a,cd)/denominator;var u=Cross(c-a,ab)/denominator;if(t>=-tolerance&&t<=1+tolerance&&u>=-tolerance&&u<=1+tolerance){first.Add(Math.Clamp(t,0,1));second.Add(Math.Clamp(u,0,1));}return;
        }
        if(DistanceToLine(c,a,b)>tolerance||DistanceToLine(d,a,b)>tolerance)return;AddProjection(c,a,b,first,tolerance);AddProjection(d,a,b,first,tolerance);AddProjection(a,c,d,second,tolerance);AddProjection(b,c,d,second,tolerance);
    }
    private static void AddProjection(Point2D point,Point2D start,Point2D end,List<double> values,double tolerance){var segment=end-start;var squared=segment.X*segment.X+segment.Y*segment.Y;if(squared<=tolerance*tolerance)return;var offset=point-start;var value=(offset.X*segment.X+offset.Y*segment.Y)/squared;if(value>=-tolerance&&value<=1+tolerance)values.Add(Math.Clamp(value,0,1));}
    private static int FindNode(List<Point2D> nodes,Point2D point,double tolerance){for(var index=0;index<nodes.Count;index++)if(nodes[index].DistanceTo(point)<=tolerance)return index;nodes.Add(point);return nodes.Count-1;}
    private static Point2D At(Point2D start,Point2D end,double value)=>new(start.X+(end.X-start.X)*value,start.Y+(end.Y-start.Y)*value);
    private static string CanonicalLoopKey(IReadOnlyList<Point2D> points,double tolerance)
    {
        var values=points.Select(point=>(X:(long)Math.Round(point.X/tolerance),Y:(long)Math.Round(point.Y/tolerance))).ToArray();var best=Enumerable.Range(0,values.Length).Select(offset=>string.Join(";",Enumerable.Range(0,values.Length).Select(index=>values[(offset+index)%values.Length]).Select(point=>$"{point.X},{point.Y}"))).Min(StringComparer.Ordinal);return best??string.Empty;
    }
    private static Guid StableRepairId(Guid sourceId,int index,IReadOnlyList<Point2D> points){var key=$"{sourceId:N}|{index}|{string.Join(";",points.Select(point=>$"{point.X:R},{point.Y:R}"))}";var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));return new Guid(hash.AsSpan(0,16));}
    private sealed record SegmentMerge(PolylineEntity Entity,IReadOnlyList<Guid> Members);
    private static SegmentMerge MergeOverlappingSingleSegments(CadEntity seed,IReadOnlyList<CadEntity> candidates,double tolerance)
    {
        TrySingleSegment(seed,out var seedStart,out var seedEnd);var direction=seedEnd-seedStart;var length=direction.Length;if(length<=tolerance)return new(new(){Id=seed.Id},[seed.Id]);var unit=new Vector2D(direction.X/length,direction.Y/length);var minimum=0d;var maximum=length;var members=new List<Guid>{seed.Id};var changed=true;
        while(changed)
        {
            changed=false;foreach(var candidate in candidates)
            {
                if(members.Contains(candidate.Id)||Equivalent(seed,candidate,tolerance)||!TrySingleSegment(candidate,out var start,out var end)||DistanceToLine(start,seedStart,seedEnd)>tolerance||DistanceToLine(end,seedStart,seedEnd)>tolerance)continue;var first=(start.X-seedStart.X)*unit.X+(start.Y-seedStart.Y)*unit.Y;var second=(end.X-seedStart.X)*unit.X+(end.Y-seedStart.Y)*unit.Y;var low=Math.Min(first,second);var high=Math.Max(first,second);if(Math.Min(maximum,high)-Math.Max(minimum,low)<=tolerance)continue;minimum=Math.Min(minimum,low);maximum=Math.Max(maximum,high);members.Add(candidate.Id);changed=true;
            }
        }
        var mergedStart=new Point2D(seedStart.X+unit.X*minimum,seedStart.Y+unit.Y*minimum);var mergedEnd=new Point2D(seedStart.X+unit.X*maximum,seedStart.Y+unit.Y*maximum);var entity=new PolylineEntity{Id=seed.Id,LayerId=seed.LayerId,MaterialSheetId=seed.MaterialSheetId,GroupId=seed.GroupId,StrokeColor=seed.StrokeColor,IsVisible=seed.IsVisible,IsLocked=seed.IsLocked,Points=[mergedStart,mergedEnd],Transform=Matrix2D.Identity};return new(entity,members);
    }
    private static bool TrySingleSegment(CadEntity entity,out Point2D start,out Point2D end)
    {
        switch(entity){case LineEntity line:start=line.Transform.Transform(line.Start);end=line.Transform.Transform(line.End);return true;case PolylineEntity{IsClosed:false,Points.Count:2} path:start=path.Transform.Transform(path.Points[0]);end=path.Transform.Transform(path.Points[1]);return true;default:start=default;end=default;return false;}
    }
}

public static class ParametricShapes
{
    private const int MaximumSides=10000;
    public static PolylineEntity Polygon(Point2D center,double radius,int sides,Guid layer){if(!Finite(center)||!double.IsFinite(radius)||sides<3||sides>MaximumSides||radius<=0)throw new ArgumentException($"Polygon needs finite coordinates, a positive radius, and 3–{MaximumSides} sides.");return new(){LayerId=layer,Points=Enumerable.Range(0,sides).Select(i=>new Point2D(center.X+radius*Math.Cos(-Math.PI/2+i*Math.PI*2/sides),center.Y+radius*Math.Sin(-Math.PI/2+i*Math.PI*2/sides))).ToList(),IsClosed=true};}
    public static PolylineEntity Star(Point2D center,double outer,double inner,int points,Guid layer){if(!Finite(center)||!double.IsFinite(outer)||!double.IsFinite(inner)||points<3||points>MaximumSides||inner<=0||outer<=inner)throw new ArgumentException($"Star needs finite coordinates, valid radii, and 3–{MaximumSides} points.");return new(){LayerId=layer,Points=Enumerable.Range(0,points*2).Select(i=>{var r=i%2==0?outer:inner;var a=-Math.PI/2+i*Math.PI/points;return new Point2D(center.X+r*Math.Cos(a),center.Y+r*Math.Sin(a));}).ToList(),IsClosed=true};}
    public static PolylineEntity RoundedRectangle(Point2D origin,double width,double height,double radius,Guid layer)=>Rounded(origin,width,height,radius,layer);
    public static PolylineEntity Slot(Point2D center,double length,double width,Guid layer)=>Rounded(new(center.X-length/2,center.Y-width/2),length,width,width/2,layer);
    private static PolylineEntity Rounded(Point2D o,double w,double h,double r,Guid layer){if(!Finite(o)||!double.IsFinite(w)||!double.IsFinite(h)||!double.IsFinite(r)||w<=0||h<=0||r<0)throw new ArgumentException("Shape coordinates and dimensions must be finite; width and height must be positive and radius non-negative.");r=Math.Clamp(r,0,Math.Min(w,h)/2);if(r<=GeometryTolerancePolicy.DefaultMinimumLengthMm)return new(){LayerId=layer,Points=[o,new(o.X+w,o.Y),new(o.X+w,o.Y+h),new(o.X,o.Y+h)],IsClosed=true};var points=new List<Point2D>();foreach(var (c,start) in new[]{(new Point2D(o.X+w-r,o.Y+r),-90d),(new Point2D(o.X+w-r,o.Y+h-r),0d),(new Point2D(o.X+r,o.Y+h-r),90d),(new Point2D(o.X+r,o.Y+r),180d)})for(var i=0;i<=8;i++){var a=(start+i*90d/8)*Math.PI/180;points.Add(new(c.X+r*Math.Cos(a),c.Y+r*Math.Sin(a)));}return new(){LayerId=layer,Points=points,IsClosed=true};}
    private static bool Finite(Point2D point)=>double.IsFinite(point.X)&&double.IsFinite(point.Y);
}
