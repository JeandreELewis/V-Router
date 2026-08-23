using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Geometry;
using CncCad.Core.Transforms;

namespace CncCad.Cam.Safety;

public sealed class SafetyPreflightService : ISafetyPreflightService
{
    private const double Epsilon = 1e-7;

    public SafetyPreflightResult Check(SafetyPreflightRequest request)
    {
        var findings = new List<SafetyFinding>();
        if (request.Machine is not { IsComplete: true } machine)
        {
            findings.Add(new(SafetySeverity.Error, "machine-data-missing", "Machine safety data is incomplete.", "Select a complete machine profile in Job Setup."));
            return new(findings, 0, null, BoundingBox3D.Empty);
        }

        foreach (var operation in request.Operations.Where(x => x.IsEnabled))
        {
            if (operation.Status != OperationStatus.Current || operation.CalculatedToolpath is null)
                findings.Add(new(SafetySeverity.Error, "operation-not-current", $"{operation.Name} is {operation.Status.ToString().ToLowerInvariant()}.", operation.Error ?? "Recalculate or correct this operation.", operation.Id));
            if (operation.Tool.CuttingLengthMm is null or <= 0 || operation.Tool.OverallLengthMm is null or <= 0 || operation.Tool.ShankDiameterMm is null or <= 0 || operation.Tool.StickOutMm is null or <= 0)
                findings.Add(new(SafetySeverity.Error, "tool-data-missing", $"{operation.Name} has incomplete tool length or shank data.", "Edit the tool and refresh the operation snapshot.", operation.Id));
            if (operation is not ProfileOperationDefinition profile) continue;
            if (profile.SpindleRpm > machine.MaximumSpindleRpm)
                findings.Add(new(SafetySeverity.Error, "spindle-limit", $"{profile.Name} requests {profile.SpindleRpm} RPM; machine maximum is {machine.MaximumSpindleRpm} RPM.", "Reduce operation RPM.", profile.Id));
            if (profile.SafeZMm + Epsilon < machine.DefaultClearanceMm)
                findings.Add(new(SafetySeverity.Error, "safe-z-low", $"{profile.Name} safe Z is below the machine clearance height.", $"Use at least {machine.DefaultClearanceMm:0.###} mm.", profile.Id));
            if (profile.Tool.CuttingLengthMm is { } cuttingLength && profile.CutDepthMm > cuttingLength + Epsilon)
                findings.Add(new(SafetySeverity.Error, "cutting-length", $"{profile.Name} cuts {profile.CutDepthMm:0.###} mm with only {cuttingLength:0.###} mm cutting length.", "Use a longer cutter or reduce depth.", profile.Id));
            if (profile.Tool.StickOutMm is { } stickOut && profile.CutDepthMm > stickOut + Epsilon)
                findings.Add(new(SafetySeverity.Error, "stickout", $"{operation.Name} exceeds the {stickOut:0.###} mm tool stick-out.", "Increase verified stick-out or reduce depth.", operation.Id));
            if(profile.FeedRateMmPerMin>profile.Tool.FeedRateMmPerMin+Epsilon)findings.Add(new(SafetySeverity.Warning,"feed-recommendation",$"{profile.Name} feed exceeds the tool snapshot recommendation.","Confirm the material and cutter can sustain this feed.",profile.Id));
            if(profile.PlungeRateMmPerMin>profile.Tool.PlungeRateMmPerMin+Epsilon)findings.Add(new(SafetySeverity.Warning,"plunge-recommendation",$"{profile.Name} plunge rate exceeds the tool snapshot recommendation.","Reduce plunge rate or verify the cutter recommendation.",profile.Id));
            if(profile.PassDepthMm>profile.Tool.StepDownMm+Epsilon)findings.Add(new(SafetySeverity.Warning,"stepdown-recommendation",$"{profile.Name} pass depth exceeds the tool snapshot recommendation.","Reduce pass depth or verify the cutter recommendation.",profile.Id));
        }
        foreach(var fixture in request.Fixtures.Where(x=>string.IsNullOrWhiteSpace(x.Name)||x.MaxXmm<=x.MinXmm||x.MaxYmm<=x.MinYmm||x.RequiredClearanceMm<0))
            findings.Add(new(SafetySeverity.Error,"fixture-invalid","A fixture has invalid bounds, name, or clearance.","Correct the fixture definition before preview or export."));

        var origin = MachineCoordinateTransform.OriginInDocument(request.Job);
        double MachineX(double x) => x - origin.X;
        double MachineY(double y) => y - origin.Y;
        double MachineZ(double z) => z + (request.Job.ZZero == CncCad.Core.Documents.ZZeroPosition.MachineBed ? request.Job.ThicknessMm : 0);
        var samples = new List<(Point2D Point, double Z, Guid OperationId, int MoveIndex, bool Rapid, double ToolDiameter, bool XyMotion)>();
        foreach (var operation in request.Program.Operations)
        {
            var definition = request.Operations.FirstOrDefault(x => x.Id == operation.SourceOperationId);
            var diameter = definition?.Tool.DiameterMm ?? 0;
            var point = new Point2D(0, 0); var z = request.Operations.OfType<ProfileOperationDefinition>().Select(x=>x.SafeZMm).DefaultIfEmpty(machine.DefaultClearanceMm).Max();
            for (var index = 0; index < operation.Moves.Count; index++)
            {
                var move = operation.Moves[index];
                switch (move)
                {
                    case RapidMove rapid: SampleLine(point, z, rapid.End, rapid.Z, operation.SourceOperationId, index, true, diameter, samples); point = rapid.End; z = rapid.Z; break;
                    case LinearMove linear: SampleLine(point, z, linear.End, linear.Z, operation.SourceOperationId, index, false, diameter, samples); point = linear.End; z = linear.Z; break;
                    case ArcMove arc: SampleArc(point, z, arc, operation.SourceOperationId, index, diameter, samples); point = arc.End; z = arc.Z; break;
                }
            }
        }

        var minClearance = double.PositiveInfinity;
        foreach (var sample in samples)
        {
            var mx = MachineX(sample.Point.X); var my = MachineY(sample.Point.Y); var mz = MachineZ(sample.Z);
            if (mx < machine.MinimumXmm - Epsilon || mx > machine.MaximumXmm + Epsilon || my < machine.MinimumYmm - Epsilon || my > machine.MaximumYmm + Epsilon || mz < machine.MinimumZmm - Epsilon || mz > machine.MaximumZmm + Epsilon)
                AddUnique(findings, new(SafetySeverity.Error, "machine-envelope", "Tool motion leaves the configured machine travel envelope.", "Change the work origin, toolpath, or machine limits.", sample.OperationId, sample.MoveIndex, sample.Point, sample.Z));

            if (!sample.Rapid && (sample.Point.X < -Epsilon || sample.Point.X > request.Job.WidthMm + Epsilon || sample.Point.Y < -Epsilon || sample.Point.Y > request.Job.HeightMm + Epsilon))
                AddUnique(findings, new(SafetySeverity.Warning, "outside-material", "Cutting motion extends outside the material boundary.", "Verify stock position and intended cutter compensation.", sample.OperationId, sample.MoveIndex, sample.Point, sample.Z));

            if(sample.Rapid&&sample.XyMotion&&sample.Z<machine.DefaultClearanceMm-Epsilon&&sample.Point.X>=0&&sample.Point.X<=request.Job.WidthMm&&sample.Point.Y>=0&&sample.Point.Y<=request.Job.HeightMm)
                AddUnique(findings,new(SafetySeverity.Error,"rapid-stock-clearance","Rapid motion is below the required material clearance height.",$"Retract to at least {machine.DefaultClearanceMm:0.###} mm before XY rapid motion.",sample.OperationId,sample.MoveIndex,sample.Point,sample.Z));

            if (!sample.Rapid && sample.Z < -request.Job.ThicknessMm - Epsilon)
            {
                var excess = -sample.Z - request.Job.ThicknessMm;
                AddUnique(findings, new(excess <= machine.AllowedSpoilboardDepthMm + Epsilon ? SafetySeverity.Warning : SafetySeverity.Error,
                    excess <= machine.AllowedSpoilboardDepthMm + Epsilon ? "spoilboard-cut" : "spoilboard-limit",
                    $"Cut extends {excess:0.###} mm below the material.", excess <= machine.AllowedSpoilboardDepthMm + Epsilon ? "Confirm the intended spoilboard allowance." : "Reduce cut depth or increase the verified allowance.", sample.OperationId, sample.MoveIndex, sample.Point, sample.Z));
            }

            foreach (var fixture in request.Fixtures)
            {
                var definition=request.Operations.FirstOrDefault(x=>x.Id==sample.OperationId);var tool=definition?.Tool;
                var radius = sample.ToolDiameter / 2;
                var insideXY = IntersectsXY(sample.Point,radius,fixture);
                if (!insideXY) continue;
                var clearance = sample.Z - fixture.TopZmm;
                minClearance = Math.Min(minClearance, clearance);
                if (sample.Z <= fixture.TopZmm + Epsilon)
                    AddUnique(findings, new(SafetySeverity.Error, "fixture-collision", $"Tool intersects fixture '{fixture.Name}'.", "Move the fixture or change the toolpath/clearance.", sample.OperationId, sample.MoveIndex, sample.Point, sample.Z));
                else if (sample.Rapid && clearance < fixture.RequiredClearanceMm - Epsilon)
                    AddUnique(findings, new(SafetySeverity.Error, "fixture-clearance", $"Rapid move does not clear fixture '{fixture.Name}'.", $"Maintain at least {fixture.RequiredClearanceMm:0.###} mm clearance.", sample.OperationId, sample.MoveIndex, sample.Point, sample.Z));
                else if (clearance < fixture.RequiredClearanceMm * 1.5)
                    AddUnique(findings, new(SafetySeverity.Warning, "fixture-near", $"Tool passes close to fixture '{fixture.Name}'.", "Verify fixture height and clearance.", sample.OperationId, sample.MoveIndex, sample.Point, sample.Z));

                if(tool?.CuttingLengthMm is{} cutting&&tool.StickOutMm is{} stickOut&&tool.ShankDiameterMm is{} shankDiameter)
                {
                    var shankBottom=sample.Z+cutting;
                    if(fixture.TopZmm>=shankBottom-Epsilon&&IntersectsXY(sample.Point,shankDiameter/2,fixture))
                        AddUnique(findings,new(SafetySeverity.Error,"shank-collision",$"Tool shank intersects fixture '{fixture.Name}'.","Increase stick-out, move the fixture, or change the route.",sample.OperationId,sample.MoveIndex,sample.Point,sample.Z));
                    var holderBottom=sample.Z+stickOut;
                    if(fixture.TopZmm>=holderBottom-Epsilon&&IntersectsXY(sample.Point,machine.HolderDiameterMm/2,fixture))
                        AddUnique(findings,new(SafetySeverity.Error,"holder-collision",$"Holder/collet intersects fixture '{fixture.Name}'.","Increase verified clearance or move the fixture.",sample.OperationId,sample.MoveIndex,sample.Point,sample.Z));
                }
            }
        }

        ValidatePark(request, machine, origin, findings);
        var deepest = Math.Max(0, samples.Count == 0 ? 0 : -samples.Min(x => x.Z));
        var extents = samples.Count == 0 ? BoundingBox3D.Empty : new(samples.Min(x => MachineX(x.Point.X)), samples.Min(x => MachineY(x.Point.Y)), samples.Min(x => MachineZ(x.Z)), samples.Max(x => MachineX(x.Point.X)), samples.Max(x => MachineY(x.Point.Y)), samples.Max(x => MachineZ(x.Z)));
        return new(findings, deepest, double.IsPositiveInfinity(minClearance) ? null : minClearance, extents);
    }

