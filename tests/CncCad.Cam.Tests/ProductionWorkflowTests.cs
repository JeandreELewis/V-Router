using CncCad.Cam.Drilling;
using CncCad.Cam.Operations;
using CncCad.Cam.Pocket;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Cam.Surface;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Simulation.Stock;

namespace CncCad.Cam.Tests;

public sealed class ProductionWorkflowTests
{
    private static ToolSnapshot Tool(double diameter=6)=>new(){Name="Cutter",ToolNumber=1,Type=ToolType.EndMill,DiameterMm=diameter,CuttingLengthMm=25,OverallLengthMm=50,ShankDiameterMm=diameter,StickOutMm=30,FeedRateMmPerMin=1200,PlungeRateMmPerMin=300,SpindleRpm=18000,StepDownMm=2};

    [Fact]
    public void Pocket_WithNestedIsland_ProducesConcentricClearingWithoutCrossingIsland()
    {
        var outer=new RectangleEntity{Width=100,Height=80};var island=new RectangleEntity{Origin=new(35,25),Width=30,Height=30};var definition=new PocketOperationDefinition{GeometryIds=[outer.Id,island.Id],Tool=Tool(),CutDepthMm=4,PassDepthMm=2,StepOverMm=3,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new PocketOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{outer.Id,outer},{island.Id,island}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));var cutting=result.Toolpath!.Moves.OfType<LinearMove>().Where(x=>x.Z<0).Select(x=>x.End).ToArray();Assert.NotEmpty(cutting);Assert.DoesNotContain(cutting,point=>point.X>35&&point.X<65&&point.Y>25&&point.Y<55);
    }

    [Fact]
    public void PeckDrill_UsesCircleAndExplicitPoint_WithDwell()
    {
        var circle=new CircleEntity{Center=new(10,12),Radius=3};var point=new DrillPointEntity{Position=new(30,40)};var definition=new DrillOperationDefinition{GeometryIds=[circle.Id,point.Id],Tool=Tool(5) with{Type=ToolType.Drill},Cycle=DrillCycle.Peck,CutDepthMm=6,PeckDepthMm=2,SafeZMm=5,RetractZMm=1,DwellSeconds=.25,PlungeRateMmPerMin=200,SpindleRpm=12000};var result=new DrillOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{circle.Id,circle},{point.Id,point}});
        Assert.True(result.Success);var feeds=result.Toolpath!.Moves.OfType<LinearMove>().ToArray();Assert.Equal(6,feeds.Length);Assert.Equal([-2d,-4d,-6d,-2d,-4d,-6d],feeds.Select(move=>move.Z));Assert.All(feeds,move=>Assert.Equal(200,move.FeedRate));Assert.Equal(2,result.Toolpath.Moves.OfType<DwellMove>().Count());Assert.Contains(result.Toolpath.Moves.OfType<RapidMove>(),x=>x.End==point.Position);Assert.All(result.Toolpath.Moves.OfType<DwellMove>(),move=>Assert.Equal(.25,move.Seconds));
    }

    [Fact]
    public void DrillDepthIsToolTipReferencedAndSelectionOrderingUsesTransformedCenters()
    {
        var first=new CircleEntity{Center=new(1,2),Radius=3,Transform=Matrix2D.Translation(10,20)};var second=new DrillPointEntity{Position=new(3,4),Transform=Matrix2D.Translation(30,40)};var definition=new DrillOperationDefinition{GeometryIds=[second.Id,first.Id],Tool=Tool(5) with{Type=ToolType.Drill},Cycle=DrillCycle.Simple,Ordering=DrillOrdering.Selection,CutDepthMm=7.25,SafeZMm=5,RetractZMm=1,PlungeRateMmPerMin=220,SpindleRpm=12000};var result=new DrillOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{first.Id,first},{second.Id,second}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));var toolpath=result.Toolpath!;Assert.Equal([new Point2D(33,44),new Point2D(11,22)],toolpath.Moves.OfType<RapidMove>().Select(move=>move.End).Distinct());Assert.Equal([-7.25,-7.25],toolpath.Moves.OfType<LinearMove>().Select(move=>move.Z));Assert.All(toolpath.Moves.OfType<LinearMove>(),move=>Assert.Equal(220,move.FeedRate));Assert.Equal(first.Id,definition.GeometryIds[1]);Assert.Equal(second.Id,definition.GeometryIds[0]);
    }

    [Fact]
    public void OptimizedDrillOrderingIsNearestFirstStableAndDeterministic()
    {
        var far=new DrillPointEntity{Position=new(10,0)};var near=new DrillPointEntity{Position=new(1,0)};var middle=new DrillPointEntity{Position=new(5,0)};var definition=new DrillOperationDefinition{GeometryIds=[far.Id,middle.Id,near.Id],Tool=Tool(3) with{Type=ToolType.Drill},Ordering=DrillOrdering.Optimized,CutDepthMm=3,SafeZMm=4,RetractZMm=.5,PlungeRateMmPerMin=180,SpindleRpm=10000};var geometry=new Dictionary<Guid,CadEntity>{{far.Id,far},{near.Id,near},{middle.Id,middle}};var calculator=new DrillOperationCalculator();var first=calculator.Calculate(definition,geometry);var second=calculator.Calculate(definition,geometry);
        Assert.True(first.Success);Assert.Equal([near.Position,middle.Position,far.Position],first.Toolpath!.Moves.OfType<RapidMove>().Select(move=>move.End).Distinct());Assert.Equal(first.Toolpath.Moves,second.Toolpath!.Moves);Assert.Equal([far.Id,middle.Id,near.Id],definition.GeometryIds);
    }

    [Fact]
    public void DrillRegistryRejectsInvalidAndNonFiniteInputsWithoutThrowing()
    {
        var point=new DrillPointEntity{Position=new(2,3)};var definition=new DrillOperationDefinition{GeometryIds=[point.Id],Tool=Tool(3) with{Type=ToolType.Drill},CutDepthMm=double.NaN,SafeZMm=5,RetractZMm=1,PlungeRateMmPerMin=200,SpindleRpm=10000};var registry=new CompositeOperationCalculator(new DrillOperationCalculator());var result=registry.Calculate(definition,new Dictionary<Guid,CadEntity>{{point.Id,point}});Assert.False(result.Success);Assert.Contains(result.Errors,error=>error.Code=="non-finite"&&error.Message.Contains("cut depth"));Assert.Null(result.Toolpath);
        definition.CutDepthMm=3;definition.SafeZMm=1;definition.RetractZMm=1;var invalid=registry.Calculate(definition,new Dictionary<Guid,CadEntity>{{point.Id,point}});Assert.False(invalid.Success);Assert.Contains(invalid.Errors,error=>error.Code=="parameters");
    }

    [Fact]
    public void Profile_AllowanceRampAndSeparateLastPass_AffectMotion()
    {
        var rectangle=new RectangleEntity{Width=100,Height=60};var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Side=ProfileSide.Outside,AllowanceMm=1,RampLengthMm=15,CutDepthMm=7,PassDepthMm=3,LastPassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new ProfileOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));var moves=result.Toolpath!.Moves.OfType<LinearMove>().ToArray();Assert.Equal(-4,moves.Min(x=>x.End.X),6);Assert.Contains(moves,x=>Math.Abs(x.Z+6)<1e-9);Assert.Contains(moves,x=>Math.Abs(x.Z+7)<1e-9);Assert.Equal(0,moves[0].Z,6);Assert.Equal(-3,moves[1].Z,6);
    }

    [Fact]
    public void ProfileManualStartUsesArcLengthRatherThanVertexIndex()
    {
        var rectangle=new RectangleEntity{Width=100,Height=20};var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Side=ProfileSide.OnVector,Direction=CutDirection.Conventional,StartPointFraction=.25,CutDepthMm=1,PassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};
        var result=new ProfileOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));var entry=Assert.IsType<RapidMove>(result.Toolpath!.Moves.First(move=>move is RapidMove));Assert.Equal(60,entry.End.X,6);Assert.Equal(0,entry.End.Y,6);
    }

    [Fact]
    public void CpuStockSimulation_ClampsResolutionAndRemovesToCutDepth()
    {
        var definition=new EngraveOperationDefinition{Tool=Tool(1),IsEnabled=true,CalculatedToolpath=new(){Moves=[new RapidMove(new(5,5),5),new LinearMove(new(5,5),-3,200),new LinearMove(new(20,5),-3,500)]}};var field=new CpuStockSimulationService().Simulate(50,30,10,[definition]);Assert.InRange(field.CellSizeMm,.1,1);Assert.InRange(field.Columns,1,2048);Assert.InRange(field.Rows,1,2048);var minimum=Enumerable.Range(0,field.Rows).SelectMany(row=>Enumerable.Range(0,field.Columns).Select(column=>field[column,row])).Min();Assert.Equal(-3,minimum,3);
    }
    [Fact]
    public void MaterialMachineToolPreset_CreatesImmutableOperationSnapshot()
    {
        var tool=new CncTool{Name="6mm",DiameterMm=6,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=16000,StepDownMm=2};var preset=new ToolCuttingPreset{ToolId=tool.Id,MachineId=Guid.NewGuid(),MaterialId=Guid.NewGuid(),FeedRateMmPerMin=2200,PlungeRateMmPerMin=500,SpindleRpm=18000,PassDepthMm=3,StepOverPercent=40};var snapshot=preset.Apply(tool);tool.FeedRateMmPerMin=900;Assert.Equal(2200,snapshot.FeedRateMmPerMin);Assert.Equal(3,snapshot.StepDownMm);Assert.Empty(preset.Validate());
    }

    [Fact]
    public void SurfaceOperation_ProducesFiniteDeterministicRasterWithinStock()
    {
        var definition=new SurfaceOperationDefinition{Tool=Tool(25) with{Type=ToolType.SurfacingBit},WidthMm=100,HeightMm=50,CutDepthMm=1,PassDepthMm=.5,StepOverMm=10,SafeZMm=5,FeedRateMmPerMin=2000,PlungeRateMmPerMin=400,SpindleRpm=16000};var calculator=new SurfaceOperationCalculator();var first=calculator.Calculate(definition,new Dictionary<Guid,CadEntity>());var second=calculator.Calculate(definition,new Dictionary<Guid,CadEntity>());
        Assert.True(first.Success);var moves=first.Toolpath!.Moves.OfType<LinearMove>().ToArray();Assert.NotEmpty(moves);Assert.All(moves,x=>{Assert.True(double.IsFinite(x.End.X));Assert.True(double.IsFinite(x.End.Y));Assert.InRange(x.End.X,0,100);Assert.InRange(x.End.Y,0,50);Assert.InRange(x.Z,-1,0);});Assert.Equal(moves.Select(x=>(x.End,x.Z)),second.Toolpath!.Moves.OfType<LinearMove>().Select(x=>(x.End,x.Z)));
    }

    [Fact]
    public void SurfaceOvertravelCoversRegionEdgesAndEveryLowLinkRetractsVerticallyFirst()
    {
        var definition=new SurfaceOperationDefinition{Tool=Tool(20) with{Type=ToolType.SurfacingBit},OriginXmm=10,OriginYmm=20,WidthMm=100,HeightMm=50,OvertravelMm=10,CutDepthMm=1,PassDepthMm=1,StepOverMm=15,SafeZMm=6,FeedRateMmPerMin=1800,PlungeRateMmPerMin=350,SpindleRpm=15000};var result=new SurfaceOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>());Assert.True(result.Success);var cutting=result.Toolpath!.Moves.OfType<LinearMove>().Where(move=>move.Z<0).ToArray();Assert.Contains(cutting,move=>Math.Abs(move.End.X-0)<1e-9);Assert.Contains(cutting,move=>Math.Abs(move.End.X-120)<1e-9);Assert.Contains(cutting,move=>Math.Abs(move.End.Y-20)<1e-9);Assert.Contains(cutting,move=>Math.Abs(move.End.Y-70)<1e-9);var evaluation=MotionEvaluator.Evaluate(new(){Operations=[result.Toolpath]},new(5000));Assert.True(evaluation.Success);Assert.All(evaluation.Events.Where(trace=>trace.Kind==MotionEventKind.Rapid&&trace.StartZ<0),trace=>Assert.Equal(trace.Start,trace.End));
        definition.OvertravelMm=10.01;var invalid=new SurfaceOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>());Assert.False(invalid.Success);Assert.Contains(invalid.Errors,error=>error.Code=="overtravel");
    }

    [Fact]
    public void CalculatorRegistryRejectsNonFiniteInputsBeforeGeneratingMotion()
    {
        var rectangle=new RectangleEntity{Width=100,Height=50};
        var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),CutDepthMm=double.NaN,PassDepthMm=2,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};
        var registry=new CompositeOperationCalculator(new ProfileOperationCalculator());
        var result=registry.Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});
        Assert.False(result.Success);
        Assert.Contains(result.Errors,error=>error.Code=="non-finite"&&error.Message.Contains("cut depth"));
    }

    [Fact]
    public void PocketHelixKeepsTheCompleteCutterEnvelopeInsideOuterAndIslandBoundaries()
    {
        var outer=new RectangleEntity{Width=100,Height=80};var island=new RectangleEntity{Origin=new(35,25),Width=30,Height=30};var definition=new PocketOperationDefinition{GeometryIds=[outer.Id,island.Id],Tool=Tool(),Strategy=PocketStrategy.Raster,EntryMode=PocketEntryMode.Helix,RampLengthMm=12,CutDepthMm=2,PassDepthMm=2,StepOverMm=4,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new PocketOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{outer.Id,outer},{island.Id,island}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));Assert.NotEmpty(result.Toolpath!.Moves.OfType<ArcMove>());var program=new ToolpathProgram{Operations=[result.Toolpath]};var evaluation=MotionEvaluator.Evaluate(program,new(5000));Assert.True(evaluation.Success);var contours=new[]{outer.Corners,island.Corners};foreach(var trace in evaluation.Events.Where(trace=>trace.Kind is MotionEventKind.Arc or MotionEventKind.Linear&&Math.Min(trace.StartZ,trace.EndZ)<=0))for(var index=0;index<=64;index++){var point=trace.PointAt(index/64d);Assert.True(CutterCenterIsSafe(point,contours,3),$"Unsafe helix/link point {point} at move {trace.MoveIndex}.");}
    }

    [Fact]
    public void PocketHelixReturnsAnActionableDiagnosticWhenTheEnvelopeCannotFit()
    {
        var narrow=new RectangleEntity{Width=12,Height=12};var definition=new PocketOperationDefinition{GeometryIds=[narrow.Id],Tool=Tool(),EntryMode=PocketEntryMode.Helix,RampLengthMm=8,CutDepthMm=2,PassDepthMm=2,StepOverMm=2,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new PocketOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{narrow.Id,narrow}});Assert.False(result.Success);Assert.Contains(result.Errors,error=>error.Code=="helix-containment"&&error.Message.Contains("Reduce the helix diameter"));
    }

    [Fact]
    public void PocketHelixIsDeterministicAcrossDepthPassesForOffsetClearing()
    {
        var pocket=new RectangleEntity{Width=80,Height=50};var definition=new PocketOperationDefinition{GeometryIds=[pocket.Id],Tool=Tool(),Strategy=PocketStrategy.Offset,EntryMode=PocketEntryMode.Helix,RampLengthMm=10,CutDepthMm=4,PassDepthMm=2,StepOverMm=4,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var geometry=new Dictionary<Guid,CadEntity>{{pocket.Id,pocket}};var calculator=new PocketOperationCalculator();var first=calculator.Calculate(definition,geometry);var second=calculator.Calculate(definition,geometry);Assert.True(first.Success,string.Join("; ",first.Errors.Select(error=>error.Message)));Assert.Equal(first.Toolpath!.Moves,second.Toolpath!.Moves);var helices=first.Toolpath.Moves.OfType<ArcMove>().ToArray();Assert.NotEmpty(helices);Assert.Contains(helices,move=>Math.Abs(move.Z+2)<1e-9);Assert.Contains(helices,move=>Math.Abs(move.Z+4)<1e-9);
    }

    [Fact]
    public void PocketLinearRampStartsEachDepthFromPreviouslyClearedLevel()
    {
        var rectangle=new RectangleEntity{Width=80,Height=40};var definition=new PocketOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),EntryMode=PocketEntryMode.LinearRamp,RampLengthMm=10,CutDepthMm=4,PassDepthMm=2,StepOverMm=4,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};
        var result=new PocketOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var starts=result.Toolpath!.Moves.OfType<LinearMove>().Where(move=>Math.Abs(move.FeedRate-definition.PlungeRateMmPerMin)<1e-9).Select(move=>move.Z).ToArray();Assert.Contains(0,starts);Assert.Contains(-2,starts);
    }

    [Fact]
    public void PocketLongLinearRampTraversesTheContainedRouteUntilTheRequestedLength()
    {
        var rectangle=new RectangleEntity{Width=20,Height=20};var definition=new PocketOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Strategy=PocketStrategy.Raster,EntryMode=PocketEntryMode.LinearRamp,RampLengthMm=50,CutDepthMm=2,PassDepthMm=2,StepOverMm=4,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new PocketOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var moves=result.Toolpath!.Moves;var plungeIndex=moves.FindIndex(move=>move is LinearMove linear&&Math.Abs(linear.FeedRate-definition.PlungeRateMmPerMin)<1e-9);var start=Assert.IsType<LinearMove>(moves[plungeIndex]).End;var travelled=0d;var previous=start;for(var index=plungeIndex+1;index<moves.Count;index++){var linear=Assert.IsType<LinearMove>(moves[index]);travelled+=previous.DistanceTo(linear.End);previous=linear.End;if(Math.Abs(linear.Z+definition.CutDepthMm)<1e-9)break;}Assert.Equal(definition.RampLengthMm,travelled,6);
    }

    [Fact]
    public void PocketFinishPassPlungesOnlyOnMaterialAlreadyClearedToFullDepth()
    {
        var rectangle=new RectangleEntity{Width=80,Height=50};var definition=new PocketOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Strategy=PocketStrategy.Offset,EntryMode=PocketEntryMode.LinearRamp,RampLengthMm=12,RoughAllowanceMm=2,FinishPassEnabled=true,CutDepthMm=4,PassDepthMm=2,StepOverMm=4,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new PocketOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var requiredRoughClearance=definition.Tool.DiameterMm/2+definition.AllowanceMm+definition.RoughAllowanceMm;var plunges=result.Toolpath!.Moves.OfType<LinearMove>().Where(move=>Math.Abs(move.FeedRate-definition.PlungeRateMmPerMin)<1e-9&&Math.Abs(move.Z+definition.CutDepthMm)<1e-9).ToArray();Assert.NotEmpty(plunges);Assert.All(plunges,move=>Assert.True(CutterCenterIsSafe(move.End,[rectangle.Corners],requiredRoughClearance),$"Finish plunge at {move.End} is not in the fully cleared rough pocket."));var finishClearance=definition.Tool.DiameterMm/2+definition.AllowanceMm;Assert.Contains(result.Toolpath.Moves.OfType<LinearMove>(),move=>Math.Abs(move.Z+definition.CutDepthMm)<1e-9&&CutterCenterIsSafe(move.End,[rectangle.Corners],finishClearance)&&!CutterCenterIsSafe(move.End,[rectangle.Corners],requiredRoughClearance));
    }

    [Fact]
    public void ProfileSupportsArcLeadsOnionSkinFinishAndSquareTabs()
    {
        var rectangle=new RectangleEntity{Width=100,Height=60};var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Side=ProfileSide.Outside,LeadStyle=ProfileLeadStyle.Arc,LeadInLengthMm=5,LeadOutLengthMm=5,OnionSkinMm=.5,FinishPassEnabled=true,CutDepthMm=7,PassDepthMm=3,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000,Tabs=new(){Enabled=true,Style=TabStyle.Square,AutomaticCount=2,WidthMm=8,RemainingHeightMm=2,RampLengthMm=4,ExclusionDistanceMm=1}};var result=new ProfileOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));Assert.NotEmpty(result.Toolpath!.Moves.OfType<ArcMove>());var z=result.Toolpath.Moves.OfType<LinearMove>().Select(x=>x.Z).ToArray();Assert.Contains(z,x=>Math.Abs(x+6.5)<1e-9);Assert.Contains(z,x=>Math.Abs(x+7)<1e-9);Assert.Contains(z,x=>Math.Abs(x+5)<1e-9);
    }

    [Fact]
    public void ProfileLongRampTraversesTheContourForTheFullRequestedLength()
    {
        var rectangle=new RectangleEntity{Width=20,Height=20};var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Side=ProfileSide.Outside,EntryMode=ProfileEntryMode.LinearRamp,RampLengthMm=150,CutDepthMm=2,PassDepthMm=2,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new ProfileOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var moves=result.Toolpath!.Moves;var rampStart=moves.FindIndex(move=>move is LinearMove linear&&Math.Abs(linear.Z)<1e-9);var start=Assert.IsType<LinearMove>(moves[rampStart]).End;var previous=start;var travelled=0d;for(var index=rampStart+1;index<moves.Count;index++){var linear=Assert.IsType<LinearMove>(moves[index]);travelled+=previous.DistanceTo(linear.End);previous=linear.End;if(Math.Abs(linear.Z+definition.CutDepthMm)<1e-9)break;}Assert.Equal(definition.RampLengthMm,travelled,6);
    }

    [Fact]
    public void ProfileFinishPassFeedsFromTheFullDepthRoughContourWithoutASecondPlunge()
    {
        var rectangle=new RectangleEntity{Width=80,Height=50};var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Side=ProfileSide.Outside,EntryMode=ProfileEntryMode.Plunge,FinishPassEnabled=true,FinishAllowanceMm=2,CutDepthMm=2,PassDepthMm=2,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new ProfileOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var finalPlunges=result.Toolpath!.Moves.OfType<LinearMove>().Where(move=>Math.Abs(move.Z+definition.CutDepthMm)<1e-9&&Math.Abs(move.FeedRate-definition.PlungeRateMmPerMin)<1e-9).ToArray();Assert.Single(finalPlunges);Assert.Contains(result.Toolpath.Moves.OfType<LinearMove>(),move=>Math.Abs(move.Z+definition.CutDepthMm)<1e-9&&Math.Abs(move.FeedRate-definition.FeedRateMmPerMin)<1e-9&&move.End.DistanceTo(finalPlunges[0].End)>1e-9);
    }

    [Fact]
    public void RouterPreparationAddsOriginalCornerReliefAndReportsOpenVectors()
    {
        var contour=new PolylineEntity{Points=[new(0,0),new(50,0),new(50,40),new(0,40)],IsClosed=true};var service=new RouterVectorPreparationService();var relief=service.AddCornerRelief(contour,[1],6,CornerReliefStyle.Dogbone);Assert.True(relief.Success);Assert.True(Assert.IsType<PolylineEntity>(Assert.Single(relief.Entities)).Points.Count>contour.Points.Count);var open=new PolylineEntity{Points=[new(0,0),new(10,0)],IsClosed=false};Assert.Contains(service.Diagnose([open],6),x=>x.Code=="open-vector");
    }

    [Fact]
    public void EngraveSupportsClosedRectangleVectors()
    {
        var rectangle=new RectangleEntity{Origin=new(5,7),Width=20,Height=10};
        var definition=new EngraveOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(2),CutDepthMm=1,PassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250,SpindleRpm=16000};
        var result=new EngraveOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));
        var cuts=result.Toolpath!.Moves.OfType<LinearMove>().Where(x=>x.Z<0).ToArray();Assert.True(cuts.Length>=5);Assert.Equal(rectangle.Origin,cuts[^1].End);
    }

    [Fact]
    public void EngravePreservesNativeArcGeometryAndReverseFlipsItsDirection()
    {
        var arc=new ArcEntity{Center=new(2,3),Radius=10,StartAngleDegrees=0,SweepAngleDegrees=90,Transform=Matrix2D.Translation(20,30)};var geometry=new Dictionary<Guid,CadEntity>{{arc.Id,arc}};var definition=new EngraveOperationDefinition{GeometryIds=[arc.Id],Tool=Tool(2),Direction=EngraveDirection.Forward,CutDepthMm=2,PassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250,SpindleRpm=16000};var calculator=new EngraveOperationCalculator();var forward=calculator.Calculate(definition,geometry);Assert.True(forward.Success);var forwardArcs=forward.Toolpath!.Moves.OfType<ArcMove>().ToArray();Assert.Equal(2,forwardArcs.Length);Assert.All(forwardArcs,move=>{Assert.Equal(new Point2D(22,43),move.End);Assert.Equal(new Point2D(22,33),move.Center);Assert.False(move.Clockwise);});Assert.Equal(new Point2D(32,33),forward.Toolpath.Moves.OfType<RapidMove>().First().End);
        definition.Direction=EngraveDirection.Reverse;var reverse=calculator.Calculate(definition,geometry);Assert.True(reverse.Success);var reverseArcs=reverse.Toolpath!.Moves.OfType<ArcMove>().ToArray();Assert.Equal(2,reverseArcs.Length);Assert.All(reverseArcs,move=>{Assert.Equal(new Point2D(32,33),move.End);Assert.True(move.Clockwise);});Assert.Equal(new Point2D(22,43),reverse.Toolpath.Moves.OfType<RapidMove>().First().End);
    }

    [Fact]
    public void EngraveUsesNativeCircleArcsButSafelyFlattensNonCircularTransforms()
    {
        var circle=new CircleEntity{Center=new(10,20),Radius=8};var stretched=new ArcEntity{Center=new(0,0),Radius=10,StartAngleDegrees=0,SweepAngleDegrees=120,Transform=Matrix2D.Scale(2,1)};var calculator=new EngraveOperationCalculator();var nativeDefinition=new EngraveOperationDefinition{GeometryIds=[circle.Id],Tool=Tool(2),CutDepthMm=1,PassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250,SpindleRpm=16000};var native=calculator.Calculate(nativeDefinition,new Dictionary<Guid,CadEntity>{{circle.Id,circle}});Assert.True(native.Success);var arcs=native.Toolpath!.Moves.OfType<ArcMove>().ToArray();Assert.Equal(2,arcs.Length);Assert.All(arcs,move=>Assert.Equal(circle.Center,move.Center));Assert.Equal(circle.Center.X+circle.Radius,arcs[^1].End.X,6);Assert.Equal(circle.Center.Y,arcs[^1].End.Y,6);
        var fallbackDefinition=nativeDefinition.Duplicate();fallbackDefinition.GeometryIds=[stretched.Id];var fallback=calculator.Calculate(fallbackDefinition,new Dictionary<Guid,CadEntity>{{stretched.Id,stretched}});Assert.True(fallback.Success);Assert.Empty(fallback.Toolpath!.Moves.OfType<ArcMove>());Assert.True(fallback.Toolpath.Moves.OfType<LinearMove>().Count(move=>Math.Abs(move.FeedRate-fallbackDefinition.FeedRateMmPerMin)<1e-9)>2);
    }

    [Fact]
    public void EngraveOptimizedOrderingChoosesTheNearestEndpointDeterministically()
    {
        var far=new LineEntity{Start=new(20,0),End=new(30,0)};var near=new LineEntity{Start=new(10,0),End=new(2,0)};var definition=new EngraveOperationDefinition{GeometryIds=[far.Id,near.Id],Tool=Tool(2),Direction=EngraveDirection.Optimize,CutDepthMm=1,PassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250,SpindleRpm=16000};var geometry=new Dictionary<Guid,CadEntity>{{far.Id,far},{near.Id,near}};var calculator=new EngraveOperationCalculator();var first=calculator.Calculate(definition,geometry);var second=calculator.Calculate(definition,geometry);Assert.True(first.Success);Assert.Equal(first.Toolpath!.Moves,second.Toolpath!.Moves);var rapids=first.Toolpath.Moves.OfType<RapidMove>().ToArray();Assert.Equal(new Point2D(2,0),rapids[0].End);Assert.Equal(new Point2D(20,0),rapids[2].End);
    }

    [Fact]
    public void VCarveRejectsOpenPolylineInsteadOfSilentlyClosingIt()
    {
        var open=new PolylineEntity{Points=[new(0,0),new(30,0),new(30,20)],IsClosed=false};
        var definition=new VCarveOperationDefinition{GeometryIds=[open.Id],Tool=Tool(6) with{Type=ToolType.VBit,VBitAngleDegrees=90,TipDiameterMm=.2},MaximumDepthMm=3,OffsetStepOverMm=.5,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250,SpindleRpm=16000};
        var result=new VCarveOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{open.Id,open}});
        Assert.False(result.Success);Assert.Contains(result.Errors,x=>x.Code=="open");
    }

    private static bool CutterCenterIsSafe(Point2D point,IReadOnlyList<Point2D>[] contours,double radius)
    {
        static double Distance(Point2D point,Point2D start,Point2D end){var delta=end-start;var length=delta.X*delta.X+delta.Y*delta.Y;var offset=point-start;var fraction=length<=1e-18?0:Math.Clamp((offset.X*delta.X+offset.Y*delta.Y)/length,0,1);return point.DistanceTo(new(start.X+delta.X*fraction,start.Y+delta.Y*fraction));}static bool Contains(IReadOnlyList<Point2D> polygon,Point2D point){var inside=false;for(int index=0,previous=polygon.Count-1;index<polygon.Count;previous=index++)if((polygon[index].Y>point.Y)!=(polygon[previous].Y>point.Y)&&point.X<(polygon[previous].X-polygon[index].X)*(point.Y-polygon[index].Y)/(polygon[previous].Y-polygon[index].Y)+polygon[index].X)inside=!inside;return inside;}if(contours.Count(contour=>Contains(contour,point))%2==0)return false;return contours.SelectMany(contour=>Enumerable.Range(0,contour.Count).Select(index=>Distance(point,contour[index],contour[(index+1)%contour.Count]))).Min()>=radius-1e-6;
    }
}
