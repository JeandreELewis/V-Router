using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed class VCarveOperationCalculator:ICamOperationCalculator
{
    public OperationCalculationResult Calculate(CamOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        if(operation is not VCarveOperationDefinition carve)return OperationCalculationResult.Failed(new OperationValidationError("unsupported","Operation is not V-carving."));
        var errors=Validate(carve,geometry);if(errors.Count>0)return new(null,errors);
        var contours=new List<(Guid Id,IReadOnlyList<Point2D> Points)>();foreach(var id in carve.GeometryIds)foreach(var contour in Contours(geometry[id]))contours.Add((id,contour));
        contours=contours.OrderByDescending(x=>ContainmentDepth(x.Points,contours.Select(c=>c.Points))).ThenBy(x=>x.Points[0].X).ThenBy(x=>x.Points[0].Y).ToList();
        var path=new ToolpathOperation{SourceOperationId=carve.Id,Name=carve.Name,ToolId=carve.Tool.SourceToolId,MaterialSheetId=carve.MaterialSheetId,SourceGeometryIds=[..carve.GeometryIds]};path.Moves.Add(new ToolChange(carve.Tool.ToolNumber,carve.Tool.Name));path.Moves.Add(new SpindleStart(carve.SpindleRpm));
        var half=carve.Tool.VBitAngleDegrees!.Value*Math.PI/360;var tangent=Math.Tan(half);var tipRadius=(carve.Tool.TipDiameterMm??0)/2;var current=new Point2D(0,0);
        foreach(var source in contours){var distance=Math.Max(carve.OffsetStepOverMm,tipRadius+carve.OffsetStepOverMm);var capped=false;while(true){var rings=ClipperGeometryAdapter.Offset(source.Points,-distance);if(rings.Count==0)break;var depth=Math.Min(carve.MaximumDepthMm,Math.Max(0,(distance-tipRadius)/tangent));if(depth<=1e-9){distance+=carve.OffsetStepOverMm;continue;}foreach(var ring in rings.OrderBy(x=>current.DistanceTo(x[0]))){var points=ring.ToList();var nearest=Enumerable.Range(0,points.Count).MinBy(i=>current.DistanceTo(points[i]));points=[..points.Skip(nearest),..points.Take(nearest),points[nearest]];path.Moves.Add(new RapidMove(points[0],carve.SafeZMm));path.Moves.Add(new LinearMove(points[0],-depth,carve.PlungeRateMmPerMin));foreach(var point in points.Skip(1))path.Moves.Add(new LinearMove(point,-depth,carve.FeedRateMmPerMin));path.Moves.Add(new RapidMove(points[^1],carve.SafeZMm));current=points[^1];}if(depth>=carve.MaximumDepthMm-1e-9){if(capped&&!carve.ClearFlatRegions)break;capped=true;}distance+=carve.OffsetStepOverMm;}}
        path.Moves.Add(new SpindleStop());return new(path,[]);
    }
    private static List<OperationValidationError> Validate(VCarveOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        var errors=new List<OperationValidationError>();if(operation.GeometryIds.Count==0)errors.Add(new("geometry","Select one or more closed outline vectors."));if(operation.Tool.Type is not ToolType.VBit and not ToolType.EngravingBit)errors.Add(new("tool","V-carving requires a V-bit or engraving bit."));if(operation.Tool.VBitAngleDegrees is not (>0 and <180))errors.Add(new("angle","The selected tool requires an included angle between 0 and 180 degrees."));if(operation.Tool.TipDiameterMm is <0)errors.Add(new("tip","Tip diameter cannot be negative."));if(operation.MaximumDepthMm<=0||operation.OffsetStepOverMm<=0)errors.Add(new("depth","Maximum depth and offset step-over must be positive."));if(operation.SafeZMm<=0||operation.FeedRateMmPerMin<=0||operation.PlungeRateMmPerMin<=0)errors.Add(new("rates","Safe Z, feed, and plunge must be positive."));foreach(var id in operation.GeometryIds){if(!geometry.TryGetValue(id,out var entity))errors.Add(new("missing",$"Referenced vector {id} is missing."));else try{if(!Contours(entity).Any())errors.Add(new("open",$"{entity.GetType().Name} contains no closed outline."));}catch(Exception ex){errors.Add(new("geometry",ex.Message));}}return errors;
    }
    private static IEnumerable<IReadOnlyList<Point2D>> Contours(CadEntity entity)=>entity switch
    {
        TextEntity{FontKind:FontKind.SingleLine}=>throw new ArgumentException("Single-line text must be engraved, not V-carved."),
        TextEntity text=>text.Outlines.Where(x=>x.IsClosed).Select(x=>ClipperGeometryAdapter.ToContour(new VectorPathEntity{Transform=text.Transform,Figures=VectorPathGeometry.Clone([x])})),
        VectorPathEntity path=>path.Figures.Where(x=>x.IsClosed).Select(x=>ClipperGeometryAdapter.ToContour(new VectorPathEntity{Transform=path.Transform,Figures=VectorPathGeometry.Clone([x])})),
        RectangleEntity or CircleEntity or PolylineEntity=>[ClipperGeometryAdapter.ToContour(entity)],
        _=>[]
    };
    private static int ContainmentDepth(IReadOnlyList<Point2D> contour,IEnumerable<IReadOnlyList<Point2D>> all)=>all.Count(other=>!ReferenceEquals(other,contour)&&Math.Abs(ClipperGeometryAdapter.SignedArea(other))>Math.Abs(ClipperGeometryAdapter.SignedArea(contour))&&ClipperGeometryAdapter.Contains(other,contour[0]));
}
