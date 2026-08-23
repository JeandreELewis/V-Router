using CncCad.Cam.Operations;
using CncCad.Cam.Tools;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncCad.App.ViewModels;

public partial class EngraveOperationEditorViewModel:ViewModelBase
{
    public EngraveOperationDefinition Draft{get;}public IReadOnlyList<CncTool> Tools{get;}public IReadOnlyList<EngraveDirection> Directions{get;}=Enum.GetValues<EngraveDirection>();
    [ObservableProperty]private CncTool? selectedLibraryTool;[ObservableProperty]private string? validationMessage;
    public int PassCount=>Draft.PassDepthMm>0&&Draft.CutDepthMm>Draft.StartDepthMm?(int)Math.Ceiling((Draft.CutDepthMm-Draft.StartDepthMm)/Draft.PassDepthMm):0;
    public EngraveOperationEditorViewModel(EngraveOperationDefinition source,IReadOnlyList<CncTool> tools){Draft=source.Duplicate();Draft.Name=source.Name;Draft.Order=source.Order;Tools=tools;SelectedLibraryTool=tools.FirstOrDefault(x=>x.Id==source.Tool.SourceToolId);}
    public void ApplyTool(CncTool tool){Draft.Tool=ToolSnapshot.FromTool(tool);Draft.FeedRateMmPerMin=tool.FeedRateMmPerMin;Draft.PlungeRateMmPerMin=tool.PlungeRateMmPerMin;Draft.SpindleRpm=tool.SpindleRpm;Draft.PassDepthMm=Math.Max(.01,tool.StepDownMm);OnPropertyChanged(nameof(Draft));OnPropertyChanged(nameof(PassCount));}
    public bool Validate(){ValidationMessage=string.IsNullOrWhiteSpace(Draft.Name)?"Operation name is required.":Draft.Tool.DiameterMm<=0?"Select a valid tool.":Draft.CutDepthMm<=Draft.StartDepthMm||Draft.PassDepthMm<=0?"Final depth must exceed start depth and pass depth must be positive.":Draft.FeedRateMmPerMin<=0||Draft.PlungeRateMmPerMin<=0||Draft.SafeZMm<=0?"Feed, plunge and safe Z must be positive.":null;OnPropertyChanged(nameof(PassCount));return ValidationMessage is null;}
}
