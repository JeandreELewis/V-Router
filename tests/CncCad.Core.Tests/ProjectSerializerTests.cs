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
    public async Task SaveAlwaysWritesCurrentProjectVersion()
    {
        var project = new CncProject { Version = 1 };
        await using var stream = new MemoryStream();
        await new JsonProjectSerializer().SaveAsync(project, stream);
        stream.Position = 0;
        using var json = await System.Text.Json.JsonDocument.ParseAsync(stream);
        Assert.Equal(CncProject.CurrentVersion, json.RootElement.GetProperty("version").GetInt32());
    }

    [Theory]
    [InlineData("{\"version\":0}")]
    [InlineData("{\"version\":7,\"document\":{\"job\":{},\"layers\":[],\"entities\":[]},\"camJob\":{\"operations\":[],\"fixtures\":[]},\"nesting\":{\"parts\":[]}}")]
    public async Task InvalidProjectStructureIsRejected(string json)
    {
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await Assert.ThrowsAsync<InvalidDataException>(() => new JsonProjectSerializer().LoadAsync(stream));
    }

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
        Assert.Equal(CncProject.CurrentVersion, loaded.Version);
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
        var document=new CadDocument();var rectangle=new RectangleEntity{LayerId=document.ActiveLayerId,Width=100,Height=50};document.Entities.Add(rectangle);var part=new NestPartDefinition{Name="Door",OuterGeometryId=rectangle.Id,Quantity=4};var sheet=new MaterialSheetDefinition{Name="Sheet 1",WidthMm=600,HeightMm=400};var placement=new NestedPartPlacement{PartId=part.Id,SheetId=sheet.Id,CopyIndex=0,Xmm=10,Ymm=12};var operation=new ProfileOperationDefinition{GeometryIds=[rectangle.Id],Tool=new(){DiameterMm=6},Tabs=new(){Enabled=true,PlacementMode=TabPlacementMode.Manual,ManualPlacements=[new(Guid.NewGuid(),.25)]}};var project=new CncProject{Document=document,CamJob=new(){Operations=[operation]},Nesting=new(){Parts=[part],AcceptedLayout=new(){Sheets=[sheet],Placements=[placement]}}};await using var stream=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(project,stream);stream.Position=0;var loaded=await serializer.LoadAsync(stream);Assert.Equal(CncProject.CurrentVersion,loaded.Version);var profile=Assert.IsType<ProfileOperationDefinition>(Assert.Single(loaded.CamJob.Operations));Assert.True(profile.Tabs.Enabled);Assert.Equal(.25,Assert.Single(profile.Tabs.ManualPlacements).NormalizedDistance);Assert.Equal(4,Assert.Single(loaded.Nesting.Parts).Quantity);Assert.Equal(sheet.Id,Assert.Single(loaded.Nesting.AcceptedLayout!.Sheets).Id);
    }

    [Fact]
    public async Task VersionSix_PreservesEditablePathTextColorsGroupsAndEngraving()
    {
        var document=new CadDocument();var guide=new CircleEntity{LayerId=document.ActiveLayerId,Center=new(50,50),Radius=30,StrokeColor="#3355FF"};var group=Guid.NewGuid();var text=new TextEntity{LayerId=document.ActiveLayerId,Text="CNC",FontFamily="Hershey Simplex",FontKind=FontKind.SingleLine,LayoutMode=TextLayoutMode.FollowPath,GroupId=group,StrokeColor="#FFAA00",PathPlacement=new(){GuideEntityId=guide.Id,BaselineOffsetMm=2,GuideSnapshot=[..EntityGeometry.WorldPoints(guide)]},GlyphAdvancesMm=[5,5,5],SourceOutlines=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(5,10))]}],Outlines=[new(){Start=new(40,60),Segments=[new LinePathSegment(new(45,65))]}],Glyphs=[new(){Cluster="C",AdvanceMm=5,Order=0,SourceFigures=[new(){Start=new(0,0)}],PlacedFigures=[new(){Start=new(40,60)}]}]};document.Entities.AddRange([guide,text]);var engrave=new EngraveOperationDefinition{GeometryIds=[text.Id],Tool=new(){DiameterMm=1},CutDepthMm=1,PassDepthMm=.5};await using var stream=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(new(){Document=document,CamJob=new(){Operations=[engrave]}},stream);stream.Position=0;var loaded=await serializer.LoadAsync(stream);Assert.Equal(CncProject.CurrentVersion,loaded.Version);var restored=Assert.IsType<TextEntity>(loaded.Document.Entities[1]);Assert.Equal(FontKind.SingleLine,restored.FontKind);Assert.Equal(group,restored.GroupId);Assert.Equal("#FFAA00",restored.StrokeColor);Assert.Equal(guide.Id,restored.PathPlacement.GuideEntityId);Assert.Single(restored.Glyphs);Assert.IsType<EngraveOperationDefinition>(Assert.Single(loaded.CamJob.Operations));
    }
    [Fact]
    public async Task VersionSeven_PreservesGlyphsAndVCarvingToolGeometry()
    {
        var document=new CadDocument();var outline=new RectangleEntity{LayerId=document.ActiveLayerId,Width=40,Height=20};document.Entities.Add(outline);var carve=new VCarveOperationDefinition{GeometryIds=[outline.Id],Tool=new(){Name="60 degree",Type=CncCad.Cam.Tools.ToolType.VBit,DiameterMm=12,VBitAngleDegrees=60,TipDiameterMm=.2},MaximumDepthMm=2.5,OffsetStepOverMm=.4,ClearFlatRegions=true};await using var stream=new MemoryStream();var serializer=new JsonProjectSerializer();await serializer.SaveAsync(new(){Document=document,CamJob=new(){Operations=[carve]}},stream);stream.Position=0;var loaded=await serializer.LoadAsync(stream);Assert.Equal(CncProject.CurrentVersion,loaded.Version);var restored=Assert.IsType<VCarveOperationDefinition>(Assert.Single(loaded.CamJob.Operations));Assert.Equal(60,restored.Tool.VBitAngleDegrees);Assert.Equal(.2,restored.Tool.TipDiameterMm);Assert.Equal(2.5,restored.MaximumDepthMm);Assert.True(restored.ClearFlatRegions);
    }
}
