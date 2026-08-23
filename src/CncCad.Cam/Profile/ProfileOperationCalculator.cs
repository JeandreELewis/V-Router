using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed class ProfileOperationCalculator : ICamOperationCalculator
{
    public OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        if (operation is not ProfileOperationDefinition profile) return OperationCalculationResult.Failed(new OperationValidationError("unsupported", "Operation type is not supported."));
        var errors = Validate(profile, geometry); if (errors.Count > 0) return new(null, errors);
        var contours = new List<Contour>();
        foreach (var id in profile.GeometryIds)
        {
            var original = ClipperGeometryAdapter.ToContour(geometry[id]);
            var delta = profile.Side switch { ProfileSide.Outside => profile.Tool.DiameterMm / 2, ProfileSide.Inside => -profile.Tool.DiameterMm / 2, _ => 0 };
            var offset = ClipperGeometryAdapter.Offset(original, delta);
            if (offset.Count == 0) return OperationCalculationResult.Failed(new OperationValidationError("collapsed", $"Inside offset collapsed geometry {id}."));
            var depth = profile.GeometryIds.Where(other => other != id && geometry.ContainsKey(other)).Select(other => ClipperGeometryAdapter.ToContour(geometry[other])).Count(container => ClipperGeometryAdapter.Contains(container, original[0]));
            contours.AddRange(offset.Select(path => new Contour(path, depth,id)));
        }
        var ordered = OrderContours(contours);
        var toolpath = new ToolpathOperation { SourceOperationId = profile.Id, Name = profile.Name, ToolId = profile.Tool.SourceToolId,MaterialSheetId=profile.MaterialSheetId,SourceGeometryIds=[..profile.GeometryIds] };
        toolpath.Moves.Add(new ToolChange(profile.Tool.ToolNumber, profile.Tool.Name)); toolpath.Moves.Add(new SpindleStart(profile.SpindleRpm));
        var current = new Point2D(0, 0);
        foreach (var contour in ordered)
        {
            var reversed=(ClipperGeometryAdapter.SignedArea(contour.Points)<0)!=ShouldBeClockwise(profile);var path = Orient(contour.Points, ShouldBeClockwise(profile)); if(!profile.Tabs.Enabled)path = RotateNearest(path, current); current = path[0];var contourTabs=reversed?ReverseTabs(profile.Tabs,contour.SourceGeometryId):profile.Tabs;
            var tabs=ProfileTabEngine.Validate(path,contourTabs,profile.CutDepthMm,double.PositiveInfinity,contour.SourceGeometryId);if(!tabs.Success)return OperationCalculationResult.Failed(tabs.Issues.Select(x=>new OperationValidationError(x.Code,x.Message)).ToArray());
            toolpath.Moves.Add(new RapidMove(current, profile.SafeZMm));
            foreach (var depth in ProfileToolpathGenerator.CalculatePassDepths(profile.StartDepthMm, profile.CutDepthMm, profile.PassDepthMm))
            {
                toolpath.Moves.Add(new LinearMove(current, -depth, profile.PlungeRateMmPerMin));
                ProfileTabEngine.AddClosedPass(toolpath.Moves,path,depth,profile.CutDepthMm,contourTabs,profile.FeedRateMmPerMin,contour.SourceGeometryId);
            }
            toolpath.Moves.Add(new RapidMove(path[0], profile.SafeZMm)); current = path[0];
        }
        toolpath.Moves.Add(new SpindleStop()); return new(toolpath, []);
    }

    private static List<OperationValidationError> Validate(ProfileOperationDefinition p, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        var errors = new List<OperationValidationError>();
        if (p.GeometryIds.Count == 0) errors.Add(new("geometry", "Select at least one closed contour."));
        if (p.Tool.DiameterMm <= 0) errors.Add(new("tool", "Tool diameter must be greater than zero."));
        if (p.CutDepthMm <= p.StartDepthMm || p.PassDepthMm <= 0) errors.Add(new("depth", "Cut depth must exceed start depth and pass depth must be positive."));
        if (p.FeedRateMmPerMin <= 0 || p.PlungeRateMmPerMin <= 0 || p.SafeZMm <= 0 || p.SpindleRpm < 0) errors.Add(new("rates", "Feed, plunge, safe Z and spindle values are invalid."));
        foreach (var id in p.GeometryIds)
        {
            if (!geometry.TryGetValue(id, out var entity)) { errors.Add(new("missing", $"Referenced geometry {id} is missing.")); continue; }
            try { _ = ClipperGeometryAdapter.ToContour(entity); } catch (ArgumentException ex) { errors.Add(new("geometry", ex.Message)); }
        }
        return errors;
    }
    private static bool ShouldBeClockwise(ProfileOperationDefinition profile) => profile.Direction == CutDirection.Climb ? profile.Side != ProfileSide.Inside : profile.Side == ProfileSide.Inside;
    private static List<Point2D> Orient(IReadOnlyList<Point2D> path, bool clockwise)
    {
        var result = path.ToList(); if ((ClipperGeometryAdapter.SignedArea(result) < 0) != clockwise) result.Reverse(); return result;
    }
    private static List<Point2D> RotateNearest(List<Point2D> path, Point2D current)
    {
        var index = Enumerable.Range(0, path.Count).MinBy(i => path[i].DistanceTo(current)); return path.Skip(index).Concat(path.Take(index)).ToList();
    }
    private static ProfileTabSettings ReverseTabs(ProfileTabSettings source,Guid geometryId){var copy=source.Copy();copy.ManualPlacements=[..copy.ManualPlacements.Select(x=>x.GeometryId is null||x.GeometryId==geometryId?x with{NormalizedDistance=(1-x.NormalizedDistance)%1}:x)];return copy;}
    private static IReadOnlyList<Contour> OrderContours(List<Contour> contours)
    {
        var ordered = new List<Contour>(); var current = new Point2D(0, 0);
        foreach (var tier in contours.GroupBy(c => c.Depth).OrderByDescending(g => g.Key))
        {
            var remaining = tier.ToList(); while (remaining.Count > 0) { var next = remaining.MinBy(c => c.Points.Min(p => p.DistanceTo(current)))!; ordered.Add(next); current = next.Points.MinBy(p => p.DistanceTo(current)); remaining.Remove(next); }
        }
        return ordered;
    }
    private sealed record Contour(IReadOnlyList<Point2D> Points, int Depth,Guid SourceGeometryId);
}
