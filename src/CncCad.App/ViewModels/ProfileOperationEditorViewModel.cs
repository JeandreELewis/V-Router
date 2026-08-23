using CncCad.Cam.Operations;
using CncCad.Cam.Profile;
using CncCad.Cam.Tools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncCad.App.ViewModels;

public partial class ProfileOperationEditorViewModel : ViewModelBase
{
    public ProfileOperationDefinition Draft { get; }
    public IReadOnlyList<CncTool> Tools { get; }
    public IReadOnlyList<ProfileSide> Sides { get; } = Enum.GetValues<ProfileSide>();
    public IReadOnlyList<CutDirection> Directions { get; } = Enum.GetValues<CutDirection>();
    public IReadOnlyList<TabPlacementMode> TabPlacementModes { get; }=Enum.GetValues<TabPlacementMode>();
    public int PassCount => Draft.PassDepthMm > 0 && Draft.CutDepthMm > Draft.StartDepthMm ? ProfileToolpathGenerator.CalculatePassDepths(Draft.StartDepthMm, Draft.CutDepthMm, Draft.PassDepthMm).Count : 0;
    [ObservableProperty] private CncTool? selectedLibraryTool;
    [ObservableProperty] private string? validationMessage;
    [ObservableProperty] private string? warningMessage;

    public ProfileOperationEditorViewModel(ProfileOperationDefinition source, IReadOnlyList<CncTool> tools, double materialThickness)
    {
        Draft = Copy(source); Tools = tools; SelectedLibraryTool = tools.FirstOrDefault(t => t.Id == source.Tool.SourceToolId); WarningMessage = source.CutDepthMm > materialThickness ? "Cut depth exceeds material thickness; ensure spoilboard clearance." : null;
    }
    public void ApplyTool(CncTool tool) { Draft.Tool = ToolSnapshot.FromTool(tool); Draft.FeedRateMmPerMin = tool.FeedRateMmPerMin; Draft.PlungeRateMmPerMin = tool.PlungeRateMmPerMin; Draft.SpindleRpm = tool.SpindleRpm; Draft.PassDepthMm = tool.StepDownMm; OnPropertyChanged(nameof(Draft)); OnPropertyChanged(nameof(PassCount)); }
    public void RefreshDerived(double materialThickness) { WarningMessage = Draft.CutDepthMm > materialThickness ? "Cut depth exceeds material thickness; ensure spoilboard clearance." : null; OnPropertyChanged(nameof(PassCount)); }
    public bool Validate(double materialThickness)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name)) ValidationMessage = "Operation name is required.";
        else if (Draft.Tool.DiameterMm <= 0) ValidationMessage = "Select a valid tool.";
        else if (Draft.CutDepthMm <= Draft.StartDepthMm || Draft.PassDepthMm <= 0) ValidationMessage = "Final depth must exceed start depth and pass depth must be positive.";
        else if (Draft.FeedRateMmPerMin <= 0 || Draft.PlungeRateMmPerMin <= 0 || Draft.SafeZMm <= 0 || Draft.SpindleRpm < 0) ValidationMessage = "Feed, plunge, safe Z and spindle values must be valid.";
        else if(Draft.Tabs.Enabled&&(Draft.Tabs.AutomaticCount<1||Draft.Tabs.WidthMm<=0||Draft.Tabs.RemainingHeightMm<=0||Draft.Tabs.RemainingHeightMm>=Draft.CutDepthMm||Draft.Tabs.RampLengthMm<0))ValidationMessage="Tab count, width, height, or ramp length is invalid.";
        else { ValidationMessage = null; WarningMessage = Draft.CutDepthMm > materialThickness ? "Cut depth exceeds material thickness; ensure spoilboard clearance." : null; OnPropertyChanged(nameof(PassCount)); return true; }
        return false;
    }
    private static ProfileOperationDefinition Copy(ProfileOperationDefinition value) => new() { Name = value.Name, GeometryIds = [.. value.GeometryIds], Tool = value.Tool with { }, IsEnabled = value.IsEnabled, Order = value.Order, Side = value.Side, Direction = value.Direction, StartDepthMm = value.StartDepthMm, CutDepthMm = value.CutDepthMm, PassDepthMm = value.PassDepthMm, SafeZMm = value.SafeZMm, FeedRateMmPerMin = value.FeedRateMmPerMin, PlungeRateMmPerMin = value.PlungeRateMmPerMin, SpindleRpm = value.SpindleRpm,MaterialSheetId=value.MaterialSheetId,Tabs=value.Tabs.Copy() };
}
