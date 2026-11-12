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
            var delta = profile.Side switch { ProfileSide.Outside => profile.Tool.DiameterMm / 2 + profile.AllowanceMm, ProfileSide.Inside => -(profile.Tool.DiameterMm / 2 + profile.AllowanceMm), _ => profile.AllowanceMm };
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
            var reversed=(ClipperGeometryAdapter.SignedArea(contour.Points)<0)!=ShouldBeClockwise(profile);var path = Orient(contour.Points, ShouldBeClockwise(profile));
            path = profile.StartPointFraction > 0 ? RotateFraction(path, profile.StartPointFraction) : !profile.Tabs.Enabled ? RotateNearest(path, current) : path;
            current = path[0];var contourTabs=reversed?ReverseTabs(profile.Tabs,contour.SourceGeometryId):profile.Tabs;
            var tabs=ProfileTabEngine.Validate(path,contourTabs,profile.CutDepthMm,double.PositiveInfinity,contour.SourceGeometryId);if(!tabs.Success)return OperationCalculationResult.Failed(tabs.Issues.Select(x=>new OperationValidationError(x.Code,x.Message)).ToArray());
            var tangent = path[1] - path[0]; var tangentLength=Math.Max(1e-9,tangent.Length); var leadStart=new Point2D(path[0].X-tangent.X/tangentLength*profile.LeadInLengthMm,path[0].Y-tangent.Y/tangentLength*profile.LeadInLengthMm);
            toolpath.Moves.Add(new RapidMove(profile.LeadInLengthMm>0?leadStart:current, profile.SafeZMm));
            var previousDepth=profile.StartDepthMm;
            foreach (var depth in PassDepths(profile))
            {
                if(profile.LeadInLengthMm>0){toolpath.Moves.Add(new LinearMove(leadStart,-previousDepth,profile.PlungeRateMmPerMin));toolpath.Moves.Add(new LinearMove(current,-depth,profile.FeedRateMmPerMin));}
                else if(profile.RampLengthMm>0)AddRamp(toolpath.Moves,path,previousDepth,depth,profile.RampLengthMm,profile.FeedRateMmPerMin);
                else toolpath.Moves.Add(new LinearMove(current, -depth, profile.PlungeRateMmPerMin));
                ProfileTabEngine.AddClosedPass(toolpath.Moves,path,depth,profile.CutDepthMm,contourTabs,profile.FeedRateMmPerMin,contour.SourceGeometryId);
                previousDepth=depth;
            }
            var retractPoint=path[0];
            if(profile.LeadOutLengthMm>0){retractPoint=new Point2D(path[0].X+tangent.X/tangentLength*profile.LeadOutLengthMm,path[0].Y+tangent.Y/tangentLength*profile.LeadOutLengthMm);toolpath.Moves.Add(new LinearMove(retractPoint,-profile.CutDepthMm,profile.FeedRateMmPerMin));}
            // Retract vertically at the actual lead-out endpoint. Combining the return-to-contour
            // XY motion with the retract would create a diagonal rapid below the configured clearance.
            toolpath.Moves.Add(new RapidMove(retractPoint, profile.SafeZMm)); current = retractPoint;
        }
        toolpath.Moves.Add(new SpindleStop()); return new(toolpath, []);
    }

    private static List<OperationValidationError> Validate(ProfileOperationDefinition p, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        var errors = new List<OperationValidationError>();
        if (p.GeometryIds.Count == 0) errors.Add(new("geometry", "Select at least one closed contour."));
        if (p.Tool.DiameterMm <= 0) errors.Add(new("tool", "Tool diameter must be greater than zero."));
        if (p.CutDepthMm <= p.StartDepthMm || p.PassDepthMm <= 0 || p.LastPassDepthMm is <= 0) errors.Add(new("depth", "Cut depth must exceed start depth and pass depths must be positive."));
        if(p.AllowanceMm<0||p.StartPointFraction is <0 or >=1||p.LeadInLengthMm<0||p.LeadOutLengthMm<0||p.RampLengthMm<0)errors.Add(new("entry","Profile allowance, start point, leads, or ramp are invalid."));
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
    private static List<Point2D> RotateFraction(List<Point2D> path,double fraction){var index=Math.Clamp((int)Math.Round(path.Count*fraction),0,path.Count-1);return path.Skip(index).Concat(path.Take(index)).ToList();}
    private static IReadOnlyList<double> PassDepths(ProfileOperationDefinition profile)
    {
        if(profile.LastPassDepthMm is not{} last||last>=profile.CutDepthMm-profile.StartDepthMm)return ProfileToolpathGenerator.CalculatePassDepths(profile.StartDepthMm,profile.CutDepthMm,profile.PassDepthMm);
        var roughBottom=profile.CutDepthMm-last;var result=roughBottom>profile.StartDepthMm?ProfileToolpathGenerator.CalculatePassDepths(profile.StartDepthMm,roughBottom,profile.PassDepthMm).ToList():[];result.Add(profile.CutDepthMm);return result;
    }
    private static void AddRamp(List<ToolpathMove> moves,IReadOnlyList<Point2D> path,double fromDepth,double toDepth,double length,double feed)
    {
        moves.Add(new LinearMove(path[0],-fromDepth,feed));var travelled=0d;var index=1;while(travelled<length&&index<=path.Count){var previous=path[(index-1)%path.Count];var next=path[index%path.Count];travelled+=previous.DistanceTo(next);var depth=fromDepth+(toDepth-fromDepth)*Math.Min(1,travelled/length);moves.Add(new LinearMove(next,-depth,feed));index++;}moves.Add(new LinearMove(path[0],-toDepth,feed));
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
