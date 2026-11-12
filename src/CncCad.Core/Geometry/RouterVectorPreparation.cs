using CncCad.Core.Entities;

namespace CncCad.Core.Geometry;

public enum CornerReliefStyle { Dogbone, TBone }
public enum MachinabilitySeverity { Information, Warning, Error }
public sealed record MachinabilityDiagnostic(MachinabilitySeverity Severity,string Code,string Message,Guid? EntityId=null,int? CornerIndex=null);

public interface IRouterVectorPreparationService
{
    VectorEditResult AddCornerRelief(PolylineEntity contour,IReadOnlyCollection<int> cornerIndices,double toolDiameterMm,CornerReliefStyle style);
    IReadOnlyList<MachinabilityDiagnostic> Diagnose(IEnumerable<CadEntity> entities,double toolDiameterMm,double joinToleranceMm=.05);
}

public sealed class RouterVectorPreparationService : IRouterVectorPreparationService
{
    public VectorEditResult AddCornerRelief(PolylineEntity contour,IReadOnlyCollection<int> cornerIndices,double toolDiameterMm,CornerReliefStyle style)
    {
        if(!contour.IsClosed||contour.Points.Count<3)return VectorEditResult.Fail("Corner relief requires one closed polyline.");
        if(toolDiameterMm<=0||!double.IsFinite(toolDiameterMm))return VectorEditResult.Fail("Choose a cutter with a positive diameter.");
        if(cornerIndices.Count==0)return VectorEditResult.Fail("Select at least one corner for relief.");
        var selected=cornerIndices.ToHashSet();if(selected.Any(i=>i<0||i>=contour.Points.Count))return VectorEditResult.Fail("A selected corner is outside the contour.");
        var points=contour.Points.Select(contour.Transform.Transform).ToArray();if(points.Any(point=>!double.IsFinite(point.X)||!double.IsFinite(point.Y)))return VectorEditResult.Fail("Corner relief requires finite geometry.");var area=SignedArea(points);if(Math.Abs(area)<1e-9)return VectorEditResult.Fail("Corner relief requires a non-degenerate contour.");var radius=toolDiameterMm/2;var output=new List<Point2D>();
        for(var i=0;i<points.Length;i++)
        {
            var corner=points[i];
            if(!selected.Contains(i)){output.Add(corner);continue;}
            var before=points[(i-1+points.Length)%points.Length];var after=points[(i+1)%points.Length];var u=Unit(before-corner);var v=Unit(after-corner);
            if(u.Length<.5||v.Length<.5)return VectorEditResult.Fail($"Corner {i+1} has a zero-length adjacent segment.");
            var bisector=Unit(new Vector2D(u.X+v.X,u.Y+v.Y));if(bisector.Length<.5)return VectorEditResult.Fail($"Corner {i+1} is straight and cannot receive relief.");
            var cross=u.X*v.Y-u.Y*v.X;var convex=cross*Math.Sign(area)<0;
            if(convex)return VectorEditResult.Fail($"Corner {i+1} is an outside corner. Dogbone and T-bone reliefs apply only to inside corners.");
            if(style==CornerReliefStyle.TBone)
            {
                var arm=before.DistanceTo(corner)>=after.DistanceTo(corner)?u:v;var center=new Point2D(corner.X+arm.X*radius,corner.Y+arm.Y*radius);output.Add(corner);AddArc(output,center,radius,Math.Atan2(corner.Y-center.Y,corner.X-center.X),Math.Sign(area)*Math.PI*2,corner);continue;
            }
            var halfAngle=Math.Acos(Math.Clamp(u.X*v.X+u.Y*v.Y,-1,1))/2;var sine=Math.Sin(halfAngle);var tangent=Math.Tan(halfAngle);if(sine<=1e-9||Math.Abs(tangent)<=1e-9)return VectorEditResult.Fail($"Corner {i+1} cannot receive a stable relief.");var centerDistance=radius/sine;var tangentDistance=radius/tangent;var centerPoint=new Point2D(corner.X+bisector.X*centerDistance,corner.Y+bisector.Y*centerDistance);var incoming=new Point2D(corner.X+u.X*tangentDistance,corner.Y+u.Y*tangentDistance);var outgoing=new Point2D(corner.X+v.X*tangentDistance,corner.Y+v.Y*tangentDistance);output.Add(incoming);var startAngle=Math.Atan2(incoming.Y-centerPoint.Y,incoming.X-centerPoint.X);var endAngle=Math.Atan2(outgoing.Y-centerPoint.Y,outgoing.X-centerPoint.X);AddArc(output,centerPoint,radius,startAngle,NormalizeSweep(endAngle-startAngle),outgoing);
        }
        if(output.Count<3||output.Any(point=>!double.IsFinite(point.X)||!double.IsFinite(point.Y)))return VectorEditResult.Fail("Corner relief produced invalid geometry.");
        return new([new PolylineEntity{Id=contour.Id,LayerId=contour.LayerId,MaterialSheetId=contour.MaterialSheetId,GroupId=contour.GroupId,StrokeColor=contour.StrokeColor,IsVisible=contour.IsVisible,IsLocked=contour.IsLocked,Transform=Matrix2D.Identity,Points=output,IsClosed=true}]);
    }

