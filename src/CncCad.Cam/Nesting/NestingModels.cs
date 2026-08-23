using CncCad.Core.Entities;
using CncCad.Core.Geometry;

namespace CncCad.Cam.Nesting;

public enum NestGrainDirection { AlongX,AlongY,None }
public enum NestingLayoutStatus { Stale,Current }
public sealed class NestPartDefinition
{
    public Guid Id { get; set; }=Guid.NewGuid();public string Name { get; set; }="Part";public Guid OuterGeometryId { get; set; }public List<Guid> InnerGeometryIds { get; set; }=[];public int Quantity { get; set; }=1;public NestGrainDirection Grain { get; set; }=NestGrainDirection.AlongX;public bool AllowQuarterTurn { get; set; }
}
public sealed class MaterialSheetDefinition
{
    public Guid Id { get; set; }=Guid.NewGuid();public string Name { get; set; }="Sheet";public double WidthMm { get; set; }=600;public double HeightMm { get; set; }=400;public double ThicknessMm { get; set; }=18;public int Quantity { get; set; }=100;
}
public sealed class NestedPartPlacement
{
    public Guid Id { get; set; }=Guid.NewGuid();public Guid PartId { get; set; }public Guid SheetId { get; set; }public int CopyIndex { get; set; }public double Xmm { get; set; }public double Ymm { get; set; }public int RotationDegrees { get; set; }public List<Guid> InstanceGeometryIds { get; set; }=[];
}
public sealed record NestingScore(int SheetCount,double WasteAreaMm2,double EstimatedRapidDistanceMm);
public sealed class NestingResult
{
    public List<MaterialSheetDefinition> Sheets { get; set; }=[];public List<NestedPartPlacement> Placements { get; set; }=[];public Dictionary<Guid,int> UnplacedQuantities { get; set; }=[];public List<string> Warnings { get; set; }=[];public NestingScore Score { get; set; }=new(0,0,0);public int Seed { get; set; }
    public bool Success=>Placements.Count>0&&UnplacedQuantities.Count==0;
}
public sealed class NestingJobDefinition
{
    public List<NestPartDefinition> Parts { get; set; }=[];public MaterialSheetDefinition SheetTemplate { get; set; }=new();public double PartSpacingMm { get; set; }=5;public double EdgeMarginMm { get; set; }=10;public int Seed { get; set; }=1729;public NestingLayoutStatus Status { get; set; }=NestingLayoutStatus.Stale;public NestingResult? AcceptedLayout { get; set; }
}
public sealed record NestingRequest(NestingJobDefinition Definition,IReadOnlyDictionary<Guid,CadEntity> Geometry);
public interface INestingOptimizer{NestingResult Optimize(NestingRequest request);}
