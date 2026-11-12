using Avalonia.Input;
using CncCad.Core.Commands;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Snapping;
using CncCad.Core.Selection;
using CncCad.Rendering.Canvas;

namespace CncCad.Core.Tests;

public class CadFoundationTests
{
    [Fact]
    public void ThreePointArc_ConstructsExpectedSemicircleAndBounds()
    {
        var arc = GeometryAlgorithms.CreateThreePointArc(new(-10, 0), new(0, 10), new(10, 0));
        Assert.Equal(0, arc.Center.X, 8); Assert.Equal(0, arc.Center.Y, 8); Assert.Equal(10, arc.Radius, 8);
        Assert.Equal(new BoundingBox(-10, 0, 10, 10), arc.GetBounds());
    }

    [Fact] public void ThreePointArc_RejectsCollinearPoints() => Assert.Throws<ArgumentException>(() => GeometryAlgorithms.CreateThreePointArc(new(0, 0), new(5, 0), new(10, 0)));

    [Fact]
    public void ThreePointArcRejectsNonFiniteDuplicateAndScaleRelativeCollinearInput()
    {
        Assert.Throws<ArgumentException>(()=>GeometryAlgorithms.CreateThreePointArc(new(double.NaN,0),new(5,5),new(10,0)));
        Assert.Throws<ArgumentException>(()=>GeometryAlgorithms.CreateThreePointArc(new(0,0),new(0,0),new(10,0)));
        Assert.Throws<ArgumentException>(()=>GeometryAlgorithms.CreateThreePointArc(new(1_000_000,1_000_000),new(1_000_005,1_000_000.0000000001),new(1_000_010,1_000_000)));
    }

    [Fact]
    public void ParametricShapesRejectUnsafeNumericsAndZeroRadiusRoundedRectangleHasFourRealCorners()
    {
        Assert.Throws<ArgumentException>(()=>ParametricShapes.Polygon(new(double.PositiveInfinity,0),10,5,default));
        Assert.Throws<ArgumentException>(()=>ParametricShapes.Polygon(new(0,0),10,10001,default));
        Assert.Throws<ArgumentException>(()=>ParametricShapes.Star(new(0,0),10,double.NaN,5,default));
        Assert.Throws<ArgumentException>(()=>ParametricShapes.Slot(new(0,0),0,4,default));
        Assert.Throws<ArgumentException>(()=>ParametricShapes.RoundedRectangle(new(0,0),10,5,-1,default));
        var rectangle=ParametricShapes.RoundedRectangle(new(2,3),10,5,0,default);Assert.Equal([new Point2D(2,3),new Point2D(12,3),new Point2D(12,8),new Point2D(2,8)],rectangle.Points);Assert.True(rectangle.IsClosed);
    }

    [Fact]
    public void TranslateAndClone_HandleEveryEntityWithoutReusingIds()
    {
        CadEntity[] entities = [new LineEntity { Start = new(0,0), End = new(5,5) }, new PolylineEntity { Points = [new(0,0), new(5,5)] }, new RectangleEntity { Width = 5, Height = 5 }, new CircleEntity { Radius = 2 }, new ArcEntity { Radius = 2, SweepAngleDegrees = 90 }, new EllipseEntity { RadiusX = 4, RadiusY = 2 }];
        foreach (var source in entities)
        {
            var before = source.GetBounds(); var clone = EntityGeometry.Clone(source); EntityGeometry.Translate(clone, new(10, 20)); var after = clone.GetBounds();
            Assert.NotEqual(source.Id, clone.Id); Assert.Equal(before.MinX + 10, after.MinX, 8); Assert.Equal(before.MinY + 20, after.MinY, 8);
        }
    }

    [Fact]
    public void SnapEngine_UsesPriorityThenDistanceAndHonorsDisabledModes()
    {
        var line = new LineEntity { Start = new(0,0), End = new(10,0) }; var engine = new SnapEngine();
        var result = engine.Snap(new(5.1, 0), [line], new SnapSettings { Grid = true, Endpoint = true, Midpoint = true, Center = true, GridSpacingMm = 5 }, 1);
        Assert.Equal(SnapType.Midpoint, result.Type); Assert.Equal(new Point2D(5, 0), result.Point);
        result = engine.Snap(new(0.2, 0), [line], new SnapSettings { Grid = true, Endpoint = false, Midpoint = false, Center = false, GridSpacingMm = 10 }, 1);
        Assert.Equal(SnapType.Grid, result.Type);
    }

