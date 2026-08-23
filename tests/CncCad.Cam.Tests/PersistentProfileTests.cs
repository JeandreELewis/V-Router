using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Tests;

public class PersistentProfileTests
{
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
