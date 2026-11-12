using CncCad.Cam.Drilling;
using CncCad.Cam.Operations;
using CncCad.Cam.Pocket;
using CncCad.Cam.Profile;
using CncCad.Cam.Safety;
using CncCad.Cam.Surface;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.IO.Projects;
using CncCad.PostProcessors.GCode;
using CncCad.PostProcessors.Models;
using CncCad.PostProcessors.Uccnc;

namespace CncCad.PostProcessors.Tests;

public sealed class ProductionAcceptanceTests
{
    private static readonly ICamOperationCalculator Calculators=new CompositeOperationCalculator(new ProfileOperationCalculator(),new PocketOperationCalculator(),new DrillOperationCalculator(),new EngraveOperationCalculator(),new VCarveOperationCalculator(),new SurfaceOperationCalculator());

    [Theory]
    [InlineData("uccnc",MeasurementUnit.Millimetres)]
    [InlineData("uccnc",MeasurementUnit.Inches)]
    [InlineData("mach3",MeasurementUnit.Millimetres)]
    [InlineData("mach3",MeasurementUnit.Inches)]
    [InlineData("mach4",MeasurementUnit.Millimetres)]
    [InlineData("mach4",MeasurementUnit.Inches)]
    [InlineData("grbl",MeasurementUnit.Millimetres)]
    [InlineData("grbl",MeasurementUnit.Inches)]
    [InlineData("linuxcnc",MeasurementUnit.Millimetres)]
    [InlineData("linuxcnc",MeasurementUnit.Inches)]
    public void EveryBuiltInPostMatchesItsExactControllerGolden(string postId,MeasurementUnit unit)
    {
        var definition=BuiltInPostDefinitions.All.Single(post=>post.Id==postId);
        var automatic=postId is "uccnc" or "mach3" or "mach4";
        var manual=postId=="linuxcnc";
        var machine=new MachineProfile{ControllerFamily=definition.ControllerFamily,ToolChangeMode=automatic?MachineToolChangeMode.Automatic:manual?MachineToolChangeMode.ManualPause:MachineToolChangeMode.SeparateFiles,SafeZMm=5,RapidRateMmPerMin=5000,MaximumSpindleRpm=24000};
        var scope=postId=="grbl"?PostExportScope.SeparateByTool:PostExportScope.Combined;
        var result=new DeclarativeGCodePostProcessor(definition).Process(new(GoldenProgram(),machine,unit,definition,scope));
        Assert.True(result.Success,string.Join("; ",result.Errors.Select(error=>error.Message)));
        var suffix=unit==MeasurementUnit.Millimetres?"metric":"imperial";
        var expected=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","PostGoldens",$"{postId}-{suffix}.golden"));
        var actual=Assert.Single(result.Files).Content;
        Assert.Equal(expected,actual);
        Assert.Contains("G2",actual);Assert.Contains("G4 P0.250",actual);
        if(automatic)Assert.Contains("T1 M6",actual);
        else if(manual)Assert.Contains("M0",actual);
        else{Assert.DoesNotContain("M0",actual);Assert.DoesNotContain("M6",actual);Assert.Contains("Load T1",actual);}
    }

    [Theory]
    [InlineData("cabinet-panel")]
    [InlineData("sign")]
    [InlineData("nested-sheet")]
    public async Task ProofJob_ReopensPreflightsAndPostsDeterministicallyInBothUnits(string fixture)
    {
        var source=ProductionAcceptanceProjects.All[fixture]();await using var saved=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(source,saved);saved.Position=0;var project=await serializer.LoadAsync(saved);Assert.Equal(CncProject.CurrentVersion,project.Version);
        var geometry=project.Document.Entities.ToDictionary(x=>x.Id);foreach(var operation in project.CamJob.Operations){var result=Calculators.Calculate(operation,geometry);Assert.True(result.Success,$"{fixture}/{operation.Name}: {string.Join("; ",result.Errors.Select(x=>x.Message))}");operation.CalculatedToolpath=result.Toolpath;operation.Status=OperationStatus.Current;}
        var program=new ToolpathProgram{Name=project.Document.Job.Name,Operations=project.CamJob.Operations.Select(x=>x.CalculatedToolpath!).ToList()};var preflight=new SafetyPreflightService().Check(new(project.Document.Job,project.CamJob.Machine,project.CamJob.Fixtures,project.CamJob.Operations,program));Assert.True(preflight.CanExport,string.Join("; ",preflight.Findings.Where(x=>x.Severity==SafetySeverity.Error).Select(x=>x.Message)));
        foreach(var definition in BuiltInPostDefinitions.All)
        foreach(var unit in Enum.GetValues<MeasurementUnit>())
        {
            var processor=new DeclarativeGCodePostProcessor(definition);var machine=PostMachine(project.CamJob.Machine!,definition.ControllerFamily);var request=new PostProcessRequest(program,machine,unit,definition,PostExportScope.SeparateByTool);var first=processor.Process(request);var second=processor.Process(request);Assert.True(first.Success,$"{fixture}/{definition.Id}/{unit}: {string.Join("; ",first.Errors.Select(x=>x.Message))}");Assert.Equal(first.Files.Select(x=>x.Content),second.Files.Select(x=>x.Content));Assert.All(first.Files,x=>{Assert.Contains(unit==MeasurementUnit.Inches?"G20":"G21",x.Content);Assert.EndsWith("M30\n",x.Content);});
        }
    }

