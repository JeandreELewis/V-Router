using CncCad.Core.Geometry;

namespace CncCad.Cam.Toolpaths;

public abstract record ToolpathMove;
public sealed record RapidMove(Point2D End, double Z) : ToolpathMove;
public sealed record LinearMove(Point2D End, double Z, double FeedRate) : ToolpathMove;
public sealed record ArcMove(Point2D End, Point2D Center, double Z, double FeedRate, bool Clockwise) : ToolpathMove;
public sealed record ToolChange(int? ToolNumber, string ToolName) : ToolpathMove;
public sealed record SpindleStart(int Rpm) : ToolpathMove;
public sealed record SpindleStop : ToolpathMove;

public sealed class ToolpathOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceOperationId { get; set; }
    public string Name { get; set; } = "Operation";
    public Guid ToolId { get; set; }
    public Guid? MaterialSheetId { get; set; }
    public List<Guid> SourceGeometryIds { get; set; }=[];
    public List<ToolpathMove> Moves { get; set; } = [];
}

public sealed class ToolpathProgram
{
    public string Name { get; set; } = "CNC Program";
    public List<ToolpathOperation> Operations { get; set; } = [];
}
