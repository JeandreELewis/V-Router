namespace CncCad.Cam.Tools;

public enum ToolType { EndMill, BallNose, VBit, EngravingBit, Drill, SurfacingBit, TaperedBallNose, CompressionBit, UpcutBit, DowncutBit, Other }

public sealed class CncTool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New tool";
    public int? ToolNumber { get; set; }
    public ToolType Type { get; set; } = ToolType.EndMill;
    public double DiameterMm { get; set; } = 6;
    public double? CuttingLengthMm { get; set; }
    public double? OverallLengthMm { get; set; }
    public double? ShankDiameterMm { get; set; }
    public double? StickOutMm { get; set; }
    public int? FluteCount { get; set; }
    public double FeedRateMmPerMin { get; set; } = 1800;
    public double PlungeRateMmPerMin { get; set; } = 600;
    public int SpindleRpm { get; set; } = 18000;
    public double StepDownMm { get; set; } = 3;
    public double StepOverPercent { get; set; } = 40;
    public double? VBitAngleDegrees { get; set; }
    public double? TipDiameterMm { get; set; }
    public string? Manufacturer { get; set; }
    public string? ManufacturerPartNumber { get; set; }
    public string? Supplier { get; set; }
    public string? ProductUrl { get; set; }
    public string? Notes { get; set; }
    public string? ImageRelativePath { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Name is required.");
        if (DiameterMm <= 0) errors.Add("Diameter must be greater than zero.");
        if (FeedRateMmPerMin <= 0) errors.Add("Feed rate must be greater than zero.");
        if (PlungeRateMmPerMin <= 0) errors.Add("Plunge rate must be greater than zero.");
        if (SpindleRpm < 0) errors.Add("RPM cannot be negative.");
        if (StepDownMm <= 0) errors.Add("Step down must be greater than zero.");
        if (StepOverPercent is <= 0 or > 100) errors.Add("Step over must be between 0 and 100 percent.");
        if (CuttingLengthMm is <= 0) errors.Add("Cutting length must be greater than zero when supplied.");
        if (OverallLengthMm is <= 0) errors.Add("Overall length must be greater than zero when supplied.");
        if (ShankDiameterMm is <= 0) errors.Add("Shank diameter must be greater than zero when supplied.");
        if (StickOutMm is <= 0) errors.Add("Stick-out must be greater than zero when supplied.");
        return errors;
    }
}
