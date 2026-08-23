using CncCad.Core.Documents;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncCad.App.ViewModels;

public sealed record OriginChoice(XyOrigin Origin,string Label,string Marker);

public partial class JobSetupViewModel : ViewModelBase
{
    [ObservableProperty] private string jobName = "Untitled job";
    [ObservableProperty] private double widthMm = 600;
    [ObservableProperty] private double heightMm = 400;
    [ObservableProperty] private double thicknessMm = 18;
    [ObservableProperty] private double safeZMm = 5;
    [ObservableProperty] private XyOrigin origin = XyOrigin.BottomLeft;
    [ObservableProperty] private OriginChoice? selectedOriginChoice;
    [ObservableProperty] private double originOffsetXmm;
    [ObservableProperty] private double originOffsetYmm;
    [ObservableProperty] private ZZeroPosition zZero = ZZeroPosition.MaterialSurface;
    [ObservableProperty] private string? validationMessage;
    public IReadOnlyList<OriginChoice> OriginChoices { get; } =
    [
        new(XyOrigin.TopLeft,"Top left","● · ·"),new(XyOrigin.TopCentre,"Top centre","· ● ·"),new(XyOrigin.TopRight,"Top right","· · ●"),
        new(XyOrigin.CentreLeft,"Centre left","● · ·"),new(XyOrigin.Centre,"Centre","· ● ·"),new(XyOrigin.CentreRight,"Centre right","· · ●"),
        new(XyOrigin.BottomLeft,"Bottom left","● · ·"),new(XyOrigin.BottomCentre,"Bottom centre","· ● ·"),new(XyOrigin.BottomRight,"Bottom right","· · ●")
    ];
    public IReadOnlyList<ZZeroPosition> ZZeroPositions { get; } = Enum.GetValues<ZZeroPosition>();

    public JobSetupViewModel(){SelectedOriginChoice=OriginChoices.First(x=>x.Origin==Origin);}
    partial void OnSelectedOriginChoiceChanged(OriginChoice? value){if(value is not null)Origin=value.Origin;}
    partial void OnOriginChanged(XyOrigin value){SelectedOriginChoice=OriginChoices.FirstOrDefault(x=>x.Origin==value)??OriginChoices[6];}
    public JobSettings? Build()
    {
        if (string.IsNullOrWhiteSpace(JobName)) { ValidationMessage = "Enter a job name."; return null; }
        if (WidthMm <= 0 || HeightMm <= 0 || ThicknessMm <= 0) { ValidationMessage = "Width, height and material thickness must be greater than zero."; return null; }
        if (SafeZMm <= 0) { ValidationMessage = "Safe Z must be greater than zero."; return null; }
        ValidationMessage = null;
        return new JobSettings { Name = JobName.Trim(), WidthMm = WidthMm, HeightMm = HeightMm, ThicknessMm = ThicknessMm, SafeZMm = SafeZMm, Origin = Origin, OriginOffsetXmm=OriginOffsetXmm,OriginOffsetYmm=OriginOffsetYmm,ZZero = ZZero };
    }
}
