using System.Text;
using CncCad.Cam.Operations;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.IO.Projects;

namespace CncCad.Core.Tests;

public sealed class RecoveryAndV8Tests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)]
    public async Task VersionsOneThroughEight_MigrateInMemoryAndRoundTripAsNine(int version)
    {
        const string template="""{"version":VERSION,"document":{"version":1,"job":{"name":"Legacy","widthMm":100,"heightMm":100,"thicknessMm":10,"safeZMm":5},"layers":[{"id":"11111111-1111-1111-1111-111111111111","name":"Layer","isVisible":true,"isLocked":false,"order":0}],"activeLayerId":"11111111-1111-1111-1111-111111111111","entities":[]},"camJob":{"operations":[],"fixtures":[],"safetySetupIncomplete":true},"nesting":{"parts":[]}}""";var json=template.Replace("VERSION",version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));var serializer=new JsonProjectSerializer();var project=await serializer.LoadAsync(stream);Assert.Equal(CncProject.CurrentVersion,project.Version);Assert.Equal("Legacy",project.Document.Job.Name);Assert.Equal(json,Encoding.UTF8.GetString(stream.ToArray()));
        await using var saved=new MemoryStream();await serializer.SaveAsync(project,saved);saved.Position=0;var reopened=await serializer.LoadAsync(saved);Assert.Equal(CncProject.CurrentVersion,reopened.Version);Assert.Equal("Legacy",reopened.Document.Job.Name);
    }

    [Fact]
    public async Task VersionEight_RoundTripsPocketDrillPointMaterialAndPostIdentity()
    {
        var document=new CadDocument();var point=new DrillPointEntity{LayerId=document.ActiveLayerId,Position=new(12,18)};document.Entities.Add(point);var machine=new CncCad.Cam.Safety.MachineProfileDefinition{PostProcessorId="uccnc"}.Snapshot();var project=new CncProject{Document=document,CamJob=new(){Machine=machine,Material=new(){Name="MDF",Category="Wood composite"},Operations=[new DrillOperationDefinition{GeometryIds=[point.Id],Tool=new(){DiameterMm=5},CutDepthMm=6},new PocketOperationDefinition{Tool=new(){DiameterMm=6},StepOverMm=2}]}};await using var stream=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(project,stream);stream.Position=0;var loaded=await serializer.LoadAsync(stream);Assert.Equal(CncProject.CurrentVersion,loaded.Version);Assert.IsType<DrillPointEntity>(Assert.Single(loaded.Document.Entities));Assert.IsType<DrillOperationDefinition>(loaded.CamJob.Operations[0]);Assert.IsType<PocketOperationDefinition>(loaded.CamJob.Operations[1]);Assert.Equal("MDF",loaded.CamJob.Material!.Name);Assert.Equal("uccnc",loaded.CamJob.Machine!.PostProcessorId);
    }

    [Fact]
    public async Task RecoveryStore_KeepsLatestThreeAndClearsJob()
    {
        var directory=Path.Combine(Path.GetTempPath(),"cnccad-recovery-tests",Guid.NewGuid().ToString("N"));try{var store=new JsonRecoveryStore(directory);for(var i=0;i<5;i++){await store.SaveAsync("job-a",$"Job {i}",null,new CncProject{Document=new(){Job=new(){Name=$"Job {i}"}}});await Task.Delay(3);}var snapshots=await store.GetRecoverableAsync();Assert.Equal(3,snapshots.Count(x=>x.JobKey=="job-a"));var loaded=await store.LoadAsync(snapshots.First());Assert.StartsWith("Job",loaded.Document.Job.Name);await store.DeleteJobAsync("job-a");Assert.Empty(await store.GetRecoverableAsync());}finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
