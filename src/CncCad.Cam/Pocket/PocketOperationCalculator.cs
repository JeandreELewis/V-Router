using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Pocket;

public sealed class PocketOperationCalculator : ICamOperationCalculator
{
    public Type OperationType=>typeof(PocketOperationDefinition);
    public OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        if (operation is not PocketOperationDefinition pocket) return OperationCalculationResult.Failed(new OperationValidationError("unsupported", "Operation is not a pocket."));
        var numericErrors=OperationNumerics.ValidateDefinition(pocket);
        if(numericErrors.Count>0)return new(null,numericErrors);
        var errors = Validate(pocket, geometry);
        if (errors.Count > 0) return new(null, errors);
        var contours = pocket.GeometryIds.Select(id => ClipperGeometryAdapter.ToContour(geometry[id])).ToArray();
        var inset=pocket.Tool.DiameterMm/2+pocket.AllowanceMm+pocket.RoughAllowanceMm;
        var paths = pocket.Strategy==PocketStrategy.Raster?RasterPaths(ClipperGeometryAdapter.InsetRegion(contours,inset),pocket.StepOverMm).Select(x=>(Points:x,Closed:false)).ToList():ClipperGeometryAdapter.PocketOffsets(contours,inset,pocket.StepOverMm).Select(x=>(Points:x,Closed:true)).ToList();
        if (paths.Count == 0) return OperationCalculationResult.Failed(new OperationValidationError("collapsed", "The selected pocket is too small for this cutter and allowance."));

        var ordered = paths.OrderBy(path => path.Closed?Math.Abs(ClipperGeometryAdapter.SignedArea(path.Points)):path.Points[0].Y).ToArray();
        var clearingIssue=ordered.Select((path,index)=>PathMachinabilityIssue(path.Points,path.Closed,contours,inset,index)).FirstOrDefault(issue=>issue is not null);
        if (clearingIssue is not null)
            return OperationCalculationResult.Failed(new OperationValidationError("clearing-containment", $"A clearing path cannot retain the complete cutter envelope inside the selected pocket ({clearingIssue}). Repair the vectors, reduce the allowance, or use a smaller cutter."));
        HelixPlan[]? helixPlans=null;if(pocket.EntryMode==PocketEntryMode.Helix){var radius=pocket.RampLengthMm/2;helixPlans=ordered.Select(raw=>FindHelixPlan(contours,OrientedPath(raw.Points,raw.Closed,pocket.Direction),raw.Closed,inset,radius)).ToArray();if(helixPlans.Any(plan=>!plan.Success))return OperationCalculationResult.Failed(new OperationValidationError("helix-containment","No helix entry fits completely inside the machinable pocket while retaining a contained link to every clearing path. Reduce the helix diameter, use a smaller tool, or choose linear ramp entry."));}
        FinishPlan[] finishPlans=[];
        if(pocket.FinishPassEnabled)
        {
            var allPoints=contours.SelectMany(contour=>contour).ToArray();
            var finishStep=Math.Max(allPoints.Max(point=>point.X)-allPoints.Min(point=>point.X),allPoints.Max(point=>point.Y)-allPoints.Min(point=>point.Y))+1;
            var finishClearance=pocket.Tool.DiameterMm/2+pocket.AllowanceMm;
            finishPlans=ClipperGeometryAdapter.PocketOffsets(contours,finishClearance,finishStep)
                .Select(finish=>PlanFinishEntry(contours,ordered,OrientedPath(finish,true,pocket.Direction),finishClearance))
                .ToArray();
            if(finishPlans.Length==0)
                return OperationCalculationResult.Failed(new OperationValidationError("finish-collapsed", "No finish contour remains for this cutter and allowance."));
            if(finishPlans.Any(plan=>!plan.Success))
                return OperationCalculationResult.Failed(new OperationValidationError("finish-entry-containment", "A finish contour cannot be reached from material already cleared to full depth without leaving the machinable pocket. Repair the vectors, reduce the rough allowance, or use a smaller cutter."));
        }