    private static void ValidatePark(SafetyPreflightRequest request, MachineSnapshot machine, Point2D origin, List<SafetyFinding> findings)
    {
        if (machine.ParkXmm < machine.MinimumXmm || machine.ParkXmm > machine.MaximumXmm || machine.ParkYmm < machine.MinimumYmm || machine.ParkYmm > machine.MaximumYmm)
            findings.Add(new(SafetySeverity.Error, "park-envelope", "The configured park position is outside machine travel.", "Edit the machine park position."));
        var documentPark = new Point2D(machine.ParkXmm + origin.X, machine.ParkYmm + origin.Y);
        foreach (var fixture in request.Fixtures.Where(x => documentPark.X >= x.MinXmm && documentPark.X <= x.MaxXmm && documentPark.Y >= x.MinYmm && documentPark.Y <= x.MaxYmm && machine.DefaultClearanceMm < x.TopZmm + x.RequiredClearanceMm))
            findings.Add(new(SafetySeverity.Error, "park-fixture", $"The safe park route does not clear fixture '{fixture.Name}'.", "Move the park position or increase clearance."));
    }

    private static void SampleLine(Point2D start, double startZ, Point2D end, double endZ, Guid operationId, int moveIndex, bool rapid, double diameter, List<(Point2D, double, Guid, int, bool, double, bool)> output)
    {
        var distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2) + Math.Pow(endZ - startZ, 2));
        var count = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(0.5, diameter / 4)));
        var xyMotion=start.DistanceTo(end)>Epsilon;for (var i = 0; i <= count; i++) { var t = i / (double)count; output.Add((new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t), startZ + (endZ - startZ) * t, operationId, moveIndex, rapid, diameter,xyMotion)); }
    }

    private static void SampleArc(Point2D start, double startZ, ArcMove arc, Guid operationId, int moveIndex, double diameter, List<(Point2D, double, Guid, int, bool, double, bool)> output)
    {
        var radius = start.DistanceTo(arc.Center); var a = Math.Atan2(start.Y - arc.Center.Y, start.X - arc.Center.X); var b = Math.Atan2(arc.End.Y - arc.Center.Y, arc.End.X - arc.Center.X); var sweep = b - a;
        if (arc.Clockwise && sweep >= 0) sweep -= Math.PI * 2; else if (!arc.Clockwise && sweep <= 0) sweep += Math.PI * 2;
        var count = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) * radius / Math.Max(0.5, diameter / 4)));
        for (var i = 0; i <= count; i++) { var t = i / (double)count; var angle = a + sweep * t; output.Add((new(arc.Center.X + radius * Math.Cos(angle), arc.Center.Y + radius * Math.Sin(angle)), startZ + (arc.Z - startZ) * t, operationId, moveIndex, false, diameter,true)); }
    }

    private static void AddUnique(List<SafetyFinding> findings, SafetyFinding finding)
    {
        if (!findings.Any(x => x.Code == finding.Code && x.OperationId == finding.OperationId && x.MoveIndex == finding.MoveIndex)) findings.Add(finding);
    }
    private static bool IntersectsXY(Point2D point,double radius,FixtureDefinition fixture)=>point.X+radius>=fixture.MinXmm&&point.X-radius<=fixture.MaxXmm&&point.Y+radius>=fixture.MinYmm&&point.Y-radius<=fixture.MaxYmm;
}
