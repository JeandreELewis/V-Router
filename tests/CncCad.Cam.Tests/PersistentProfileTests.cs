using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Tools;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public class PersistentProfileTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public void VCarve_UsesVariableDepthAndHonorsMaximum(double angle)
    {
        var rectangle=new RectangleEntity{Width=40,Height=20};var operation=new VCarveOperationDefinition{GeometryIds=[rectangle.Id],Tool=new(){Name=$"{angle} degree V-bit",Type=ToolType.VBit,DiameterMm=12,VBitAngleDegrees=angle,TipDiameterMm=.2},MaximumDepthMm=2,OffsetStepOverMm=.5,ClearFlatRegions=true,SafeZMm=5,FeedRateMmPerMin=900,PlungeRateMmPerMin=250};var result=new VCarveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{rectangle.Id,rectangle}});Assert.True(result.Success,string.Join("; ",result.Errors.Select(x=>x.Message)));var depths=result.Toolpath!.Moves.OfType<LinearMove>().Select(x=>-x.Z).Where(x=>x>0).Distinct().Order().ToArray();Assert.NotEmpty(depths);Assert.True(depths[^1]<=2+1e-9);Assert.True(depths.Length>1);
    }
    [Fact]
    public void VCarve_RejectsSingleLineTextAndMissingToolAngle()
    {
        var text=new TextEntity{FontKind=FontKind.SingleLine,Outlines=[new(){Start=new(0,0),Segments=[new LinePathSegment(new(10,0))]}]};var operation=new VCarveOperationDefinition{GeometryIds=[text.Id],Tool=new(){Type=ToolType.VBit,DiameterMm=6},MaximumDepthMm=2,OffsetStepOverMm=.5};var result=new VCarveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{text.Id,text}});Assert.False(result.Success);Assert.Contains(result.Errors,x=>x.Code=="angle");Assert.Contains(result.Errors,x=>x.Message.Contains("Single-line"));
    }
    [Fact]
    public void Engraving_OpenCenterlines_GeneratesIndependentDepthPasses()
    {
        var line=new LineEntity{Start=new(0,0),End=new(20,0)};var path=new PolylineEntity{Points=[new(30,0),new(30,10),new(40,10)]};var operation=new EngraveOperationDefinition{Name="Lettering",GeometryIds=[line.Id,path.Id],Tool=new(){DiameterMm=1,Name="Engraver",FeedRateMmPerMin=800,PlungeRateMmPerMin=300,StepDownMm=.5},CutDepthMm=1,PassDepthMm=.5,SafeZMm=5,FeedRateMmPerMin=800,PlungeRateMmPerMin=300};var result=new EngraveOperationCalculator().Calculate(operation,new Dictionary<Guid,CadEntity>{{line.Id,line},{path.Id,path}});Assert.True(result.Success);Assert.Equal(8,result.Toolpath!.Moves.OfType<RapidMove>().Count());Assert.Contains(result.Toolpath.Moves.OfType<LinearMove>(),x=>Math.Abs(x.Z+.5)<1e-9);Assert.Contains(result.Toolpath.Moves.OfType<LinearMove>(),x=>Math.Abs(x.Z+1)<1e-9);
    }
    [Fact]
    public void OpenEngravingRepositionsAtClearanceBeforeEveryPlunge()
    {
        var line = new LineEntity { Start = new(10, 10), End = new(60, 30) };
        var operation = new EngraveOperationDefinition { GeometryIds = [line.Id], Tool = new() { DiameterMm = 1 },
            CutDepthMm = 3, PassDepthMm = 1, SafeZMm = 5, FeedRateMmPerMin = 800, PlungeRateMmPerMin = 200 };
        var result = new EngraveOperationCalculator().Calculate(operation, new Dictionary<Guid, CadEntity> { [line.Id] = line });
        Assert.True(result.Success);
        var moves = result.Toolpath!.Moves;
        var plunges = 0;
        for (var i = 1; i < moves.Count; i++)
        {
            if (moves[i] is not LinearMove { FeedRate: 200 } plunge) continue;
            var approach = Assert.IsType<RapidMove>(moves[i - 1]);
            Assert.Equal(plunge.End, approach.End);
            Assert.Equal(5, approach.Z);
            plunges++;
        }
        Assert.Equal(3, plunges);
    }
    private static ProfileOperationDefinition Operation(CadEntity entity, ProfileSide side = ProfileSide.Outside) => new()
    {
        GeometryIds = [entity.Id], Side = side, Tool = new ToolSnapshot { Name = "6mm", DiameterMm = 6 },
        CutDepthMm = 7, PassDepthMm = 3, SafeZMm = 5, FeedRateMmPerMin = 1200, PlungeRateMmPerMin = 300, SpindleRpm = 18000
    };
    private static OperationCalculationResult Calculate(ProfileOperationDefinition operation, params CadEntity[] entities) => new ProfileOperationCalculator().Calculate(operation, entities.ToDictionary(e => e.Id));

    [Theory]
    [InlineData(ProfileSide.Outside, -3, 103)]
    [InlineData(ProfileSide.Inside, 3, 97)]
    [InlineData(ProfileSide.OnVector, 0, 100)]
    public void Rectangle_UsesExactCutterCompensation(ProfileSide side, double minimum, double maximum)
    {
        var rectangle = new RectangleEntity { Width = 100, Height = 100 }; var result = Calculate(Operation(rectangle, side), rectangle);
        Assert.True(result.Success); var points = result.Toolpath!.Moves.OfType<LinearMove>().Select(m => m.End).ToArray();
        Assert.Equal(minimum, points.Min(p => p.X), 6); Assert.Equal(maximum, points.Max(p => p.X), 6);
    }

    [Fact] public void ClosedPolylineAndCircle_AreAccepted()
    {
        var polygon = new PolylineEntity { IsClosed = true, Points = [new(0,0), new(80,0), new(40,50)] };
        var circle = new CircleEntity { Center = new(150, 50), Radius = 25 };
        var operation = Operation(polygon); operation.GeometryIds.Add(circle.Id);
        var result = Calculate(operation, polygon, circle);
        Assert.True(result.Success); Assert.True(result.Toolpath!.Moves.OfType<LinearMove>().Count() > 30);
    }

    [Fact] public void FinalPass_IsExact_WhenStepDoesNotDivideDepth()
    {
        var rectangle = new RectangleEntity { Width = 100, Height = 100 }; var result = Calculate(Operation(rectangle), rectangle);
        var depths = result.Toolpath!.Moves.OfType<LinearMove>().Where(m => m.FeedRate == 300).Select(m => m.Z).ToArray();
        Assert.Equal([-3d, -6d, -7d], depths);
    }

    [Fact] public void InnerContour_IsCompletedBeforeOuterContour()
    {
        var outer = new RectangleEntity { Width = 200, Height = 100 }; var inner = new CircleEntity { Center = new(100,50), Radius = 20 };
        var operation = Operation(outer); operation.GeometryIds.Add(inner.Id); var result = Calculate(operation, outer, inner);
        var rapids = result.Toolpath!.Moves.OfType<RapidMove>().ToArray();
        Assert.InRange(rapids[0].End.X, 76.9, 123.1); Assert.Equal(4, rapids.Length); // entry/retract per contour
    }

    [Fact] public void InvalidAndCollapsedGeometry_ReturnStructuredErrors()
    {
        var open = new PolylineEntity { Points = [new(0,0), new(10,0), new(10,10)] }; var openResult = Calculate(Operation(open), open);
        Assert.Contains(openResult.Errors, e => e.Code == "geometry");
        var tiny = new RectangleEntity { Width = 4, Height = 4 }; var collapsed = Calculate(Operation(tiny, ProfileSide.Inside), tiny);
        Assert.Contains(collapsed.Errors, e => e.Code == "collapsed");
    }

    [Fact] public void ToolSnapshotAndDuplicate_AreIndependent()
    {
        var rectangle = new RectangleEntity { Width = 100, Height = 100 }; var original = Operation(rectangle); var copy = original.Duplicate();
        original.Tool = original.Tool with { DiameterMm = 12 }; original.GeometryIds.Clear();
        Assert.Equal(6, copy.Tool.DiameterMm); Assert.Single(copy.GeometryIds); Assert.NotEqual(original.Id, copy.Id);
    }

    [Fact] public void CamJob_MarksReferencedOperationsStaleOrInvalid()
    {
        var id = Guid.NewGuid(); var operation = new ProfileOperationDefinition { GeometryIds = [id], Status = OperationStatus.Current }; var job = new CamJob { Operations = [operation] };
        job.MarkGeometryChanged([id]); Assert.Equal(OperationStatus.Stale, operation.Status);
        job.MarkGeometryChanged([id], true); Assert.Equal(OperationStatus.Invalid, operation.Status); Assert.NotNull(operation.Error);
    }
}
