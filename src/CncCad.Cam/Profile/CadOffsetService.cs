using Clipper2Lib;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public enum CadOffsetJoin { Round, Miter, Bevel }
public sealed record CadOffsetResult(IReadOnlyList<CadEntity> Entities, string? Error = null) { public bool Success => Error is null && Entities.Count > 0; }
public interface ICadOffsetService { CadOffsetResult Offset(IEnumerable<CadEntity> entities, double signedDistanceMm, CadOffsetJoin join); }

public sealed class CadOffsetService : ICadOffsetService
{
    public CadOffsetResult Offset(IEnumerable<CadEntity> entities,double signedDistanceMm,CadOffsetJoin join)
    {
        if(!double.IsFinite(signedDistanceMm)||Math.Abs(signedDistanceMm)<1e-9)return new([],"Offset distance must be finite and non-zero.");
        try
        {
            var output=new List<CadEntity>();
            foreach(var entity in entities)
            {
                var closed=entity is RectangleEntity or CircleEntity or PolylineEntity{IsClosed:true}||entity is VectorPathEntity{Figures.Count:1} vector&&vector.Figures[0].IsClosed;
                if(closed)
                {
                    var contour=ClipperGeometryAdapter.ToContour(entity); var paths=new PathsD{new(contour.Select(p=>new PointD(p.X,p.Y)))};
                    var type=join switch{CadOffsetJoin.Round=>JoinType.Round,CadOffsetJoin.Bevel=>JoinType.Bevel,_=>JoinType.Miter};
                    foreach(var path in Clipper.InflatePaths(paths,signedDistanceMm,type,EndType.Polygon,6,ClipperGeometryAdapter.DecimalPrecision).Where(p=>p.Count>=3)) output.Add(new PolylineEntity{LayerId=entity.LayerId,MaterialSheetId=entity.MaterialSheetId,GroupId=entity.GroupId,StrokeColor=entity.StrokeColor,IsVisible=entity.IsVisible,IsLocked=entity.IsLocked,Points=path.Select(p=>new Point2D(p.x,p.y)).ToList(),IsClosed=true});
                }
                else
                {
                    foreach(var path in OpenPaths(entity))
                    {
                        var offset=OffsetOpenPath(path,signedDistanceMm,join);if(offset.Count<2)throw new ArgumentException("Open offset collapsed to fewer than two distinct points.");output.Add(new PolylineEntity{LayerId=entity.LayerId,MaterialSheetId=entity.MaterialSheetId,GroupId=entity.GroupId,StrokeColor=entity.StrokeColor,IsVisible=entity.IsVisible,IsLocked=entity.IsLocked,Points=offset,IsClosed=false});
                    }
                }
            }
            return output.Count==0?new([],"Offset collapsed all selected geometry."):new(output);
        }catch(Exception ex){return new([],ex.Message);}
    }
    private static IEnumerable<IReadOnlyList<Point2D>> OpenPaths(CadEntity entity)
    {
        switch(entity)
        {
            case LineEntity line:yield return EntityGeometry.WorldPoints(line);yield break;
            case PolylineEntity{IsClosed:false} polyline:yield return EntityGeometry.WorldPoints(polyline);yield break;
            case ArcEntity arc:yield return EntityGeometry.WorldPoints(arc);yield break;
            case VectorPathEntity vector:
                foreach(var figure in vector.Figures)
                {
                    if(figure.IsClosed)throw new ArgumentException("Mixed open and closed figures must be offset separately.");yield return VectorPathGeometry.Flatten([figure],.01).Select(vector.Transform.Transform).ToArray();
                }
                yield break;
            default:throw new ArgumentException($"{entity.GetType().Name} is not a supported open or closed offset source.");
        }
    }
    private static List<Point2D> OffsetOpenPath(IReadOnlyList<Point2D> source,double distance,CadOffsetJoin join)
    {
        var points=source.Where(point=>double.IsFinite(point.X)&&double.IsFinite(point.Y)).ToList();if(points.Count!=source.Count)throw new ArgumentException("Open offset source contains a non-finite coordinate.");points=RemoveAdjacentDuplicates(points,1e-9);if(points.Count<2)throw new ArgumentException("Open offset requires at least two distinct points.");var directions=new List<Vector2D>();var normals=new List<Vector2D>();for(var index=0;index<points.Count-1;index++){var delta=points[index+1]-points[index];var length=delta.Length;if(length<=1e-9)throw new ArgumentException("Open offset contains a collapsed segment.");var direction=new Vector2D(delta.X/length,delta.Y/length);directions.Add(direction);normals.Add(new(-direction.Y,direction.X));}
        var result=new List<Point2D>{Shift(points[0],normals[0],distance)};for(var index=1;index<points.Count-1;index++){var vertex=points[index];var previous=directions[index-1];var next=directions[index];var cross=Cross(previous,next);var dot=previous.X*next.X+previous.Y*next.Y;var previousOffset=Shift(vertex,normals[index-1],distance);var nextOffset=Shift(vertex,normals[index],distance);if(Math.Abs(cross)<=1e-10){if(dot<0)throw new ArgumentException("Open offset cannot resolve a 180-degree reversal.");result.Add(nextOffset);continue;}var intersection=InfiniteIntersection(Shift(points[index-1],normals[index-1],distance),previousOffset,nextOffset,Shift(points[index+1],normals[index],distance));if(cross*distance>0){result.Add(intersection);continue;}switch(join){case CadOffsetJoin.Miter:if(intersection.DistanceTo(vertex)<=Math.Abs(distance)*6)result.Add(intersection);else{result.Add(previousOffset);result.Add(nextOffset);}break;case CadOffsetJoin.Bevel:result.Add(previousOffset);result.Add(nextOffset);break;default:AddRoundJoin(result,vertex,previousOffset,nextOffset,cross);break;}}
        result.Add(Shift(points[^1],normals[^1],distance));result=RemoveAdjacentDuplicates(result,1e-8);result=RemoveSelfIntersectionLoops(result,1e-8);return RemoveAdjacentDuplicates(result,1e-8);
    }
    private static void AddRoundJoin(List<Point2D> output,Point2D center,Point2D start,Point2D end,double turn)
    {
        output.Add(start);var radius=start.DistanceTo(center);var startAngle=Math.Atan2(start.Y-center.Y,start.X-center.X);var endAngle=Math.Atan2(end.Y-center.Y,end.X-center.X);var sweep=turn>0?NormalizePositive(endAngle-startAngle):-NormalizePositive(startAngle-endAngle);var tolerance=Math.Min(.01,radius*.1);var step=2*Math.Acos(Math.Clamp(1-tolerance/radius,-1,1));var count=Math.Max(1,(int)Math.Ceiling(Math.Abs(sweep)/Math.Max(step,1e-4)));for(var index=1;index<count;index++){var angle=startAngle+sweep*index/count;output.Add(new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)));}output.Add(end);
    }
    private static List<Point2D> RemoveSelfIntersectionLoops(List<Point2D> source,double tolerance)
    {
        var result=source.ToList();var changed=true;var guard=0;while(changed&&guard++<source.Count*source.Count){changed=false;for(var first=0;first<result.Count-3&&!changed;first++)for(var second=first+2;second<result.Count-1;second++){if(SegmentIntersection(result[first],result[first+1],result[second],result[second+1],tolerance) is not{} intersection)continue;var cleaned=new List<Point2D>();cleaned.AddRange(result.Take(first+1));cleaned.Add(intersection);cleaned.AddRange(result.Skip(second+1));result=RemoveAdjacentDuplicates(cleaned,tolerance);changed=true;break;}}return result;
    }
    private static Point2D? SegmentIntersection(Point2D a,Point2D b,Point2D c,Point2D d,double tolerance)
    {
        var r=b-a;var s=d-c;var denominator=Cross(r,s);if(Math.Abs(denominator)<=tolerance)return null;var t=Cross(c-a,s)/denominator;var u=Cross(c-a,r)/denominator;if(t<=tolerance||t>=1-tolerance||u<=tolerance||u>=1-tolerance)return null;return new(a.X+r.X*t,a.Y+r.Y*t);
    }
    private static Point2D InfiniteIntersection(Point2D a,Point2D b,Point2D c,Point2D d)
    {
        var r=b-a;var s=d-c;var denominator=Cross(r,s);if(Math.Abs(denominator)<=1e-12)throw new ArgumentException("Offset join is numerically parallel.");var t=Cross(c-a,s)/denominator;var point=new Point2D(a.X+r.X*t,a.Y+r.Y*t);if(!double.IsFinite(point.X)||!double.IsFinite(point.Y))throw new ArgumentException("Offset join produced a non-finite point.");return point;
    }
    private static List<Point2D> RemoveAdjacentDuplicates(IEnumerable<Point2D> source,double tolerance){var output=new List<Point2D>();foreach(var point in source)if(output.Count==0||output[^1].DistanceTo(point)>tolerance)output.Add(point);return output;}
    private static Point2D Shift(Point2D point,Vector2D normal,double distance)=>new(point.X+normal.X*distance,point.Y+normal.Y*distance);
    private static double Cross(Vector2D a,Vector2D b)=>a.X*b.Y-a.Y*b.X;
    private static double NormalizePositive(double angle){var value=angle%(Math.PI*2);return value<0?value+Math.PI*2:value;}
}
