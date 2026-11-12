using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed record TabValidationIssue(string Code,string Message);
public sealed record TabValidationResult(IReadOnlyList<double> DistancesMm,IReadOnlyList<TabValidationIssue> Issues){public bool Success=>Issues.Count==0;}

public static class ProfileTabEngine
{
    public static IReadOnlyList<Point2D> Positions(IReadOnlyList<Point2D> path,ProfileTabSettings tabs,Guid? geometryId=null){var validation=Validate(path,tabs,double.MaxValue,double.PositiveInfinity,geometryId);if(!validation.Success)return[];var perimeter=Perimeter(path);return validation.DistancesMm.Select(d=>PointAt(path,d,perimeter)).ToArray();}
    public static TabValidationResult Validate(IReadOnlyList<Point2D> path,ProfileTabSettings tabs,double cutDepthMm,double materialThicknessMm=double.PositiveInfinity,Guid? geometryId=null)
    {
        if(!tabs.Enabled)return new([],[]);var issues=new List<TabValidationIssue>();var perimeter=Perimeter(path);
        if(tabs.WidthMm<=0||tabs.RampLengthMm<0||tabs.RemainingHeightMm<=0)issues.Add(new("tab-dimensions","Tab width and remaining height must be positive; ramp length cannot be negative."));
        if(tabs.RemainingHeightMm>=cutDepthMm)issues.Add(new("tab-height","Remaining tab height must be less than final cut depth."));
        if(tabs.RemainingHeightMm>materialThicknessMm)issues.Add(new("tab-material","Remaining tab height exceeds material thickness."));
        var ramp=tabs.Style==TabStyle.Ramped?tabs.RampLengthMm:0;var span=tabs.WidthMm+2*ramp;var cornerDistances=CornerDistances(path);var normalized=tabs.PlacementMode==TabPlacementMode.Automatic?AutomaticPositions(perimeter,tabs.AutomaticCount,cornerDistances,span/2+tabs.ExclusionDistanceMm).Select(x=>x/perimeter).ToArray():tabs.ManualPlacements.Where(x=>x.GeometryId is null||geometryId is null||x.GeometryId==geometryId).Select(x=>x.NormalizedDistance).ToArray();
        if(normalized.Length==0)issues.Add(new("tab-count","At least one tab is required when tabs are enabled."));if(normalized.Any(x=>x<0||x>=1))issues.Add(new("tab-position","Manual tab positions must be between 0 and 1."));
        if(perimeter<=normalized.Length*(span+tabs.ExclusionDistanceMm))issues.Add(new("tab-spacing","The contour is too short for the requested tabs, ramps, and exclusion spacing."));
        var distances=normalized.Select(x=>((x%1)+1)%1*perimeter).Order().ToArray();for(var i=0;i<distances.Length;i++){var next=i==distances.Length-1?distances[0]+perimeter:distances[i+1];if(next-distances[i]<span+tabs.ExclusionDistanceMm)issues.Add(new("tab-neighbor","Tabs overlap or violate the neighboring-tab exclusion distance."));}
        if(distances.Any(distance=>cornerDistances.Any(corner=>CircularDistance(distance,corner,perimeter)<span/2+tabs.ExclusionDistanceMm-GeometryTolerancePolicy.DefaultEndpointEqualityMm)))issues.Add(new("tab-corner","A tab or its ramp violates the corner exclusion distance."));
        return new(distances,issues.DistinctBy(x=>x.Code).ToArray());
    }

