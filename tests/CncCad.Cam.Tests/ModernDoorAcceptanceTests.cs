using CncCad.Cam.Drilling;
using CncCad.Cam.Operations;
using CncCad.Cam.Pocket;
using CncCad.Cam.Profile;
using CncCad.Cam.Safety;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.IO.Projects;

namespace CncCad.Cam.Tests;

public sealed class ModernDoorAcceptanceTests
{
    [Fact]
    public void Fixture_ContainsNamedLayersDoorFeaturesAndAllFiveOperations()
    {
        var project=ModernDoorAcceptanceProject.Create();
        Assert.Equal((1000d,2300d,50d),(project.Document.Job.WidthMm,project.Document.Job.HeightMm,project.Document.Job.ThicknessMm));
        Assert.Equal(["Door Boundary","Recessed Panel","Flutes","Hardware","Lettering","QA Geometry"],project.Document.Layers.OrderBy(x=>x.Order).Select(x=>x.Name));
        Assert.Equal(11,project.Document.Entities.OfType<LineEntity>().Count());
        Assert.Equal(2,project.Document.Entities.OfType<DrillPointEntity>().Count());
        Assert.Contains(project.Document.Entities.OfType<TextEntity>(),x=>x.Text=="23");
        Assert.Contains(project.Document.Entities.OfType<TextEntity>(),x=>x.Text=="VR");
        Assert.Collection(project.CamJob.Operations,
            x=>Assert.IsType<ProfileOperationDefinition>(x),x=>Assert.IsType<PocketOperationDefinition>(x),x=>Assert.IsType<DrillOperationDefinition>(x),
            x=>Assert.IsType<EngraveOperationDefinition>(x),x=>Assert.IsType<VCarveOperationDefinition>(x));
    }

    [Fact]
    public void Fixture_AllOperationsCalculateAndPassSafetyGate()
    {
        var project=ModernDoorAcceptanceProject.Create();var geometry=project.Document.Entities.ToDictionary(x=>x.Id);
        var calculator=new CompositeOperationCalculator(new ProfileOperationCalculator(),new PocketOperationCalculator(),new DrillOperationCalculator(),new EngraveOperationCalculator(),new VCarveOperationCalculator());
        var program=new ToolpathProgram{Name=project.Document.Job.Name};
        foreach(var operation in project.CamJob.Operations)
        {
            var result=calculator.Calculate(operation,geometry);Assert.True(result.Success,$"{operation.Name}: {string.Join("; ",result.Errors.Select(x=>x.Message))}");
            operation.CalculatedToolpath=result.Toolpath;operation.Status=OperationStatus.Current;program.Operations.Add(result.Toolpath!);
        }
        var preflight=new SafetyPreflightService().Check(new(project.Document.Job,project.CamJob.Machine,project.CamJob.Fixtures,project.CamJob.Operations,program));
        Assert.True(preflight.CanExport,string.Join("; ",preflight.Findings.Where(x=>x.Severity==SafetySeverity.Error).Select(x=>$"{x.Code}: {x.Message}")));
        Assert.Equal(18,preflight.DeepestCutMm,3);
    }

    [Fact]
    public async Task Fixture_V8RoundTripPreservesEditableOperationsAndText()
    {
        var serializer=new JsonProjectSerializer();await using var stream=new MemoryStream();await serializer.SaveAsync(ModernDoorAcceptanceProject.Create(),stream);stream.Position=0;
        var loaded=await serializer.LoadAsync(stream);Assert.Equal(CncProject.CurrentVersion,loaded.Version);Assert.Equal(5,loaded.CamJob.Operations.Count);
        Assert.Equal("23",loaded.Document.Entities.OfType<TextEntity>().Single(x=>x.Text=="23").Text);Assert.Equal(6,loaded.Document.Layers.Count);
    }
}
