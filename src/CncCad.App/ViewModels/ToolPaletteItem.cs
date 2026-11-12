using CncCad.Rendering.Canvas;

namespace CncCad.App.ViewModels;

public sealed record ToolPaletteItem(CanvasTool Tool, string Label, string IconData, string Tooltip, string HelpKey)
{
    public static ToolPaletteItem Create(CanvasTool tool, string label, string path, string tooltip, string helpKey) => new(tool, label, path, tooltip, helpKey);
    public static IReadOnlyList<ToolPaletteItem> DefaultDrawingTools { get; } =
    [
        Create(CanvasTool.Select, "Select", "M3,3 L17,10 10,12 7,19 Z", "Select, marquee, and move geometry", "draw.select"),
        Create(CanvasTool.Line, "Line", "M3,17 L17,3", "Draw a line between two points", "draw.line"),
        Create(CanvasTool.Polyline, "Polyline", "M2,16 L7,6 12,13 18,3", "Draw a connected polyline", "draw.polyline"),
        Create(CanvasTool.Rectangle, "Rectangle", "M3,4 L17,4 17,16 3,16 Z", "Draw a rectangle corner to corner", "draw.rectangle"),
        Create(CanvasTool.Circle, "Circle", "M10,2 A8,8 0 1 1 9.999,2", "Draw a circle from center and radius", "draw.circle"),
        Create(CanvasTool.Arc, "3-Point Arc", "M2,15 C5,4 15,4 18,15", "Draw an arc through three points", "draw.arc"),
        Create(CanvasTool.DrillPoint,"Drill Point","M10,2 L10,18 M2,10 L18,10 M5,10 A5,5 0 1 0 15,10 A5,5 0 1 0 5,10","Place an explicit drilling point", "draw.drill"),
        Create(CanvasTool.Trim, "Trim", "M3,5 A3,3 0 1 0 9,5 M3,15 A3,3 0 1 0 9,15 M8,7 L19,17 M8,13 L19,3", "Scissors: click a crossing-bounded vector portion to remove it", "edit.trim")
    ];
}