    [Fact]
    public void HitTesting_DoesNotCreatePhantomSegmentsBetweenDisconnectedFigures()
    {
        var path=new VectorPathEntity{Figures=[
            new(){Start=new(0,0),Segments=[new LinePathSegment(new(10,0))]},
            new(){Start=new(100,100),Segments=[new LinePathSegment(new(110,100))]}
        ]};
        Assert.Same(path,EntityHitTester.HitTest([path],new(5,.1),.25));
        Assert.Null(EntityHitTester.HitTest([path],new(55,50),.25));
    }

    [Fact]
    public void MoveCommand_UndoRedoMovesAllEntitiesExactlyOnce()
    {
        var document = new CadDocument(); var a = new CircleEntity { Center = new(1,2), Radius = 1 }; var b = new RectangleEntity { Origin = new(5,6), Width = 2, Height = 3 }; document.Entities.AddRange([a,b]);
        var history = new CommandHistory(); history.Execute(new MoveEntitiesCommand(document, [a.Id,b.Id], new(10,-2)));
        Assert.Equal(new Point2D(11,0), a.Center); Assert.Equal(new Point2D(15,4), b.Origin);
        history.Undo(); Assert.Equal(new Point2D(1,2), a.Center); Assert.Equal(new Point2D(5,6), b.Origin);
        history.Redo(); Assert.Equal(new Point2D(11,0), a.Center);
    }

    [Fact]
    public void DrawingSession_CompletesLinePolylineCircleRectangleArcAndDrillPoint()
    {
        var session = new DrawingToolSession(); var layer = Guid.NewGuid();
        session.SetTool(CanvasTool.Line); Assert.Null(session.PointerPressed(new(0,0), layer, 1)); Assert.IsType<LineEntity>(session.PointerPressed(new(10,0), layer, 1));
        session.SetTool(CanvasTool.Polyline); session.PointerPressed(new(0,0), layer, 1); session.PointerPressed(new(5,0), layer, 1);session.PointerPressed(new(5,5),layer,1); Assert.IsType<PolylineEntity>(session.HandleKey(Key.C, layer));
        session.SetTool(CanvasTool.Circle); session.PointerPressed(new(0,0), layer, 1); Assert.IsType<CircleEntity>(session.PointerPressed(new(5,0), layer, 1));
        session.SetTool(CanvasTool.Rectangle); session.PointerPressed(new(0,0), layer, 1); Assert.IsType<RectangleEntity>(session.PointerReleased(new(5,5), layer));
        session.SetTool(CanvasTool.Arc); session.PointerPressed(new(-5,0), layer, 1); session.PointerPressed(new(0,5), layer, 1); Assert.IsType<ArcEntity>(session.PointerPressed(new(5,0), layer, 1));
        session.SetTool(CanvasTool.DrillPoint);var drill=Assert.IsType<DrillPointEntity>(session.PointerPressed(new(7,9),layer,1));Assert.Equal(new Point2D(7,9),drill.Position);Assert.Equal(layer,drill.LayerId);
    }

    [Fact]
    public void DrawingSessionRejectsCollapsedAndNonFiniteResultsAndEscapeCancelsWithoutOutput()
    {
        var session=new DrawingToolSession();var layer=Guid.NewGuid();
        session.SetTool(CanvasTool.Line);Assert.Null(session.PointerPressed(new(1,1),layer,1));Assert.Null(session.PointerPressed(new(1,1),layer,1));Assert.True(session.IsActive);Assert.Null(session.HandleKey(Key.Escape,layer));Assert.False(session.IsActive);
        session.SetTool(CanvasTool.Rectangle);session.PointerPressed(new(0,0),layer,1);Assert.Null(session.PointerReleased(new(0,10),layer));Assert.False(session.IsActive);
        session.SetTool(CanvasTool.Polyline);session.PointerPressed(new(0,0),layer,1);session.PointerPressed(new(10,0),layer,1);Assert.Null(session.HandleKey(Key.C,layer));Assert.True(session.IsActive);session.PointerPressed(new(10,10),layer,1);Assert.IsType<PolylineEntity>(session.HandleKey(Key.C,layer));
        session.SetTool(CanvasTool.DrillPoint);Assert.Null(session.PointerPressed(new(double.NaN,2),layer,1));
    }
}
