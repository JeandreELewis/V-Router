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
        var errors = Validate(surface);
        if (errors.Count > 0) return new(null, errors);

        var path = new ToolpathOperation { SourceOperationId=surface.Id, Name=surface.Name, ToolId=surface.Tool.SourceToolId, MaterialSheetId=surface.MaterialSheetId };
        path.Moves.Add(new ToolChange(surface.Tool.ToolNumber, surface.Tool.Name));
        path.Moves.Add(new SpindleStart(surface.SpindleRpm));
        foreach (var depth in ProfileToolpathGenerator.CalculatePassDepths(surface.StartDepthMm, surface.CutDepthMm, surface.PassDepthMm))
        {
            foreach (var lane in Paths(surface))
            {
                path.Moves.Add(new RapidMove(lane[0], surface.SafeZMm));
                path.Moves.Add(new LinearMove(lane[0], -depth, surface.PlungeRateMmPerMin));
                foreach(var point in lane.Skip(1))path.Moves.Add(new LinearMove(point,-depth,surface.FeedRateMmPerMin));
                if(lane.Count>2)path.Moves.Add(new LinearMove(lane[0],-depth,surface.FeedRateMmPerMin));
                path.Moves.Add(new RapidMove(lane.Count>2?lane[0]:lane[^1], surface.SafeZMm));
            }
        }
        path.Moves.Add(new SpindleStop());
        return new(path, []);
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
}
