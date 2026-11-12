using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Pocket;

public sealed class PocketOperationCalculator : ICamOperationCalculator
{
    public OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        if (operation is not PocketOperationDefinition pocket) return OperationCalculationResult.Failed(new OperationValidationError("unsupported", "Operation is not a pocket."));
        var errors = Validate(pocket, geometry);
        if (errors.Count > 0) return new(null, errors);
        var contours = pocket.GeometryIds.Select(id => ClipperGeometryAdapter.ToContour(geometry[id])).ToArray();
        var paths = ClipperGeometryAdapter.PocketOffsets(contours, pocket.Tool.DiameterMm / 2 + pocket.AllowanceMm, pocket.StepOverMm).ToList();
        if (paths.Count == 0) return OperationCalculationResult.Failed(new OperationValidationError("collapsed", "The selected pocket is too small for this cutter and allowance."));

        var toolpath = new ToolpathOperation { SourceOperationId=pocket.Id,Name=pocket.Name,ToolId=pocket.Tool.SourceToolId,MaterialSheetId=pocket.MaterialSheetId,SourceGeometryIds=[..pocket.GeometryIds] };
        toolpath.Moves.Add(new ToolChange(pocket.Tool.ToolNumber, pocket.Tool.Name));
        toolpath.Moves.Add(new SpindleStart(pocket.SpindleRpm));
        var ordered = paths.OrderBy(path => Math.Abs(ClipperGeometryAdapter.SignedArea(path))).ToArray();
        foreach (var depth in ProfileToolpathGenerator.CalculatePassDepths(pocket.StartDepthMm, pocket.CutDepthMm, pocket.PassDepthMm))
        foreach (var raw in ordered)
        {
            var path = raw.ToList();
            var clockwise = ClipperGeometryAdapter.SignedArea(path) < 0;
            if (clockwise != (pocket.Direction == CutDirection.Climb)) path.Reverse();
            var start = path[0];
            toolpath.Moves.Add(new RapidMove(start, pocket.SafeZMm));
            if (pocket.EntryMode == PocketEntryMode.LinearRamp && pocket.RampLengthMm > 0 && path.Count > 1)
            {
                toolpath.Moves.Add(new LinearMove(start, -pocket.StartDepthMm, pocket.PlungeRateMmPerMin));
                var travelled = 0d;
                var index = 1;
                while (travelled < pocket.RampLengthMm && index <= path.Count)
                {
                    var next = path[index % path.Count];
                    travelled += path[(index - 1) % path.Count].DistanceTo(next);
                    var z = -pocket.StartDepthMm - (depth - pocket.StartDepthMm) * Math.Min(1, travelled / pocket.RampLengthMm);
                    toolpath.Moves.Add(new LinearMove(next, z, pocket.FeedRateMmPerMin));
                    index++;
                }
                if (travelled < pocket.RampLengthMm) toolpath.Moves.Add(new LinearMove(start, -depth, pocket.FeedRateMmPerMin));
            }
            else toolpath.Moves.Add(new LinearMove(start, -depth, pocket.PlungeRateMmPerMin));
            foreach (var point in path.Skip(1)) toolpath.Moves.Add(new LinearMove(point, -depth, pocket.FeedRateMmPerMin));
            toolpath.Moves.Add(new LinearMove(start, -depth, pocket.FeedRateMmPerMin));
            toolpath.Moves.Add(new RapidMove(start, pocket.SafeZMm));
        }
        toolpath.Moves.Add(new SpindleStop());
        return new(toolpath, []);
    }

    private static List<OperationValidationError> Validate(PocketOperationDefinition pocket, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        var errors = new List<OperationValidationError>();
        if (pocket.GeometryIds.Count == 0) errors.Add(new("geometry", "Select at least one closed pocket boundary."));
        if (pocket.Tool.DiameterMm <= 0) errors.Add(new("tool", "Select a valid pocket tool."));
        if (pocket.CutDepthMm <= pocket.StartDepthMm || pocket.PassDepthMm <= 0 || pocket.StepOverMm <= 0 || pocket.StepOverMm > pocket.Tool.DiameterMm) errors.Add(new("depth", "Pocket depths and stepover are invalid."));
        if (pocket.AllowanceMm < 0 || pocket.SafeZMm <= 0 || pocket.FeedRateMmPerMin <= 0 || pocket.PlungeRateMmPerMin <= 0 || pocket.SpindleRpm <= 0) errors.Add(new("parameters", "Pocket allowance, rates, safe Z, or spindle speed are invalid."));
        foreach (var id in pocket.GeometryIds)
        {
            if (!geometry.TryGetValue(id, out var entity)) errors.Add(new("missing", $"Referenced vector {id} is missing."));
            else try { _ = ClipperGeometryAdapter.ToContour(entity); } catch (ArgumentException ex) { errors.Add(new("geometry", ex.Message)); }
        }
        return errors;
    }
}
