using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Preview;
using CncCad.Core.Transforms;
using CncCad.IO.Projects;
using CncCad.Rendering.Preview;
using CncCad.Simulation.Stock;

namespace CncCad.Core.Tests;

public sealed class StockPreviewRenderingTests
{
    [Fact]
    public void PreviewSettings_NormalizeTintAndResolveAutomaticMaterials()
    {
        Assert.Equal("#A0B1C2",ProjectPreviewSettings.NormalizeTint("a0b1c2"));
        Assert.Null(ProjectPreviewSettings.NormalizeTint("not-a-colour"));
        var settings=new ProjectPreviewSettings{MaterialPreset=MaterialAppearancePreset.Auto};
        Assert.Equal(MaterialAppearancePreset.Mdf,settings.ResolveMaterial("MDF","Wood composite"));
        Assert.Equal(MaterialAppearancePreset.Plywood,settings.ResolveMaterial("Birch plywood","Sheet"));
        Assert.Equal(MaterialAppearancePreset.Softwood,settings.ResolveMaterial("Pine","Timber"));
        Assert.Equal(MaterialAppearancePreset.Hardwood,settings.ResolveMaterial("Oak","Timber"));
    }

    [Fact]
    public async Task ProjectV8_RoundTripsPreviewAppearanceWithoutChangingCamData()
    {
        var source=ModernDoorAcceptanceProject.Create();
        source.PreviewSettings=new(){Style=PreviewRenderStyle.Material,MaterialPreset=MaterialAppearancePreset.Plywood,GrainDirection=GrainDirection.AlongStockX,TintHex="#C88D4D"};
        await using var stream=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(source,stream);stream.Position=0;
        var loaded=await serializer.LoadAsync(stream);
        Assert.Equal(8,loaded.Version);Assert.Equal(source.PreviewSettings,loaded.PreviewSettings);Assert.Equal(5,loaded.CamJob.Operations.Count);
        Assert.Equal(source.CamJob.Operations.Select(x=>x.Tool),loaded.CamJob.Operations.Select(x=>x.Tool));
    }

    [Fact]
    public void SurfaceMesh_ContainsSimulatedCutDepthAndClosedStockFaces()
    {
        var operation=new EngraveOperationDefinition
        {
            Tool=new ToolSnapshot{Name="Engraver",ToolNumber=1,Type=ToolType.EngravingBit,DiameterMm=3,FeedRateMmPerMin=500,PlungeRateMmPerMin=200,SpindleRpm=18000,StepDownMm=1},
            IsEnabled=true,
            CalculatedToolpath=new ToolpathOperation{Moves=[new RapidMove(new(10,10),5),new LinearMove(new(10,10),-3,200),new LinearMove(new(80,10),-3,500)]}
        };
        var field=new CpuStockSimulationService().Simulate(100,50,10,[operation]);
        var mesh=StockSurfaceMeshBuilder.Build(field,100,50,10,100);
        Assert.Contains(mesh.Triangles,x=>x.Kind==StockSurfaceKind.Top&&new[]{x.A.Position.Z,x.B.Position.Z,x.C.Position.Z}.Min()<=-2.9);
        Assert.Contains(mesh.Triangles,x=>x.Kind==StockSurfaceKind.Side);
        Assert.Contains(mesh.Triangles,x=>x.Kind==StockSurfaceKind.Bottom);
        Assert.All(mesh.Triangles.SelectMany(x=>new[]{x.A.Normal,x.B.Normal,x.C.Normal}),normal=>Assert.InRange(Math.Sqrt(normal.X*normal.X+normal.Y*normal.Y+normal.Z*normal.Z),.999,1.001));
    }

    [Fact]
    public void Renderer_IsDeterministicAndMaterialStylesProduceDistinctPixels()
    {
        var mesh=StockSurfaceMeshBuilder.Build(null,100,60,12,80);var orbit=new OrbitCamera();orbit.SetPreset(CameraPreset.Isometric);orbit.Fit(100,60,12,5,320,220);
        var renderer=new StockPreviewRenderer();var camera=PreviewCameraState.From(orbit);
        var technical=new ProjectPreviewSettings{Style=PreviewRenderStyle.Technical};
        var hardwood=new ProjectPreviewSettings{Style=PreviewRenderStyle.Material,MaterialPreset=MaterialAppearancePreset.Hardwood,GrainDirection=GrainDirection.AlongStockY};
        var first=renderer.Render(mesh,camera,hardwood,"Hardwood","Timber",320,220,320,220);var second=renderer.Render(mesh,camera,hardwood,"Hardwood","Timber",320,220,320,220);
        var neutral=renderer.Render(mesh,camera,technical,null,null,320,220,320,220);
        Assert.Equal(first.BgraPixels,second.BgraPixels);
        Assert.NotEqual(first.BgraPixels,neutral.BgraPixels);
        Assert.True(first.BgraPixels.Where((_,index)=>index%4==3).Count(alpha=>alpha==255)>1000);
        var materialColours=Enumerable.Range(0,first.Width*first.Height).Where(i=>first.BgraPixels[i*4+3]>0).Select(i=>(first.BgraPixels[i*4],first.BgraPixels[i*4+1],first.BgraPixels[i*4+2])).Distinct().Count();
        Assert.True(materialColours>20);
    }

    [Fact]
    public void AllMaterialPresetsAndGrainAxesProduceDistinctDeterministicImages()
    {
        var mesh=StockSurfaceMeshBuilder.Build(null,120,80,18,72);var orbit=new OrbitCamera();orbit.SetPreset(CameraPreset.Top);orbit.Fit(120,80,18,5,240,180);var renderer=new StockPreviewRenderer();var camera=PreviewCameraState.From(orbit);
        var hashes=Enum.GetValues<MaterialAppearancePreset>().Where(x=>x!=MaterialAppearancePreset.Auto).Select(preset=>
        {
            var settings=new ProjectPreviewSettings{Style=PreviewRenderStyle.Material,MaterialPreset=preset,GrainDirection=GrainDirection.AlongStockY};
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(renderer.Render(mesh,camera,settings,null,null,240,180,240,180).BgraPixels));
        }).ToArray();
        Assert.Equal(4,hashes.Distinct().Count());
        var xGrain=new ProjectPreviewSettings{Style=PreviewRenderStyle.Material,MaterialPreset=MaterialAppearancePreset.Hardwood,GrainDirection=GrainDirection.AlongStockX};
        var yGrain=xGrain with{GrainDirection=GrainDirection.AlongStockY};
        Assert.NotEqual(renderer.Render(mesh,camera,xGrain,null,null,240,180,240,180).BgraPixels,renderer.Render(mesh,camera,yGrain,null,null,240,180,240,180).BgraPixels);
    }
}
