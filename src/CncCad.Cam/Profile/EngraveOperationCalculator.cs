using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed class CompositeOperationCalculator(params ICamOperationCalculator[] calculators):ICamOperationCalculator
{
    public OperationCalculationResult Calculate(CamOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry){foreach(var calculator in calculators){var result=calculator.Calculate(operation,geometry);if(result.Errors.All(x=>x.Code!="unsupported"))return result;}return OperationCalculationResult.Failed(new OperationValidationError("unsupported",$"{operation.GetType().Name} is not supported."));}
}
public sealed class EngraveOperationCalculator:ICamOperationCalculator
{
    public OperationCalculationResult Calculate(CamOperationDefinition operation,IReadOnlyDictionary<Guid,CadEntity> geometry)
    {
        if(operation is not EngraveOperationDefinition engrave)return OperationCalculationResult.Failed(new OperationValidationError("unsupported","Operation is not engraving."));var errors=Validate(engrave,geometry);if(errors.Count>0)return new(null,errors);var paths=engrave.GeometryIds.SelectMany(id=>Paths(geometry[id]).Select(path=>(Id:id,Points:path))).Where(x=>x.Points.Count>=2).ToList();var toolpath=new ToolpathOperation{SourceOperationId=engrave.Id,Name=engrave.Name,ToolId=engrave.Tool.SourceToolId,MaterialSheetId=engrave.MaterialSheetId,SourceGeometryIds=[..engrave.GeometryIds]};toolpath.Moves.Add(new ToolChange(engrave.Tool.ToolNumber,engrave.Tool.Name));toolpath.Moves.Add(new SpindleStart(engrave.SpindleRpm));var current=new Point2D(0,0);while(paths.Count>0){var index=engrave.Direction==EngraveDirection.Optimize?Enumerable.Range(0,paths.Count).MinBy(i=>Math.Min(current.DistanceTo(paths[i].Points[0]),current.DistanceTo(paths[i].Points[^1]))):0;var path=paths[index].Points.ToList();paths.RemoveAt(index);if(engrave.Direction==EngraveDirection.Reverse||engrave.Direction==EngraveDirection.Optimize&&current.DistanceTo(path[^1])<current.DistanceTo(path[0]))path.Reverse();foreach(var depth in ProfileToolpathGenerator.CalculatePassDepths(engrave.StartDepthMm,engrave.CutDepthMm,engrave.PassDepthMm)){toolpath.Moves.Add(new RapidMove(path[0],engrave.SafeZMm));toolpath.Moves.Add(new LinearMove(path[0],-depth,engrave.PlungeRateMmPerMin));foreach(var point in path.Skip(1))toolpath.Moves.Add(new LinearMove(point,-depth,engrave.FeedRateMmPerMin));toolpath.Moves.Add(new RapidMove(path[^1],engrave.SafeZMm));}current=path[^1];}toolpath.Moves.Add(new SpindleStop());return new(toolpath,[]);
    }
    private static List<OperationValidationError> Validate(EngraveOperationDefinition e,IReadOnlyDictionary<Guid,CadEntity> geometry){var errors=new List<OperationValidationError>();if(e.GeometryIds.Count==0)errors.Add(new("geometry","Select engraving vectors."));if(e.Tool.DiameterMm<=0)errors.Add(new("tool","Select a valid engraving tool."));if(e.CutDepthMm<=e.StartDepthMm||e.PassDepthMm<=0)errors.Add(new("depth","Engraving depth and pass depth are invalid."));if(e.FeedRateMmPerMin<=0||e.PlungeRateMmPerMin<=0||e.SafeZMm<=0)errors.Add(new("rates","Engraving rates and safe Z must be positive."));foreach(var id in e.GeometryIds){if(!geometry.TryGetValue(id,out var entity))errors.Add(new("missing",$"Referenced vector {id} is missing."));else if(Paths(entity).All(x=>x.Count<2))errors.Add(new("degenerate",$"{entity.GetType().Name} contains no machinable stroke."));}return errors;}
    private static IEnumerable<IReadOnlyList<Point2D>> Paths(CadEntity entity)=>entity switch
    {
        LineEntity line=>[EntityGeometry.WorldPoints(line)],PolylineEntity poly=>[Close(EntityGeometry.WorldPoints(poly),poly.IsClosed)],ArcEntity arc=>[EntityGeometry.WorldPoints(arc)],CircleEntity circle=>[Close(EntityGeometry.WorldPoints(circle),true)],EllipseEntity ellipse=>[Close(EntityGeometry.WorldPoints(ellipse),true)],VectorPathEntity path=>path.Figures.Select(f=>(IReadOnlyList<Point2D>)VectorPathGeometry.Flatten([f],.02).Select(path.Transform.Transform).ToArray()),TextEntity text=>text.Outlines.Select(f=>(IReadOnlyList<Point2D>)VectorPathGeometry.Flatten([f],.02).Select(text.Transform.Transform).ToArray()),_=>[]
    };
    private static IReadOnlyList<Point2D> Close(IReadOnlyList<Point2D> points,bool closed)=>closed&&points.Count>1&&points[0].DistanceTo(points[^1])>1e-9?[..points,points[0]]:points;
}
