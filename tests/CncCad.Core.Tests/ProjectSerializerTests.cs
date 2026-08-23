using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.IO.Projects;
using CncCad.Cam.Operations;
using CncCad.Cam.Nesting;

namespace CncCad.Core.Tests;

public class ProjectSerializerTests
{
    [Fact]
    public async Task RoundTrip_PreservesJobAndTypedGeometry()
    {
        var document = new CadDocument { Job = new JobSettings { Name = "Cabinet side", WidthMm = 800, HeightMm = 450, ThicknessMm = 16,Origin=XyOrigin.TopCentre,OriginOffsetXmm=4,OriginOffsetYmm=-2,ZZero=ZZeroPosition.MachineBed } };
        document.Entities.Add(new RectangleEntity { LayerId = document.ActiveLayerId, Origin = new Point2D(25, 30), Width = 200, Height = 100 });
        var serializer = new JsonProjectSerializer();
        await using var stream = new MemoryStream();
        await serializer.SaveAsync(new CncProject { Document = document }, stream);
        stream.Position = 0;
        var loaded = await serializer.LoadAsync(stream);
        Assert.Equal(5, loaded.Version);
        Assert.Equal("Cabinet side", loaded.Document.Job.Name);
        Assert.Equal(16, loaded.Document.Job.ThicknessMm);
        Assert.Equal(XyOrigin.TopCentre,loaded.Document.Job.Origin);Assert.Equal(4,loaded.Document.Job.OriginOffsetXmm);Assert.Equal(-2,loaded.Document.Job.OriginOffsetYmm);Assert.Equal(ZZeroPosition.MachineBed,loaded.Document.Job.ZZero);
        var rectangle = Assert.IsType<RectangleEntity>(Assert.Single(loaded.Document.Entities));
        Assert.Equal(new Point2D(25, 30), rectangle.Origin);
        Assert.Equal(200, rectangle.Width);
    }

    [Fact]
    public async Task RoundTrip_PreservesEverySupportedEntityType()
    {
        var document = new CadDocument(); var layer = document.ActiveLayerId;
        var figure=new VectorPathFigure{Start=new(0,0),IsClosed=true,Segments=[new CubicPathSegment(new(1,0),new(1,1),new(0,1)),new LinePathSegment(new(0,0))]};
        document.Entities.AddRange([new LineEntity { LayerId = layer, Start = new(1,2), End = new(3,4) }, new PolylineEntity { LayerId = layer, Points = [new(1,1), new(2,2), new(3,1)], IsClosed = true }, new RectangleEntity { LayerId = layer, Origin = new(4,5), Width = 6, Height = 7 }, new CircleEntity { LayerId = layer, Center = new(8,9), Radius = 3 }, new ArcEntity { LayerId = layer, Center = new(10,10), Radius = 5, StartAngleDegrees = 20, SweepAngleDegrees = 140 }, new EllipseEntity { LayerId = layer, Center = new(12,13), RadiusX = 6, RadiusY = 2 },new VectorPathEntity{LayerId=layer,Figures=[figure]},new TextEntity{LayerId=layer,Text="CNC",SizeMm=12,Outlines=[figure]}]);
        EntityGeometry.Translate(document.Entities[0], new(10, 20));
        var serializer = new JsonProjectSerializer(); await using var stream = new MemoryStream(); await serializer.SaveAsync(new CncProject { Document = document }, stream); stream.Position = 0; var loaded = await serializer.LoadAsync(stream);
        Assert.Collection(loaded.Document.Entities, e => Assert.IsType<LineEntity>(e), e => Assert.IsType<PolylineEntity>(e), e => Assert.IsType<RectangleEntity>(e), e => Assert.IsType<CircleEntity>(e), e => Assert.IsType<ArcEntity>(e), e => Assert.IsType<EllipseEntity>(e),e=>Assert.IsType<VectorPathEntity>(e),e=>Assert.IsType<TextEntity>(e));
        Assert.Equal(new Point2D(11,22), ((LineEntity)loaded.Document.Entities[0]).Start);
    }

    [Fact]
    public async Task VersionOne_LoadsAsGeometryOnlyProject()
    {
        const string json = """{"version":1,"job":{"name":"Legacy","widthMm":600,"heightMm":400,"thicknessMm":18,"safeZMm":5,"origin":0,"zZero":0},"layers":[{"id":"11111111-1111-1111-1111-111111111111","name":"Layer 1","isVisible":true,"isLocked":false}],"activeLayerId":"11111111-1111-1111-1111-111111111111","entities":[]}""";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var loaded = await new JsonProjectSerializer().LoadAsync(stream);
        Assert.Equal("Legacy", loaded.Document.Job.Name);
        Assert.Empty(loaded.CamJob.Operations);
    }

    [Fact]
    public async Task VersionTwo_PreservesOperationAndToolSnapshot()
    {
        var document = new CadDocument(); var rectangle = new RectangleEntity { LayerId = document.ActiveLayerId, Width = 100, Height = 100 }; document.Entities.Add(rectangle);
        var operation = new ProfileOperationDefinition { Name = "Outline", GeometryIds = [rectangle.Id], Tool = new ToolSnapshot { Name = "6mm End Mill", DiameterMm = 6, FeedRateMmPerMin = 1800 }, CutDepthMm = 18, PassDepthMm = 3 };
        var project = new CncProject { Document = document, CamJob = new CamJob { Operations = [operation] } };
        await using var stream = new MemoryStream(); var serializer = new JsonProjectSerializer(); await serializer.SaveAsync(project, stream); stream.Position = 0;
        var loaded = await serializer.LoadAsync(stream); var profile = Assert.IsType<ProfileOperationDefinition>(Assert.Single(loaded.CamJob.Operations));
        Assert.Equal("Outline", profile.Name); Assert.Equal(6, profile.Tool.DiameterMm); Assert.Equal(rectangle.Id, Assert.Single(profile.GeometryIds));
    }

    [Fact]
    public async Task VersionFive_PreservesTabsNestingSheetsAndInstances()
    {
        var document=new CadDocument();var rectangle=new RectangleEntity{LayerId=document.ActiveLayerId,Width=100,Height=50};document.Entities.Add(rectangle);var part=new NestPartDefinition{Name="Door",OuterGeometryId=rectangle.Id,Quantity=4};var sheet=new MaterialSheetDefinition{Name="Sheet 1",WidthMm=600,HeightMm=400};var placement=new NestedPartPlacement{PartId=part.Id,SheetId=sheet.Id,CopyIndex=0,Xmm=10,Ymm=12};var operation=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=new(){DiameterMm=6},Tabs=new(){Enabled=true,PlacementMode=TabPlacementMode.Manual,ManualPlacements=[new(Guid.NewGuid(),.25)]}};var project=new CncProject{Document=document,CamJob=new(){Operations=[operation]},Nesting=new(){Parts=[part],AcceptedLayout=new(){Sheets=[sheet],Placements=[placement]}}};await using var stream=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(project,stream);stream.Position=0;var loaded=await serializer.LoadAsync(stream);Assert.Equal(5,loaded.Version);var profile=Assert.IsType<ProfileOperationDefinition>(Assert.Single(loaded.CamJob.Operations));Assert.True(profile.Tabs.Enabled);Assert.Equal(.25,Assert.Single(profile.Tabs.ManualPlacements).NormalizedDistance);Assert.Equal(4,Assert.Single(loaded.Nesting.Parts).Quantity);Assert.Equal(sheet.Id,Assert.Single(loaded.Nesting.AcceptedLayout!.Sheets).Id);
    }
}
