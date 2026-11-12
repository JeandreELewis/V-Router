namespace CncCad.PostProcessors.Models;

public sealed class MachineProfile
{
    public string Name { get; set; } = "UCCNC CNC Router";
    public string PostProcessor { get; set; } = "uccnc-mm";
    public double MaximumXmm { get; set; } = 1200;
    public double MaximumYmm { get; set; } = 1200;
    public double MaximumZmm { get; set; } = 150;
    public double SafeZMm { get; set; } = 5;
    public double WorkZeroDocumentXmm { get; set; }
    public double WorkZeroDocumentYmm { get; set; }
    public double WorkZeroZOffsetMm { get; set; }
    public int DefaultSpindleRpm { get; set; } = 18000;
    public int MaximumSpindleRpm { get; set; } = 24000;
    public double RapidRateMmPerMin { get; set; } = 5000;
    public double ParkXmm { get; set; }
    public double ParkYmm { get; set; }
}
