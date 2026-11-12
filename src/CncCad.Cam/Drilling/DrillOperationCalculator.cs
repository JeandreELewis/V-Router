using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Drilling;

public sealed class DrillOperationCalculator : ICamOperationCalculator
{
    public OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        if (operation is not DrillOperationDefinition drill) return OperationCalculationResult.Failed(new OperationValidationError("unsupported", "Operation is not drilling."));
        var errors = Validate(drill, geometry);
        if (errors.Count > 0) return new(null, errors);
        var points = drill.GeometryIds.Select(id => Center(geometry[id])).ToList();
        if (drill.Ordering == DrillOrdering.Optimized) points = NearestNeighbor(points);
        var toolpath = new ToolpathOperation { SourceOperationId=drill.Id,Name=drill.Name,ToolId=drill.Tool.SourceToolId,MaterialSheetId=drill.MaterialSheetId,SourceGeometryIds=[..drill.GeometryIds] };
        toolpath.Moves.Add(new ToolChange(drill.Tool.ToolNumber, drill.Tool.Name));
        toolpath.Moves.Add(new SpindleStart(drill.SpindleRpm));
        foreach (var point in points)
        {
            toolpath.Moves.Add(new RapidMove(point, drill.SafeZMm));
            toolpath.Moves.Add(new RapidMove(point, drill.RetractZMm));
            if (drill.Cycle == DrillCycle.Peck)
            {
                for (var depth = Math.Min(drill.PeckDepthMm, drill.CutDepthMm);; depth = Math.Min(depth + drill.PeckDepthMm, drill.CutDepthMm))
                {
                    toolpath.Moves.Add(new LinearMove(point, -depth, drill.PlungeRateMmPerMin));
                    if (depth >= drill.CutDepthMm - 1e-9) break;
                    toolpath.Moves.Add(new RapidMove(point, drill.RetractZMm));
                }
            }
            else toolpath.Moves.Add(new LinearMove(point, -drill.CutDepthMm, drill.PlungeRateMmPerMin));
            if (drill.DwellSeconds > 0) toolpath.Moves.Add(new DwellMove(drill.DwellSeconds));
            toolpath.Moves.Add(new RapidMove(point, drill.SafeZMm));
        }
        toolpath.Moves.Add(new SpindleStop());
        return new(toolpath, []);
    }

    private static List<OperationValidationError> Validate(DrillOperationDefinition drill, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        var errors = new List<OperationValidationError>();
        if (drill.GeometryIds.Count == 0) errors.Add(new("geometry", "Select circles or drill points."));
        if (drill.Tool.DiameterMm <= 0 || drill.CutDepthMm <= 0 || drill.SafeZMm <= drill.RetractZMm || drill.RetractZMm < 0 || drill.PlungeRateMmPerMin <= 0 || drill.SpindleRpm <= 0) errors.Add(new("parameters", "Drilling tool, depth, Z heights, feed, or spindle speed are invalid."));
        if (drill.Cycle == DrillCycle.Peck && drill.PeckDepthMm <= 0) errors.Add(new("peck", "Peck depth must be positive."));
        if (drill.DwellSeconds < 0) errors.Add(new("dwell", "Dwell cannot be negative."));
        foreach (var id in drill.GeometryIds)
            if (!geometry.TryGetValue(id, out var entity)) errors.Add(new("missing", $"Referenced drill geometry {id} is missing."));
            else if (entity is not CircleEntity and not DrillPointEntity) errors.Add(new("geometry", "Drilling supports circle centers and explicit drill points."));
        return errors;
    }

    private static Point2D Center(CadEntity entity) => entity switch
    {
        CircleEntity circle => circle.Transform.Transform(circle.Center),
        DrillPointEntity point => point.Transform.Transform(point.Position),
        _ => throw new ArgumentException("Unsupported drilling geometry.")
    };

    private static List<Point2D> NearestNeighbor(List<Point2D> source)
    {
        var result = new List<Point2D>(); var current = new Point2D(0, 0);
        while (source.Count > 0) { var next = source.MinBy(point => current.DistanceTo(point)); result.Add(next); source.Remove(next); current = next; }
        return result;
    }
}
