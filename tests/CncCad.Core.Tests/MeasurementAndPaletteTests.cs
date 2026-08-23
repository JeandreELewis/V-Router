using Avalonia;
using Avalonia.Input;
using CncCad.App.ViewModels;
using CncCad.Core.Geometry;
using CncCad.Rendering.Canvas;

namespace CncCad.Core.Tests;

public class MeasurementAndPaletteTests
{
    [Fact]
    public void LineMeasurement_ReportsLengthAngleAndDeltas()
    {
        var session = Started(CanvasTool.Line, new(0, 0)); session.PointerMoved(new(3, 4));
        AssertValue(session.Measurement, "L", 5); AssertValue(session.Measurement, "∠", 53.130102); AssertValue(session.Measurement, "ΔX", 3); AssertValue(session.Measurement, "ΔY", 4);
    }

    [Fact]
    public void PolylineMeasurement_ReportsSegmentAndAccumulatedLength()
    {
        var session = Started(CanvasTool.Polyline, new(0, 0)); session.PointerPressed(new(3, 0), Guid.Empty, 1); session.PointerMoved(new(3, 4));
        AssertValue(session.Measurement, "Segment", 4); AssertValue(session.Measurement, "Total", 7);
    }

    [Fact]
    public void RectangleCircleAndArc_ReportExpectedDimensions()
    {
        var rectangle = Started(CanvasTool.Rectangle, new(1, 2)); rectangle.PointerMoved(new(6, 9)); AssertValue(rectangle.Measurement, "W", 5); AssertValue(rectangle.Measurement, "H", 7);
        var circle = Started(CanvasTool.Circle, new(0, 0)); circle.PointerMoved(new(3, 4)); AssertValue(circle.Measurement, "R", 5); AssertValue(circle.Measurement, "Ø", 10);
        var arc = Started(CanvasTool.Arc, new(-5, 0)); arc.PointerPressed(new(0, 5), Guid.Empty, 1); arc.PointerMoved(new(5, 0)); AssertValue(arc.Measurement, "R", 5); AssertValue(arc.Measurement, "Sweep", 180);
    }

    [Fact]
    public void EscapeCancellation_HidesMeasurement()
    {
        var session = Started(CanvasTool.Line, new(0, 0)); session.PointerMoved(new(5, 0)); Assert.True(session.Measurement.IsVisible);
        session.HandleKey(Key.Escape, Guid.Empty); Assert.False(session.Measurement.IsVisible);
    }

    [Fact]
    public void HudLayout_StaysInsideEveryCanvasEdge()
    {
        var canvas = new Size(800, 600); var hud = new Size(220, 90);
        foreach (var cursor in new[] { new Point(0,0), new Point(799,0), new Point(0,599), new Point(799,599), new Point(400,300) })
        {
            var rect = HudLayout.Place(canvas, cursor, hud);
            Assert.True(rect.Left >= 8 && rect.Top >= 8 && rect.Right <= 792 && rect.Bottom <= 592);
        }
    }

    [Fact]
    public void DefaultPalette_ContainsEveryToolExactlyOnce()
    {
        var tools = ToolPaletteItem.DefaultDrawingTools;
        Assert.Equal(7, tools.Count); Assert.Equal(7, tools.Select(item => item.Tool).Distinct().Count()); Assert.All(tools, item => { Assert.False(string.IsNullOrWhiteSpace(item.IconData)); Assert.False(string.IsNullOrWhiteSpace(item.Tooltip)); });
        Assert.Equal(Enum.GetValues<CanvasTool>().Order(), tools.Select(item => item.Tool).Order());
    }

    private static DrawingToolSession Started(CanvasTool tool, Point2D start) { var session = new DrawingToolSession(); session.SetTool(tool); session.PointerPressed(start, Guid.Empty, 1); return session; }
    private static void AssertValue(DrawingMeasurement measurement, string label, double expected) => Assert.Equal(expected, Assert.Single(measurement.Values, value => value.Label == label).Value, 5);
}
