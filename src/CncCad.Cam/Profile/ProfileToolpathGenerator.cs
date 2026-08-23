using CncCad.Cam.Tools;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Profile;

public enum ProfileSide { Outside, Inside, OnVector }
public enum CutDirection { Climb, Conventional }

public sealed record ProfileParameters(double StartDepthMm, double CutDepthMm, double PassDepthMm, double SafeZMm,
    double FeedRateMmPerMin, double PlungeRateMmPerMin, int SpindleRpm, ProfileSide Side = ProfileSide.Outside,
    CutDirection Direction = CutDirection.Climb);

public sealed class ProfileToolpathGenerator
{
    public ToolpathOperation Generate(RectangleEntity rectangle, CncTool tool, ProfileParameters parameters)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0) throw new ArgumentOutOfRangeException(nameof(rectangle));
        if (tool.DiameterMm <= 0) throw new ArgumentOutOfRangeException(nameof(tool));
        if (parameters.CutDepthMm <= parameters.StartDepthMm || parameters.PassDepthMm <= 0) throw new ArgumentOutOfRangeException(nameof(parameters));

        var offset = parameters.Side switch { ProfileSide.Outside => tool.DiameterMm / 2, ProfileSide.Inside => -tool.DiameterMm / 2, _ => 0 };
        var minX = rectangle.Origin.X - offset;
        var minY = rectangle.Origin.Y - offset;
        var maxX = rectangle.Origin.X + rectangle.Width + offset;
        var maxY = rectangle.Origin.Y + rectangle.Height + offset;
        if (minX >= maxX || minY >= maxY) throw new InvalidOperationException("Tool is too large for the inside profile.");
        var corners = new[] { new Point2D(minX, minY), new Point2D(maxX, minY), new Point2D(maxX, maxY), new Point2D(minX, maxY) };
        if (parameters.Direction == CutDirection.Conventional) Array.Reverse(corners);

        var operation = new ToolpathOperation { Name = $"Profile - {rectangle.Width:0.###} x {rectangle.Height:0.###} mm", ToolId = tool.Id };
        operation.Moves.Add(new ToolChange(tool.ToolNumber, tool.Name));
        operation.Moves.Add(new SpindleStart(parameters.SpindleRpm));
        operation.Moves.Add(new RapidMove(corners[0], parameters.SafeZMm));
        foreach (var depth in CalculatePassDepths(parameters.StartDepthMm, parameters.CutDepthMm, parameters.PassDepthMm))
        {
            operation.Moves.Add(new LinearMove(corners[0], -depth, parameters.PlungeRateMmPerMin));
            foreach (var corner in corners.Skip(1)) operation.Moves.Add(new LinearMove(corner, -depth, parameters.FeedRateMmPerMin));
            operation.Moves.Add(new LinearMove(corners[0], -depth, parameters.FeedRateMmPerMin));
        }
        operation.Moves.Add(new RapidMove(corners[0], parameters.SafeZMm));
        operation.Moves.Add(new SpindleStop());
        return operation;
    }

    public static IReadOnlyList<double> CalculatePassDepths(double startDepth, double cutDepth, double passDepth)
    {
        var result = new List<double>();
        for (var depth = startDepth + passDepth; depth < cutDepth; depth += passDepth) result.Add(depth);
        result.Add(cutDepth);
        return result;
    }
}
