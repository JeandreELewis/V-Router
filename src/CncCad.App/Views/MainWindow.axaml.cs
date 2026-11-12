using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using CncCad.App.ViewModels;
using CncCad.Rendering.Canvas;
using CncCad.Simulation.Playback;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.Media;
using System.Diagnostics;
using CncCad.Core.Transforms;
using CncCad.Core.Preview;
using CncCad.Infrastructure.Settings;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using System.Text;
using Avalonia.VisualTree;
using CncCad.App.Services;

namespace CncCad.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Stopwatch _playbackClock = new();
    private bool _updatingPlayback;
    private readonly IApplicationPreferencesStore? _preferencesStore;
    private ApplicationPreferences _preferences = ApplicationPreferences.Defaults;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private Flyout? _commandHelpFlyout;
    private bool _helpMode;
    public MainWindow() : this(null) { }
    public MainWindow(IApplicationPreferencesStore? preferencesStore) { _preferencesStore=preferencesStore;InitializeComponent(); Loaded += async (_, _) => { ConfigureCanvas();ViewModel.SetResponsiveLayout(Bounds.Width);await LoadPreferencesAsync();ConfigurePlatformMenu();}; SizeChanged+=(_,e)=>ViewModel.SetResponsiveLayout(e.NewSize.Width);Closing += OnClosing; KeyDown += OnKeyDown;AddHandler(PointerPressedEvent,OnCommandHelpPointerPressed,RoutingStrategies.Tunnel,true); _playbackTimer.Tick += PlaybackTick; _playbackTimer.Start();_recoveryTimer.Tick+=async(_,_)=>{try{await ViewModel.SaveRecoveryIfDueAsync();}catch(Exception ex){ViewModel.Message=$"Recovery snapshot failed: {ex.Message}";}};_recoveryTimer.Start(); _playbackClock.Start(); }

    private void HelpMode_Click(object? sender,RoutedEventArgs e)
    {
        _helpMode=HelpModeToggle.IsChecked==true;
        ViewModel.Message=_helpMode?"Command help mode — click any command for an explanation; Esc exits":"Command help mode closed";
    }

    private void OnCommandHelpPointerPressed(object? sender,PointerPressedEventArgs e)
    {
        var point=e.GetCurrentPoint(this);var secondary=point.Properties.PointerUpdateKind==PointerUpdateKind.RightButtonPressed;
        if(!secondary&&!_helpMode)return;
        var target=FindHelpTarget(e.Source as Visual);if(target is null)return;
        ShowCommandHelp(target);e.Handled=true;
    }

    private static Control? FindHelpTarget(Visual? visual)
    {
        for(var current=visual;current is not null;current=current.GetVisualParent())
            if(current is Control control&&control is not Window&&CommandHelp.DescriptorFor(control) is not null)return control;
        return null;
    }

    private void ShowCommandHelp(Control target)
    {
        if(CommandHelp.DescriptorFor(target) is not{} help)return;
        _commandHelpFlyout?.Hide();
        var body=new StackPanel{Spacing=7,MaxWidth=330};
        body.Children.Add(new TextBlock{Text=help.Title,FontSize=16,FontWeight=FontWeight.SemiBold});
        body.Children.Add(new TextBlock{Text=help.Summary,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse("#C7D5DC"))});
        body.Children.Add(new TextBlock{Text="HOW TO USE",FontSize=10,FontWeight=FontWeight.Bold,Foreground=new SolidColorBrush(Color.Parse("#7ECBEF")),Margin=new Thickness(0,5,0,0)});
        body.Children.Add(new TextBlock{Text=help.HowTo,TextWrapping=TextWrapping.Wrap});
        if(!target.IsEffectivelyEnabled&&!string.IsNullOrWhiteSpace(help.Requirement))body.Children.Add(new Border{Background=new SolidColorBrush(Color.Parse("#3A3020")),CornerRadius=new CornerRadius(4),Padding=new Thickness(8),Child=new TextBlock{Text=$"Unavailable: {help.Requirement}",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse("#F1C86B"))}});
        if(!string.IsNullOrWhiteSpace(help.Result))body.Children.Add(new TextBlock{Text=$"Result: {help.Result}",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse("#9FB0BA"))});
        if(!string.IsNullOrWhiteSpace(help.Shortcut))body.Children.Add(new TextBlock{Text=$"Shortcut: {help.Shortcut}",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#7ECBEF"))});
        _commandHelpFlyout=new Flyout{Placement=PlacementMode.RightEdgeAlignedTop,Content=new Border{Background=new SolidColorBrush(Color.Parse("#20292F")),BorderBrush=new SolidColorBrush(Color.Parse("#4A5B65")),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Padding=new Thickness(14),Child=body}};
        _commandHelpFlyout.ShowAt(target);ViewModel.Message=$"Help: {help.Title}";
    }
    private void ConfigureCanvas()
    {
        Canvas.Document = ViewModel.Document; Canvas.Toolpaths = ViewModel.Program;Canvas.Operations=ViewModel.Operations; Canvas.Selection = ViewModel.Selection; Canvas.SnapSettings = ViewModel.SnapSettings;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings;Canvas.StockSimulation=ViewModel.StockSimulation;
        Canvas.EntityCreated = entity => { ViewModel.AddEntity(entity); Canvas.InvalidateVisual(); };
        Canvas.SelectionMoved = delta => { ViewModel.MoveSelection(delta); Canvas.InvalidateVisual(); };
        Canvas.SelectionTransformed = (transform,description) => { ViewModel.TransformSelection(transform,description); Canvas.InvalidateVisual(); };
        Canvas.NodeSelected = (entity,index,point) => ViewModel.SelectNode(entity,index,point);
        Canvas.NodeMoved = (entity,index,point) => { ViewModel.MoveNode(entity,index,point); Canvas.InvalidateVisual(); };
        Canvas.TabPlacementRequested=point=>{try{ViewModel.AddManualTabAt(point);Canvas.InvalidateVisual();}catch(Exception ex){ViewModel.Message=ex.Message;}};
        Canvas.TrimRequested=candidate=>{ViewModel.ApplyInteractiveTrim(candidate);Canvas.InvalidateVisual();return true;};Canvas.TrimStatusChanged=status=>ViewModel.Message=status;
        Canvas.SelectionChanged = ViewModel.SelectionChanged; Canvas.PointerStatusChanged = ViewModel.UpdatePointer; Canvas.InteractionStatusChanged = ViewModel.UpdateInteraction; Canvas.SetTool(CanvasTool.Select);
        PreviewCanvas.Document=ViewModel.Document;PreviewCanvas.Toolpaths=ViewModel.Program;PreviewCanvas.Operations=ViewModel.Operations;PreviewCanvas.Selection=ViewModel.Selection;PreviewCanvas.SnapSettings=ViewModel.SnapSettings;PreviewCanvas.Fixtures=ViewModel.CamJob.Fixtures;PreviewCanvas.SafetyFindings=ViewModel.SafetyFindings;PreviewCanvas.ToolpathPreviewMode=true;PreviewCanvas.ShowGrid=false;PreviewCanvas.ShowToolpaths=true;RefreshPreviewAppearance();
    }
    private void RefreshPreviewAppearance()=>PreviewCanvas.SetPreviewAppearance(ViewModel.PreviewSettings,ViewModel.CamJob.Material?.Name,ViewModel.CamJob.Material?.Category);
    private async Task LoadPreferencesAsync()
    {
        if(_preferencesStore is not null)_preferences=await _preferencesStore.LoadAsync();
        ApplyPreferences(_preferences);
    }
    private void ApplyPreferences(ApplicationPreferences p)
    {
        _preferences=p;Canvas.ShowGrid=p.ShowGrid;Canvas.SnappingEnabled=p.SnappingEnabled;Canvas.ShowToolpaths=p.ShowToolpaths;ViewModel.IsAdvancedMode=p.ExperienceMode==ExperienceMode.Advanced;
        ViewModel.SnapSettings.Grid=p.SnapToGrid;ViewModel.SnapSettings.Endpoint=p.SnapToEndpoints;ViewModel.SnapSettings.Midpoint=p.SnapToMidpoints;ViewModel.SnapSettings.Center=p.SnapToCenters;ViewModel.SnapSettings.GridSpacingMm=p.GridSpacingMm;
        GridToggle.IsChecked=p.ShowGrid;SnapToggle.IsChecked=p.SnappingEnabled;PathsToggle.IsChecked=p.ShowToolpaths;
        if(ViewModel.HasJob)SetWorkspaceMode(p.LastWorkspaceMode==WorkspaceMode.Toolpaths?WorkspaceMode.Toolpaths:WorkspaceMode.Design);else SetWorkspaceMode(WorkspaceMode.Startup);
        SetViewportLayout(p.ViewportLayout);
        Resources["CncAccentBrush"]=new SolidColorBrush(p.Accent==AccentPreference.System?Color.Parse("#4CC2FF"):Color.Parse("#65C9FF"));Canvas.InvalidateVisual();
    }
    private void ConfigurePlatformMenu(){ApplicationMenuBar.IsVisible=!OperatingSystem.IsMacOS();if(OperatingSystem.IsMacOS())NativeDock.SetMenu(this,BuildNativeMenu());}
    private NativeMenu BuildNativeMenu()
    {
        NativeMenuItem Item(string label,Action action,KeyGesture? gesture=null,Func<bool>? enabled=null){var command=new ApplicationCommand(label,label,action,enabled,gesture);return new NativeMenuItem(label){Gesture=gesture,Command=command};}
        NativeMenuItem Root(string label,params NativeMenuItemBase[] children){var menu=new NativeMenu();foreach(var child in children)menu.Add(child);return new NativeMenuItem(label){Menu=menu};}
        var menu=new NativeMenu();
        var recentMenu=new NativeMenu();void RefreshRecent(){recentMenu.Items.Clear();foreach(var project in ViewModel.RecentProjects){var path=project.Path;recentMenu.Add(Item(project.Name,()=>_=OpenRecentPathAsync(path)));}if(ViewModel.RecentProjects.Count>0)recentMenu.Add(new NativeMenuItemSeparator());recentMenu.Add(Item("Clear Recent Projects",()=>_=ClearRecentProjectsAsync()));}recentMenu.NeedsUpdate+=(_,_)=>RefreshRecent();RefreshRecent();var recentRoot=new NativeMenuItem("Open Recent"){Menu=recentMenu};
        menu.Add(Root("V-Router",Item("About V-Router",()=>About_Click(null,new RoutedEventArgs())),Item("Settings…",()=>Settings_Click(null,new RoutedEventArgs()),new(Key.OemComma,KeyModifiers.Meta)),new NativeMenuItemSeparator(),Item("Quit V-Router",Close,new(Key.Q,KeyModifiers.Meta))));
        menu.Add(Root("File",Item("New Job",()=>NewJob_Click(null,new RoutedEventArgs()),new(Key.N,KeyModifiers.Meta)),Item("Open…",()=>Open_Click(null,new RoutedEventArgs()),new(Key.O,KeyModifiers.Meta)),recentRoot,Item("Save",()=>Save_Click(null,new RoutedEventArgs()),new(Key.S,KeyModifiers.Meta)),Item("Save As…",()=>SaveAs_Click(null,new RoutedEventArgs()),new(Key.S,KeyModifiers.Meta|KeyModifiers.Shift)),Item("Import SVG/DXF…",()=>Import_Click(null,new RoutedEventArgs())),Item("Save Toolpaths…",()=>SaveToolpaths_Click(null,new RoutedEventArgs()),enabled:()=>ViewModel.CanSaveToolpaths)));
        menu.Add(Root("Edit",Item("Undo",()=>Undo_Click(null,new RoutedEventArgs()),new(Key.Z,KeyModifiers.Meta)),Item("Redo",()=>Redo_Click(null,new RoutedEventArgs()),new(Key.Z,KeyModifiers.Meta|KeyModifiers.Shift)),Item("Copy",()=>Copy_Click(null,new RoutedEventArgs()),new(Key.C,KeyModifiers.Meta)),Item("Paste",()=>Paste_Click(null,new RoutedEventArgs()),new(Key.V,KeyModifiers.Meta)),Item("Duplicate",()=>Duplicate_Click(null,new RoutedEventArgs()),new(Key.D,KeyModifiers.Meta)),Item("Delete",()=>Delete_Click(null,new RoutedEventArgs())),Item("Select All",()=>SelectAll_Click(null,new RoutedEventArgs()),new(Key.A,KeyModifiers.Meta))));
        menu.Add(Root("View",Item("Design Focus",()=>DesignFocus_Click(null,new RoutedEventArgs())),Item("Toolpaths Focus",()=>ToolpathFocus_Click(null,new RoutedEventArgs())),Item("2D Design",()=>DesignView_Click(null,new RoutedEventArgs())),Item("Toolpath Preview",()=>ToolpathView_Click(null,new RoutedEventArgs())),Item("Toggle Technical / Material",()=>AppearanceToggle_Click(null,new RoutedEventArgs()),enabled:()=>ViewModel.HasJob),Item("Hardwood Preview",()=>{ViewModel.SetPreviewMaterial(MaterialAppearancePreset.Hardwood);RefreshPreviewAppearance();},enabled:()=>ViewModel.HasJob),Item("Plywood Preview",()=>{ViewModel.SetPreviewMaterial(MaterialAppearancePreset.Plywood);RefreshPreviewAppearance();},enabled:()=>ViewModel.HasJob),Item("Zoom to Fit",()=>ZoomFit_Click(null,new RoutedEventArgs()),new(Key.F,KeyModifiers.None)),Item("Zoom to Selection",()=>ZoomSelection_Click(null,new RoutedEventArgs()))));
        menu.Add(Root("Design",Item("Select / Move",()=>SetDrawingTool(CanvasTool.Select)),Item("Line",()=>SetDrawingTool(CanvasTool.Line)),Item("Polyline",()=>SetDrawingTool(CanvasTool.Polyline)),Item("Rectangle",()=>SetDrawingTool(CanvasTool.Rectangle)),Item("Circle",()=>SetDrawingTool(CanvasTool.Circle)),Item("Three-Point Arc",()=>SetDrawingTool(CanvasTool.Arc)),Item("Scissors Trim",()=>TrimTool_Click(null,new RoutedEventArgs()))));
        menu.Add(Root("Toolpaths",Item("Create Surface…",()=>CreateSurface_Click(null,new RoutedEventArgs())),Item("Create Profile…",()=>CreateProfile_Click(null,new RoutedEventArgs())),Item("Create Pocket…",()=>CreatePocket_Click(null,new RoutedEventArgs())),Item("Create Drill…",()=>CreateDrill_Click(null,new RoutedEventArgs())),Item("Create Engraving…",()=>CreateEngrave_Click(null,new RoutedEventArgs())),Item("Create V-Carve…",()=>CreateVCarve_Click(null,new RoutedEventArgs()),enabled:()=>ViewModel.CanCreateVCarve),Item("Run Preflight",()=>RunPreflight_Click(null,new RoutedEventArgs())),Item("Preview Selected",()=>PreviewSelected_Click(null,new RoutedEventArgs())),Item("Preview All",()=>PreviewGCode_Click(null,new RoutedEventArgs())),Item("Save Toolpaths…",()=>SaveToolpaths_Click(null,new RoutedEventArgs()),enabled:()=>ViewModel.CanSaveToolpaths)));
        menu.Add(Root("Help",Item("Keyboard Shortcuts…",()=>Shortcuts_Click(null,new RoutedEventArgs())),Item("About V-Router",()=>About_Click(null,new RoutedEventArgs()))));return menu;
    }
    private void DrawingToolPalette_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ToolPaletteItem item }) return;
        Canvas.SetTool(item.Tool);
        ViewModel.SetActiveToolGuidance(item.Tool);
        if(item.Tool is CanvasTool.Line or CanvasTool.Polyline or CanvasTool.Rectangle or CanvasTool.Circle or CanvasTool.Arc)ViewModel.OpenDesignTask(DesignTaskPage.Construction);
        ViewModel.Message = item.Tool switch { CanvasTool.Select => "Select tool — click, marquee, or drag a selection", CanvasTool.Line => "Line tool — click start and end", CanvasTool.Polyline => "Polyline — click points, Enter to finish, C to close", CanvasTool.Rectangle => "Rectangle tool — drag corner to corner", CanvasTool.Circle => "Circle tool — click center and radius",CanvasTool.DrillPoint=>"Drill point — click each hole location", _ => "3-point arc — click start, curve point, and end" };
    }
    private void ZoomFit_Click(object? s, RoutedEventArgs e) { Canvas.ZoomToFit();if(PreviewCanvas.IsVisible)PreviewCanvas.Fit3D(); }
    private void ZoomSelection_Click(object? s, RoutedEventArgs e) => Canvas.ZoomToSelection();
    private void SelectTool_Click(object? s, RoutedEventArgs e) { DrawingToolPalette.SelectedItem = null; Canvas.SetTool(CanvasTool.Select); ViewModel.ActivePaletteItem = null; ViewModel.SetActiveToolGuidance(CanvasTool.Select);ViewModel.CloseDesignTask(); }
    private void SetDrawingTool(CanvasTool tool){if(tool==CanvasTool.Select){SelectTool_Click(null,new RoutedEventArgs());return;}var item=ViewModel.CreationTools.FirstOrDefault(x=>x.Tool==tool);if(item is not null)DrawingToolPalette.SelectedItem=item;}
    private void DrawingToolMenu_Click(object? sender,RoutedEventArgs e){if(sender is MenuItem{Tag:string value}&&Enum.TryParse<CanvasTool>(value,out var tool))SetDrawingTool(tool);}
    private void CenterSelection_Click(object? s, RoutedEventArgs e) { ViewModel.CenterSelectionOnMaterial(); Canvas.InvalidateVisual(); }
    private void GridToggle_Click(object? s, RoutedEventArgs e) { Canvas.ShowGrid = GridToggle.IsChecked == true; Canvas.InvalidateVisual(); }
    private void SnapToggle_Click(object? s, RoutedEventArgs e) { Canvas.SnappingEnabled = SnapToggle.IsChecked == true; Canvas.InvalidateVisual(); }
    private void PathsToggle_Click(object? s, RoutedEventArgs e) { Canvas.ShowToolpaths = PathsToggle.IsChecked == true;PreviewCanvas.ShowToolpaths=Canvas.ShowToolpaths; Canvas.InvalidateVisual();PreviewCanvas.InvalidateVisual(); }
    private void CutterToggle_Click(object? s,RoutedEventArgs e){PreviewCanvas.ShowCutter=CutterToggle.IsChecked==true;PreviewCanvas.InvalidateVisual();}
    private void GridMenu_Click(object? s,RoutedEventArgs e){GridToggle.IsChecked=!GridToggle.IsChecked;GridToggle_Click(s,e);}
    private void SnapMenu_Click(object? s,RoutedEventArgs e){SnapToggle.IsChecked=!SnapToggle.IsChecked;SnapToggle_Click(s,e);}
    private void PathsMenu_Click(object? s,RoutedEventArgs e){PathsToggle.IsChecked=!PathsToggle.IsChecked;PathsToggle_Click(s,e);}
    private void CutterMenu_Click(object? s,RoutedEventArgs e){CutterToggle.IsChecked=!CutterToggle.IsChecked;CutterToggle_Click(s,e);}
    private void CameraPresetMenu_Click(object? sender,RoutedEventArgs e)=>ApplyCameraPreset(sender);
    private void ViewportOverflow_Click(object? sender,RoutedEventArgs e){if(sender is Button button&&button.ContextMenu is{} menu)menu.Open(button);}
    private void AppearanceToggle_Click(object? sender,RoutedEventArgs e){if(!ViewModel.HasJob)return;ViewModel.TogglePreviewStyle();RefreshPreviewAppearance();}
    private void AppearanceMenu_Click(object? sender,RoutedEventArgs e){if(sender is Button button&&button.ContextMenu is{} menu)menu.Open(button);}
    private void PreviewMaterial_Click(object? sender,RoutedEventArgs e){if(sender is MenuItem{Tag:string tag}&&Enum.TryParse<MaterialAppearancePreset>(tag,out var preset)){ViewModel.SetPreviewMaterial(preset);RefreshPreviewAppearance();}}
    private void PreviewGrain_Click(object? sender,RoutedEventArgs e){if(sender is MenuItem{Tag:string tag}&&Enum.TryParse<GrainDirection>(tag,out var direction)){ViewModel.SetPreviewGrain(direction);RefreshPreviewAppearance();}}
    private void PreviewTint_Click(object? sender,RoutedEventArgs e){if(sender is MenuItem{Tag:string tag}){ViewModel.SetPreviewTint(string.IsNullOrWhiteSpace(tag)?null:tag);RefreshPreviewAppearance();}}
    private void PreviewAppearanceReset_Click(object? sender,RoutedEventArgs e){ViewModel.ResetPreviewAppearance();RefreshPreviewAppearance();}
    private void CubeMore_Click(object? sender,RoutedEventArgs e){if(sender is Button button&&button.ContextMenu is{} menu)menu.Open(button);e.Handled=true;}
    private void OpenDesignTask_Click(object? sender,RoutedEventArgs e){if(sender is Button{Tag:string value}&&Enum.TryParse<DesignTaskPage>(value,out var page))ViewModel.OpenDesignTask(page);}
    private void CloseDesignTask_Click(object? sender,RoutedEventArgs e)=>ViewModel.CloseDesignTask();
    private void OpenToolpathTask_Click(object? sender,RoutedEventArgs e){if(sender is Button{Tag:string value}&&Enum.TryParse<ToolpathTaskPage>(value,out var page))ViewModel.OpenToolpathTask(page);}
    private void CloseToolpathTask_Click(object? sender,RoutedEventArgs e)=>ViewModel.CloseToolpathTask();
    private void DesignView_Click(object? s, RoutedEventArgs e) => SetViewportLayout(ViewportLayout.Design2D);
    private void ToolpathView_Click(object? s, RoutedEventArgs e) => SetViewportLayout(ViewportLayout.Preview3D);
    private void HorizontalSplit_Click(object? s,RoutedEventArgs e)=>SetViewportLayout(ViewportLayout.HorizontalSplit);
    private void VerticalSplit_Click(object? s,RoutedEventArgs e)=>SetViewportLayout(ViewportLayout.VerticalSplit);
    private void SetWorkspaceView(WorkspaceView view)=>SetViewportLayout(view==WorkspaceView.Design?ViewportLayout.Design2D:ViewportLayout.Preview3D);
    private void SetViewportLayout(ViewportLayout layout)
    {
        ViewModel.SetViewportLayout(layout);ViewportGrid.RowDefinitions.Clear();ViewportGrid.ColumnDefinitions.Clear();Canvas.ToolpathPreviewMode=false;Canvas.IsVisible=layout!=ViewportLayout.Preview3D;PreviewCanvas.IsVisible=layout!=ViewportLayout.Design2D;
        if(layout==ViewportLayout.HorizontalSplit){ViewportGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));ViewportGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));ViewportGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));Grid.SetRow(Canvas,0);Grid.SetColumn(Canvas,0);Grid.SetRow(PreviewCanvas,1);Grid.SetColumn(PreviewCanvas,0);}
        else if(layout==ViewportLayout.VerticalSplit){ViewportGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));ViewportGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));ViewportGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));Grid.SetRow(Canvas,0);Grid.SetColumn(Canvas,0);Grid.SetRow(PreviewCanvas,0);Grid.SetColumn(PreviewCanvas,1);}
        else{ViewportGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));ViewportGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));Grid.SetRow(Canvas,0);Grid.SetColumn(Canvas,0);Grid.SetRow(PreviewCanvas,0);Grid.SetColumn(PreviewCanvas,0);}
        Grid.SetRow(ViewCube,layout==ViewportLayout.HorizontalSplit?1:0);
        Grid.SetColumn(ViewCube,layout==ViewportLayout.VerticalSplit?1:0);
        if(PreviewCanvas.IsVisible){RefreshPreviewAppearance();PreviewCanvas.Enter3DPreview();}CenterWorkspaceGrid.RowDefinitions[2].Height=new GridLength(ViewModel.ShowPlaybackControls?86:0);DesignViewButton.IsChecked=layout==ViewportLayout.Design2D;PreviewViewButton.IsChecked=layout==ViewportLayout.Preview3D;HorizontalSplitButton.IsChecked=layout==ViewportLayout.HorizontalSplit;VerticalSplitButton.IsChecked=layout==ViewportLayout.VerticalSplit;Canvas.InvalidateVisual();PreviewCanvas.InvalidateVisual();
    }
    private void CameraPreset_Click(object? sender, RoutedEventArgs e)=>ApplyCameraPreset(sender);
    private void CameraCubeFace_PointerPressed(object? sender,PointerPressedEventArgs e){ApplyCameraPreset(sender);e.Handled=true;}
    private void ApplyCameraPreset(object? sender)
    {
        if(sender is not Control{Tag:string tag}||!Enum.TryParse<CameraPreset>(tag,out var preset))return;
        if(ViewModel.ViewportLayout==ViewportLayout.Design2D)SetViewportLayout(ViewportLayout.Preview3D);
        PreviewCanvas.SetCameraPreset(preset);ViewModel.CameraView=preset.ToString();
    }
    private void DesignFocus_Click(object? s, RoutedEventArgs e) => SetWorkspaceMode(WorkspaceMode.Design);
    private void ToolpathFocus_Click(object? s, RoutedEventArgs e) => SetWorkspaceMode(WorkspaceMode.Toolpaths);
    private void SetWorkspaceFocus(WorkspaceFocus focus) => SetWorkspaceMode(focus==WorkspaceFocus.Toolpaths?WorkspaceMode.Toolpaths:WorkspaceMode.Design);
    private void SetWorkspaceMode(WorkspaceMode mode){ViewModel.SetWorkspaceMode(mode);WorkspaceGrid.ColumnDefinitions[0].Width=new GridLength(mode==WorkspaceMode.Design?WorkspaceLayoutMetrics.DesignPanelWidth:0);WorkspaceGrid.ColumnDefinitions[2].Width=new GridLength(mode==WorkspaceMode.Toolpaths?WorkspaceLayoutMetrics.ToolpathsPanelWidth:0);}
    private void PlaybackTick(object? sender, EventArgs e)
    {
        var elapsed = _playbackClock.Elapsed; _playbackClock.Restart(); ViewModel.Playback.Advance(elapsed); UpdatePlaybackFrame();
    }
    private void UpdatePlaybackFrame() { _updatingPlayback = true; ViewModel.UpdatePlaybackStatus(); CenterWorkspaceGrid.RowDefinitions[2].Height=new GridLength(ViewModel.ShowPlaybackControls?86:0); Canvas.PlaybackFrame = ViewModel.Playback.Frame;Canvas.StockSimulation=ViewModel.StockSimulation;PreviewCanvas.PlaybackFrame=ViewModel.Playback.Frame;PreviewCanvas.StockSimulation=ViewModel.StockSimulation;RefreshPreviewAppearance(); Canvas.InvalidateVisual();PreviewCanvas.InvalidateVisual(); _updatingPlayback = false; }
    private void PlayPause_Click(object? s, RoutedEventArgs e) { if (ViewModel.Playback.State == PlaybackState.Playing) ViewModel.Playback.Pause(); else ViewModel.Playback.Play(); _playbackClock.Restart(); UpdatePlaybackFrame(); }
    private void PlaybackStop_Click(object? s, RoutedEventArgs e) { ViewModel.Playback.Stop(); UpdatePlaybackFrame(); }
    private void PlaybackRestart_Click(object? s, RoutedEventArgs e) { ViewModel.Playback.Restart(); _playbackClock.Restart(); UpdatePlaybackFrame(); }
    private void PlaybackSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { if (_updatingPlayback || !ViewModel.HasPlayback) return; ViewModel.Playback.Seek(e.NewValue / 100); UpdatePlaybackFrame(); }
    private void PlaybackSpeed_SelectionChanged(object? sender, SelectionChangedEventArgs e) { if (sender is ComboBox { SelectedItem: double speed }) { ViewModel.PlaybackSpeed = speed; ViewModel.Playback.SetSpeed(speed); } }
    private void Delete_Click(object? s, RoutedEventArgs e) { ViewModel.DeleteSelection(); Canvas.InvalidateVisual(); }
    private void SelectAll_Click(object? s,RoutedEventArgs e){ViewModel.Selection.Set(ViewModel.Document.Entities.Where(x=>x.IsVisible&&!x.IsLocked).Select(x=>x.Id));ViewModel.SelectionChanged();Canvas.InvalidateVisual();}
    private void Undo_Click(object? s, RoutedEventArgs e) { ViewModel.Undo(); Canvas.InvalidateVisual(); }
    private void Redo_Click(object? s, RoutedEventArgs e) { ViewModel.Redo(); Canvas.InvalidateVisual(); }
    private void Copy_Click(object? s, RoutedEventArgs e) => ViewModel.CopySelection();
    private void Paste_Click(object? s, RoutedEventArgs e) { ViewModel.Paste(); Canvas.InvalidateVisual(); }
    private void Duplicate_Click(object? s, RoutedEventArgs e) { ViewModel.DuplicateSelection(); Canvas.InvalidateVisual(); }
    private void ApplyPosition_Click(object? s, RoutedEventArgs e) { ViewModel.ApplySelectionPosition(); Canvas.InvalidateVisual(); }
    private void CreateExact_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CreateExact);
    private void AddNumericNode_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.AddNumericPolylineNode);
    private void FinishNumericPolyline_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.FinishNumericPolyline(false));
    private void CloseNumericPolyline_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.FinishNumericPolyline(true));
    private void Rotate_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.RotateSelection);
    private void Scale_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ScaleSelection);
    private void MirrorH_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.MirrorSelection(true));
    private void MirrorV_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.MirrorSelection(false));
    private void Align_Click(object? s,RoutedEventArgs e){if(s is Button{Tag:string mode})RunCadAction(()=>ViewModel.AlignSelection(mode));}
    private void DistributeH_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.DistributeSelection(true));
    private void DistributeV_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.DistributeSelection(false));
    private void Offset_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.OffsetSelection);
    private void Trim_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.TrimSelectionAtPoint);
    private void TrimTool_Click(object? s,RoutedEventArgs e){NodeEditToggle.IsChecked=false;Canvas.NodeEditMode=false;DrawingToolPalette.SelectedItem=null;Canvas.SetTool(CanvasTool.Trim);ViewModel.ActivePaletteItem=null;ViewModel.SetActiveToolGuidance(CanvasTool.Trim);ViewModel.Message="Scissors active · hover a crossing-bounded segment and click";}
    private void Chamfer_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ChamferSelection);
    private void Fillet_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.FilletSelection);
    private void Join_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.JoinSelection());
    private void OpenPath_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.SetPathClosed(false));
    private void ClosePath_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.SetPathClosed(true));
    private void NodeEdit_Click(object? s,RoutedEventArgs e){Canvas.NodeEditMode=NodeEditToggle.IsChecked==true;Canvas.SetTool(CanvasTool.Select);Canvas.InvalidateVisual();}
    private void NodeEditMenu_Click(object? s,RoutedEventArgs e){NodeEditToggle.IsChecked=!NodeEditToggle.IsChecked;NodeEdit_Click(s,e);}
    private void ApplyNode_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ApplyNodePosition);
    private void InsertNode_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.InsertNode);
    private void DeleteNode_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.DeleteNode);
    private void MakeStartNode_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.MakeStartNode);
    private void RunCadAction(Action action){try{action();Canvas.InvalidateVisual();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private async void Import_Click(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import vector artwork",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SVG or DXF vectors") { Patterns = ["*.svg", "*.dxf"] }]
        });
        if (files.Count == 0) return;
        try
        {
            await using var source = await files[0].OpenReadAsync();using var bytes=new MemoryStream();await source.CopyToAsync(bytes);bytes.Position=0;var preview=ViewModel.InspectVectors(files[0].Name,bytes);var options=await ShowImportOptionsAsync(preview);if(options is null)return;bytes.Position=0;
            var result = ViewModel.ImportVectors(files[0].Name, bytes,options.Value.Scale,new(options.Value.X,options.Value.Y),options.Value.PreserveLayers);
            Canvas.Document = ViewModel.Document;
            Canvas.ZoomToSelection();
            Canvas.InvalidateVisual();
            ViewModel.Message = result.Warnings.Count == 0
                ? $"Imported {result.Entities.Count} vectors from {files[0].Name}"
                : $"Imported {result.Entities.Count} vectors with {result.Warnings.Count} warning(s)";
        }
        catch (Exception ex) { ViewModel.Message = $"Import failed: {ex.Message}"; }
    }
    private async Task<(double Scale,double X,double Y,bool PreserveLayers)?> ShowImportOptionsAsync(CncCad.IO.Import.VectorImportResult preview)
    {
        var dialog=new Window{Title="Import vectors",Width=560,Height=520,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.Parse("#181E23"))};var scale=new NumericUpDown{Value=1,Minimum=.000001m,Maximum=100000};var x=new NumericUpDown{Value=0};var y=new NumericUpDown{Value=0};var layers=new CheckBox{Content="Preserve DXF layers / SVG groups",IsChecked=true};var diagnostics=FormatImportDiagnostics(preview);var result=((double,double,double,bool)?)null;var import=new Button{Content="Import",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};var cancel=new Button{Content="Cancel"};import.Click+=(_,_)=>{result=((double)(scale.Value??1),(double)(x.Value??0),(double)(y.Value??0),layers.IsChecked==true);dialog.Close();};cancel.Click+=(_,_)=>dialog.Close();
        dialog.Content=new StackPanel{Margin=new Thickness(22),Spacing=10,Children={new TextBlock{Text=$"{preview.Entities.Count} supported vectors · detected units: {preview.DetectedUnit}",FontSize=17,FontWeight=FontWeight.Bold},new TextBlock{Text=$"Conversion applied: × {preview.AppliedScaleToMillimetres:0.######} to millimetres",Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},new TextBlock{Text=$"Bounds: {preview.Bounds.Width:0.###} × {preview.Bounds.Height:0.###} mm",Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},new TextBlock{Text="Scale override"},scale,new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=8,Children={new StackPanel{Children={new TextBlock{Text="Target X (mm)"},x}},new StackPanel{[Grid.ColumnProperty]=1,Children={new TextBlock{Text="Target Y (mm)"},y}}}},layers,new ScrollViewer{MaxHeight=180,Content=new TextBlock{Text=diagnostics,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse("#E8B85C"))}},new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,Children={cancel,import}}}};await dialog.ShowDialog(this);return result;
    }
    internal static string FormatImportDiagnostics(CncCad.IO.Import.VectorImportResult preview)
    {
        var lines=new List<string>();
        if(preview.Approximations.Count>0){lines.Add("Approximations");lines.AddRange(preview.Approximations.Select(item=>$"• {item.Message}"));}
        if(preview.OtherWarnings.Count>0){if(lines.Count>0)lines.Add("");lines.Add("Warnings");lines.AddRange(preview.OtherWarnings.Select(item=>$"• {item.Message}"));}
        if(preview.UnsupportedDetails.Count>0){if(lines.Count>0)lines.Add("");lines.Add("Unsupported content (will not be imported)");lines.AddRange(preview.UnsupportedDetails.Select(item=>$"• {item.Element}{(item.Line is null?string.Empty:$" at line {item.Line}{(item.Column is null?string.Empty:$", column {item.Column}")}")}"));}
        return lines.Count==0?"No approximations, warnings, or unsupported content detected.":string.Join("\n",lines);
    }
    private void CreateSlot_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CreateSlot);
    private void CreateRounded_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CreateRoundedRectangle);
    private void CreatePolygon_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CreatePolygon);
    private void CreateStar_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CreateStar);
    private void CreateText_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CreateText);
    private void UpdateText_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.UpdateSelectedText);
    private void CenterTextPath_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CenterTextOnPath);
    private void ReverseTextPath_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ReverseTextPath);
    private void FlipTextPath_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.FlipTextPath);
    private void ResetTextSpacing_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ResetTextSpacing);
    private void ConvertText_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ConvertSelectedTextToVectors);
    private void ApplyColor_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ApplyVectorColor);
    private void PaletteColor_Click(object? s,RoutedEventArgs e){if(s is Control{Tag:string color})RunCadAction(()=>ViewModel.ChoosePaletteColor(color));}
    private void PickColor_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.PickPrimaryColor);
    private void LayerColor_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.UseLayerColor);
    private void Group_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.GroupSelection);
    private void Ungroup_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.UngroupSelection);
    private void AnalyzeRepair_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.AnalyzeRepair);
    private void ApplyRepair_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ApplyRepair);
    private void AddLayer_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.AddLayer);
    private void DeleteLayer_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.DeleteSelectedLayer);
    private void ToggleLayerVisibility_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ToggleLayerVisibility);
    private void ToggleLayerLock_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ToggleLayerLock);
    private void ActivateLayer_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.ActivateLayer);
    private void SelectLayer_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.SelectLayerContents);
    private void MoveToLayer_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.MoveSelectionToLayer);
    private void LayerProperties_LostFocus(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.CommitLayerProperties);
    private void MoveLayerUp_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.MoveSelectedLayer(-1));
    private void MoveLayerDown_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.MoveSelectedLayer(1));
    private void AddNestParts_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.AddNestPartsFromSelection);
    private void RemoveNestPart_Click(object? s,RoutedEventArgs e)=>RunCadAction(ViewModel.RemoveSelectedNestPart);
    private void OptimizeNest_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>ViewModel.OptimizeNest());
    private void AcceptNest_Click(object? s,RoutedEventArgs e)=>RunCadAction(()=>{ViewModel.AcceptNest();Canvas.ActiveMaterialSheetId=ViewModel.SelectedMaterialSheet?.Id;Canvas.ZoomToFit();});
    private void MaterialSheet_SelectionChanged(object? s,SelectionChangedEventArgs e){ViewModel.SelectMaterialSheet(ViewModel.SelectedMaterialSheet);Canvas.ActiveMaterialSheetId=ViewModel.SelectedMaterialSheet?.Id;Canvas.ZoomToFit();Canvas.InvalidateVisual();}
    private void SourceDesign_Click(object? s,RoutedEventArgs e){ViewModel.SelectMaterialSheet(null);Canvas.ActiveMaterialSheetId=null;Canvas.ZoomToFit();Canvas.InvalidateVisual();}
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if(e.Key==Key.F1)
        {
            if(FocusManager?.GetFocusedElement() is Control focused&&CommandHelp.DescriptorFor(focused) is not null)ShowCommandHelp(focused);
            else{_helpMode=true;HelpModeToggle.IsChecked=true;ViewModel.Message="Command help mode — click any command for an explanation; Esc exits";}
            e.Handled=true;return;
        }
        if(e.Key==Key.Escape&&_helpMode){_helpMode=false;HelpModeToggle.IsChecked=false;_commandHelpFlyout?.Hide();ViewModel.Message="Command help mode closed";e.Handled=true;return;}
        if (FocusManager?.GetFocusedElement() is TextBox or NumericUpDown) return;
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if(command&&e.Key==Key.N){NewJob_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if(command&&e.Key==Key.O){Open_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if(command&&e.Key==Key.S&&e.KeyModifiers.HasFlag(KeyModifiers.Shift)){SaveAs_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if(command&&e.Key==Key.S){Save_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if(command&&e.Key==Key.OemComma){Settings_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if(command&&e.Key==Key.A){SelectAll_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if (command && e.Key == Key.C) { ViewModel.CopySelection(); e.Handled = true; }
        else if (command && e.Key == Key.V) { ViewModel.Paste(); Canvas.InvalidateVisual(); e.Handled = true; }
        else if (command && e.Key == Key.D) { ViewModel.DuplicateSelection(); Canvas.InvalidateVisual(); e.Handled = true; }
        else if (e.Key is Key.Delete or Key.Back) { ViewModel.DeleteSelection(); Canvas.InvalidateVisual(); e.Handled = true; }
        else if (command && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { ViewModel.Redo(); Canvas.InvalidateVisual(); e.Handled = true; }
        else if (command && e.Key == Key.Z) { ViewModel.Undo(); Canvas.InvalidateVisual(); e.Handled = true; }
        else if (command && e.Key == Key.Y) { ViewModel.Redo(); Canvas.InvalidateVisual(); e.Handled = true; }
        else if(e.Key==Key.F){ZoomFit_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if(e.Key==Key.Space){SelectTool_Click(null,new RoutedEventArgs());ViewModel.Message="Select / Move tool active";e.Handled=true;}
        else if(e.Key==Key.Escape){if(!Canvas.HandleDrawingKey(e.Key))SelectTool_Click(null,new RoutedEventArgs());e.Handled=true;}
        else if (e.Key is Key.Enter or Key.C && Canvas.HandleDrawingKey(e.Key)) e.Handled = true;
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var distance = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10d : 1d;
            var (x, y) = e.Key switch { Key.Left => (-distance, 0d), Key.Right => (distance, 0d), Key.Up => (0d, distance), _ => (0d, -distance) };
            ViewModel.NudgeSelection(x, y); Canvas.InvalidateVisual(); e.Handled = true;
        }
    }
    private async void NewJob_Click(object? s, RoutedEventArgs e)
    {
        DismissToolTip(s);
        if (!await ConfirmSafeToReplaceAsync()) return;
        var settings = await new JobSetupWindow().ShowDialog<CncCad.Core.Documents.JobSettings?>(this);
        if (settings is null) return;
        ViewModel.CreateJob(settings); Canvas.Document = ViewModel.Document; Canvas.Toolpaths = ViewModel.Program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings; Canvas.Reset3DView(); Canvas.ZoomToFit(); Canvas.InvalidateVisual();
        PreviewCanvas.Document=ViewModel.Document;PreviewCanvas.Toolpaths=ViewModel.Program;PreviewCanvas.Fixtures=ViewModel.CamJob.Fixtures;SetWorkspaceMode(WorkspaceMode.Design);SetViewportLayout(ViewportLayout.Design2D);
        await SaveAsAsync();
    }
    private async void EditJob_Click(object? s, RoutedEventArgs e)
    {
        DismissToolTip(s);
        var dialog = new JobSetupWindow();
        var vm = (JobSetupViewModel)dialog.DataContext!;
        vm.JobName = ViewModel.Document.Job.Name; vm.WidthMm = ViewModel.Document.Job.WidthMm; vm.HeightMm = ViewModel.Document.Job.HeightMm; vm.ThicknessMm = ViewModel.Document.Job.ThicknessMm; vm.SafeZMm = ViewModel.Document.Job.SafeZMm; vm.Origin = ViewModel.Document.Job.Origin;vm.OriginOffsetXmm=ViewModel.Document.Job.OriginOffsetXmm;vm.OriginOffsetYmm=ViewModel.Document.Job.OriginOffsetYmm; vm.ZZero = ViewModel.Document.Job.ZZero;
        var settings = await dialog.ShowDialog<CncCad.Core.Documents.JobSettings?>(this);
        if (settings is null) return;
        ViewModel.UpdateJob(settings); Canvas.Reset3DView(); Canvas.ZoomToFit();
    }
    private async void Save_Click(object? s, RoutedEventArgs e)
    {
        try { if (ViewModel.ProjectPath is null) await SaveAsAsync(); else await ViewModel.SaveProjectAsync(ViewModel.ProjectPath); }
        catch (Exception ex) { ViewModel.Message = $"Could not save project: {ex.Message}"; }
    }
    private async void SaveAs_Click(object? s, RoutedEventArgs e) => await SaveAsAsync();
    private async Task<bool> SaveAsAsync()
    {
        var suggestedName=ViewModel.Document.Job.Name.Trim();
        while(suggestedName.EndsWith(".cnccad",StringComparison.OrdinalIgnoreCase))suggestedName=suggestedName[..^".cnccad".Length];
        if(string.IsNullOrWhiteSpace(suggestedName))suggestedName="Untitled CNC Job";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save V-Router Job", SuggestedFileName = suggestedName, DefaultExtension = "cnccad", FileTypeChoices = [new FilePickerFileType("V-Router project") { Patterns = ["*.cnccad"] }] });
        if (file is null) { ViewModel.Message = "Job created but not yet saved"; return false; }
        try { await ViewModel.SaveProjectAsync(file.Path.LocalPath); return true; }
        catch (Exception ex) { ViewModel.Message = $"Could not save project: {ex.Message}"; return false; }
    }
    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmSafeToReplaceAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open V-Router Job", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("V-Router project") { Patterns = ["*.cnccad"] }] });
        if (files.Count > 0) await OpenProjectAsync(files[0].Path.LocalPath);
    }
    private async void RecentProject_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !await ConfirmSafeToReplaceAsync()) return;
        if (!File.Exists(path)) { await ViewModel.RemoveRecentProjectAsync(path); ViewModel.Message = "That recent project no longer exists."; return; }
        await OpenProjectAsync(path);
    }
    private async void RecentMenuItem_Click(object? sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem{Tag:string path}||!await ConfirmSafeToReplaceAsync())return;
        if(!File.Exists(path)){await ViewModel.RemoveRecentProjectAsync(path);ViewModel.Message="That recent project no longer exists.";return;}await OpenProjectAsync(path);
    }
    private async void RecentProjectButton_Click(object? sender,RoutedEventArgs e){if(sender is Button{Tag:string path})await OpenRecentPathAsync(path);}
    private async void RecoveryProjectButton_Click(object? sender,RoutedEventArgs e)
    {
        if(sender is not Button{Tag:CncCad.IO.Projects.RecoverySnapshotInfo snapshot}||!await ConfirmSafeToReplaceAsync())return;try{await ViewModel.OpenRecoveryAsync(snapshot);Canvas.Document=ViewModel.Document;PreviewCanvas.Document=ViewModel.Document;Canvas.Toolpaths=ViewModel.Program;PreviewCanvas.Toolpaths=ViewModel.Program;SetWorkspaceMode(WorkspaceMode.Design);SetViewportLayout(ViewportLayout.Design2D);Canvas.ZoomToFit();}catch(Exception ex){ViewModel.Message=$"Recovery failed: {ex.Message}";}
    }
    private async void DiscardRecovery_Click(object? sender,RoutedEventArgs e){if(sender is Button{Tag:CncCad.IO.Projects.RecoverySnapshotInfo snapshot})await ViewModel.DiscardRecoveryAsync(snapshot);}
    private async Task OpenRecentPathAsync(string path){if(!await ConfirmSafeToReplaceAsync())return;if(!File.Exists(path)){await ViewModel.RemoveRecentProjectAsync(path);ViewModel.Message="That recent project no longer exists.";return;}await OpenProjectAsync(path);}
    private async Task ClearRecentProjectsAsync(){await ViewModel.ClearRecentProjectsAsync();ViewModel.Message="Recent projects cleared";}
    private async void ClearRecent_Click(object? sender,RoutedEventArgs e)=>await ClearRecentProjectsAsync();
    private async void Settings_Click(object? sender,RoutedEventArgs e)
    {
        DismissToolTip(sender);
        var result=await new SettingsWindow(_preferences).ShowDialog<ApplicationPreferences?>(this);if(result is null)return;
        if(_preferencesStore is not null)await _preferencesStore.SaveAsync(result);ApplyPreferences(result);ViewModel.Message="Application settings saved";
    }
    private void About_Click(object? sender,RoutedEventArgs e){DismissToolTip(sender);new AboutWindow().ShowDialog(this);}
    private void Shortcuts_Click(object? sender,RoutedEventArgs e){DismissToolTip(sender);new KeyboardShortcutsWindow().ShowDialog(this);}
    private void Exit_Click(object? sender,RoutedEventArgs e)=>Close();
    private async Task OpenProjectAsync(string path)
    {
        try { await ViewModel.OpenProjectAsync(path); Canvas.Document = ViewModel.Document; Canvas.Toolpaths = ViewModel.Program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings;PreviewCanvas.Document=ViewModel.Document;PreviewCanvas.Toolpaths=ViewModel.Program;PreviewCanvas.Fixtures=ViewModel.CamJob.Fixtures;PreviewCanvas.SafetyFindings=ViewModel.SafetyFindings; Canvas.Reset3DView();PreviewCanvas.Reset3DView(); Canvas.ZoomToFit();SetWorkspaceMode(WorkspaceMode.Design); Canvas.InvalidateVisual(); }
        catch (Exception ex) { ViewModel.Message = $"Could not open project: {ex.Message}"; }
    }
    private async Task<bool> ConfirmSafeToReplaceAsync()
    {
        if (!ViewModel.HasJob || !ViewModel.IsDirty) return true;
        var choice = await new UnsavedChangesWindow(ViewModel.JobTitle).ShowDialog<UnsavedChoice>(this);
        if (choice == UnsavedChoice.Cancel) return false;
        if (choice == UnsavedChoice.Discard) { await ViewModel.DiscardCurrentRecoveryAsync(); return true; }
        if (ViewModel.ProjectPath is null) return await SaveAsAsync();
        try { await ViewModel.SaveProjectAsync(ViewModel.ProjectPath); return true; }
        catch (Exception ex) { ViewModel.Message = $"Could not save project: {ex.Message}"; return false; }
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if(_preferencesStore is not null)try{_preferences=_preferences with{LastWorkspaceMode=ViewModel.WorkspaceMode,ViewportLayout=ViewModel.ViewportLayout,StartupWorkspace=ViewModel.WorkspaceMode==WorkspaceMode.Toolpaths?StartupWorkspace.Toolpaths:StartupWorkspace.Design,ExperienceMode=ViewModel.IsAdvancedMode?ExperienceMode.Advanced:ExperienceMode.Guided};await _preferencesStore.SaveAsync(_preferences);}catch{}
        if (_closeApproved || !ViewModel.HasJob || !ViewModel.IsDirty) return;
        e.Cancel = true;
        if (!await ConfirmSafeToReplaceAsync()) return;
        _closeApproved = true; Close();
    }
    private void CreateProfile_Click(object? s, RoutedEventArgs e)=>OpenInlineEditor(ViewModel.CreateProfileDraft);
    private void CreateEngrave_Click(object? s,RoutedEventArgs e)=>OpenInlineEditor(ViewModel.CreateEngraveDraft);
    private void CreateVCarve_Click(object? s,RoutedEventArgs e)=>OpenInlineEditor(ViewModel.CreateVCarveDraft);
    private void CreatePocket_Click(object? s,RoutedEventArgs e)=>OpenInlineEditor(ViewModel.CreatePocketDraft);
    private void CreateDrill_Click(object? s,RoutedEventArgs e)=>OpenInlineEditor(ViewModel.CreateDrillDraft);
    private void CreateSurface_Click(object? s,RoutedEventArgs e)=>OpenInlineEditor(ViewModel.CreateSurfaceDraft);
    private void OpenInlineEditor(Func<CncCad.Cam.Operations.CamOperationDefinition> create){try{ViewModel.BeginOperationEditor(create(),true);SetWorkspaceMode(WorkspaceMode.Toolpaths);}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void EditProfile_Click(object? s, RoutedEventArgs e){if(ViewModel.SelectedOperation is{} operation)ViewModel.BeginOperationEditor(operation,false);}
    private void SelectEditorVectors_Click(object? s,RoutedEventArgs e){try{ViewModel.SelectVectorsForEditor();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void UseEditorTool_Click(object? s,RoutedEventArgs e){try{ViewModel.ApplySelectedToolToEditor();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void CancelOperationEditor_Click(object? s,RoutedEventArgs e)=>ViewModel.CancelOperationEditor();
    private async void ApplyOperationEditor_Click(object? s,RoutedEventArgs e){try{ViewModel.ApplyOperationEditor();Canvas.InvalidateVisual();await PersistCamChangesAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private async void DeleteProfile_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is { } operation) { ViewModel.DeleteOperation(operation); Canvas.InvalidateVisual(); await PersistCamChangesAsync(); } }
    private async void DuplicateProfile_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is CncCad.Cam.Operations.ProfileOperationDefinition operation)ViewModel.DuplicateOperation(operation);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.EngraveOperationDefinition engraving)ViewModel.DuplicateOperation(engraving);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.VCarveOperationDefinition carve)ViewModel.DuplicateOperation(carve);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.PocketOperationDefinition pocket)ViewModel.DuplicateOperation(pocket);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.DrillOperationDefinition drill)ViewModel.DuplicateOperation(drill);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.SurfaceOperationDefinition surface)ViewModel.DuplicateOperation(surface);else return;Canvas.InvalidateVisual();await PersistCamChangesAsync(); }
    private async void ToggleProfile_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is { } operation) { ViewModel.ToggleOperation(operation); Canvas.InvalidateVisual(); await PersistCamChangesAsync(); } }
    private void RecalculateProfile_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is { } operation) { ViewModel.CalculateOperation(operation); Canvas.InvalidateVisual(); } }
    private void TabEdit_Click(object? s,RoutedEventArgs e){Canvas.TabEditMode=TabEditToggle.IsChecked==true;Canvas.SetTool(CanvasTool.Select);ViewModel.Message=Canvas.TabEditMode?"Click a profile contour to place manual tabs":"Tab placement finished";}
    private void DeleteTab_Click(object? s,RoutedEventArgs e){ViewModel.DeleteLastManualTab();Canvas.InvalidateVisual();}
    private async void MoveProfileUp_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is { } operation) { ViewModel.MoveOperation(operation, -1); await PersistCamChangesAsync(); } }
    private async void MoveProfileDown_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is { } operation) { ViewModel.MoveOperation(operation, 1); await PersistCamChangesAsync(); } }
    private async Task PersistCamChangesAsync()
    {
        if(ViewModel.ProjectPath is null){ViewModel.Message="Toolpath added to this job — save the job to keep it";return;}
        try{await ViewModel.SaveProjectAsync(ViewModel.ProjectPath);ViewModel.Message="Toolpaths saved with job";}catch(Exception ex){ViewModel.Message=$"Toolpath exists in memory, but the job could not be saved: {ex.Message}";}
    }
    private void PreviewGCode_Click(object? s, RoutedEventArgs e) { try { ViewModel.LoadAllPlayback(); Canvas.Toolpaths = ViewModel.Program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings;PreviewCanvas.Toolpaths=ViewModel.Program;PreviewCanvas.Fixtures=ViewModel.CamJob.Fixtures;PreviewCanvas.SafetyFindings=ViewModel.SafetyFindings; SetWorkspaceFocus(WorkspaceFocus.Toolpaths); SetViewportLayout(ViewportLayout.Preview3D); UpdatePlaybackFrame(); ViewModel.Message = "All enabled toolpaths loaded — press Play"; } catch (Exception ex) { ViewModel.Message = ex.Message; } }
    private void PreviewSelected_Click(object? s, RoutedEventArgs e)
    {
        try { ViewModel.LoadSelectedPlayback(); var operation = ViewModel.SelectedOperation!; var program=new CncCad.Cam.Toolpaths.ToolpathProgram { Name = operation.Name, Operations = [operation.CalculatedToolpath!] };Canvas.Toolpaths=program;PreviewCanvas.Toolpaths=program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings;PreviewCanvas.Fixtures=ViewModel.CamJob.Fixtures;PreviewCanvas.SafetyFindings=ViewModel.SafetyFindings; SetWorkspaceFocus(WorkspaceFocus.Toolpaths); SetViewportLayout(ViewportLayout.Preview3D); UpdatePlaybackFrame(); ViewModel.Message = $"{operation.Name} loaded — press Play"; }
        catch (Exception ex) { ViewModel.Message = ex.Message; }
    }
    private async void AddTool_Click(object? s, RoutedEventArgs e) { try { await ViewModel.AddToolAsync(); } catch (Exception ex) { ViewModel.Message = ex.Message; } }
    private async void SaveTool_Click(object? s,RoutedEventArgs e){try{await ViewModel.SaveSelectedToolAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private async void DeleteTool_Click(object? s,RoutedEventArgs e){try{await ViewModel.DeleteSelectedToolAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void UseMachine_Click(object? s, RoutedEventArgs e) { try { ViewModel.SelectMachineSnapshot(); } catch(Exception ex) { ViewModel.Message=ex.Message; } }
    private async void SaveMachine_Click(object? s,RoutedEventArgs e){try{await ViewModel.SaveSelectedMachineAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void AddMachine_Click(object? s,RoutedEventArgs e)=>ViewModel.AddMachineProfile();
    private async void DeleteMachine_Click(object? s,RoutedEventArgs e){try{await ViewModel.DeleteSelectedMachineAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void AddFixture_Click(object? s, RoutedEventArgs e) { ViewModel.AddDefaultFixture(); Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.InvalidateVisual(); }
    private void DeleteFixture_Click(object? s, RoutedEventArgs e) { ViewModel.DeleteSelectedFixture();Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.InvalidateVisual(); }
    private void FixtureField_LostFocus(object? s,RoutedEventArgs e){ViewModel.FixtureChanged();Canvas.InvalidateVisual();}
    private void RunPreflight_Click(object? s, RoutedEventArgs e) { try { ViewModel.RecalculateAll();var result=ViewModel.RunPreflight();ViewModel.Message=$"Preflight: {result.Status} — {result.ErrorCount} errors, {result.WarningCount} warnings";Canvas.SafetyFindings=result.Findings;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.InvalidateVisual(); } catch(Exception ex){ViewModel.Message=ex.Message;} }
    private async void ExportNc_Click(object? s,RoutedEventArgs e)=>await ExportAsync(GCodeExportFormat.All[0]);
    private async void ExportTxt_Click(object? s,RoutedEventArgs e)=>await ExportAsync(GCodeExportFormat.All[1]);
    private async void ExportTap_Click(object? s,RoutedEventArgs e)=>await ExportAsync(GCodeExportFormat.All[2]);
    private async void SaveToolpaths_Click(object? s,RoutedEventArgs e)
    {
        DismissToolTip(s);
        if(!ViewModel.CanSaveToolpaths){ViewModel.Message=ViewModel.ExportAvailabilityMessage;return;}
        try
        {
            var scope=await new SaveToolpathsWindow(ViewModel).ShowDialog<ToolpathExportScope?>(this);if(scope is null)return;var files=ViewModel.GenerateExportPrograms(scope.Value);var warnings=files.Sum(x=>x.WarningCount);if(warnings>0&&!await ConfirmWarningsAsync(warnings))return;
            if(scope==ToolpathExportScope.VisibleSeparateFiles||files.Count>1){var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title="Save generated machine programs",AllowMultiple=false});if(folders.Count==0)return;var paths=files.Select(file=>Path.Combine(folders[0].Path.LocalPath,file.FileStem+file.Extension)).ToArray();var existing=paths.Where(File.Exists).ToArray();if(existing.Length>0&&!await ConfirmOverwriteAsync(existing))return;await AtomicExportBatchWriter.WriteAsync(files.Select((file,index)=>new AtomicExportFile(paths[index],file.Text)));ViewModel.Message=$"Exported {files.Count} machine programs";return;}
            var generated=files[0];var destination=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title="Save machine program",SuggestedFileName=generated.FileStem,DefaultExtension=generated.Extension.TrimStart('.'),FileTypeChoices=[new FilePickerFileType("Machine program"){Patterns=[$"*{generated.Extension}"]}]});if(destination is null)return;var path=GCodeExportFormat.NormalizePath(destination.Path.LocalPath,generated.Extension);if(File.Exists(path)&&!await ConfirmOverwriteAsync([path]))return;await WriteTextAtomicallyAsync(path,generated.Text);ViewModel.Message=$"Exported {Path.GetFileName(path)}";
        }
        catch(Exception ex){ViewModel.Message=$"Could not save toolpaths: {ex.Message}";}
    }
    private async void ExportSheets_Click(object? s,RoutedEventArgs e)
    {
        if(!ViewModel.CanSaveToolpaths){ViewModel.Message=ViewModel.ExportAvailabilityMessage;return;}
        try
        {
            var extension=(s as Button)?.Tag?.ToString()??"nc";var format=GCodeExportFormat.All.First(x=>x.Extension.Equals($".{extension}",StringComparison.OrdinalIgnoreCase));ViewModel.SelectedExportFormat=format;var programs=ViewModel.GenerateSheetPrograms();var warningCount=programs.Sum(x=>x.WarningCount);if(warningCount>0&&!await ConfirmWarningsAsync(warningCount))return;
            var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title=$"Export {format.Extension} material sheet programs",AllowMultiple=false});if(folders.Count==0)return;
            var paths=programs.Select(program=>Path.Combine(folders[0].Path.LocalPath,program.FileStem+program.Extension)).ToArray();
            var existing=paths.Where(File.Exists).ToArray();if(existing.Length>0&&!await ConfirmOverwriteAsync(existing))return;
            await AtomicExportBatchWriter.WriteAsync(programs.Select((program,index)=>new AtomicExportFile(paths[index],program.GCode)));
            ViewModel.Message=$"Exported {programs.Count} material sheet programs as {format.Extension}";
        }
        catch(Exception ex){ViewModel.Message=$"Could not export G-code: {ex.Message}";}
    }
    private async Task ExportAsync(GCodeExportFormat format)
    {
        if(!ViewModel.CanSaveToolpaths){ViewModel.Message=ViewModel.ExportAvailabilityMessage;return;}
        try
        {
            var generated=ViewModel.GenerateExportPrograms(ToolpathExportScope.VisibleCombined);if(generated.Count!=1)throw new InvalidOperationException("This machine requires separate files per tool. Use Save Toolpaths to choose a destination folder.");var program=generated[0];
            if(program.WarningCount>0&&!await ConfirmWarningsAsync(program.WarningCount))return;
            ViewModel.SelectedExportFormat=format;var extension=program.Extension.TrimStart('.');
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export machine program", SuggestedFileName = program.FileStem, DefaultExtension=extension, FileTypeChoices = [new FilePickerFileType("Machine program") { Patterns = [$"*{program.Extension}"] }] });
            if (file is null) return;
            var path=GCodeExportFormat.NormalizePath(file.Path.LocalPath,program.Extension);await WriteTextAtomicallyAsync(path,program.Text);ViewModel.Message=$"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex) { ViewModel.Message = $"Could not export G-code: {ex.Message}"; }
    }
    private async Task<bool> ConfirmWarningsAsync(int count)
    {
        var dialog=new Window{Title="Acknowledge safety warnings",Width=470,Height=230,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.Parse("#181E23"))};
        var result=false;var proceed=new Button{Content="I understand — export",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};var cancel=new Button{Content="Cancel"};
        proceed.Click+=(_,_)=>{result=true;dialog.Close();};cancel.Click+=(_,_)=>dialog.Close();
        dialog.Content=new StackPanel{Margin=new Thickness(22),Spacing=14,Children={new TextBlock{Text=$"Preflight found {count} warning(s).",FontSize=17,FontWeight=Avalonia.Media.FontWeight.Bold},new TextBlock{Text="Review every warning before continuing. Safety checks reduce risk but do not guarantee a crash-free setup.",TextWrapping=Avalonia.Media.TextWrapping.Wrap},new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,Children={cancel,proceed}}}};
        await dialog.ShowDialog(this);return result;
    }
    private async Task<bool> ConfirmOverwriteAsync(IReadOnlyList<string> paths)
    {
        var dialog=new Window{Title="Replace existing G-code files?",Width=500,Height=240,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.Parse("#181E23"))};
        var result=false;var replace=new Button{Content="Replace files",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};var cancel=new Button{Content="Cancel"};replace.Click+=(_,_)=>{result=true;dialog.Close();};cancel.Click+=(_,_)=>dialog.Close();
        dialog.Content=new StackPanel{Margin=new Thickness(22),Spacing=14,Children={new TextBlock{Text=$"{paths.Count} export file{(paths.Count==1?" already exists":"s already exist")}",FontSize=17,FontWeight=Avalonia.Media.FontWeight.Bold},new TextBlock{Text="Replacing them cannot be undone. Choose Cancel to select a different folder.",TextWrapping=Avalonia.Media.TextWrapping.Wrap},new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,Children={cancel,replace}}}};
        await dialog.ShowDialog(this);return result;
    }
    private static async Task WriteTextAtomicallyAsync(string path,string content)
    {
        var fullPath=Path.GetFullPath(path);var directory=Path.GetDirectoryName(fullPath)??Directory.GetCurrentDirectory();Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,$".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try{await File.WriteAllTextAsync(temporary,content,new UTF8Encoding(false));File.Move(temporary,fullPath,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static void DismissToolTip(object? sender)
    {
        if(sender is Control control) ToolTip.SetIsOpen(control,false);
    }
}