        var toolpath = new ToolpathOperation { SourceOperationId=pocket.Id,Name=pocket.Name,ToolId=pocket.Tool.SourceToolId,MaterialSheetId=pocket.MaterialSheetId,SourceGeometryIds=[..pocket.GeometryIds] };
        toolpath.Moves.Add(new ToolChange(pocket.Tool.ToolNumber, pocket.Tool.Name));
        toolpath.Moves.Add(new SpindleStart(pocket.SpindleRpm));
        var previousDepth=pocket.StartDepthMm;
        foreach (var depth in ProfileToolpathGenerator.CalculatePassDepths(pocket.StartDepthMm, pocket.CutDepthMm, pocket.PassDepthMm))
        {
        for(var pathIndex=0;pathIndex<ordered.Length;pathIndex++)
        {
            var raw=ordered[pathIndex];
            var path = pocket.EntryMode==PocketEntryMode.Helix?helixPlans![pathIndex].Path:OrientedPath(raw.Points,raw.Closed,pocket.Direction);
            var start = path[0];
            if(pocket.EntryMode==PocketEntryMode.Helix)
            {
                var entry=helixPlans![pathIndex];toolpath.Moves.Add(new RapidMove(entry.Start,pocket.SafeZMm));toolpath.Moves.Add(new LinearMove(entry.Start,-previousDepth,pocket.PlungeRateMmPerMin));toolpath.Moves.Add(new ArcMove(entry.Start,entry.Center,-depth,pocket.FeedRateMmPerMin,pocket.Direction==CutDirection.Climb));if(entry.Start.DistanceTo(start)>1e-9)toolpath.Moves.Add(new LinearMove(start,-depth,pocket.FeedRateMmPerMin));
            }
            else if (pocket.EntryMode == PocketEntryMode.LinearRamp && pocket.RampLengthMm > 0 && path.Count > 1)
            {
                toolpath.Moves.Add(new RapidMove(start, pocket.SafeZMm));
                toolpath.Moves.Add(new LinearMove(start, -previousDepth, pocket.PlungeRateMmPerMin));
                var rampRoute=AddContainedLinearRamp(toolpath.Moves,path,raw.Closed,previousDepth,depth,pocket.RampLengthMm,pocket.FeedRateMmPerMin);
                // Return over the exact ramp route. A direct chord can leave a concave
                // pocket or cross an island even when both endpoints are valid.
                foreach(var point in rampRoute.Reverse().Skip(1))
                    toolpath.Moves.Add(new LinearMove(point,-depth,pocket.FeedRateMmPerMin));
            }
            else{toolpath.Moves.Add(new RapidMove(start,pocket.SafeZMm));toolpath.Moves.Add(new LinearMove(start, -depth, pocket.PlungeRateMmPerMin));}
            foreach (var point in path.Skip(1)) toolpath.Moves.Add(new LinearMove(point, -depth, pocket.FeedRateMmPerMin));
            if(raw.Closed)toolpath.Moves.Add(new LinearMove(start, -depth, pocket.FeedRateMmPerMin));
            toolpath.Moves.Add(new RapidMove(raw.Closed?start:path[^1], pocket.SafeZMm));
        }
        previousDepth=depth;
        }
        foreach(var finish in finishPlans)
        {
            toolpath.Moves.Add(new RapidMove(finish.ClearedStart,pocket.SafeZMm));
            toolpath.Moves.Add(new LinearMove(finish.ClearedStart,-pocket.CutDepthMm,pocket.PlungeRateMmPerMin));
            if(finish.ClearedStart.DistanceTo(finish.Path[0])>1e-9)
                toolpath.Moves.Add(new LinearMove(finish.Path[0],-pocket.CutDepthMm,pocket.FeedRateMmPerMin));
            foreach(var point in finish.Path.Skip(1))toolpath.Moves.Add(new LinearMove(point,-pocket.CutDepthMm,pocket.FeedRateMmPerMin));
            toolpath.Moves.Add(new LinearMove(finish.Path[0],-pocket.CutDepthMm,pocket.FeedRateMmPerMin));
            toolpath.Moves.Add(new RapidMove(finish.Path[0],pocket.SafeZMm));
        }
        toolpath.Moves.Add(new SpindleStop());
        return OperationNumerics.ValidateResult(new(toolpath, []));
    }

    private static List<OperationValidationError> Validate(PocketOperationDefinition pocket, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        var errors = new List<OperationValidationError>();
        if (pocket.GeometryIds.Count == 0) errors.Add(new("geometry", "Select at least one closed pocket boundary."));
        if (pocket.Tool.DiameterMm <= 0) errors.Add(new("tool", "Select a valid pocket tool."));
        if (pocket.CutDepthMm <= pocket.StartDepthMm || pocket.PassDepthMm <= 0 || pocket.StepOverMm <= 0 || pocket.StepOverMm > pocket.Tool.DiameterMm) errors.Add(new("depth", "Pocket depths and stepover are invalid."));
        if (pocket.AllowanceMm < 0 || pocket.RoughAllowanceMm<0 || pocket.RampLengthMm<0 || pocket.SafeZMm <= 0 || pocket.FeedRateMmPerMin <= 0 || pocket.PlungeRateMmPerMin <= 0 || pocket.SpindleRpm <= 0) errors.Add(new("parameters", "Pocket allowance, entry, rates, safe Z, or spindle speed are invalid."));
        if (pocket.EntryMode == PocketEntryMode.Helix&&pocket.RampLengthMm<=0) errors.Add(new("helix-diameter", "Helix entry requires a positive helix diameter."));
        foreach (var id in pocket.GeometryIds)
        {
            if (!geometry.TryGetValue(id, out var entity)) errors.Add(new("missing", $"Referenced vector {id} is missing."));
            else try { _ = ClipperGeometryAdapter.ToContour(entity); } catch (ArgumentException ex) { errors.Add(new("geometry", ex.Message)); }
        }
        return errors;
    }

    private static IReadOnlyList<IReadOnlyList<Point2D>> RasterPaths(IReadOnlyList<IReadOnlyList<Point2D>> contours,double stepOver)
    {
        var all=contours.SelectMany(x=>x).ToArray();if(all.Length==0)return[];var minY=all.Min(x=>x.Y);var maxY=all.Max(x=>x.Y);var result=new List<IReadOnlyList<Point2D>>();var reverse=false;
        for(var y=minY;y<=maxY+1e-9;y+=stepOver)
        {
            var intersections=new List<double>();
            foreach(var contour in contours)for(var i=0;i<contour.Count;i++){var a=contour[i];var b=contour[(i+1)%contour.Count];if((a.Y>y)!=(b.Y>y))intersections.Add(a.X+(y-a.Y)*(b.X-a.X)/(b.Y-a.Y));}
            intersections.Sort();
            for(var i=0;i+1<intersections.Count;i+=2){var left=intersections[i];var right=intersections[i+1];if(right-left<=1e-6)continue;var line=(IReadOnlyList<Point2D>)[new(left,y),new(right,y)];result.Add(reverse?line.Reverse().ToArray():line);reverse=!reverse;}
        }
        return result;
    }
    private readonly record struct HelixEntry(Point2D Center,Point2D Start,bool Success);
    private sealed record HelixPlan(Point2D Center,Point2D Start,List<Point2D> Path,bool Success);
    private sealed record FinishPlan(Point2D ClearedStart,List<Point2D> Path,bool Success);
    private static List<Point2D> OrientedPath(IReadOnlyList<Point2D> source,bool closed,CutDirection direction){var path=source.ToList();var clockwise=closed&&ClipperGeometryAdapter.SignedArea(path)<0;if(closed&&clockwise!=(direction==CutDirection.Climb))path.Reverse();return path;}
    private static HelixPlan FindHelixPlan(IReadOnlyList<Point2D>[] contours,List<Point2D> path,bool closed,double cutterClearance,double radius)
    {
        foreach(var route in EntryRoutes(path,closed)){var entry=FindHelixEntry(contours,route[0],cutterClearance,radius);if(entry.Success)return new(entry.Center,entry.Start,route,true);}return new(default,default,path,false);
    }
    private static IEnumerable<List<Point2D>> EntryRoutes(IReadOnlyList<Point2D> path,bool closed)
    {
        if(closed){for(var index=0;index<path.Count;index++)yield return[..path.Skip(index),..path.Take(index)];yield break;}if(path.Count==2){var a=path[0];var b=path[1];foreach(var fraction in new[]{.5,.25,.75,0d,1d}){var point=new Point2D(a.X+(b.X-a.X)*fraction,a.Y+(b.Y-a.Y)*fraction);var route=new List<Point2D>{point};if(point.DistanceTo(a)>1e-9)route.Add(a);if(route[^1].DistanceTo(point)>1e-9)route.Add(point);if(point.DistanceTo(b)>1e-9)route.Add(b);yield return route;}yield break;}yield return path.ToList();
    }
    private static HelixEntry FindHelixEntry(IReadOnlyList<Point2D>[] contours,Point2D pathStart,double cutterClearance,double radius)
    {
        var all=contours.SelectMany(contour=>contour).ToArray();var required=cutterClearance+radius;var minX=all.Min(point=>point.X)+required;var maxX=all.Max(point=>point.X)-required;var minY=all.Min(point=>point.Y)+required;var maxY=all.Max(point=>point.Y)-required;if(minX>maxX||minY>maxY)return default;var candidates=new List<Point2D>{new((minX+maxX)/2,(minY+maxY)/2)};var step=Math.Clamp(Math.Min(Math.Max(radius,.5),5),.5,5);for(var y=minY;y<=maxY+1e-9;y+=step)for(var x=minX;x<=maxX+1e-9;x+=step)candidates.Add(new(x,y));foreach(var center in candidates.Distinct().OrderBy(candidate=>candidate.DistanceTo(pathStart)).ThenBy(candidate=>candidate.Y).ThenBy(candidate=>candidate.X)){if(!CenterIsMachinable(center,contours,required))continue;for(var angleIndex=0;angleIndex<16;angleIndex++){var angle=angleIndex*Math.PI/8;var start=new Point2D(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle));if(SegmentIsMachinable(start,pathStart,contours,cutterClearance))return new(center,start,true);}}return default;
    }
    private static bool CenterIsMachinable(Point2D point,IReadOnlyList<Point2D>[] contours,double clearance)
    {
        // Use the same world tolerance as curve tessellation. Polygonal offsets
        // around sampled curves can differ from the analytic clearance within
        // that bound; this is representational, not a machining allowance.
        const double clipperCoordinateToleranceMm=ClipperGeometryAdapter.CircleToleranceMm;
        if(contours.Count(contour=>ClipperGeometryAdapter.Contains(contour,point))%2==0)return false;return contours.SelectMany(contour=>Enumerable.Range(0,contour.Count).Select(index=>DistanceToSegment(point,contour[index],contour[(index+1)%contour.Count]))).DefaultIfEmpty(0).Min()>=clearance-clipperCoordinateToleranceMm;
    }
    private static bool SegmentIsMachinable(Point2D start,Point2D end,IReadOnlyList<Point2D>[] contours,double clearance)
    {
        var length=start.DistanceTo(end);var samples=Math.Max(1,(int)Math.Ceiling(length/Math.Max(.25,clearance/3)));for(var index=0;index<=samples;index++){var fraction=(double)index/samples;var point=new Point2D(start.X+(end.X-start.X)*fraction,start.Y+(end.Y-start.Y)*fraction);if(!CenterIsMachinable(point,contours,clearance))return false;}return true;
    }
    private static bool PathIsMachinable(IReadOnlyList<Point2D> path,bool closed,IReadOnlyList<Point2D>[] contours,double clearance)
    {
        if(path.Count<2||path.Any(point=>!CenterIsMachinable(point,contours,clearance)))return false;
        for(var index=1;index<path.Count;index++)if(!SegmentIsMachinable(path[index-1],path[index],contours,clearance))return false;
        return !closed||SegmentIsMachinable(path[^1],path[0],contours,clearance);
    }
    private static string? PathMachinabilityIssue(IReadOnlyList<Point2D> path,bool closed,IReadOnlyList<Point2D>[] contours,double clearance,int pathIndex)
    {
        if(path.Count<2)return $"path {pathIndex+1} is degenerate";
        for(var index=0;index<path.Count;index++)
        {
            var point=path[index];if(CenterIsMachinable(point,contours,clearance))continue;var parity=contours.Count(contour=>ClipperGeometryAdapter.Contains(contour,point));var nearest=contours.SelectMany(contour=>Enumerable.Range(0,contour.Count).Select(segment=>DistanceToSegment(point,contour[segment],contour[(segment+1)%contour.Count]))).DefaultIfEmpty(0).Min();return $"path {pathIndex+1}, point {index+1}, X {point.X:0.######}, Y {point.Y:0.######}, boundary clearance {nearest:0.######} mm, required {clearance:0.######} mm, region parity {parity}";
        }
        for(var index=1;index<path.Count;index++)if(!SegmentIsMachinable(path[index-1],path[index],contours,clearance))return $"path {pathIndex+1}, segment {index}";
        if(closed&&!SegmentIsMachinable(path[^1],path[0],contours,clearance))return $"path {pathIndex+1}, closing segment";
        return null;
    }
    private static FinishPlan PlanFinishEntry(IReadOnlyList<Point2D>[] contours,(IReadOnlyList<Point2D> Points,bool Closed)[] clearedPaths,List<Point2D> finishPath,double clearance)
    {
        if(!PathIsMachinable(finishPath,true,contours,clearance))return new(default,finishPath,false);
        var candidates=clearedPaths.SelectMany((path,pathIndex)=>path.Points.Select((point,pointIndex)=>(Point:point,PathIndex:pathIndex,PointIndex:pointIndex)))
            .SelectMany(cleared=>finishPath.Select((finish,finishIndex)=>(Cleared:cleared.Point,Finish:finish,cleared.PathIndex,cleared.PointIndex,FinishIndex:finishIndex,Distance:cleared.Point.DistanceTo(finish))))
            .OrderBy(candidate=>candidate.Distance).ThenBy(candidate=>candidate.PathIndex).ThenBy(candidate=>candidate.PointIndex).ThenBy(candidate=>candidate.FinishIndex);
        foreach(var candidate in candidates)
        {
            if(!SegmentIsMachinable(candidate.Cleared,candidate.Finish,contours,clearance))continue;
            var rotated=finishPath.Skip(candidate.FinishIndex).Concat(finishPath.Take(candidate.FinishIndex)).ToList();
            return new(candidate.Cleared,rotated,true);
        }
        return new(default,finishPath,false);
    }
    private static IReadOnlyList<Point2D> AddContainedLinearRamp(List<ToolpathMove> moves,IReadOnlyList<Point2D> path,bool closed,double previousDepth,double targetDepth,double rampLength,double feedRate)
    {
        var route=new List<Point2D>{path[0]};
        var currentIndex=0;
        var direction=1;
        var travelled=0d;
        while(travelled<rampLength-1e-9)
        {
            int nextIndex;
            if(closed)nextIndex=(currentIndex+1)%path.Count;
            else
            {
                if(currentIndex==path.Count-1)direction=-1;
                else if(currentIndex==0)direction=1;
                nextIndex=currentIndex+direction;
            }
            var from=path[currentIndex];
            var to=path[nextIndex];
            var segmentLength=from.DistanceTo(to);
            if(segmentLength<=1e-12)break;
            var remaining=rampLength-travelled;
            var fraction=Math.Min(1,remaining/segmentLength);
            var next=fraction>=1-1e-12?to:new Point2D(from.X+(to.X-from.X)*fraction,from.Y+(to.Y-from.Y)*fraction);
            travelled+=segmentLength*fraction;
            var z=-previousDepth-(targetDepth-previousDepth)*Math.Min(1,travelled/rampLength);
            moves.Add(new LinearMove(next,z,feedRate));
            route.Add(next);
            if(fraction<1-1e-12)break;
            currentIndex=nextIndex;
        }
        return route;
    }
    private static double DistanceToSegment(Point2D point,Point2D start,Point2D end){var delta=end-start;var lengthSquared=delta.X*delta.X+delta.Y*delta.Y;if(lengthSquared<=1e-18)return point.DistanceTo(start);var offset=point-start;var fraction=Math.Clamp((offset.X*delta.X+offset.Y*delta.Y)/lengthSquared,0,1);return point.DistanceTo(new(start.X+delta.X*fraction,start.Y+delta.Y*fraction));}
}