    public static void AddClosedPass(List<ToolpathMove> moves,IReadOnlyList<Point2D> path,double depth,double finalDepth,ProfileTabSettings tabs,double feedRate,Guid? geometryId=null)
    {
        if(!tabs.Enabled||depth<=finalDepth-tabs.RemainingHeightMm+GeometryTolerancePolicy.DefaultEndpointEqualityMm){foreach(var point in path.Skip(1))moves.Add(new LinearMove(point,-depth,feedRate));moves.Add(new LinearMove(path[0],-depth,feedRate));return;}
        var validation=Validate(path,tabs,finalDepth,double.PositiveInfinity,geometryId);if(!validation.Success)throw new InvalidOperationException(validation.Issues[0].Message);var perimeter=Perimeter(path);var ramp=tabs.Style==TabStyle.Ramped?tabs.RampLengthMm:0;var breaks=new SortedSet<double>{0,perimeter};foreach(var center in validation.DistancesMm)foreach(var d in new[]{center-tabs.WidthMm/2-ramp,center-tabs.WidthMm/2,center+tabs.WidthMm/2,center+tabs.WidthMm/2+ramp}){var wrapped=((d%perimeter)+perimeter)%perimeter;breaks.Add(wrapped);}var cumulative=0d;for(var i=0;i<path.Count;i++){var a=path[i];var b=path[(i+1)%path.Count];var length=a.DistanceTo(b);foreach(var distance in breaks.Where(x=>x>cumulative+GeometryTolerancePolicy.DefaultEndpointEqualityMm&&x<cumulative+length-GeometryTolerancePolicy.DefaultEndpointEqualityMm).Append(cumulative+length)){var t=(distance-cumulative)/length;var point=new Point2D(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);moves.Add(new LinearMove(point,ZAt(distance,depth,finalDepth,tabs,validation.DistancesMm,perimeter),feedRate));}cumulative+=length;}
    }
    private static double ZAt(double distance,double depth,double finalDepth,ProfileTabSettings tabs,IReadOnlyList<double> centers,double perimeter)
    {
        var top=-(finalDepth-tabs.RemainingHeightMm);var z=-depth;var ramp=tabs.Style==TabStyle.Ramped?tabs.RampLengthMm:0;foreach(var center in centers){var delta=Math.Abs(distance-center);delta=Math.Min(delta,perimeter-delta);var half=tabs.WidthMm/2;if(delta<=half)z=Math.Max(z,top);else if(ramp>0&&delta<half+ramp){var f=(delta-half)/ramp;z=Math.Max(z,top+(-depth-top)*f);}}return z;
    }
    private static double Perimeter(IReadOnlyList<Point2D> path)=>Enumerable.Range(0,path.Count).Sum(i=>path[i].DistanceTo(path[(i+1)%path.Count]));
    private static IReadOnlyList<double> CornerDistances(IReadOnlyList<Point2D> path){var result=new List<double>();var distance=0d;for(var i=0;i<path.Count;i++){result.Add(distance);distance+=path[i].DistanceTo(path[(i+1)%path.Count]);}return result;}
    private static IReadOnlyList<double> AutomaticPositions(double perimeter,int count,IReadOnlyList<double> corners,double requiredCornerDistance){if(count<=0||perimeter<=0)return[];var spacing=perimeter/count;var bestOffset=spacing/2;var bestClearance=-1d;for(var candidate=0;candidate<128;candidate++){var offset=spacing*candidate/128d;var clearance=Enumerable.Range(0,count).Select(i=>(offset+i*spacing)%perimeter).Min(position=>corners.Min(corner=>CircularDistance(position,corner,perimeter)));if(clearance>bestClearance){bestClearance=clearance;bestOffset=offset;}if(clearance>=requiredCornerDistance)break;}return Enumerable.Range(0,count).Select(i=>(bestOffset+i*spacing)%perimeter).Order().ToArray();}
    private static double CircularDistance(double a,double b,double perimeter){var distance=Math.Abs(a-b);return Math.Min(distance,perimeter-distance);}
    private static Point2D PointAt(IReadOnlyList<Point2D> path,double distance,double perimeter){distance=((distance%perimeter)+perimeter)%perimeter;var current=0d;for(var i=0;i<path.Count;i++){var a=path[i];var b=path[(i+1)%path.Count];var length=a.DistanceTo(b);if(current+length>=distance){var t=(distance-current)/length;return new(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}current+=length;}return path[0];}
}
