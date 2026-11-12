using CncCad.Cam.Drilling;
using CncCad.Cam.Operations;
using CncCad.Cam.Pocket;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Entities;
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
        Assert.True(result.Success);Assert.Equal(6,result.Toolpath!.Moves.OfType<LinearMove>().Count());Assert.Equal(2,result.Toolpath.Moves.OfType<DwellMove>().Count());Assert.Contains(result.Toolpath.Moves.OfType<RapidMove>(),x=>x.End==point.Position);
    }

    [Fact]
    public void Profile_AllowanceRampAndSeparateLastPass_AffectMotion()
    {
        var rectangle=new RectangleEntity{Width=100,Height=60};var definition=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=Tool(),Side=ProfileSide.Outside,AllowanceMm=1,RampLengthMm=15,CutDepthMm=7,PassDepthMm=3,LastPassDepthMm=1,SafeZMm=5,FeedRateMmPerMin=1000,PlungeRateMmPerMin=250,SpindleRpm=18000};var result=new ProfileOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));var moves=result.Toolpath!.Moves.OfType<LinearMove>().ToArray();Assert.Equal(-4,moves.Min(x=>x.End.X),6);Assert.Contains(moves,x=>Math.Abs(x.Z+6)<1e-9);Assert.Contains(moves,x=>Math.Abs(x.Z+7)<1e-9);Assert.Equal(0,moves[0].Z,6);Assert.Equal(-3,moves[1].Z,6);
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
}
