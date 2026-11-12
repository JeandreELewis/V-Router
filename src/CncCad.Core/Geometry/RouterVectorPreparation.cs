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
        var radius=toolDiameterMm/2;var output=new List<Point2D>();
        for(var i=0;i<contour.Points.Count;i++)
        {
            var corner=contour.Points[i];
            if(!selected.Contains(i)){output.Add(corner);continue;}
            var before=contour.Points[(i-1+contour.Points.Count)%contour.Points.Count];var after=contour.Points[(i+1)%contour.Points.Count];var u=Unit(before-corner);var v=Unit(after-corner);
            if(u.Length<.5||v.Length<.5)return VectorEditResult.Fail($"Corner {i+1} has a zero-length adjacent segment.");
            var bisector=Unit(new Vector2D(u.X+v.X,u.Y+v.Y));if(bisector.Length<.5)return VectorEditResult.Fail($"Corner {i+1} is straight and cannot receive relief.");
            var center=style==CornerReliefStyle.Dogbone?new Point2D(corner.X-bisector.X*radius,corner.Y-bisector.Y*radius):new Point2D(corner.X-u.X*radius,corner.Y-u.Y*radius);
            output.Add(new(corner.X+u.X*radius,corner.Y+u.Y*radius));
            var start=Math.Atan2(output[^1].Y-center.Y,output[^1].X-center.X);var end=Math.Atan2(corner.Y+v.Y*radius-center.Y,corner.X+v.X*radius-center.X);var sweep=NormalizeSweep(end-start);
            for(var step=1;step<6;step++){var angle=start+sweep*step/6;output.Add(new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)));}
            output.Add(new(corner.X+v.X*radius,corner.Y+v.Y*radius));
        }
        return new([new PolylineEntity{LayerId=contour.LayerId,Transform=contour.Transform,Points=output,IsClosed=true,StrokeColor=contour.StrokeColor,IsVisible=contour.IsVisible,IsLocked=contour.IsLocked,MaterialSheetId=contour.MaterialSheetId}]);
    }

    public IReadOnlyList<MachinabilityDiagnostic> Diagnose(IEnumerable<CadEntity> entities,double toolDiameterMm,double joinToleranceMm=.05)
    {
        var source=entities.ToArray();var result=new List<MachinabilityDiagnostic>();
        if(toolDiameterMm<=0)result.Add(new(MachinabilitySeverity.Error,"tool","Tool diameter must be positive."));
        foreach(var entity in source)
        {
            var points=EntityGeometry.WorldPoints(entity);
            if(entity is PolylineEntity{IsClosed:false} poly){var gap=poly.Points.Count>1?points[0].DistanceTo(points[^1]):double.PositiveInfinity;result.Add(new(gap<=joinToleranceMm?MachinabilitySeverity.Warning:MachinabilitySeverity.Error,gap<=joinToleranceMm?"joinable-open":"open-vector",gap<=joinToleranceMm?$"Open endpoints are {gap:0.###} mm apart and can be joined.":"Open vector cannot be used for a closed profile or pocket.",entity.Id));}
            for(var i=1;i<points.Count;i++)if(points[i-1].DistanceTo(points[i])<joinToleranceMm)result.Add(new(MachinabilitySeverity.Warning,"tiny-segment",$"Segment {i} is shorter than the {joinToleranceMm:0.###} mm repair tolerance.",entity.Id,i));
            if(toolDiameterMm>0&&entity is PolylineEntity{IsClosed:true} closed)
            for(var i=0;i<closed.Points.Count;i++){var a=closed.Points[(i-1+closed.Points.Count)%closed.Points.Count];var b=closed.Points[i];var c=closed.Points[(i+1)%closed.Points.Count];if(Math.Min(a.DistanceTo(b),b.DistanceTo(c))<toolDiameterMm)result.Add(new(MachinabilitySeverity.Warning,"corner-clearance",$"Corner {i+1} has less than one cutter diameter of adjacent clearance.",entity.Id,i));}
        }
        for(var i=0;i<source.Length;i++)for(var j=i+1;j<source.Length;j++){var a=EntityGeometry.WorldPoints(source[i]);var b=EntityGeometry.WorldPoints(source[j]);if(a.Count==b.Count&&a.Zip(b).All(x=>x.First.DistanceTo(x.Second)<=joinToleranceMm))result.Add(new(MachinabilitySeverity.Error,"duplicate",$"{source[i].GetType().Name} duplicates another vector.",source[j].Id));}
        return result;
    }

    private static Vector2D Unit(Vector2D value)=>value.Length<1e-12?new(0,0):new(value.X/value.Length,value.Y/value.Length);
    private static double NormalizeSweep(double value){while(value<=-Math.PI)value+=Math.PI*2;while(value>Math.PI)value-=Math.PI*2;return value;}
}
