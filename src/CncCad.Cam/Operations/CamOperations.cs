using System.Text.Json.Serialization;
using CncCad.Cam.Profile;
using CncCad.Cam.Tools;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Safety;

namespace CncCad.Cam.Operations;

public enum OperationStatus { Stale, Current, Invalid, Disabled }
public enum TabPlacementMode { Automatic, Manual }
public enum TabStyle { Ramped }
public sealed record TabPlacement(Guid Id,double NormalizedDistance,Guid? GeometryId=null)
{
    public TabPlacement():this(Guid.NewGuid(),0,null){}
}
public sealed class ProfileTabSettings
{
    public bool Enabled { get; set; }
    public TabPlacementMode PlacementMode { get; set; } = TabPlacementMode.Automatic;
    public TabStyle Style { get; set; } = TabStyle.Ramped;
    public int AutomaticCount { get; set; } = 4;
    public double WidthMm { get; set; } = 8;
    public double RemainingHeightMm { get; set; } = 3;
    public double RampLengthMm { get; set; } = 4;
    public double ExclusionDistanceMm { get; set; } = 3;
    public List<TabPlacement> ManualPlacements { get; set; } = [];
    public ProfileTabSettings Copy()=>new(){Enabled=Enabled,PlacementMode=PlacementMode,Style=Style,AutomaticCount=AutomaticCount,WidthMm=WidthMm,RemainingHeightMm=RemainingHeightMm,RampLengthMm=RampLengthMm,ExclusionDistanceMm=ExclusionDistanceMm,ManualPlacements=[..ManualPlacements.Select(x=>x with{})]};
}

public sealed record ToolSnapshot
{
    public Guid SourceToolId { get; init; }
    public string Name { get; init; } = "Tool";
    public int? ToolNumber { get; init; }
    public ToolType Type { get; init; }
    public double DiameterMm { get; init; }
    public double FeedRateMmPerMin { get; init; }
    public double PlungeRateMmPerMin { get; init; }
    public int SpindleRpm { get; init; }
    public double StepDownMm { get; init; }
    public double? CuttingLengthMm { get; init; }
    public double? OverallLengthMm { get; init; }
    public double? ShankDiameterMm { get; init; }
    public double? StickOutMm { get; init; }
    public double? VBitAngleDegrees { get; init; }
    public double? TipDiameterMm { get; init; }
    public static ToolSnapshot FromTool(CncTool tool) => new() { SourceToolId = tool.Id, Name = tool.Name, ToolNumber = tool.ToolNumber, Type = tool.Type, DiameterMm = tool.DiameterMm, FeedRateMmPerMin = tool.FeedRateMmPerMin, PlungeRateMmPerMin = tool.PlungeRateMmPerMin, SpindleRpm = tool.SpindleRpm, StepDownMm = tool.StepDownMm, CuttingLengthMm = tool.CuttingLengthMm, OverallLengthMm = tool.OverallLengthMm, ShankDiameterMm = tool.ShankDiameterMm, StickOutMm = tool.StickOutMm,VBitAngleDegrees=tool.VBitAngleDegrees,TipDiameterMm=tool.TipDiameterMm };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ProfileOperationDefinition), "profile")]
[JsonDerivedType(typeof(EngraveOperationDefinition), "engrave")]
[JsonDerivedType(typeof(VCarveOperationDefinition), "vcarve")]
public abstract class CamOperationDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Operation";
    public List<Guid> GeometryIds { get; set; } = [];
    public ToolSnapshot Tool { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public int Order { get; set; }
    [JsonIgnore] public OperationStatus Status { get; set; } = OperationStatus.Stale;
    [JsonIgnore] public string? Error { get; set; }
    [JsonIgnore] public ToolpathOperation? CalculatedToolpath { get; set; }
    [JsonIgnore] public TimeSpan EstimatedTime { get; set; }
    [JsonIgnore] public string ToolSummary => $"{Tool.Name} · Ø{Tool.DiameterMm:0.###} mm";
    [JsonIgnore] public string TimeSummary => EstimatedTime == TimeSpan.Zero ? "—" : EstimatedTime.TotalMinutes < 1 ? $"{EstimatedTime.TotalSeconds:0}s" : $"{EstimatedTime.TotalMinutes:0.0} min";
    [JsonIgnore] public abstract string DetailSummary { get; }
}

