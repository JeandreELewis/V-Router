using CncCad.Cam.Operations;using CncCad.Cam.Tools;
namespace CncCad.App.ViewModels;
public sealed class VCarveOperationEditorViewModel:ViewModelBase
{
    public VCarveOperationDefinition Draft{get;}public IReadOnlyList<CncTool> Tools{get;}public CncTool? SelectedLibraryTool{get;set;}
    public double WidthAtDepth=>Draft.WidthAtMaximumDepthMm;public string ValidationMessage=>Draft.Tool.VBitAngleDegrees is not (>0 and <180)?"Choose a V-bit with a valid included angle.":Draft.MaximumDepthMm<=0||Draft.OffsetStepOverMm<=0?"Depth and step-over must be positive.":string.Empty;
    public VCarveOperationEditorViewModel(VCarveOperationDefinition source,IReadOnlyList<CncTool> tools){Draft=source.Duplicate();Draft.Name=source.Name;Draft.Order=source.Order;Tools=tools.Where(x=>x.Type is ToolType.VBit or ToolType.EngravingBit).ToArray();SelectedLibraryTool=Tools.FirstOrDefault(x=>x.Id==source.Tool.SourceToolId);}
    public void ApplyTool(CncTool tool){Draft.Tool=ToolSnapshot.FromTool(tool);Draft.FeedRateMmPerMin=tool.FeedRateMmPerMin;Draft.PlungeRateMmPerMin=tool.PlungeRateMmPerMin;Draft.SpindleRpm=tool.SpindleRpm;OnPropertyChanged(nameof(Draft));OnPropertyChanged(nameof(WidthAtDepth));OnPropertyChanged(nameof(ValidationMessage));}
}