    public IReadOnlyList<MachinabilityDiagnostic> Diagnose(IEnumerable<CadEntity> entities,double toolDiameterMm,double joinToleranceMm=.05)
    {
        var source=entities.ToArray();var result=new List<MachinabilityDiagnostic>();
        if(!double.IsFinite(toolDiameterMm)||toolDiameterMm<=0)result.Add(new(MachinabilitySeverity.Error,"tool","Tool diameter must be a positive finite value."));if(!double.IsFinite(joinToleranceMm)||joinToleranceMm<=0){result.Add(new(MachinabilitySeverity.Error,"tolerance","Join tolerance must be a positive finite value."));return result;}
        foreach(var entity in source)
        {
            var points=EntityGeometry.WorldPoints(entity);
            if(entity is PolylineEntity{IsClosed:false} poly){var gap=poly.Points.Count>1?points[0].DistanceTo(points[^1]):double.PositiveInfinity;result.Add(new(gap<=joinToleranceMm?MachinabilitySeverity.Warning:MachinabilitySeverity.Error,gap<=joinToleranceMm?"joinable-open":"open-vector",gap<=joinToleranceMm?$"Open endpoints are {gap:0.###} mm apart and can be joined.":"Open vector cannot be used for a closed profile or pocket.",entity.Id));}
            for(var i=1;i<points.Count;i++)if(points[i-1].DistanceTo(points[i])<joinToleranceMm)result.Add(new(MachinabilitySeverity.Warning,"tiny-segment",$"Segment {i} is shorter than the {joinToleranceMm:0.###} mm repair tolerance.",entity.Id,i));
            if(toolDiameterMm>0&&entity is PolylineEntity{IsClosed:true})
            for(var i=0;i<points.Count;i++){var a=points[(i-1+points.Count)%points.Count];var b=points[i];var c=points[(i+1)%points.Count];if(Math.Min(a.DistanceTo(b),b.DistanceTo(c))<toolDiameterMm)result.Add(new(MachinabilitySeverity.Warning,"corner-clearance",$"Corner {i+1} has less than one cutter diameter of adjacent world-space clearance.",entity.Id,i));}
        }
        for(var i=0;i<source.Length;i++)for(var j=i+1;j<source.Length;j++){var a=EntityGeometry.WorldPoints(source[i]);var b=EntityGeometry.WorldPoints(source[j]);if(a.Count==b.Count&&a.Zip(b).All(x=>x.First.DistanceTo(x.Second)<=joinToleranceMm))result.Add(new(MachinabilitySeverity.Error,"duplicate",$"{source[i].GetType().Name} duplicates another vector.",source[j].Id));}
        return result;
    }

    private static Vector2D Unit(Vector2D value)=>value.Length<1e-12?new(0,0):new(value.X/value.Length,value.Y/value.Length);
    private static double NormalizeSweep(double value){while(value<=-Math.PI)value+=Math.PI*2;while(value>Math.PI)value-=Math.PI*2;return value;}
    private static double SignedArea(IReadOnlyList<Point2D> points){var area=0d;for(var index=0;index<points.Count;index++){var next=points[(index+1)%points.Count];area+=points[index].X*next.Y-next.X*points[index].Y;}return area/2;}
    private static void AddArc(List<Point2D> output,Point2D center,double radius,double start,double sweep,Point2D end)
    {
        var maximumStep=2*Math.Acos(Math.Clamp(1-.02/radius,-1,1));if(!double.IsFinite(maximumStep)||maximumStep<=1e-6)maximumStep=Math.PI/180;var count=Math.Clamp((int)Math.Ceiling(Math.Abs(sweep)/maximumStep),1,16384);for(var step=1;step<count;step++){var angle=start+sweep*step/count;output.Add(new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)));}output.Add(end);
    }
}
