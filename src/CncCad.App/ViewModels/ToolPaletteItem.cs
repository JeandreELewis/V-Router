using CncCad.Rendering.Canvas;

namespace CncCad.App.ViewModels;

public sealed record ToolPaletteItem(CanvasTool Tool, string Label, string IconData, string Tooltip)
{
    public static ToolPaletteItem Create(CanvasTool tool, string label, string path, string tooltip) => new(tool, label, path, tooltip);
    public static IReadOnlyList<ToolPaletteItem> DefaultDrawingTools { get; } =
    [
        Create(CanvasTool.Select, "Select", "M3,3 L17,10 10,12 7,19 Z", "Select, marquee, and move geometry"),
        Create(CanvasTool.Line, "Line", "M3,17 L17,3", "Draw a line between two points"),
        Create(CanvasTool.Polyline, "Polyline", "M2,16 L7,6 12,13 18,3", "Draw a connected polyline"),
        Create(CanvasTool.Rectangle, "Rectangle", "M3,4 L17,4 17,16 3,16 Z", "Draw a rectangle corner to corner"),
        Create(CanvasTool.Circle, "Circle", "M10,2 A8,8 0 1 1 9.999,2", "Draw a circle from center and radius"),
        Create(CanvasTool.Arc, "3-Point Arc", "M2,15 C5,4 15,4 18,15", "Draw an arc through three points")
    ];
}
