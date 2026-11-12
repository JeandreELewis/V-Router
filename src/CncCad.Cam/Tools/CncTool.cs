using CncCad.Cam.Operations;

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
        if (!double.IsFinite(DiameterMm)||DiameterMm <= 0) errors.Add("Diameter must be finite and greater than zero.");
        if (!double.IsFinite(FeedRateMmPerMin)||FeedRateMmPerMin <= 0) errors.Add("Feed rate must be finite and greater than zero.");
        if (!double.IsFinite(PlungeRateMmPerMin)||PlungeRateMmPerMin <= 0) errors.Add("Plunge rate must be finite and greater than zero.");
        if (SpindleRpm < 0) errors.Add("RPM cannot be negative.");
        if (!double.IsFinite(StepDownMm)||StepDownMm <= 0) errors.Add("Step down must be finite and greater than zero.");
        if (!double.IsFinite(StepOverPercent)||StepOverPercent is <= 0 or > 100) errors.Add("Step over must be finite and between 0 and 100 percent.");
        if (CuttingLengthMm is{} cutting&&(!double.IsFinite(cutting)||cutting<=0)) errors.Add("Cutting length must be finite and greater than zero when supplied.");
        if (OverallLengthMm is{} overall&&(!double.IsFinite(overall)||overall<=0)) errors.Add("Overall length must be finite and greater than zero when supplied.");
        if (ShankDiameterMm is{} shank&&(!double.IsFinite(shank)||shank<=0)) errors.Add("Shank diameter must be finite and greater than zero when supplied.");
        if (StickOutMm is{} stickOut&&(!double.IsFinite(stickOut)||stickOut<=0)) errors.Add("Stick-out must be finite and greater than zero when supplied.");
        if (VBitAngleDegrees is{} angle&&(!double.IsFinite(angle)||angle<=0||angle>=180)) errors.Add("V-bit angle must be finite and between 0 and 180 degrees when supplied.");
        if (TipDiameterMm is{} tip&&(!double.IsFinite(tip)||tip<0)) errors.Add("Tip diameter must be finite and non-negative when supplied.");
        return errors;
    }
}

public sealed class MaterialDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New material";
    public string Category { get; set; } = "General";
    public string? Notes { get; set; }
}

public sealed class ToolCuttingPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ToolId { get; set; }
    public Guid MachineId { get; set; }
    public Guid MaterialId { get; set; }
    public double FeedRateMmPerMin { get; set; }
    public double PlungeRateMmPerMin { get; set; }
    public int SpindleRpm { get; set; }
    public double PassDepthMm { get; set; }
    public double StepOverPercent { get; set; }
    public ToolSnapshot Apply(CncTool tool) => ToolSnapshot.FromTool(tool) with { FeedRateMmPerMin=FeedRateMmPerMin,PlungeRateMmPerMin=PlungeRateMmPerMin,SpindleRpm=SpindleRpm,StepDownMm=PassDepthMm };
    public IReadOnlyList<string> Validate() =>
        !double.IsFinite(FeedRateMmPerMin)||!double.IsFinite(PlungeRateMmPerMin)||!double.IsFinite(PassDepthMm)||!double.IsFinite(StepOverPercent)||FeedRateMmPerMin<=0||PlungeRateMmPerMin<=0||SpindleRpm<=0||PassDepthMm<=0||StepOverPercent is <=0 or >100
            ? ["Preset feed, plunge, RPM, pass depth, and stepover must be valid positive values."] : [];
}