public sealed class ProfileOperationDefinition : CamOperationDefinition
{
    [JsonIgnore] public override string DetailSummary => $"{Side} · {GeometryIds.Count} contour{(GeometryIds.Count == 1 ? "" : "s")} · {CutDepthMm:0.###} mm · {PassCount} passes";
    [JsonIgnore] public int PassCount => PassDepthMm > 0 && CutDepthMm > StartDepthMm ? (int)Math.Ceiling((CutDepthMm - StartDepthMm) / PassDepthMm) : 0;
    public ProfileSide Side { get; set; } = ProfileSide.Outside;
    public CutDirection Direction { get; set; } = CutDirection.Climb;
    public double StartDepthMm { get; set; }
    public double CutDepthMm { get; set; } = 18;
    public double PassDepthMm { get; set; } = 3;
    public double SafeZMm { get; set; } = 5;
    public double FeedRateMmPerMin { get; set; } = 1800;
    public double PlungeRateMmPerMin { get; set; } = 600;
    public int SpindleRpm { get; set; } = 18000;
    public Guid? MaterialSheetId { get; set; }
    public ProfileTabSettings Tabs { get; set; } = new();
    public ProfileOperationDefinition Duplicate() => new() { Name = $"{Name} Copy", GeometryIds = [.. GeometryIds], Tool = Tool with { }, IsEnabled = IsEnabled, Order = Order + 1, Side = Side, Direction = Direction, StartDepthMm = StartDepthMm, CutDepthMm = CutDepthMm, PassDepthMm = PassDepthMm, SafeZMm = SafeZMm, FeedRateMmPerMin = FeedRateMmPerMin, PlungeRateMmPerMin = PlungeRateMmPerMin, SpindleRpm = SpindleRpm,MaterialSheetId=MaterialSheetId,Tabs=Tabs.Copy() };
}
public enum EngraveDirection { Forward, Reverse, Optimize }
public sealed class EngraveOperationDefinition:CamOperationDefinition
{
    [JsonIgnore]public override string DetailSummary=>$"Engrave · {GeometryIds.Count} vector{(GeometryIds.Count==1?"":"s")} · {CutDepthMm:0.###} mm · {PassCount} passes";
    [JsonIgnore]public int PassCount=>PassDepthMm>0&&CutDepthMm>StartDepthMm?(int)Math.Ceiling((CutDepthMm-StartDepthMm)/PassDepthMm):0;
    public EngraveDirection Direction{get;set;}=EngraveDirection.Optimize;public double StartDepthMm{get;set;}public double CutDepthMm{get;set;}=1;public double PassDepthMm{get;set;}=1;public double SafeZMm{get;set;}=5;public double FeedRateMmPerMin{get;set;}=1200;public double PlungeRateMmPerMin{get;set;}=400;public int SpindleRpm{get;set;}=18000;public Guid? MaterialSheetId{get;set;}
    public EngraveOperationDefinition Duplicate()=>new(){Name=$"{Name} Copy",GeometryIds=[..GeometryIds],Tool=Tool with{},IsEnabled=IsEnabled,Order=Order+1,Direction=Direction,StartDepthMm=StartDepthMm,CutDepthMm=CutDepthMm,PassDepthMm=PassDepthMm,SafeZMm=SafeZMm,FeedRateMmPerMin=FeedRateMmPerMin,PlungeRateMmPerMin=PlungeRateMmPerMin,SpindleRpm=SpindleRpm,MaterialSheetId=MaterialSheetId};
}
public sealed class VCarveOperationDefinition:CamOperationDefinition
{
    [JsonIgnore]public override string DetailSummary=>$"V-Carve · {GeometryIds.Count} contour{(GeometryIds.Count==1?"":"s")} · max {MaximumDepthMm:0.###} mm";
    public double MaximumDepthMm{get;set;}=3;public bool ClearFlatRegions{get;set;}=true;public double OffsetStepOverMm{get;set;}=.5;public double SafeZMm{get;set;}=5;public double FeedRateMmPerMin{get;set;}=1000;public double PlungeRateMmPerMin{get;set;}=300;public int SpindleRpm{get;set;}=18000;public Guid? MaterialSheetId{get;set;}
    [JsonIgnore]public double WidthAtMaximumDepthMm=>(Tool.TipDiameterMm??0)+2*MaximumDepthMm*Math.Tan((Tool.VBitAngleDegrees??0)*Math.PI/360);
    public VCarveOperationDefinition Duplicate()=>new(){Name=$"{Name} Copy",GeometryIds=[..GeometryIds],Tool=Tool with{},IsEnabled=IsEnabled,Order=Order+1,MaximumDepthMm=MaximumDepthMm,ClearFlatRegions=ClearFlatRegions,OffsetStepOverMm=OffsetStepOverMm,SafeZMm=SafeZMm,FeedRateMmPerMin=FeedRateMmPerMin,PlungeRateMmPerMin=PlungeRateMmPerMin,SpindleRpm=SpindleRpm,MaterialSheetId=MaterialSheetId};
}

public sealed class CamJob
{
    public List<CamOperationDefinition> Operations { get; set; } = [];
    public MachineSnapshot? Machine { get; set; }
    public List<FixtureDefinition> Fixtures { get; set; } = [];
    public bool SafetySetupIncomplete { get; set; }
    public void NormalizeOrder() { for (var i = 0; i < Operations.Count; i++) Operations[i].Order = i; }
    public void MarkGeometryChanged(IEnumerable<Guid> ids, bool deleted = false)
    {
        var affected = ids.ToHashSet();
        foreach (var operation in Operations.Where(o => o.GeometryIds.Any(affected.Contains))) { operation.Status = deleted ? OperationStatus.Invalid : OperationStatus.Stale; operation.Error = deleted ? "Referenced geometry was deleted." : null; operation.CalculatedToolpath = null; }
    }
}

public sealed record OperationValidationError(string Code, string Message);
public sealed record OperationCalculationResult(ToolpathOperation? Toolpath, IReadOnlyList<OperationValidationError> Errors)
{
    public bool Success => Toolpath is not null && Errors.Count == 0;
    public static OperationCalculationResult Failed(params OperationValidationError[] errors) => new(null, errors);
}

public interface ICamOperationCalculator
{
    OperationCalculationResult Calculate(CamOperationDefinition operation, IReadOnlyDictionary<Guid, CncCad.Core.Entities.CadEntity> geometry);
}
