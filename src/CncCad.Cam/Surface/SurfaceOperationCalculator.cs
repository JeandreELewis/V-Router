using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Surface;

public sealed class SurfaceOperationCalculator : ICamOperationCalculator
{
    public Type OperationType => typeof(SurfaceOperationDefinition);

    public OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        if (operation is not SurfaceOperationDefinition surface)
            return OperationCalculationResult.Failed(new OperationValidationError("unsupported", "Operation is not a surfacing operation."));
        var numericErrors=OperationNumerics.ValidateDefinition(surface);
        if(numericErrors.Count>0)return new(null,numericErrors);
        var errors = Validate(surface);
        if (errors.Count > 0) return new(null, errors);
        var region=ResolveRegion(surface,geometry);if(region.Error is not null)return OperationCalculationResult.Failed(region.Error);var lanes=region.Contour is null?Paths(surface).ToArray():RegionPaths(surface,region.Contour).ToArray();if(lanes.Length==0)return OperationCalculationResult.Failed(new OperationValidationError("region-empty","The selected surfacing region is too small for the requested strategy and stepover."));

        var path = new ToolpathOperation { SourceOperationId=surface.Id, Name=surface.Name, ToolId=surface.Tool.SourceToolId, MaterialSheetId=surface.MaterialSheetId,SourceGeometryIds=[..surface.GeometryIds] };
        path.Moves.Add(new ToolChange(surface.Tool.ToolNumber, surface.Tool.Name));
        path.Moves.Add(new SpindleStart(surface.SpindleRpm));
        foreach (var depth in ProfileToolpathGenerator.CalculatePassDepths(surface.StartDepthMm, surface.CutDepthMm, surface.PassDepthMm))
        {
            foreach (var lane in lanes)
            {
                path.Moves.Add(new RapidMove(lane[0], surface.SafeZMm));
                path.Moves.Add(new LinearMove(lane[0], -depth, surface.PlungeRateMmPerMin));
                foreach(var point in lane.Skip(1))path.Moves.Add(new LinearMove(point,-depth,surface.FeedRateMmPerMin));
                if(lane.Count>2)path.Moves.Add(new LinearMove(lane[0],-depth,surface.FeedRateMmPerMin));
                path.Moves.Add(new RapidMove(lane.Count>2?lane[0]:lane[^1], surface.SafeZMm));
            }
        }
        path.Moves.Add(new SpindleStop());
        return OperationNumerics.ValidateResult(new(path, []));
    }

    private static (IReadOnlyList<Point2D>? Contour,OperationValidationError? Error) ResolveRegion(SurfaceOperationDefinition surface,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        if(surface.GeometryIds.Count==0)return(default,default);if(surface.GeometryIds.Count!=1)return(default,new("region-count","Surfacing accepts one closed boundary. Combine or repair vectors before calculating."));if(!geometry.TryGetValue(surface.GeometryIds[0],out var entity))return(default,new("missing-geometry","The selected surfacing boundary no longer exists."));try{return(ClipperGeometryAdapter.ToContour(entity),default);}catch(ArgumentException exception){return(default,new("invalid-region",$"Surfacing requires one finite, simple closed boundary. {exception.Message}"));}
    }

    private static List<OperationValidationError> Validate(SurfaceOperationDefinition s)
    {
        var errors=new List<OperationValidationError>();
        if(s.Tool.DiameterMm<=0)errors.Add(new("tool","Select a valid surfacing tool."));
        if(s.WidthMm<=0||s.HeightMm<=0)errors.Add(new("stock","Surface width and height must be positive."));
        if(s.CutDepthMm<=s.StartDepthMm||s.PassDepthMm<=0)errors.Add(new("depth","Surface depth and pass depth are invalid."));
        if(s.StepOverMm<=0||s.StepOverMm>s.Tool.DiameterMm)errors.Add(new("stepover","Surface stepover must be positive and no larger than the cutter diameter."));
        if(s.OvertravelMm<0||s.OvertravelMm>s.Tool.DiameterMm/2)errors.Add(new("overtravel","Surface overtravel must be between zero and the cutter radius."));
        if(s.SafeZMm<=0||s.FeedRateMmPerMin<=0||s.PlungeRateMmPerMin<=0||s.SpindleRpm<=0)errors.Add(new("rates","Surface rates, safe Z and spindle speed must be positive."));
        return errors;
    }

    private static IEnumerable<IReadOnlyList<Point2D>> Paths(SurfaceOperationDefinition s)
    {
        if(s.Strategy==SurfaceStrategy.Offset)
        {
            var originX=s.OriginXmm-s.OvertravelMm;var originY=s.OriginYmm-s.OvertravelMm;var width=s.WidthMm+s.OvertravelMm*2;var height=s.HeightMm+s.OvertravelMm*2;for(var inset=0d;inset*2<Math.Min(width,height);inset+=s.StepOverMm)yield return [new(originX+inset,originY+inset),new(originX+width-inset,originY+inset),new(originX+width-inset,originY+height-inset),new(originX+inset,originY+height-inset)];
            yield break;
        }
        var alongX=s.Strategy!=SurfaceStrategy.RasterY;
        var laneLength=alongX?s.WidthMm:s.HeightMm;
        var crossLength=alongX?s.HeightMm:s.WidthMm;
        var count=Math.Max(1,(int)Math.Ceiling(crossLength/s.StepOverMm));
        for(var i=0;i<=count;i++)
        {
            var cross=Math.Min(crossLength,i*s.StepOverMm);
            var a=alongX?new Point2D(s.OriginXmm-s.OvertravelMm,s.OriginYmm+cross):new Point2D(s.OriginXmm+cross,s.OriginYmm-s.OvertravelMm);
            var b=alongX?new Point2D(s.OriginXmm+laneLength+s.OvertravelMm,s.OriginYmm+cross):new Point2D(s.OriginXmm+cross,s.OriginYmm+laneLength+s.OvertravelMm);
            yield return i%2==0?[a,b]:[b,a];
        }
    }

    private static IEnumerable<IReadOnlyList<Point2D>> RegionPaths(SurfaceOperationDefinition surface,IReadOnlyList<Point2D> source)
    {
        var expanded=surface.OvertravelMm>0?ClipperGeometryAdapter.OffsetRound(source,surface.OvertravelMm):[source];if(expanded.Count==0)yield break;
        if(surface.Strategy==SurfaceStrategy.Offset)
        {
            var current=expanded;for(var guard=0;guard<10000&&current.Count>0;guard++){foreach(var contour in current.OrderByDescending(path=>Math.Abs(ClipperGeometryAdapter.SignedArea(path))))yield return contour;current=current.SelectMany(contour=>ClipperGeometryAdapter.Offset(contour,-surface.StepOverMm)).ToArray();}yield break;
        }
        var alongX=surface.Strategy==SurfaceStrategy.RasterX;var bounds=BoundingBox.FromPoints(expanded.SelectMany(contour=>contour));var minimum=alongX?bounds.MinY:bounds.MinX;var maximum=alongX?bounds.MaxY:bounds.MaxX;var span=maximum-minimum;if(span<=GeometryValidation.CoordinateToleranceMm)yield break;var count=Math.Max(1,(int)Math.Ceiling(span/surface.StepOverMm));var reverse=false;
        for(var index=0;index<=count;index++)
        {
            var coordinate=Math.Min(maximum,minimum+index*surface.StepOverMm);if(index==count)coordinate=Math.Max(minimum,maximum-Math.Min(1e-7,span/1_000_000));var intersections=expanded.SelectMany(contour=>ScanIntersections(contour,coordinate,alongX)).Order().DistinctBy(value=>Math.Round(value,6)).ToArray();for(var pair=0;pair+1<intersections.Length;pair+=2){var first=alongX?new Point2D(intersections[pair],coordinate):new Point2D(coordinate,intersections[pair]);var second=alongX?new Point2D(intersections[pair+1],coordinate):new Point2D(coordinate,intersections[pair+1]);yield return reverse?[second,first]:[first,second];reverse=!reverse;}
        }
    }

    private static IEnumerable<double> ScanIntersections(IReadOnlyList<Point2D> contour,double coordinate,bool horizontal)
    {
        for(var index=0;index<contour.Count;index++){var a=contour[index];var b=contour[(index+1)%contour.Count];var aCross=horizontal?a.Y:a.X;var bCross=horizontal?b.Y:b.X;if(!((aCross<=coordinate&&bCross>coordinate)||(bCross<=coordinate&&aCross>coordinate)))continue;var fraction=(coordinate-aCross)/(bCross-aCross);yield return horizontal?a.X+(b.X-a.X)*fraction:a.Y+(b.Y-a.Y)*fraction;}
    }
}
