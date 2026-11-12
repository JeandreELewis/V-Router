using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
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

        foreach(var operation in request.Operations.Where(x=>x.IsEnabled))
            foreach(var error in OperationNumerics.ValidateDefinition(operation))
                findings.Add(new(SafetySeverity.Error,error.Code,error.Message,"Correct the operation value and recalculate it.",operation.Id));

        foreach(var operation in request.Program.Operations)
            foreach(var error in OperationNumerics.ValidateMoves(operation))
                findings.Add(new(SafetySeverity.Error,error.Code,error.Message,"Recalculate the operation before preview or export.",operation.SourceOperationId));

        var enabledIds=request.Operations.Where(operation=>operation.IsEnabled).Select(operation=>operation.Id).ToHashSet();
        var programIds=request.Program.Operations.Select(operation=>operation.SourceOperationId).ToArray();
        if(programIds.Distinct().Count()!=programIds.Length)
            findings.Add(new(SafetySeverity.Error,"program-duplicate-operation","The motion program contains the same source operation more than once.","Rebuild the preview or export program."));
        if(!enabledIds.SetEquals(programIds))
            findings.Add(new(SafetySeverity.Error,"program-scope-mismatch","The motion program does not exactly match the enabled operations in this preview/export scope.","Recalculate the selected scope before continuing."));
        foreach(var programOperation in request.Program.Operations)
        {
            var definition=request.Operations.FirstOrDefault(operation=>operation.Id==programOperation.SourceOperationId);
            if(definition?.CalculatedToolpath is not null&&definition.CalculatedToolpath.Id!=programOperation.Id)
                findings.Add(new(SafetySeverity.Error,"program-stale-copy",$"{definition.Name} motion does not match its current calculated toolpath.","Recalculate the operation and rebuild the program.",definition.Id));
        }

        if(findings.Any(x=>x.Severity==SafetySeverity.Error))
            return new(findings,0,null,BoundingBox3D.Empty);

        foreach (var operation in request.Operations.Where(x => x.IsEnabled))
        {
            if (operation.Status != OperationStatus.Current || operation.CalculatedToolpath is null)
                findings.Add(new(SafetySeverity.Error, "operation-not-current", $"{operation.Name} is {operation.Status.ToString().ToLowerInvariant()}.", operation.Error ?? "Recalculate or correct this operation.", operation.Id));
            if (operation.Tool.CuttingLengthMm is null or <= 0 || operation.Tool.OverallLengthMm is null or <= 0 || operation.Tool.ShankDiameterMm is null or <= 0 || operation.Tool.StickOutMm is null or <= 0)
                findings.Add(new(SafetySeverity.Error, "tool-data-missing", $"{operation.Name} has incomplete tool length or shank data.", "Edit the tool and refresh the operation snapshot.", operation.Id));
            (int SpindleRpm,double SafeZMm,double CutDepthMm,double FeedRateMmPerMin,double PlungeRateMmPerMin,double PassDepthMm) parameters=operation switch{ProfileOperationDefinition p=>(p.SpindleRpm,p.SafeZMm,p.CutDepthMm,p.FeedRateMmPerMin,p.PlungeRateMmPerMin,p.PassDepthMm),EngraveOperationDefinition e=>(e.SpindleRpm,e.SafeZMm,e.CutDepthMm,e.FeedRateMmPerMin,e.PlungeRateMmPerMin,e.PassDepthMm),VCarveOperationDefinition v=>(v.SpindleRpm,v.SafeZMm,v.MaximumDepthMm,v.FeedRateMmPerMin,v.PlungeRateMmPerMin,v.OffsetStepOverMm),PocketOperationDefinition p=>(p.SpindleRpm,p.SafeZMm,p.CutDepthMm,p.FeedRateMmPerMin,p.PlungeRateMmPerMin,p.PassDepthMm),DrillOperationDefinition d=>(d.SpindleRpm,d.SafeZMm,d.CutDepthMm,d.PlungeRateMmPerMin,d.PlungeRateMmPerMin,d.PeckDepthMm),SurfaceOperationDefinition s=>(s.SpindleRpm,s.SafeZMm,s.CutDepthMm,s.FeedRateMmPerMin,s.PlungeRateMmPerMin,s.PassDepthMm),_=>(0,0,0,0,0,0)};
            if (parameters.SpindleRpm > machine.MaximumSpindleRpm)
                findings.Add(new(SafetySeverity.Error, "spindle-limit", $"{operation.Name} requests {parameters.SpindleRpm} RPM; machine maximum is {machine.MaximumSpindleRpm} RPM.", "Reduce operation RPM.", operation.Id));
            if (parameters.SafeZMm + Epsilon < machine.DefaultClearanceMm)
                findings.Add(new(SafetySeverity.Error, "safe-z-low", $"{operation.Name} safe Z is below the machine clearance height.", $"Use at least {machine.DefaultClearanceMm:0.###} mm.", operation.Id));
            if (operation.Tool.CuttingLengthMm is { } cuttingLength && parameters.CutDepthMm > cuttingLength + Epsilon)
                findings.Add(new(SafetySeverity.Error, "cutting-length", $"{operation.Name} cuts {parameters.CutDepthMm:0.###} mm with only {cuttingLength:0.###} mm cutting length.", "Use a longer cutter or reduce depth.", operation.Id));
            if (operation.Tool.StickOutMm is { } stickOut && parameters.CutDepthMm > stickOut + Epsilon)
                findings.Add(new(SafetySeverity.Error, "stickout", $"{operation.Name} exceeds the {stickOut:0.###} mm tool stick-out.", "Increase verified stick-out or reduce depth.", operation.Id));
            if(parameters.FeedRateMmPerMin>operation.Tool.FeedRateMmPerMin+Epsilon)findings.Add(new(SafetySeverity.Warning,"feed-recommendation",$"{operation.Name} feed exceeds the tool snapshot recommendation.","Confirm the material and cutter can sustain this feed.",operation.Id));
            if(parameters.PlungeRateMmPerMin>operation.Tool.PlungeRateMmPerMin+Epsilon)findings.Add(new(SafetySeverity.Warning,"plunge-recommendation",$"{operation.Name} plunge rate exceeds the tool snapshot recommendation.","Reduce plunge rate or verify the cutter recommendation.",operation.Id));
            if(parameters.PassDepthMm>operation.Tool.StepDownMm+Epsilon)findings.Add(new(SafetySeverity.Warning,"stepdown-recommendation",$"{operation.Name} pass depth exceeds the tool snapshot recommendation.","Reduce pass depth or verify the cutter recommendation.",operation.Id));
        }
        foreach(var fixture in request.Fixtures.Where(x=>string.IsNullOrWhiteSpace(x.Name)||x.MaxXmm<=x.MinXmm||x.MaxYmm<=x.MinYmm||x.RequiredClearanceMm<0))
            findings.Add(new(SafetySeverity.Error,"fixture-invalid","A fixture has invalid bounds, name, or clearance.","Correct the fixture definition before preview or export."));

        var origin = MachineCoordinateTransform.OriginInDocument(request.Job);
        double MachineX(double x) => x - origin.X;
        double MachineY(double y) => y - origin.Y;
        double MachineZ(double z) => z + (request.Job.ZZero == CncCad.Core.Documents.ZZeroPosition.MachineBed ? request.Job.ThicknessMm : 0);
        var samples = new List<(Point2D Point, double Z, Guid OperationId, int MoveIndex, bool Rapid, double ToolDiameter, bool XyMotion)>();
        var motion=MotionEvaluator.Evaluate(request.Program,new(machine.RapidRateMmPerMin,machine.ToolChangeSeconds));
        foreach(var diagnostic in motion.Diagnostics)
            findings.Add(new(SafetySeverity.Error,diagnostic.Code,diagnostic.Message,"Correct and recalculate the affected operation."));
        foreach(var trace in motion.Events.Where(trace=>trace.Kind is MotionEventKind.Rapid or MotionEventKind.Linear or MotionEventKind.Arc))
        {
            var definition=request.Operations.FirstOrDefault(operation=>operation.Id==trace.OperationId);
            SampleTrace(trace,definition?.Tool.DiameterMm??0,samples);
            if(definition is not null)ValidateSweptFixtures(trace,definition.Tool,machine,request.Fixtures,findings);
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
        if(!machine.ParkEnabled)return;
        if(!double.IsFinite(machine.ParkXmm)||!double.IsFinite(machine.ParkYmm)){findings.Add(new(SafetySeverity.Error,"park-invalid","The configured park position is not finite.","Enter finite machine-coordinate park values or disable parking."));return;}
        if (machine.ParkXmm < machine.MinimumXmm || machine.ParkXmm > machine.MaximumXmm || machine.ParkYmm < machine.MinimumYmm || machine.ParkYmm > machine.MaximumYmm)
            findings.Add(new(SafetySeverity.Error, "park-envelope", "The configured park position is outside machine travel.", "Edit the machine park position."));
        var documentPark = new Point2D(machine.ParkXmm + origin.X, machine.ParkYmm + origin.Y);
        foreach (var fixture in request.Fixtures.Where(x => documentPark.X >= x.MinXmm && documentPark.X <= x.MaxXmm && documentPark.Y >= x.MinYmm && documentPark.Y <= x.MaxYmm && machine.DefaultClearanceMm < x.TopZmm + x.RequiredClearanceMm))
            findings.Add(new(SafetySeverity.Error, "park-fixture", $"The safe park route does not clear fixture '{fixture.Name}'.", "Move the park position or increase clearance."));
    }

    private static void SampleTrace(MotionTraceEvent trace,double diameter,List<(Point2D, double, Guid, int, bool, double, bool)> output)
    {
        var minimumSamples=trace.Kind==MotionEventKind.Arc?8:1;
        var count=Math.Max(minimumSamples,(int)Math.Ceiling(trace.PathLengthMm/Math.Max(.5,diameter/4)));
        var xyMotion=trace.Kind==MotionEventKind.Arc||trace.Start.DistanceTo(trace.End)>Epsilon;
        var rapid=trace.Kind==MotionEventKind.Rapid;
        for(var index=0;index<=count;index++)
        {
            var progress=index/(double)count;
            output.Add((trace.PointAt(progress),trace.ZAt(progress),trace.OperationId,trace.MoveIndex,rapid,diameter,xyMotion));
        }
    }

    private static void AddUnique(List<SafetyFinding> findings, SafetyFinding finding)
    {
        if (!findings.Any(x => x.Code == finding.Code && x.OperationId == finding.OperationId && x.MoveIndex == finding.MoveIndex)) findings.Add(finding);
    }
    private static void ValidateSweptFixtures(MotionTraceEvent trace,ToolSnapshot tool,MachineSnapshot machine,IReadOnlyList<FixtureDefinition> fixtures,List<SafetyFinding> findings)
    {
        if(fixtures.Count==0)return;
        foreach(var fixture in fixtures)
        {
            Check(tool.DiameterMm/2,0,"fixture-collision",$"Tool intersects fixture '{fixture.Name}'.","Move the fixture or change the toolpath/clearance.");
            if(tool.CuttingLengthMm is{} cutting&&tool.ShankDiameterMm is{} shank)Check(shank/2,cutting,"shank-collision",$"Tool shank intersects fixture '{fixture.Name}'.","Increase stick-out, move the fixture, or change the route.");
            if(tool.StickOutMm is{} stickOut)Check(machine.HolderDiameterMm/2,stickOut,"holder-collision",$"Holder/collet intersects fixture '{fixture.Name}'.","Increase verified clearance or move the fixture.");
            if(trace.Kind==MotionEventKind.Rapid)
            foreach(var interval in TraceIntervals(trace,fixture,tool.DiameterMm/2))
            {
                var lowestProgress=trace.ZAt(interval.Enter)<=trace.ZAt(interval.Exit)?interval.Enter:interval.Exit;var lowest=trace.ZAt(lowestProgress);
                if(lowest-fixture.TopZmm<fixture.RequiredClearanceMm-Epsilon)AddUnique(findings,new(SafetySeverity.Error,"fixture-clearance",$"Rapid move does not clear fixture '{fixture.Name}'.",$"Maintain at least {fixture.RequiredClearanceMm:0.###} mm clearance.",trace.OperationId,trace.MoveIndex,trace.PointAt(lowestProgress),lowest));
            }
            void Check(double radius,double verticalOffset,string code,string message,string suggestion)
            {
                if(radius<=0)return;
                foreach(var interval in TraceIntervals(trace,fixture,radius))
                {
                    var lowestProgress=trace.ZAt(interval.Enter)<=trace.ZAt(interval.Exit)?interval.Enter:interval.Exit;var lowest=trace.ZAt(lowestProgress)+verticalOffset;
                    if(lowest<=fixture.TopZmm+Epsilon)AddUnique(findings,new(SafetySeverity.Error,code,message,suggestion,trace.OperationId,trace.MoveIndex,trace.PointAt(lowestProgress),trace.ZAt(lowestProgress)));
                }
            }
        }
    }
    private readonly record struct TraceInterval(double Enter,double Exit);
    private static IReadOnlyList<TraceInterval> TraceIntervals(MotionTraceEvent trace,FixtureDefinition fixture,double radius)
    {
        if(trace.Kind!=MotionEventKind.Arc||trace.ArcCenter is not{} center)
            return TryInterval(trace.Start,trace.End,fixture,radius,out var enter,out var exit)?[new(enter,exit)]:[];

        var arcRadius=trace.Start.DistanceTo(center);if(arcRadius<=Epsilon)return IntersectsXY(trace.Start,radius,fixture)?[new(0,1)]:[];
        var minimumX=fixture.MinXmm-radius;var maximumX=fixture.MaxXmm+radius;var minimumY=fixture.MinYmm-radius;var maximumY=fixture.MaxYmm+radius;
        var startAngle=Math.Atan2(trace.Start.Y-center.Y,trace.Start.X-center.X);var sweep=MotionTraceEvent.ArcSweep(trace.Start,trace.End,center,trace.Clockwise);var breaks=new List<double>{0,1};
        foreach(var x in new[]{minimumX,maximumX})
        {
            var cosine=(x-center.X)/arcRadius;if(cosine is < -1-Epsilon or > 1+Epsilon)continue;var angle=Math.Acos(Math.Clamp(cosine,-1,1));AddAngle(angle);AddAngle(-angle);
        }
        foreach(var y in new[]{minimumY,maximumY})
        {
            var sine=(y-center.Y)/arcRadius;if(sine is < -1-Epsilon or > 1+Epsilon)continue;var angle=Math.Asin(Math.Clamp(sine,-1,1));AddAngle(angle);AddAngle(Math.PI-angle);
        }
        var ordered=breaks.Select(value=>Math.Clamp(value,0,1)).DistinctBy(value=>Math.Round(value,12)).Order().ToArray();var intervals=new List<TraceInterval>();
        for(var index=0;index+1<ordered.Length;index++){var from=ordered[index];var to=ordered[index+1];if(Inside(trace.PointAt((from+to)/2)))intervals.Add(new(from,to));}
        foreach(var value in ordered.Where(value=>Inside(trace.PointAt(value))))intervals.Add(new(value,value));
        if(intervals.Count==0)return[];var merged=new List<TraceInterval>();foreach(var interval in intervals.OrderBy(value=>value.Enter).ThenBy(value=>value.Exit)){if(merged.Count==0||interval.Enter>merged[^1].Exit+1e-10)merged.Add(interval);else merged[^1]=new(merged[^1].Enter,Math.Max(merged[^1].Exit,interval.Exit));}return merged;

        bool Inside(Point2D point)=>point.X>=minimumX-Epsilon&&point.X<=maximumX+Epsilon&&point.Y>=minimumY-Epsilon&&point.Y<=maximumY+Epsilon;
        void AddAngle(double angle)
        {
            var distance=sweep>=0?Normalize(angle-startAngle):Normalize(startAngle-angle);var magnitude=Math.Abs(sweep);if(distance<=magnitude+Epsilon)breaks.Add(magnitude<=Epsilon?0:distance/magnitude);
        }
        static double Normalize(double angle){var value=angle%(Math.PI*2);return value<0?value+Math.PI*2:value;}
    }
    private static bool TryInterval(Point2D a,Point2D b,FixtureDefinition fixture,double radius,out double enter,out double exit)
    {
        enter=0;exit=1;return Slab(a.X,b.X,fixture.MinXmm-radius,fixture.MaxXmm+radius,ref enter,ref exit)&&Slab(a.Y,b.Y,fixture.MinYmm-radius,fixture.MaxYmm+radius,ref enter,ref exit);
        static bool Slab(double start,double end,double minimum,double maximum,ref double enter,ref double exit){var delta=end-start;if(Math.Abs(delta)<=Epsilon)return start>=minimum-Epsilon&&start<=maximum+Epsilon;var first=(minimum-start)/delta;var second=(maximum-start)/delta;if(first>second)(first,second)=(second,first);enter=Math.Max(enter,first);exit=Math.Min(exit,second);return enter<=exit+Epsilon&&exit>=0&&enter<=1;}
    }
    private static bool IntersectsXY(Point2D point,double radius,FixtureDefinition fixture)=>point.X+radius>=fixture.MinXmm&&point.X-radius<=fixture.MaxXmm&&point.Y+radius>=fixture.MinYmm&&point.Y-radius<=fixture.MaxYmm;
}
