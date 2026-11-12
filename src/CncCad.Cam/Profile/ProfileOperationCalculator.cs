using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public sealed class ProfileOperationCalculator : ICamOperationCalculator
{
    public Type OperationType=>typeof(ProfileOperationDefinition);
    public OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        if (operation is not ProfileOperationDefinition profile) return OperationCalculationResult.Failed(new OperationValidationError("unsupported", "Operation type is not supported."));
        var numericErrors=OperationNumerics.ValidateDefinition(profile);if(numericErrors.Count>0)return new(null,numericErrors);
        var errors = Validate(profile, geometry); if (errors.Count > 0) return new(null, errors);
        var contours = new List<Contour>();
        foreach (var id in profile.GeometryIds)
        {
            var original = ClipperGeometryAdapter.ToContour(geometry[id]);
            var roughAllowance=profile.AllowanceMm+(profile.FinishPassEnabled?profile.FinishAllowanceMm:0);var delta = profile.Side switch { ProfileSide.Outside => profile.Tool.DiameterMm / 2 + roughAllowance, ProfileSide.Inside => -(profile.Tool.DiameterMm / 2 + roughAllowance), _ => roughAllowance };
            var offset = ClipperGeometryAdapter.Offset(original, delta);
            if (offset.Count == 0) return OperationCalculationResult.Failed(new OperationValidationError("collapsed", $"Inside offset collapsed geometry {id}."));
            var finishDelta=profile.Side switch{ProfileSide.Outside=>profile.Tool.DiameterMm/2+profile.AllowanceMm,ProfileSide.Inside=>-(profile.Tool.DiameterMm/2+profile.AllowanceMm),_=>profile.AllowanceMm};var finish=profile.FinishPassEnabled?ClipperGeometryAdapter.Offset(original,finishDelta):[];
            var depth = profile.GeometryIds.Where(other => other != id && geometry.ContainsKey(other)).Select(other => ClipperGeometryAdapter.ToContour(geometry[other])).Count(container => ClipperGeometryAdapter.Contains(container, original[0]));
            contours.AddRange(offset.Select((path,index) => new Contour(path, depth,id,index<finish.Count?finish[index]:null)));
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
            var tangent = path[1] - path[0]; var tangentLength=Math.Max(GeometryTolerancePolicy.DefaultMinimumLengthMm,tangent.Length);var unit=new Point2D(tangent.X/tangentLength,tangent.Y/tangentLength);var normal=new Point2D(-unit.Y,unit.X);var arcLead=profile.LeadStyle==ProfileLeadStyle.Arc;var leadStart=arcLead?new Point2D(path[0].X-unit.X*profile.LeadInLengthMm+normal.X*profile.LeadInLengthMm,path[0].Y-unit.Y*profile.LeadInLengthMm+normal.Y*profile.LeadInLengthMm):new Point2D(path[0].X-unit.X*profile.LeadInLengthMm,path[0].Y-unit.Y*profile.LeadInLengthMm);
            toolpath.Moves.Add(new RapidMove(profile.LeadInLengthMm>0?leadStart:current, profile.SafeZMm));
            var previousDepth=profile.StartDepthMm;
            foreach (var depth in PassDepths(profile))
            {
                if(profile.LeadInLengthMm>0){toolpath.Moves.Add(new LinearMove(leadStart,-previousDepth,profile.PlungeRateMmPerMin));if(arcLead){var center=new Point2D(path[0].X-unit.X*profile.LeadInLengthMm,path[0].Y-unit.Y*profile.LeadInLengthMm);toolpath.Moves.Add(new ArcMove(current,center,-depth,profile.FeedRateMmPerMin,ShouldBeClockwise(profile)));}else toolpath.Moves.Add(new LinearMove(current,-depth,profile.FeedRateMmPerMin));}
                else if(profile.EntryMode==ProfileEntryMode.LinearRamp&&profile.RampLengthMm>0)AddRamp(toolpath.Moves,path,previousDepth,depth,profile.RampLengthMm,profile.FeedRateMmPerMin);
                else toolpath.Moves.Add(new LinearMove(current, -depth, profile.PlungeRateMmPerMin));
                ProfileTabEngine.AddClosedPass(toolpath.Moves,path,depth,profile.CutDepthMm,contourTabs,profile.FeedRateMmPerMin,contour.SourceGeometryId);
                previousDepth=depth;
            }
            var retractPoint=path[0];
            if(profile.FinishPassEnabled&&contour.FinishPoints is{} finishSource)
            {
                var finish=Orient(finishSource,ShouldBeClockwise(profile));
                finish=RotateNearest(finish,path[0]);
                // The rough contour has just been cut to full depth. Feed laterally into
                // the finish allowance from that cleared contour; plunging on the finish
                // contour would descend through material intentionally left by roughing.
                if(path[0].DistanceTo(finish[0])>GeometryTolerancePolicy.DefaultEndpointEqualityMm)toolpath.Moves.Add(new LinearMove(finish[0],-profile.CutDepthMm,profile.FeedRateMmPerMin));
                ProfileTabEngine.AddClosedPass(toolpath.Moves,finish,profile.CutDepthMm,profile.CutDepthMm,contourTabs,profile.FeedRateMmPerMin,contour.SourceGeometryId);
                toolpath.Moves.Add(new RapidMove(finish[0],profile.SafeZMm));current=finish[0];continue;
            }
            if(profile.LeadOutLengthMm>0){retractPoint=arcLead?new Point2D(path[0].X+unit.X*profile.LeadOutLengthMm+normal.X*profile.LeadOutLengthMm,path[0].Y+unit.Y*profile.LeadOutLengthMm+normal.Y*profile.LeadOutLengthMm):new Point2D(path[0].X+unit.X*profile.LeadOutLengthMm,path[0].Y+unit.Y*profile.LeadOutLengthMm);if(arcLead){var center=new Point2D(path[0].X+unit.X*profile.LeadOutLengthMm,path[0].Y+unit.Y*profile.LeadOutLengthMm);toolpath.Moves.Add(new ArcMove(retractPoint,center,-profile.CutDepthMm,profile.FeedRateMmPerMin,!ShouldBeClockwise(profile)));}else toolpath.Moves.Add(new LinearMove(retractPoint,-profile.CutDepthMm,profile.FeedRateMmPerMin));}
            // Retract vertically at the actual lead-out endpoint. Combining the return-to-contour
            // XY motion with the retract would create a diagonal rapid below the configured clearance.
            toolpath.Moves.Add(new RapidMove(retractPoint, profile.SafeZMm)); current = retractPoint;
        }
        toolpath.Moves.Add(new SpindleStop()); return OperationNumerics.ValidateResult(new(toolpath, []));
    }

    private static List<OperationValidationError> Validate(ProfileOperationDefinition p, IReadOnlyDictionary<Guid, CadEntity> geometry)
    {
        var errors = new List<OperationValidationError>();
        if (p.GeometryIds.Count == 0) errors.Add(new("geometry", "Select at least one closed contour."));
        if (p.Tool.DiameterMm <= 0) errors.Add(new("tool", "Tool diameter must be greater than zero."));
        if (p.CutDepthMm <= p.StartDepthMm || p.PassDepthMm <= 0 || p.LastPassDepthMm is <= 0) errors.Add(new("depth", "Cut depth must exceed start depth and pass depths must be positive."));
        if(p.AllowanceMm<0||p.FinishAllowanceMm<0||p.OnionSkinMm<0||p.OnionSkinMm>=p.CutDepthMm-p.StartDepthMm||p.StartPointFraction is <0 or >=1||p.LeadInLengthMm<0||p.LeadOutLengthMm<0||p.RampLengthMm<0)errors.Add(new("entry","Profile allowance, finish allowance, onion skin, start point, leads, or ramp are invalid."));
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
    private static List<Point2D> RotateFraction(List<Point2D> path,double fraction)
    {
        if(path.Count<2)return path;
        var lengths=Enumerable.Range(0,path.Count).Select(index=>path[index].DistanceTo(path[(index+1)%path.Count])).ToArray();var perimeter=lengths.Sum();if(perimeter<=GeometryTolerancePolicy.DefaultMinimumLengthMm)return path;
        var target=Math.Clamp(fraction,0,Math.BitDecrement(1d))*perimeter;var travelled=0d;
        for(var index=0;index<path.Count;index++)
        {
            var length=lengths[index];if(target>travelled+length&&index<path.Count-1){travelled+=length;continue;}
            var local=length<=1e-12?0:(target-travelled)/length;if(local<=1e-9)return path.Skip(index).Concat(path.Take(index)).ToList();
            if(local>=1-1e-9){var next=(index+1)%path.Count;return path.Skip(next).Concat(path.Take(next)).ToList();}
            var start=path[index];var end=path[(index+1)%path.Count];var point=new Point2D(start.X+(end.X-start.X)*local,start.Y+(end.Y-start.Y)*local);
            return [point,..path.Skip(index+1),..path.Take(index+1)];
        }
        return path;
    }
    private static IReadOnlyList<double> PassDepths(ProfileOperationDefinition profile)
    {
        if(profile.OnionSkinMm>0){var rough=ProfileToolpathGenerator.CalculatePassDepths(profile.StartDepthMm,profile.CutDepthMm-profile.OnionSkinMm,profile.PassDepthMm).ToList();rough.Add(profile.CutDepthMm);return rough;}
        if(profile.LastPassDepthMm is not{} last||last>=profile.CutDepthMm-profile.StartDepthMm)return ProfileToolpathGenerator.CalculatePassDepths(profile.StartDepthMm,profile.CutDepthMm,profile.PassDepthMm);
        var roughBottom=profile.CutDepthMm-last;var result=roughBottom>profile.StartDepthMm?ProfileToolpathGenerator.CalculatePassDepths(profile.StartDepthMm,roughBottom,profile.PassDepthMm).ToList():[];result.Add(profile.CutDepthMm);return result;
    }
    private static void AddRamp(List<ToolpathMove> moves,IReadOnlyList<Point2D> path,double fromDepth,double toDepth,double length,double feed)
    {
        moves.Add(new LinearMove(path[0],-fromDepth,feed));
        var route=new List<Point2D>{path[0]};var travelled=0d;var index=0;
        while(travelled<length-GeometryTolerancePolicy.DefaultEndpointEqualityMm)
        {
            var nextIndex=(index+1)%path.Count;var start=path[index];var end=path[nextIndex];var segmentLength=start.DistanceTo(end);if(segmentLength<=1e-12)break;var remaining=length-travelled;var fraction=Math.Min(1,remaining/segmentLength);var next=fraction>=1-1e-12?end:new Point2D(start.X+(end.X-start.X)*fraction,start.Y+(end.Y-start.Y)*fraction);travelled+=segmentLength*fraction;var depth=fromDepth+(toDepth-fromDepth)*Math.Min(1,travelled/length);moves.Add(new LinearMove(next,-depth,feed));route.Add(next);if(fraction<1-1e-12)break;index=nextIndex;
        }
        // Retrace the ramp geometry at depth rather than shortcutting across a
        // concave contour before starting the complete profile pass.
        foreach(var point in route.AsEnumerable().Reverse().Skip(1))moves.Add(new LinearMove(point,-toDepth,feed));
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
    private sealed record Contour(IReadOnlyList<Point2D> Points, int Depth,Guid SourceGeometryId,IReadOnlyList<Point2D>? FinishPoints);
}
