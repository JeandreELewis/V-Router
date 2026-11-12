using System.Collections.ObjectModel;
using CncCad.Cam.Operations;
using CncCad.Cam.Tools;
using CncCad.Core.Documents;
using CncCad.Core.Selection;
using CncCad.Infrastructure.Settings;
using CncCad.PostProcessors.GCode;
using CncCad.Simulation.Playback;
using CncCad.Simulation.Stock;

namespace CncCad.App.ViewModels;

public enum ResponsiveLayoutState { Compact, Regular }
public enum DesignTaskPage { Palette, Construction, Position, Transform, Edit, OffsetLayout, TextShapes, Appearance, Repair, Snapping, Layers, Nesting, Job }
public enum ToolpathTaskPage { Palette, OperationEditor, ToolLibrary, MachineFixtures }

public static class WorkspaceLayoutMetrics
{
    public const double MenuHeight = 28;
    public const double CommandBarHeight = 44;
    public const double CanvasStripHeight = 34;
    public const double StatusBarHeight = 24;
    public const double DesignPanelWidth = 216;
    public const double ToolpathsPanelWidth = 304;
    public const double MinimumCanvasWidth = 760;
    public const double CompactBreakpoint = 1360;

    public static ResponsiveLayoutState ForWidth(double width) =>
        width >= CompactBreakpoint ? ResponsiveLayoutState.Regular : ResponsiveLayoutState.Compact;

    public static double CanvasWidth(double windowWidth, WorkspaceMode mode) =>
        Math.Max(0, windowWidth - (mode switch
        {
            WorkspaceMode.Design => DesignPanelWidth,
            WorkspaceMode.Toolpaths => ToolpathsPanelWidth,
            _ => 0
        }));
}

public sealed class ShellWorkspaceViewModel(Action<WorkspaceMode> setMode,Action<ViewportLayout> setLayout)
{
    public WorkspaceMode Mode { get; internal set; } = WorkspaceMode.Startup;
    public ViewportLayout Layout { get; internal set; } = ViewportLayout.Design2D;
    public ResponsiveLayoutState ResponsiveLayout { get; internal set; } = ResponsiveLayoutState.Regular;
    public void SwitchMode(WorkspaceMode mode)=>setMode(mode);
    public void SwitchLayout(ViewportLayout layout)=>setLayout(layout);
}

public sealed record DesignWorkspaceViewModel(CadDocument Document,SelectionModel Selection,ObservableCollection<CadLayer> Layers);
public sealed record ToolpathWorkspaceViewModel(CamJob Job,ObservableCollection<CamOperationDefinition> Operations,ObservableCollection<CncTool> Tools);
public sealed record PreviewWorkspaceViewModel(ToolpathPlaybackSession Playback,Func<StockHeightfield?> CurrentStock);
public sealed record ExportWorkspaceViewModel(IPostProcessorRegistry Registry)
{
    public IReadOnlyList<PostProcessorDescriptor> AvailablePostProcessors=>Registry.Descriptors;
}
