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
        if(Math.Abs(signedDistanceMm)<1e-9)return new([],"Offset distance must be non-zero.");
        try
        {
            var output=new List<CadEntity>();
            foreach(var entity in entities)
            {
                var closed=entity is RectangleEntity or CircleEntity or PolylineEntity{IsClosed:true};
                if(closed)
                {
                    var contour=ClipperGeometryAdapter.ToContour(entity); var paths=new PathsD{new(contour.Select(p=>new PointD(p.X,p.Y)))};
                    var type=join switch{CadOffsetJoin.Round=>JoinType.Round,CadOffsetJoin.Bevel=>JoinType.Bevel,_=>JoinType.Miter};
                    foreach(var path in Clipper.InflatePaths(paths,signedDistanceMm,type,EndType.Polygon,6).Where(p=>p.Count>=3)) output.Add(new PolylineEntity{LayerId=entity.LayerId,Points=path.Select(p=>new Point2D(p.x,p.y)).ToList(),IsClosed=true});
                }
                else
                {
                    var points=entity switch{LineEntity line=>EntityGeometry.WorldPoints(line),PolylineEntity{IsClosed:false} poly=>EntityGeometry.WorldPoints(poly),_=>throw new ArgumentException($"{entity.GetType().Name.Replace("Entity","")} cannot be offset.")};
                    if(points.Count<2)throw new ArgumentException("Open path needs at least two nodes.");
                    var shifted=new List<Point2D>();
                    for(var i=0;i<points.Count;i++){var a=points[Math.Max(0,i-1)];var b=points[Math.Min(points.Count-1,i+1)];var d=b-a;var length=Math.Max(1e-12,d.Length);shifted.Add(points[i]+new Vector2D(-d.Y/length*signedDistanceMm,d.X/length*signedDistanceMm));}
                    output.Add(new PolylineEntity{LayerId=entity.LayerId,Points=shifted,IsClosed=false});
                }
            }
            return output.Count==0?new([],"Offset collapsed all selected geometry."):new(output);
        }catch(Exception ex){return new([],ex.Message);}
    }
}
