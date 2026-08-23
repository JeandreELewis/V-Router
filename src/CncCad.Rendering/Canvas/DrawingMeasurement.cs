using System.Globalization;

namespace CncCad.Rendering.Canvas;

public enum DrawingPhase { Idle, DefiningStart, DefiningGeometry, Completing }
public readonly record struct MeasurementValue(string Label, double Value, string Unit = "mm")
{
    public string Display => $"{Label} {Value.ToString("0.###", CultureInfo.InvariantCulture)}{(string.IsNullOrEmpty(Unit) ? string.Empty : $" {Unit}")}";
}
public sealed record DrawingMeasurement(string ToolName, DrawingPhase Phase, IReadOnlyList<MeasurementValue> Values, string Guidance)
{
    public static DrawingMeasurement Idle { get; } = new(string.Empty, DrawingPhase.Idle, [], string.Empty);
    public bool IsVisible => Phase != DrawingPhase.Idle && Values.Count > 0;
    public string Summary => string.Join("   ", Values.Select(value => value.Display));
    public static DrawingMeasurement Move(double x, double y) => new("Move", DrawingPhase.DefiningGeometry, [new("ΔX", x), new("ΔY", y)], "Release to place");
}
