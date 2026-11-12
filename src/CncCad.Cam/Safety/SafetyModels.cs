using CncCad.Cam.Operations;
using CncCad.Cam.Toolpaths;
using CncCad.Core.Documents;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Safety;

public enum ControllerFamily { Uccnc, Mach3, Mach4, Grbl, LinuxCnc, Community }
public enum MachineToolChangeMode { SeparateFiles, ManualPause, Automatic }

public sealed class MachineProfileDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "CNC Router";
    public double MinimumXmm { get; set; } = -1200;
    public double MaximumXmm { get; set; } = 1200;
    public double MinimumYmm { get; set; } = -1200;
    public double MaximumYmm { get; set; } = 1200;
    public double MinimumZmm { get; set; } = -150;
    public double MaximumZmm { get; set; } = 50;
    public double RapidRateMmPerMin { get; set; } = 5000;
    public double ToolChangeSeconds { get; set; } = 30;
    public int MaximumSpindleRpm { get; set; } = 24000;
    public double DefaultClearanceMm { get; set; } = 5;
    public double HolderDiameterMm { get; set; } = 35;
    public double HolderLengthMm { get; set; } = 45;
    public double ToolChangeXmm { get; set; }
    public double ToolChangeYmm { get; set; }
    public double ParkXmm { get; set; }
    public double ParkYmm { get; set; }
    public bool ParkEnabled { get; set; }
    public double AllowedSpoilboardDepthMm { get; set; } = 0.5;
    public string PostProcessorId { get; set; } = "uccnc";
    public ControllerFamily ControllerFamily { get; set; } = ControllerFamily.Uccnc;
    public MeasurementUnit OutputUnit { get; set; } = MeasurementUnit.Millimetres;
    public bool SupportsArcs { get; set; } = true;
    public MachineToolChangeMode ToolChangeMode { get; set; } = MachineToolChangeMode.SeparateFiles;

    public MachineSnapshot Snapshot() => new()
    {
        SourceMachineId = Id, Name = Name, MinimumXmm = MinimumXmm, MaximumXmm = MaximumXmm,
        MinimumYmm = MinimumYmm, MaximumYmm = MaximumYmm, MinimumZmm = MinimumZmm, MaximumZmm = MaximumZmm,
        RapidRateMmPerMin = RapidRateMmPerMin, ToolChangeSeconds=ToolChangeSeconds, MaximumSpindleRpm = MaximumSpindleRpm,
        DefaultClearanceMm = DefaultClearanceMm, HolderDiameterMm = HolderDiameterMm, HolderLengthMm = HolderLengthMm,
        ToolChangeXmm = ToolChangeXmm, ToolChangeYmm = ToolChangeYmm, ParkXmm = ParkXmm, ParkYmm = ParkYmm, ParkEnabled=ParkEnabled,
        AllowedSpoilboardDepthMm = AllowedSpoilboardDepthMm, PostProcessorId = PostProcessorId,
        ControllerFamily=ControllerFamily,OutputUnit=OutputUnit,SupportsArcs=SupportsArcs,ToolChangeMode=ToolChangeMode
    };
}

public sealed record MachineSnapshot
{
    public Guid SourceMachineId { get; init; }
    public string Name { get; init; } = "";
    public double MinimumXmm { get; init; }
    public double MaximumXmm { get; init; }
    public double MinimumYmm { get; init; }
    public double MaximumYmm { get; init; }
    public double MinimumZmm { get; init; }
    public double MaximumZmm { get; init; }
    public double RapidRateMmPerMin { get; init; }
    public double ToolChangeSeconds { get; init; }
    public int MaximumSpindleRpm { get; init; }
    public double DefaultClearanceMm { get; init; }
    public double HolderDiameterMm { get; init; }
    public double HolderLengthMm { get; init; }
    public double ToolChangeXmm { get; init; }
    public double ToolChangeYmm { get; init; }
    public double ParkXmm { get; init; }
    public double ParkYmm { get; init; }
    public bool ParkEnabled { get; init; }
    public double AllowedSpoilboardDepthMm { get; init; }
    public string PostProcessorId { get; init; } = "uccnc";
    public ControllerFamily ControllerFamily { get; init; } = ControllerFamily.Uccnc;
    public MeasurementUnit OutputUnit { get; init; } = MeasurementUnit.Millimetres;
    public bool SupportsArcs { get; init; } = true;
    public MachineToolChangeMode ToolChangeMode { get; init; } = MachineToolChangeMode.SeparateFiles;
    public bool IsComplete => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(PostProcessorId)
        && Finite(MinimumXmm,MaximumXmm,MinimumYmm,MaximumYmm,MinimumZmm,MaximumZmm,RapidRateMmPerMin,ToolChangeSeconds,DefaultClearanceMm,HolderDiameterMm,HolderLengthMm,ToolChangeXmm,ToolChangeYmm,ParkXmm,ParkYmm,AllowedSpoilboardDepthMm)
        && MaximumXmm > MinimumXmm && MaximumYmm > MinimumYmm && MaximumZmm > MinimumZmm && RapidRateMmPerMin > 0 && ToolChangeSeconds>=0 && MaximumSpindleRpm > 0 && DefaultClearanceMm > 0 && HolderDiameterMm > 0 && HolderLengthMm > 0 && AllowedSpoilboardDepthMm >= 0;
    private static bool Finite(params double[] values)=>values.All(double.IsFinite);
}

public sealed record FixtureDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Clamp";
    public double MinXmm { get; set; }
    public double MinYmm { get; set; }
    public double MaxXmm { get; set; }
    public double MaxYmm { get; set; }
    public double TopZmm { get; set; }
    public double RequiredClearanceMm { get; set; } = 2;
}

public sealed record ToolAssemblySnapshot(double CutterDiameterMm, double CuttingLengthMm, double OverallLengthMm, double ShankDiameterMm, double StickOutMm, double HolderDiameterMm, double HolderLengthMm)
{
    public bool IsComplete => CutterDiameterMm > 0 && CuttingLengthMm > 0 && OverallLengthMm > 0 && ShankDiameterMm > 0 && StickOutMm > 0 && HolderDiameterMm > 0 && HolderLengthMm > 0;
}

public enum SafetySeverity { Warning, Error }
public sealed record SafetyFinding(SafetySeverity Severity, string Code, string Message, string Suggestion, Guid? OperationId = null, int? MoveIndex = null, Point2D? Position = null, double? Z = null);

public sealed record SafetyPreflightRequest(JobSettings Job, MachineSnapshot? Machine, IReadOnlyList<FixtureDefinition> Fixtures, IReadOnlyList<CamOperationDefinition> Operations, ToolpathProgram Program);

public sealed record SafetyPreflightResult(IReadOnlyList<SafetyFinding> Findings, double DeepestCutMm, double? MinimumFixtureClearanceMm, BoundingBox3D MachineExtents)
{
    public int ErrorCount => Findings.Count(x => x.Severity == SafetySeverity.Error);
    public int WarningCount => Findings.Count(x => x.Severity == SafetySeverity.Warning);
    public bool CanPreview => ErrorCount == 0;
    public bool CanExport => ErrorCount == 0;
    public string Status => ErrorCount > 0 ? "Errors" : WarningCount > 0 ? "Warnings" : "Safe";
    public static SafetyPreflightResult NotChecked { get; } = new([], 0, null, BoundingBox3D.Empty);
}

public readonly record struct BoundingBox3D(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    public static BoundingBox3D Empty => new(0, 0, 0, 0, 0, 0);
}

public interface ISafetyPreflightService
{
    SafetyPreflightResult Check(SafetyPreflightRequest request);
}
