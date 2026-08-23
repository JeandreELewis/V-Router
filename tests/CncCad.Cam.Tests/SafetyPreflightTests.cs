using CncCad.Cam.Operations;
using CncCad.Cam.Safety;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Documents;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public class SafetyPreflightTests
{
    private static readonly ToolSnapshot CompleteTool = new() { DiameterMm=6, CuttingLengthMm=25, OverallLengthMm=50, ShankDiameterMm=6, StickOutMm=30, FeedRateMmPerMin=1800, PlungeRateMmPerMin=600, StepDownMm=3 };
    private static MachineSnapshot Machine(double spoilboard=.5)=>new MachineProfileDefinition{MaximumXmm=600,MaximumYmm=400,MinimumZmm=-100,MaximumZmm=50,AllowedSpoilboardDepthMm=spoilboard}.Snapshot();
    private static (ProfileOperationDefinition Definition,ToolpathProgram Program) Program(params ToolpathMove[] moves)
    {
        var definition=new ProfileOperationDefinition{Name="Profile",Tool=CompleteTool,SafeZMm=5,CutDepthMm=18,PassDepthMm=3,SpindleRpm=18000,Status=OperationStatus.Current};
        var path=new ToolpathOperation{SourceOperationId=definition.Id,Name=definition.Name,Moves=[..moves]};definition.CalculatedToolpath=path;
        return(definition,new ToolpathProgram{Operations=[path]});
    }
    [Fact] public void CompleteSafeProgram_Passes()
    {
        var (definition,program)=Program(new RapidMove(new(20,20),5),new LinearMove(new(80,20),-18,1200));
        var result=new SafetyPreflightService().Check(new(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18},Machine(),[],[definition],program));
        Assert.Equal("Safe",result.Status);Assert.Equal(18,result.DeepestCutMm);
    }
    [Fact] public void MissingMachine_Blocks()
    {
        var (definition,program)=Program(new RapidMove(new(20,20),5));var result=new SafetyPreflightService().Check(new(new JobSettings(),null,[],[definition],program));
        Assert.Contains(result.Findings,x=>x.Code=="machine-data-missing"&&x.Severity==SafetySeverity.Error);
    }
    [Fact] public void FixtureCollisionAndLowRapid_Block()
    {
        var (definition,program)=Program(new RapidMove(new(50,50),9),new LinearMove(new(50,50),-3,500));
        var clamp=new FixtureDefinition{Name="Front clamp",MinXmm=40,MinYmm=40,MaxXmm=60,MaxYmm=60,TopZmm=8,RequiredClearanceMm=2};
        var result=new SafetyPreflightService().Check(new(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18},Machine(),[clamp],[definition],program));
        Assert.Contains(result.Findings,x=>x.Code=="fixture-collision");Assert.Contains(result.Findings,x=>x.Code=="fixture-clearance");
    }
    [Fact] public void SpoilboardAllowance_WarnsThenErrors()
    {
        var (definition,program)=Program(new LinearMove(new(50,50),-18.25,500));definition.CutDepthMm=18.25;
        var warning=new SafetyPreflightService().Check(new(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18},Machine(.5),[],[definition],program));Assert.Contains(warning.Findings,x=>x.Code=="spoilboard-cut"&&x.Severity==SafetySeverity.Warning);
        var error=new SafetyPreflightService().Check(new(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18},Machine(.1),[],[definition],program));Assert.Contains(error.Findings,x=>x.Code=="spoilboard-limit"&&x.Severity==SafetySeverity.Error);
    }
    [Fact] public void TravelAndSpindleLimits_Block()
    {
        var (definition,program)=Program(new RapidMove(new(650,20),5));definition.SpindleRpm=30000;
        var result=new SafetyPreflightService().Check(new(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18},Machine(),[],[definition],program));
        Assert.Contains(result.Findings,x=>x.Code=="machine-envelope");Assert.Contains(result.Findings,x=>x.Code=="spindle-limit");
    }
}