    [Fact]
    public void ExpandedPeckDrillIsPortableAcrossEveryBuiltInPostWithoutCannedCycles()
    {
        var point=new DrillPointEntity{Position=new(25.4,50.8)};var definition=new DrillOperationDefinition{GeometryIds=[point.Id],Tool=new(){SourceToolId=Guid.NewGuid(),Name="5mm Drill",ToolNumber=9,Type=CncCad.Cam.Tools.ToolType.Drill,DiameterMm=5,FeedRateMmPerMin=500,PlungeRateMmPerMin=240,SpindleRpm=12000,StepDownMm=2},Cycle=DrillCycle.Peck,CutDepthMm=6,PeckDepthMm=2,SafeZMm=5,RetractZMm=1,DwellSeconds=.2,PlungeRateMmPerMin=240,SpindleRpm=12000};var calculated=new DrillOperationCalculator().Calculate(definition,new Dictionary<Guid,CadEntity>{{point.Id,point}});Assert.True(calculated.Success);var program=new ToolpathProgram{Name="Portable drill",Operations=[calculated.Toolpath!]};
        foreach(var post in BuiltInPostDefinitions.All)
        foreach(var unit in Enum.GetValues<MeasurementUnit>())
        {
            var machine=new MachineProfile{ControllerFamily=post.ControllerFamily,ToolChangeMode=MachineToolChangeMode.SeparateFiles};var result=new DeclarativeGCodePostProcessor(post).Process(new(program,machine,unit,post,PostExportScope.SeparateByTool));Assert.True(result.Success,$"{post.Id}/{unit}: {string.Join("; ",result.Errors.Select(error=>error.Message))}");var code=Assert.Single(result.Files).Content;Assert.DoesNotContain("G81",code);Assert.DoesNotContain("G82",code);Assert.DoesNotContain("G83",code);Assert.Contains(unit==MeasurementUnit.Millimetres?"Z-6.000":"Z-0.236",code);Assert.Contains(post.CommentStyle==PostCommentStyle.Parentheses?"(Load T9 5mm Drill)":"; Load T9 5mm Drill",code);
        }
    }

    private static ToolpathProgram GoldenProgram()=>new()
    {
        Name="Golden controller proof",
        Operations=
        [
            new ToolpathOperation
            {
                Name="Contour proof",
                ToolId=Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Moves=
                [
                    new ToolChange(1,"6mm End Mill"),
                    new SpindleStart(18000),
                    new RapidMove(new(25.4,12.7),5),
                    new LinearMove(new(25.4,12.7),-2.54,254),
                    new ArcMove(new(50.8,12.7),new(38.1,12.7),-2.54,508,true),
                    new DwellMove(.25),
                    new SpindleStop()
                ]
            }
        ]
    };

    private static MachineProfile PostMachine(MachineSnapshot machine,ControllerFamily family)=>new(){Name=machine.Name,PostProcessor=machine.PostProcessorId,MaximumXmm=machine.MaximumXmm,MaximumYmm=machine.MaximumYmm,MaximumZmm=machine.MaximumZmm,SafeZMm=machine.DefaultClearanceMm,MaximumSpindleRpm=machine.MaximumSpindleRpm,RapidRateMmPerMin=machine.RapidRateMmPerMin,ParkXmm=machine.ParkXmm,ParkYmm=machine.ParkYmm,ControllerFamily=family,SupportsArcs=machine.SupportsArcs,ToolChangeMode=MachineToolChangeMode.SeparateFiles};
}
