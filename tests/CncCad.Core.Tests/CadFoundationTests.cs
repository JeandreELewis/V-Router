using Avalonia.Input;
using CncCad.Core.Commands;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Snapping;
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
    public void MoveCommand_UndoRedoMovesAllEntitiesExactlyOnce()
    {
        var document = new CadDocument(); var a = new CircleEntity { Center = new(1,2), Radius = 1 }; var b = new RectangleEntity { Origin = new(5,6), Width = 2, Height = 3 }; document.Entities.AddRange([a,b]);
        var history = new CommandHistory(); history.Execute(new MoveEntitiesCommand(document, [a.Id,b.Id], new(10,-2)));
        Assert.Equal(new Point2D(11,0), a.Center); Assert.Equal(new Point2D(15,4), b.Origin);
        history.Undo(); Assert.Equal(new Point2D(1,2), a.Center); Assert.Equal(new Point2D(5,6), b.Origin);
        history.Redo(); Assert.Equal(new Point2D(11,0), a.Center);
    }

    [Fact]
    public void DrawingSession_CompletesLinePolylineCircleRectangleAndArc()
    {
        var session = new DrawingToolSession(); var layer = Guid.NewGuid();
        session.SetTool(CanvasTool.Line); Assert.Null(session.PointerPressed(new(0,0), layer, 1)); Assert.IsType<LineEntity>(session.PointerPressed(new(10,0), layer, 1));
        session.SetTool(CanvasTool.Polyline); session.PointerPressed(new(0,0), layer, 1); session.PointerPressed(new(5,0), layer, 1); Assert.IsType<PolylineEntity>(session.HandleKey(Key.C, layer));
        session.SetTool(CanvasTool.Circle); session.PointerPressed(new(0,0), layer, 1); Assert.IsType<CircleEntity>(session.PointerPressed(new(5,0), layer, 1));
        session.SetTool(CanvasTool.Rectangle); session.PointerPressed(new(0,0), layer, 1); Assert.IsType<RectangleEntity>(session.PointerReleased(new(5,5), layer));
        session.SetTool(CanvasTool.Arc); session.PointerPressed(new(-5,0), layer, 1); session.PointerPressed(new(0,5), layer, 1); Assert.IsType<ArcEntity>(session.PointerPressed(new(5,0), layer, 1));
    }
}
