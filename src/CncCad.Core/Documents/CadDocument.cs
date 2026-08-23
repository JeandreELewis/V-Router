using CncCad.Core.Entities;

namespace CncCad.Core.Documents;

public enum MeasurementUnit { Millimetres, Inches }
public enum XyOrigin { BottomLeft=0,BottomRight=1,TopLeft=2,TopRight=3,Centre=4,Custom=5,BottomCentre=6,CentreLeft=7,CentreRight=8,TopCentre=9 }
public enum ZZeroPosition { MaterialSurface, MachineBed }

public sealed class JobSettings
{
    public string Name { get; set; } = "Untitled job";
    public double WidthMm { get; set; } = 600;
    public double HeightMm { get; set; } = 400;
    public double ThicknessMm { get; set; } = 18;
    public MeasurementUnit DisplayUnit { get; set; } = MeasurementUnit.Millimetres;
    public XyOrigin Origin { get; set; } = XyOrigin.BottomLeft;
    public double OriginOffsetXmm { get; set; }
    public double OriginOffsetYmm { get; set; }
    public ZZeroPosition ZZero { get; set; } = ZZeroPosition.MaterialSurface;
    public double SafeZMm { get; set; } = 5;
}

public sealed class CadLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer 1";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public string Color { get; set; } = "#61C8FF";
    public int Order { get; set; }
}

public sealed class CadDocument
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    public JobSettings Job { get; set; } = new();
    public List<CadLayer> Layers { get; set; } = [new()];
    public List<CadEntity> Entities { get; set; } = [];
    private Guid _activeLayerId;
    public Guid ActiveLayerId { get=>_activeLayerId!=Guid.Empty&&Layers.Any(x=>x.Id==_activeLayerId)?_activeLayerId:Layers[0].Id; set=>_activeLayerId=value; }
    public CadLayer ActiveLayer => Layers.First(x=>x.Id==ActiveLayerId);
}
