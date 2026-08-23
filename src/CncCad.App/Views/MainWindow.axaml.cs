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
using CncCad.Infrastructure.Settings;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace CncCad.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _playbackClock = new();
    private bool _updatingPlayback;
    private readonly IApplicationPreferencesStore? _preferencesStore;
    private ApplicationPreferences _preferences = ApplicationPreferences.Defaults;
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    public MainWindow() : this(null) { }
    public MainWindow(IApplicationPreferencesStore? preferencesStore) { _preferencesStore=preferencesStore;InitializeComponent(); Loaded += async (_, _) => { ConfigureCanvas();await LoadPreferencesAsync();ConfigurePlatformMenu();}; Closing += OnClosing; KeyDown += OnKeyDown; _playbackTimer.Tick += PlaybackTick; _playbackTimer.Start(); _playbackClock.Start(); }
    private void ConfigureCanvas()
    {
        Canvas.Document = ViewModel.Document; Canvas.Toolpaths = ViewModel.Program;Canvas.Operations=ViewModel.Operations; Canvas.Selection = ViewModel.Selection; Canvas.SnapSettings = ViewModel.SnapSettings;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings;
        Canvas.EntityCreated = entity => { ViewModel.AddEntity(entity); Canvas.InvalidateVisual(); };
        Canvas.SelectionMoved = delta => { ViewModel.MoveSelection(delta); Canvas.InvalidateVisual(); };
        Canvas.SelectionTransformed = (transform,description) => { ViewModel.TransformSelection(transform,description); Canvas.InvalidateVisual(); };
        Canvas.NodeMoved = (entity,index,point) => { ViewModel.MoveNode(entity,index,point); Canvas.InvalidateVisual(); };
        Canvas.TabPlacementRequested=point=>{try{ViewModel.AddManualTabAt(point);Canvas.InvalidateVisual();}catch(Exception ex){ViewModel.Message=ex.Message;}};
        Canvas.TrimRequested=candidate=>{try{ViewModel.ApplyInteractiveTrim(candidate);Canvas.InvalidateVisual();}catch(Exception ex){ViewModel.Message=ex.Message;}};Canvas.TrimStatusChanged=status=>ViewModel.Message=status;
        Canvas.SelectionChanged = ViewModel.SelectionChanged; Canvas.PointerStatusChanged = ViewModel.UpdatePointer; Canvas.InteractionStatusChanged = ViewModel.UpdateInteraction; Canvas.SetTool(CanvasTool.Select);
    }
    private async Task LoadPreferencesAsync()
    {
        if(_preferencesStore is not null)_preferences=await _preferencesStore.LoadAsync();
        ApplyPreferences(_preferences);
    }
    private void ApplyPreferences(ApplicationPreferences p)
    {
        _preferences=p;Canvas.ShowGrid=p.ShowGrid;Canvas.SnappingEnabled=p.SnappingEnabled;Canvas.ShowToolpaths=p.ShowToolpaths;
        ViewModel.SnapSettings.Grid=p.SnapToGrid;ViewModel.SnapSettings.Endpoint=p.SnapToEndpoints;ViewModel.SnapSettings.Midpoint=p.SnapToMidpoints;ViewModel.SnapSettings.Center=p.SnapToCenters;ViewModel.SnapSettings.GridSpacingMm=p.GridSpacingMm;
        GridToggle.IsChecked=p.ShowGrid;SnapToggle.IsChecked=p.SnappingEnabled;PathsToggle.IsChecked=p.ShowToolpaths;
        SetWorkspaceFocus(p.StartupWorkspace==StartupWorkspace.Design?WorkspaceFocus.Design:WorkspaceFocus.Toolpaths,p.StartupWorkspace==StartupWorkspace.Design?300:0,p.StartupWorkspace==StartupWorkspace.Toolpaths?410:0);
        Resources["CncAccentBrush"]=new SolidColorBrush(p.Accent==AccentPreference.System?Color.Parse("#4CC2FF"):Color.Parse("#65C9FF"));Canvas.InvalidateVisual();
    }
    private void ConfigurePlatformMenu(){ApplicationMenuBar.IsVisible=!OperatingSystem.IsMacOS();if(OperatingSystem.IsMacOS())NativeDock.SetMenu(this,BuildNativeMenu());}
    private NativeMenu BuildNativeMenu()
    {
        NativeMenuItem Item(string label,Action action,KeyGesture? gesture=null,Func<bool>? enabled=null){var command=new ApplicationCommand(label,label,action,enabled,gesture);return new NativeMenuItem(label){Gesture=gesture,Command=command};}
        NativeMenuItem Root(string label,params NativeMenuItemBase[] children){var menu=new NativeMenu();foreach(var child in children)menu.Add(child);return new NativeMenuItem(label){Menu=menu};}
        var menu=new NativeMenu();
        var recentMenu=new NativeMenu();void RefreshRecent(){recentMenu.Items.Clear();foreach(var project in ViewModel.RecentProjects){var path=project.Path;recentMenu.Add(Item(project.Name,()=>_=OpenRecentPathAsync(path)));}if(ViewModel.RecentProjects.Count>0)recentMenu.Add(new NativeMenuItemSeparator());recentMenu.Add(Item("Clear Recent Projects",()=>_=ClearRecentProjectsAsync()));}recentMenu.NeedsUpdate+=(_,_)=>RefreshRecent();RefreshRecent();var recentRoot=new NativeMenuItem("Open Recent"){Menu=recentMenu};
        menu.Add(Root("CNC CAD",Item("About CNC CAD",()=>About_Click(null,new RoutedEventArgs())),Item("Settings…",()=>Settings_Click(null,new RoutedEventArgs()),new(Key.OemComma,KeyModifiers.Meta)),new NativeMenuItemSeparator(),Item("Quit CNC CAD",Close,new(Key.Q,KeyModifiers.Meta))));
        menu.Add(Root("File",Item("New Job",()=>NewJob_Click(null,new RoutedEventArgs()),new(Key.N,KeyModifiers.Meta)),Item("Open…",()=>Open_Click(null,new RoutedEventArgs()),new(Key.O,KeyModifiers.Meta)),recentRoot,Item("Save",()=>Save_Click(null,new RoutedEventArgs()),new(Key.S,KeyModifiers.Meta)),Item("Save As…",()=>SaveAs_Click(null,new RoutedEventArgs()),new(Key.S,KeyModifiers.Meta|KeyModifiers.Shift)),Item("Import SVG/DXF…",()=>Import_Click(null,new RoutedEventArgs())),Item("Export .nc…",()=>ExportNc_Click(null,new RoutedEventArgs())),Item("Export .txt…",()=>ExportTxt_Click(null,new RoutedEventArgs())),Item("Export .tap…",()=>ExportTap_Click(null,new RoutedEventArgs()))));
        menu.Add(Root("Edit",Item("Undo",()=>Undo_Click(null,new RoutedEventArgs()),new(Key.Z,KeyModifiers.Meta)),Item("Redo",()=>Redo_Click(null,new RoutedEventArgs()),new(Key.Z,KeyModifiers.Meta|KeyModifiers.Shift)),Item("Copy",()=>Copy_Click(null,new RoutedEventArgs()),new(Key.C,KeyModifiers.Meta)),Item("Paste",()=>Paste_Click(null,new RoutedEventArgs()),new(Key.V,KeyModifiers.Meta)),Item("Duplicate",()=>Duplicate_Click(null,new RoutedEventArgs()),new(Key.D,KeyModifiers.Meta)),Item("Delete",()=>Delete_Click(null,new RoutedEventArgs())),Item("Select All",()=>SelectAll_Click(null,new RoutedEventArgs()),new(Key.A,KeyModifiers.Meta))));
        menu.Add(Root("View",Item("Design Focus",()=>DesignFocus_Click(null,new RoutedEventArgs())),Item("Toolpaths Focus",()=>ToolpathFocus_Click(null,new RoutedEventArgs())),Item("2D Design",()=>DesignView_Click(null,new RoutedEventArgs())),Item("Toolpath Preview",()=>ToolpathView_Click(null,new RoutedEventArgs())),Item("Zoom to Fit",()=>ZoomFit_Click(null,new RoutedEventArgs()),new(Key.F,KeyModifiers.None)),Item("Zoom to Selection",()=>ZoomSelection_Click(null,new RoutedEventArgs()))));
        menu.Add(Root("Design",Item("Select / Move",()=>SetDrawingTool(CanvasTool.Select)),Item("Line",()=>SetDrawingTool(CanvasTool.Line)),Item("Polyline",()=>SetDrawingTool(CanvasTool.Polyline)),Item("Rectangle",()=>SetDrawingTool(CanvasTool.Rectangle)),Item("Circle",()=>SetDrawingTool(CanvasTool.Circle)),Item("Three-Point Arc",()=>SetDrawingTool(CanvasTool.Arc)),Item("Scissors Trim",()=>TrimTool_Click(null,new RoutedEventArgs()))));
        menu.Add(Root("Toolpaths",Item("Create Profile…",()=>CreateProfile_Click(null,new RoutedEventArgs())),Item("Create Engraving…",()=>CreateEngrave_Click(null,new RoutedEventArgs())),Item("Create V-Carve…",()=>CreateVCarve_Click(null,new RoutedEventArgs())),Item("Run Preflight",()=>RunPreflight_Click(null,new RoutedEventArgs())),Item("Preview Selected",()=>PreviewSelected_Click(null,new RoutedEventArgs())),Item("Preview All",()=>PreviewGCode_Click(null,new RoutedEventArgs()))));
        menu.Add(Root("Help",Item("Keyboard Shortcuts…",()=>Shortcuts_Click(null,new RoutedEventArgs())),Item("About CNC CAD",()=>About_Click(null,new RoutedEventArgs()))));return menu;
    }
    private void DrawingToolPalette_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox { SelectedItem: ToolPaletteItem item }) return;
        Canvas.SetTool(item.Tool);
        ViewModel.SetActiveToolGuidance(item.Tool);
        ViewModel.Message = item.Tool switch { CanvasTool.Select => "Select tool — click, marquee, or drag a selection", CanvasTool.Line => "Line tool — click start and end", CanvasTool.Polyline => "Polyline — click points, Enter to finish, C to close", CanvasTool.Rectangle => "Rectangle tool — drag corner to corner", CanvasTool.Circle => "Circle tool — click center and radius", _ => "3-point arc — click start, curve point, and end" };
    }
    private void ZoomFit_Click(object? s, RoutedEventArgs e) { if (Canvas.ToolpathPreviewMode) Canvas.Fit3D(); else Canvas.ZoomToFit(); }
    private void ZoomSelection_Click(object? s, RoutedEventArgs e) => Canvas.ZoomToSelection();
    private void SelectTool_Click(object? s, RoutedEventArgs e) { DrawingToolPalette.SelectedItem = null; Canvas.SetTool(CanvasTool.Select); ViewModel.ActivePaletteItem = null; ViewModel.SetActiveToolGuidance(CanvasTool.Select); }
    private void SetDrawingTool(CanvasTool tool){if(tool==CanvasTool.Select){SelectTool_Click(null,new RoutedEventArgs());return;}var item=ViewModel.CreationTools.FirstOrDefault(x=>x.Tool==tool);if(item is not null)DrawingToolPalette.SelectedItem=item;}
    private void DrawingToolMenu_Click(object? sender,RoutedEventArgs e){if(sender is MenuItem{Tag:string value}&&Enum.TryParse<CanvasTool>(value,out var tool))SetDrawingTool(tool);}
    private void CenterSelection_Click(object? s, RoutedEventArgs e) { ViewModel.CenterSelectionOnMaterial(); Canvas.InvalidateVisual(); }
    private void GridToggle_Click(object? s, RoutedEventArgs e) { Canvas.ShowGrid = GridToggle.IsChecked == true; Canvas.InvalidateVisual(); }
    private void SnapToggle_Click(object? s, RoutedEventArgs e) { Canvas.SnappingEnabled = SnapToggle.IsChecked == true; Canvas.InvalidateVisual(); }
    private void PathsToggle_Click(object? s, RoutedEventArgs e) { Canvas.ShowToolpaths = PathsToggle.IsChecked == true; Canvas.InvalidateVisual(); }
    private void GridMenu_Click(object? s,RoutedEventArgs e){GridToggle.IsChecked=!GridToggle.IsChecked;GridToggle_Click(s,e);}
    private void SnapMenu_Click(object? s,RoutedEventArgs e){SnapToggle.IsChecked=!SnapToggle.IsChecked;SnapToggle_Click(s,e);}
    private void PathsMenu_Click(object? s,RoutedEventArgs e){PathsToggle.IsChecked=!PathsToggle.IsChecked;PathsToggle_Click(s,e);}
    private void CameraPresetMenu_Click(object? sender,RoutedEventArgs e){if(sender is MenuItem{Tag:string tag}&&Enum.TryParse<CameraPreset>(tag,out var preset)){Canvas.SetCameraPreset(preset);ViewModel.CameraView=preset.ToString();}}
    private void DesignView_Click(object? s, RoutedEventArgs e) { SetWorkspaceView(WorkspaceView.Design); }
    private void ToolpathView_Click(object? s, RoutedEventArgs e) { SetWorkspaceView(WorkspaceView.ToolpathPreview); }
    private void SetWorkspaceView(WorkspaceView view) { ViewModel.SetWorkspaceView(view); Canvas.ToolpathPreviewMode = view == WorkspaceView.ToolpathPreview; if (Canvas.ToolpathPreviewMode) Canvas.Enter3DPreview(); CenterWorkspaceGrid.RowDefinitions[2].Height=new GridLength(ViewModel.ShowPlaybackControls?86:0); DesignViewButton.IsChecked = view == WorkspaceView.Design; PreviewViewButton.IsChecked = view == WorkspaceView.ToolpathPreview; Canvas.InvalidateVisual(); }
    private void CameraPreset_Click(object? sender, RoutedEventArgs e) { if (sender is Button { Tag: string tag } && Enum.TryParse<CameraPreset>(tag, out var preset)) { Canvas.SetCameraPreset(preset); ViewModel.CameraView = preset.ToString(); } }
    private void DesignFocus_Click(object? s, RoutedEventArgs e) => SetWorkspaceFocus(WorkspaceFocus.Design, 300, 0);
    private void ToolpathFocus_Click(object? s, RoutedEventArgs e) => SetWorkspaceFocus(WorkspaceFocus.Toolpaths, 0, 410);
    private void SetWorkspaceFocus(WorkspaceFocus focus, double designWidth, double toolpathWidth) { ViewModel.SetWorkspaceFocus(focus); WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(designWidth); WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(toolpathWidth); }
    private void PlaybackTick(object? sender, EventArgs e)
    {
        var elapsed = _playbackClock.Elapsed; _playbackClock.Restart(); ViewModel.Playback.Advance(elapsed); UpdatePlaybackFrame();
    }
    private void UpdatePlaybackFrame() { _updatingPlayback = true; ViewModel.UpdatePlaybackStatus(); CenterWorkspaceGrid.RowDefinitions[2].Height=new GridLength(ViewModel.ShowPlaybackControls?86:0); Canvas.PlaybackFrame = ViewModel.Playback.Frame; Canvas.InvalidateVisual(); _updatingPlayback = false; }
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
        var dialog=new Window{Title="Import vectors",Width=520,Height=430,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.Parse("#181E23"))};var scale=new NumericUpDown{Value=1,Minimum=.000001m,Maximum=100000};var x=new NumericUpDown{Value=0};var y=new NumericUpDown{Value=0};var layers=new CheckBox{Content="Preserve DXF layers / SVG groups",IsChecked=true};var warnings=string.Join("\n",preview.Warnings.Select(w=>"• "+w.Message).Concat(preview.Unsupported.Select(u=>"• Unsupported: "+u)));var result=((double,double,double,bool)?)null;var import=new Button{Content="Import",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};var cancel=new Button{Content="Cancel"};import.Click+=(_,_)=>{result=((double)(scale.Value??1),(double)(x.Value??0),(double)(y.Value??0),layers.IsChecked==true);dialog.Close();};cancel.Click+=(_,_)=>dialog.Close();
        dialog.Content=new StackPanel{Margin=new Thickness(22),Spacing=10,Children={new TextBlock{Text=$"{preview.Entities.Count} supported vectors · detected units: {preview.DetectedUnit}",FontSize=17,FontWeight=FontWeight.Bold},new TextBlock{Text=$"Bounds: {preview.Bounds.Width:0.###} × {preview.Bounds.Height:0.###} mm",Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},new TextBlock{Text="Scale override"},scale,new Grid{ColumnDefinitions=new("*,*"),ColumnSpacing=8,Children={new StackPanel{Children={new TextBlock{Text="Target X (mm)"},x}},new StackPanel{[Grid.ColumnProperty]=1,Children={new TextBlock{Text="Target Y (mm)"},y}}}},layers,new TextBlock{Text=string.IsNullOrWhiteSpace(warnings)?"No unsupported content detected.":warnings,MaxHeight=120,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse("#E8B85C"))},new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,Children={cancel,import}}}};await dialog.ShowDialog(this);return result;
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
        if (!await ConfirmSafeToReplaceAsync()) return;
        var settings = await new JobSetupWindow().ShowDialog<CncCad.Core.Documents.JobSettings?>(this);
        if (settings is null) return;
        ViewModel.CreateJob(settings); Canvas.Document = ViewModel.Document; Canvas.Toolpaths = ViewModel.Program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings; Canvas.Reset3DView(); Canvas.ZoomToFit(); Canvas.InvalidateVisual();
        await SaveAsAsync();
    }
    private async void EditJob_Click(object? s, RoutedEventArgs e)
    {
        var dialog = new JobSetupWindow();
        var vm = (JobSetupViewModel)dialog.DataContext!;
        vm.JobName = ViewModel.Document.Job.Name; vm.WidthMm = ViewModel.Document.Job.WidthMm; vm.HeightMm = ViewModel.Document.Job.HeightMm; vm.ThicknessMm = ViewModel.Document.Job.ThicknessMm; vm.SafeZMm = ViewModel.Document.Job.SafeZMm; vm.Origin = ViewModel.Document.Job.Origin;vm.OriginOffsetXmm=ViewModel.Document.Job.OriginOffsetXmm;vm.OriginOffsetYmm=ViewModel.Document.Job.OriginOffsetYmm; vm.ZZero = ViewModel.Document.Job.ZZero;
        var settings = await dialog.ShowDialog<CncCad.Core.Documents.JobSettings?>(this);
        if (settings is null) return;
        ViewModel.UpdateJob(settings); Canvas.Reset3DView(); Canvas.ZoomToFit();
    }
    private async void Save_Click(object? s, RoutedEventArgs e) { if (ViewModel.ProjectPath is null) await SaveAsAsync(); else await ViewModel.SaveProjectAsync(ViewModel.ProjectPath); }
    private async void SaveAs_Click(object? s, RoutedEventArgs e) => await SaveAsAsync();
    private async Task<bool> SaveAsAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save CNC Job", SuggestedFileName = $"{ViewModel.Document.Job.Name}.cnccad", DefaultExtension = "cnccad", FileTypeChoices = [new FilePickerFileType("CncCad project") { Patterns = ["*.cnccad"] }] });
        if (file is not null) await ViewModel.SaveProjectAsync(file.Path.LocalPath);
        else ViewModel.Message = "Job created but not yet saved";
        return file is not null;
    }
    private async void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmSafeToReplaceAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open CNC Job", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("CncCad project") { Patterns = ["*.cnccad"] }] });
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
    private async Task OpenRecentPathAsync(string path){if(!await ConfirmSafeToReplaceAsync())return;if(!File.Exists(path)){await ViewModel.RemoveRecentProjectAsync(path);ViewModel.Message="That recent project no longer exists.";return;}await OpenProjectAsync(path);}
    private async Task ClearRecentProjectsAsync(){await ViewModel.ClearRecentProjectsAsync();ViewModel.Message="Recent projects cleared";}
    private async void ClearRecent_Click(object? sender,RoutedEventArgs e)=>await ClearRecentProjectsAsync();
    private async void Settings_Click(object? sender,RoutedEventArgs e)
    {
        var result=await new SettingsWindow(_preferences).ShowDialog<ApplicationPreferences?>(this);if(result is null)return;
        if(_preferencesStore is not null)await _preferencesStore.SaveAsync(result);ApplyPreferences(result);ViewModel.Message="Application settings saved";
    }
    private void About_Click(object? sender,RoutedEventArgs e)=>new AboutWindow().ShowDialog(this);
    private void Shortcuts_Click(object? sender,RoutedEventArgs e)=>new KeyboardShortcutsWindow().ShowDialog(this);
    private void Exit_Click(object? sender,RoutedEventArgs e)=>Close();
    private async Task OpenProjectAsync(string path)
    {
        try { await ViewModel.OpenProjectAsync(path); Canvas.Document = ViewModel.Document; Canvas.Toolpaths = ViewModel.Program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings; Canvas.Reset3DView(); Canvas.ZoomToFit(); Canvas.InvalidateVisual(); }
        catch (Exception ex) { ViewModel.Message = $"Could not open project: {ex.Message}"; }
    }
    private async Task<bool> ConfirmSafeToReplaceAsync()
    {
        if (!ViewModel.HasJob || !ViewModel.IsDirty) return true;
        var choice = await new UnsavedChangesWindow(ViewModel.JobTitle).ShowDialog<UnsavedChoice>(this);
        if (choice == UnsavedChoice.Cancel) return false;
        if (choice == UnsavedChoice.Discard) return true;
        if (ViewModel.ProjectPath is null) return await SaveAsAsync();
        await ViewModel.SaveProjectAsync(ViewModel.ProjectPath); return true;
    }
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || !ViewModel.HasJob || !ViewModel.IsDirty) return;
        e.Cancel = true;
        if (!await ConfirmSafeToReplaceAsync()) return;
        _closeApproved = true; Close();
    }
    private async void CreateProfile_Click(object? s, RoutedEventArgs e)
    {
        try { var draft = ViewModel.CreateProfileDraft(); var result = await new ProfileOperationWindow(draft, ViewModel.Tools, ViewModel.Document.Job.ThicknessMm).ShowDialog<CncCad.Cam.Operations.ProfileOperationDefinition?>(this); if (result is not null) { ViewModel.AddOperation(result); Canvas.InvalidateVisual(); await PersistCamChangesAsync(); } }
        catch (Exception ex) { ViewModel.Message = ex.Message; }
    }
    private async void CreateEngrave_Click(object? s,RoutedEventArgs e){try{var draft=ViewModel.CreateEngraveDraft();var result=await new EngraveOperationWindow(draft,ViewModel.Tools).ShowDialog<CncCad.Cam.Operations.EngraveOperationDefinition?>(this);if(result is null)return;ViewModel.AddOperation(result);Canvas.InvalidateVisual();await PersistCamChangesAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private async void CreateVCarve_Click(object? s,RoutedEventArgs e){try{var draft=ViewModel.CreateVCarveDraft();var result=await new VCarveOperationWindow(draft,ViewModel.Tools).ShowDialog<CncCad.Cam.Operations.VCarveOperationDefinition?>(this);if(result is null)return;ViewModel.AddOperation(result);Canvas.InvalidateVisual();await PersistCamChangesAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private async void EditProfile_Click(object? s, RoutedEventArgs e)
    {
        if(ViewModel.SelectedOperation is CncCad.Cam.Operations.ProfileOperationDefinition operation){var result=await new ProfileOperationWindow(operation,ViewModel.Tools,ViewModel.Document.Job.ThicknessMm).ShowDialog<CncCad.Cam.Operations.ProfileOperationDefinition?>(this);if(result is not null)ViewModel.UpdateOperation(operation,result);else return;}else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.EngraveOperationDefinition engraving){var result=await new EngraveOperationWindow(engraving,ViewModel.Tools).ShowDialog<CncCad.Cam.Operations.EngraveOperationDefinition?>(this);if(result is not null)ViewModel.UpdateOperation(engraving,result);else return;}else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.VCarveOperationDefinition carve){var result=await new VCarveOperationWindow(carve,ViewModel.Tools).ShowDialog<CncCad.Cam.Operations.VCarveOperationDefinition?>(this);if(result is not null)ViewModel.UpdateOperation(carve,result);else return;}else return;Canvas.InvalidateVisual();await PersistCamChangesAsync();
    }
    private async void DeleteProfile_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is { } operation) { ViewModel.DeleteOperation(operation); Canvas.InvalidateVisual(); await PersistCamChangesAsync(); } }
    private async void DuplicateProfile_Click(object? s, RoutedEventArgs e) { if (ViewModel.SelectedOperation is CncCad.Cam.Operations.ProfileOperationDefinition operation)ViewModel.DuplicateOperation(operation);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.EngraveOperationDefinition engraving)ViewModel.DuplicateOperation(engraving);else if(ViewModel.SelectedOperation is CncCad.Cam.Operations.VCarveOperationDefinition carve)ViewModel.DuplicateOperation(carve);else return;Canvas.InvalidateVisual();await PersistCamChangesAsync(); }
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
    private void PreviewGCode_Click(object? s, RoutedEventArgs e) { try { ViewModel.LoadAllPlayback(); Canvas.Toolpaths = ViewModel.Program;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings; SetWorkspaceFocus(WorkspaceFocus.Toolpaths, 0, 410); SetWorkspaceView(WorkspaceView.ToolpathPreview); UpdatePlaybackFrame(); ViewModel.Message = "All enabled toolpaths loaded — press Play"; } catch (Exception ex) { ViewModel.Message = ex.Message; } }
    private void PreviewSelected_Click(object? s, RoutedEventArgs e)
    {
        try { ViewModel.LoadSelectedPlayback(); var operation = ViewModel.SelectedOperation!; Canvas.Toolpaths = new CncCad.Cam.Toolpaths.ToolpathProgram { Name = operation.Name, Operations = [operation.CalculatedToolpath!] };Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.SafetyFindings=ViewModel.SafetyFindings; SetWorkspaceFocus(WorkspaceFocus.Toolpaths, 0, 410); SetWorkspaceView(WorkspaceView.ToolpathPreview); UpdatePlaybackFrame(); ViewModel.Message = $"{operation.Name} loaded — press Play"; }
        catch (Exception ex) { ViewModel.Message = ex.Message; }
    }
    private async void AddTool_Click(object? s, RoutedEventArgs e) { try { await ViewModel.AddToolAsync(); } catch (Exception ex) { ViewModel.Message = ex.Message; } }
    private void UseMachine_Click(object? s, RoutedEventArgs e) { try { ViewModel.SelectMachineSnapshot(); } catch(Exception ex) { ViewModel.Message=ex.Message; } }
    private async void SaveMachine_Click(object? s,RoutedEventArgs e){try{await ViewModel.SaveSelectedMachineAsync();}catch(Exception ex){ViewModel.Message=ex.Message;}}
    private void AddFixture_Click(object? s, RoutedEventArgs e) { ViewModel.AddDefaultFixture(); Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.InvalidateVisual(); }
    private void DeleteFixture_Click(object? s, RoutedEventArgs e) { ViewModel.DeleteSelectedFixture();Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.InvalidateVisual(); }
    private void FixtureField_LostFocus(object? s,RoutedEventArgs e){ViewModel.FixtureChanged();Canvas.InvalidateVisual();}
    private void RunPreflight_Click(object? s, RoutedEventArgs e) { try { ViewModel.RecalculateAll();var result=ViewModel.RunPreflight();ViewModel.Message=$"Preflight: {result.Status} — {result.ErrorCount} errors, {result.WarningCount} warnings";Canvas.SafetyFindings=result.Findings;Canvas.Fixtures=ViewModel.CamJob.Fixtures;Canvas.InvalidateVisual(); } catch(Exception ex){ViewModel.Message=ex.Message;} }
    private async void ExportNc_Click(object? s,RoutedEventArgs e)=>await ExportAsync(GCodeExportFormat.All[0]);
    private async void ExportTxt_Click(object? s,RoutedEventArgs e)=>await ExportAsync(GCodeExportFormat.All[1]);
    private async void ExportTap_Click(object? s,RoutedEventArgs e)=>await ExportAsync(GCodeExportFormat.All[2]);
    private async void ExportSheets_Click(object? s,RoutedEventArgs e)
    {
        try{var extension=(s as Button)?.Tag?.ToString()??"nc";var format=GCodeExportFormat.All.First(x=>x.Extension.Equals($".{extension}",StringComparison.OrdinalIgnoreCase));ViewModel.SelectedExportFormat=format;var programs=ViewModel.GenerateSheetPrograms();var warningCount=programs.Sum(x=>x.WarningCount);if(warningCount>0&&!await ConfirmWarningsAsync(warningCount))return;var folders=await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions{Title=$"Export {format.Extension} material sheet programs",AllowMultiple=false});if(folders.Count==0)return;for(var i=0;i<programs.Count;i++){var name=$"{ViewModel.Document.Job.Name}-Sheet-{i+1:00}{format.Extension}";var path=Path.Combine(folders[0].Path.LocalPath,name);await File.WriteAllTextAsync(path,programs[i].GCode);}ViewModel.Message=$"Exported {programs.Count} material sheet programs as {format.Extension}";}catch(Exception ex){ViewModel.Message=ex.Message;}
    }
    private async Task ExportAsync(GCodeExportFormat format)
    {
        try
        {
            ViewModel.PreviewGCode();
            if(ViewModel.LastPreflight.WarningCount>0&&!await ConfirmWarningsAsync(ViewModel.LastPreflight.WarningCount))return;
            ViewModel.SelectedExportFormat=format;var extension=format.Extension.TrimStart('.');
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = $"Export {format.Label}", SuggestedFileName = $"cnc-program{format.Extension}", DefaultExtension=extension, FileTypeChoices = [new FilePickerFileType(format.Label) { Patterns = [$"*{format.Extension}"] }] });
            if (file is null) return;
            var path=format.NormalizePath(file.Path.LocalPath);await using var stream=File.Create(path);await using var writer=new StreamWriter(stream);await writer.WriteAsync(ViewModel.GCodePreview);ViewModel.Message=$"Exported {Path.GetFileName(path)}";
        }
        catch (Exception ex) { ViewModel.Message = ex.Message; }
    }
    private async Task<bool> ConfirmWarningsAsync(int count)
    {
        var dialog=new Window{Title="Acknowledge safety warnings",Width=470,Height=230,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.Parse("#181E23"))};
        var result=false;var proceed=new Button{Content="I understand — export",HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right};var cancel=new Button{Content="Cancel"};
        proceed.Click+=(_,_)=>{result=true;dialog.Close();};cancel.Click+=(_,_)=>dialog.Close();
        dialog.Content=new StackPanel{Margin=new Thickness(22),Spacing=14,Children={new TextBlock{Text=$"Preflight found {count} warning(s).",FontSize=17,FontWeight=Avalonia.Media.FontWeight.Bold},new TextBlock{Text="Review every warning before continuing. Safety checks reduce risk but do not guarantee a crash-free setup.",TextWrapping=Avalonia.Media.TextWrapping.Wrap},new StackPanel{Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,Children={cancel,proceed}}}};
        await dialog.ShowDialog(this);return result;
    }
}
