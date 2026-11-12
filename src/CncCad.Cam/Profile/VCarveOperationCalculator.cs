using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed class VCarveOperationCalculator:ICamOperationCalculator
{
    public Type OperationType=>typeof(VCarveOperationDefinition);
    public OperationCalculationResult Calculate(CamOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        if(operation is not VCarveOperationDefinition carve)return OperationCalculationResult.Failed(new OperationValidationError("unsupported","Operation is not V-carving."));
        var numericErrors=OperationNumerics.ValidateDefinition(carve);if(numericErrors.Count>0)return new(null,numericErrors);
        var errors=Validate(carve,geometry);if(errors.Count>0)return new(null,errors);
        var contours=new List<IReadOnlyList<Point2D>>();foreach(var id in carve.GeometryIds)contours.AddRange(Contours(geometry[id]));
        var path=new ToolpathOperation{SourceOperationId=carve.Id,Name=carve.Name,ToolId=carve.Tool.SourceToolId,MaterialSheetId=carve.MaterialSheetId,SourceGeometryIds=[..carve.GeometryIds]};path.Moves.Add(new ToolChange(carve.Tool.ToolNumber,carve.Tool.Name));path.Moves.Add(new SpindleStart(carve.SpindleRpm));
        var half=carve.Tool.VBitAngleDegrees!.Value*Math.PI/360;var tangent=Math.Tan(half);var tipRadius=(carve.Tool.TipDiameterMm??0)/2;var current=new Point2D(0,0);var generated=0;
        foreach(var segment in MedialAxisBuilder.Build(contours,Math.Max(.1,carve.OffsetStepOverMm)))
        {
            var start=segment.Start;var end=segment.End;var startClearance=segment.StartClearanceMm;var endClearance=segment.EndClearanceMm;if(current.DistanceTo(end)<current.DistanceTo(start)){(start,end)=(end,start);(startClearance,endClearance)=(endClearance,startClearance);}var midpoint=new Point2D((start.X+end.X)/2,(start.Y+end.Y)/2);var midpointClearance=MedialAxisBuilder.BoundaryDistance(contours,midpoint);var startDepth=Depth(startClearance);var midpointDepth=Depth(midpointClearance);var endDepth=Depth(endClearance);if(Math.Max(startDepth,Math.Max(midpointDepth,endDepth))<=1e-7)continue;path.Moves.Add(new RapidMove(start,carve.SafeZMm));path.Moves.Add(new LinearMove(start,-startDepth,carve.PlungeRateMmPerMin));path.Moves.Add(new LinearMove(midpoint,-midpointDepth,carve.FeedRateMmPerMin));path.Moves.Add(new LinearMove(end,-endDepth,carve.FeedRateMmPerMin));path.Moves.Add(new RapidMove(end,carve.SafeZMm));current=end;generated++;
        }
        if(carve.ClearFlatRegions)
        {
            var effectiveRadius=tipRadius+carve.MaximumDepthMm*tangent;foreach(var ring in ClipperGeometryAdapter.PocketOffsets(contours,effectiveRadius,carve.OffsetStepOverMm).OrderBy(ring=>ring.Min(point=>point.X)).ThenBy(ring=>ring.Min(point=>point.Y))){var points=ring.ToList();var nearest=Enumerable.Range(0,points.Count).OrderBy(index=>current.DistanceTo(points[index])).ThenBy(index=>index).First();points=[..points.Skip(nearest),..points.Take(nearest),points[nearest]];path.Moves.Add(new RapidMove(points[0],carve.SafeZMm));path.Moves.Add(new LinearMove(points[0],-carve.MaximumDepthMm,carve.PlungeRateMmPerMin));foreach(var point in points.Skip(1))path.Moves.Add(new LinearMove(point,-carve.MaximumDepthMm,carve.FeedRateMmPerMin));path.Moves.Add(new RapidMove(points[^1],carve.SafeZMm));current=points[^1];generated++;}
        }
        if(generated==0)return OperationCalculationResult.Failed(new OperationValidationError("collapsed","The selected outlines are too narrow for this V-bit tip diameter. Choose a finer tip or wider geometry."));
        path.Moves.Add(new SpindleStop());return OperationNumerics.ValidateResult(new(path,[]));
        double Depth(double clearance)=>Math.Min(carve.MaximumDepthMm,Math.Max(0,(clearance-tipRadius)/tangent));
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
        RectangleEntity or CircleEntity or PolylineEntity{IsClosed:true}=>[ClipperGeometryAdapter.ToContour(entity)],
        _=>[]
    };
}
