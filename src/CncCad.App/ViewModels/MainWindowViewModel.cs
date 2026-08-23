using System.Collections.ObjectModel;
using CncCad.Cam.Profile;
using CncCad.Cam.Operations;
using CncCad.Cam.Tools;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Safety;
using CncCad.Cam.Nesting;
using CncCad.Core.Documents;
using CncCad.Core.Commands;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Selection;
using CncCad.Core.Snapping;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using CncCad.IO.Projects;
using CncCad.IO.Import;
using CncCad.PostProcessors.Models;
using CncCad.PostProcessors.Uccnc;
using CncCad.Rendering.Canvas;
using CncCad.Simulation.MachineTime;
using CncCad.Simulation.Playback;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncCad.App.ViewModels;

public enum WorkspaceFocus { Balanced, Design, Toolpaths }
public enum WorkspaceView { Design, ToolpathPreview }

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IToolLibrary _toolLibrary;
    private readonly IRecentProjectStore _recentProjectStore;
    private readonly IMachineProfileRepository? _machineProfiles;
    private readonly ISafetyPreflightService _safetyPreflight = new SafetyPreflightService();
    private readonly ICamOperationCalculator _operationCalculator = new CompositeOperationCalculator(new ProfileOperationCalculator(),new EngraveOperationCalculator(),new VCarveOperationCalculator());
    private readonly ICadOffsetService _offsetService = new CadOffsetService();
    private readonly UccncPostProcessor _postProcessor = new();
    private readonly JsonProjectSerializer _projectSerializer = new();
    private readonly IVectorImportService _vectorImporter = new CompositeVectorImportService([new SvgVectorImporter(),new DxfVectorImporter()]);
    private readonly IVectorRepairService _repairService = new VectorRepairService();
    private readonly IVectorTopologyService _topologyService = new VectorTopologyService();
    private readonly ITextOutlineService _textOutlineService = new PortableFallbackTextOutlineService();
    private readonly IFontCatalog? _fontCatalog;
    private readonly INestingOptimizer _nestingOptimizer=new DeterministicNestingOptimizer();
    private readonly CommandHistory _history = new();
    private readonly List<CadEntity> _clipboard = [];
    private int _pasteCount;
    public CadDocument Document { get; } = new();
    public CamJob CamJob { get; private set; } = new();
    public NestingJobDefinition NestingJob { get; private set; }=new();
    public ToolpathProgram Program { get; } = new() { Name = "Untitled job" };
    public ObservableCollection<CncTool> Tools { get; } = [];
    public ObservableCollection<MachineProfileDefinition> Machines { get; } = [];
    public ObservableCollection<FixtureDefinition> Fixtures { get; } = [];
    public ObservableCollection<SafetyFinding> SafetyFindings { get; } = [];
    public ObservableCollection<CamOperationDefinition> Operations { get; } = [];
    public ObservableCollection<RecentProject> RecentProjects { get; } = [];
    public ObservableCollection<CadLayer> Layers { get; } = [];
    public ObservableCollection<RepairIssue> RepairIssues { get; }=[];
    public ObservableCollection<NestPartDefinition> NestParts { get; }=[];
    public ObservableCollection<MaterialSheetDefinition> MaterialSheets { get; }=[];
    public ObservableCollection<FontCatalogEntry> Fonts { get; }=[];
    public ObservableCollection<string> RecentColors { get; }=[];
    public IReadOnlyList<string> PaletteColors{get;}=["#65C9FF","#FFD84D","#FF6B6B","#70E08D","#B98CFF","#FFFFFF","#FF8C42","#38D6C5"];
    public IReadOnlyList<ToolPaletteItem> DrawingTools { get; } = ToolPaletteItem.DefaultDrawingTools;
    public IReadOnlyList<ToolPaletteItem> CreationTools { get; } = ToolPaletteItem.DefaultDrawingTools.Where(item => item.Tool is not CanvasTool.Select and not CanvasTool.Trim).ToArray();
    public SelectionModel Selection { get; } = new();
    public SnapSettings SnapSettings { get; } = new();
    public ToolpathPlaybackSession Playback { get; } = new();
    public IReadOnlyList<double> PlaybackSpeeds => ToolpathPlaybackSession.SupportedSpeeds;
    public IReadOnlyList<GCodeExportFormat> ExportFormats => GCodeExportFormat.All;
    [ObservableProperty] private CncTool? selectedTool;
    [ObservableProperty] private CadLayer? selectedLayer;
    [ObservableProperty] private MachineProfileDefinition? selectedMachine;
    [ObservableProperty] private FixtureDefinition? selectedFixture;
    [ObservableProperty] private SafetyFinding? selectedSafetyFinding;
    [ObservableProperty] private string preflightStatus = "Not checked";
    [ObservableProperty] private string preflightSummary = "Run preflight before preview or export.";
    [ObservableProperty] private bool hasPreflightErrors;
    [ObservableProperty] private bool hasPreflightWarnings;
    public SafetyPreflightResult LastPreflight { get; private set; } = SafetyPreflightResult.NotChecked;
    [ObservableProperty] private string pointerStatus = "X 0.000   Y 0.000";
    [ObservableProperty] private string zoomStatus = "100%";
    [ObservableProperty] private string message = "Ready — draw a rectangle to begin";
    [ObservableProperty] private string gCodePreview = "Create a profile toolpath to preview G-code.";
    [ObservableProperty] private double cutDepthMm = 18;
    [ObservableProperty] private double passDepthMm = 3;
    [ObservableProperty] private bool hasJob;
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string jobTitle = "No job open";
    [ObservableProperty] private string selectionStatus = "Nothing selected";
    [ObservableProperty] private bool canUndo;
    [ObservableProperty] private bool canRedo;
    [ObservableProperty] private double selectionX;
    [ObservableProperty] private double selectionY;
    [ObservableProperty] private string interactionStatus = "Snap: none";
    [ObservableProperty] private bool hasSelection;
    [ObservableProperty] private bool canAlign;
    [ObservableProperty] private bool canDistribute;
    [ObservableProperty] private ToolPaletteItem? activePaletteItem;
    [ObservableProperty] private CamOperationDefinition? selectedOperation;
    [ObservableProperty] private string workflowStage = "1  Job Setup";
    [ObservableProperty] private string designGuidance = "Create a job to begin drawing.";
    [ObservableProperty] private string profileGuidance = "Create a job, then select closed vectors.";
    [ObservableProperty] private bool canCreateProfile;
    [ObservableProperty] private bool hasGeometry;
    [ObservableProperty] private bool hasOperations;
    [ObservableProperty] private bool hasClipboard;
    [ObservableProperty] private WorkspaceFocus workspaceFocus = WorkspaceFocus.Design;
    [ObservableProperty] private WorkspaceView activeWorkspaceView;
    [ObservableProperty] private bool showEmptyDesign;
    [ObservableProperty] private bool isSelectActive = true;
    [ObservableProperty] private double playbackProgress;
    [ObservableProperty] private double playbackSpeed = 5;
    [ObservableProperty] private string playbackTime = "00:00 / 00:00";
    [ObservableProperty] private string playbackSessionLabel = "No preview loaded";
    [ObservableProperty] private string playbackPosition = "X 0.000  Y 0.000  Z 0.000";
    [ObservableProperty] private string playbackDepth = "Material top · 0% depth";
    [ObservableProperty] private string cameraView = "Isometric";
    [ObservableProperty] private string playbackAction = "Play";
    [ObservableProperty] private bool hasPlayback;
    [ObservableProperty] private bool isToolpathPreview;
    [ObservableProperty] private bool showPlaybackControls;
    [ObservableProperty] private GCodeExportFormat selectedExportFormat = GCodeExportFormat.All[0];
    [ObservableProperty] private MaterialSheetDefinition? selectedMaterialSheet;
    [ObservableProperty] private NestPartDefinition? selectedNestPart;
    [ObservableProperty] private string nestingSummary="Select closed vectors to define parts.";
    [ObservableProperty] private int nestQuantity=1;
    [ObservableProperty] private bool nestAllowQuarterTurn;
    [ObservableProperty] private double nestSpacing=5;
    [ObservableProperty] private double nestEdgeMargin=10;
    [ObservableProperty] private bool showExactLine;
    [ObservableProperty] private bool showExactPolyline;
    [ObservableProperty] private bool showExactRectangle;
    [ObservableProperty] private bool showExactCircle;
    [ObservableProperty] private bool showExactArc;
    [ObservableProperty] private double point1X;
    [ObservableProperty] private double point1Y;
    [ObservableProperty] private double point2X = 100;
    [ObservableProperty] private double point2Y = 100;
    [ObservableProperty] private double point3X = 50;
    [ObservableProperty] private double point3Y = 50;
    [ObservableProperty] private double exactWidth = 100;
    [ObservableProperty] private double exactHeight = 100;
    [ObservableProperty] private double exactRadius = 25;
    [ObservableProperty] private bool rectangleFromCenter;
    [ObservableProperty] private bool circleUsesDiameter;
    [ObservableProperty] private bool exactUsesLengthAngle;
    [ObservableProperty] private double exactLength = 100;
    [ObservableProperty] private double exactAngle;
    [ObservableProperty] private double transformAngle;
    [ObservableProperty] private double scaleX = 1;
    [ObservableProperty] private double scaleY = 1;
    [ObservableProperty] private double pivotX;
    [ObservableProperty] private double pivotY;
    [ObservableProperty] private double offsetDistance = 3;
    [ObservableProperty] private CadOffsetJoin offsetJoin = CadOffsetJoin.Miter;
    [ObservableProperty] private double repairTolerance=.01;
    [ObservableProperty] private double minimumSegment=.01;
    [ObservableProperty] private int shapeSides=6;
    [ObservableProperty] private double shapeOuterRadius=30;
    [ObservableProperty] private double shapeInnerRadius=15;
    [ObservableProperty] private double shapeWidth=100;
    [ObservableProperty] private double shapeHeight=40;
    [ObservableProperty] private double cornerRadius=8;
    [ObservableProperty] private string textValue="Text";
    [ObservableProperty] private string textFont="Inter";
    [ObservableProperty] private double textSize=12;
    [ObservableProperty] private string textFontStyle="Regular";
    [ObservableProperty] private double textLetterSpacing;
    [ObservableProperty] private double textLineSpacing=1.2;
    [ObservableProperty] private TextAlignment textAlignment;
    [ObservableProperty] private FontCatalogEntry? selectedFont;
    [ObservableProperty] private string fontSearch="";
    [ObservableProperty] private string fontKindFilter="All";
    [ObservableProperty] private double textPathOffset;
    [ObservableProperty] private double textPathStart;
    [ObservableProperty] private bool textPathReverse;
    [ObservableProperty] private bool textPathFlip;
    [ObservableProperty] private bool textPathFit;
    [ObservableProperty] private bool textKeepUpright=true;
    public IReadOnlyList<TextAlignment> TextAlignments=>Enum.GetValues<TextAlignment>();
    [ObservableProperty] private string vectorColor="#61C8FF";
    [ObservableProperty] private bool useDrawingColor;
    [ObservableProperty] private int cornerIndex=1;
    [ObservableProperty] private double chamferDistance=5;
    [ObservableProperty] private double filletRadius=5;
    private RepairAnalysis? _repairAnalysis;
    public IReadOnlyList<CadOffsetJoin> OffsetJoins => Enum.GetValues<CadOffsetJoin>();
    public IReadOnlyList<string> FontKindFilters { get; }=["All","Outline","SingleLine","Stencil"];
    private readonly List<Point2D> _numericPolyline=[];
    [ObservableProperty] private double nodeX;
    [ObservableProperty] private double nodeY;
    [ObservableProperty] private bool hasActiveNode;
    private CadEntity? _activeNodeEntity; private int _activeNodeIndex=-1;
    public string MaterialSummary => HasJob ? $"{Document.Job.WidthMm:0.###} × {Document.Job.HeightMm:0.###} × {Document.Job.ThicknessMm:0.###} mm" : "No material defined";
    public string OriginSummary=>HasJob?$"XY zero: {Document.Job.Origin}{(Math.Abs(Document.Job.OriginOffsetXmm)>1e-9||Math.Abs(Document.Job.OriginOffsetYmm)>1e-9?$" + ({Document.Job.OriginOffsetXmm:0.###}, {Document.Job.OriginOffsetYmm:0.###}) mm":"")} · Z zero: {Document.Job.ZZero}":"No datum defined";
    public string? ProjectPath { get; private set; }

    public MainWindowViewModel(IToolLibrary toolLibrary, IRecentProjectStore recentProjectStore, IMachineProfileRepository? machineProfiles = null,ITextOutlineService? textOutlineService=null,IFontCatalog? fontCatalog=null) { _toolLibrary = toolLibrary; _recentProjectStore = recentProjectStore; _machineProfiles = machineProfiles;if(textOutlineService is not null)_textOutlineService=textOutlineService;_fontCatalog=fontCatalog; SetActiveToolGuidance(CanvasTool.Select); }
    public async Task LoadAsync()
    {
        Tools.Clear(); foreach (var tool in await _toolLibrary.GetAllAsync()) Tools.Add(tool);
        if (_machineProfiles is not null) { Machines.Clear(); foreach (var machine in await _machineProfiles.GetAllAsync()) Machines.Add(machine); SelectedMachine = Machines.FirstOrDefault(); }
        SelectedTool = Tools.FirstOrDefault(t => t.Name.StartsWith("6mm End Mill")) ?? Tools.FirstOrDefault();
        Fonts.Clear();if(_fontCatalog is not null)foreach(var font in _fontCatalog.Fonts)Fonts.Add(font);SelectedFont=Fonts.FirstOrDefault(x=>x.Descriptor.Family.Equals("Inter",StringComparison.OrdinalIgnoreCase))??Fonts.FirstOrDefault();if(SelectedFont is not null)TextFont=SelectedFont.Descriptor.Family;
        await RefreshRecentProjectsAsync();
        RefreshLayers();
    }
    partial void OnFontSearchChanged(string value)=>RefreshFonts();partial void OnFontKindFilterChanged(string value)=>RefreshFonts();private void RefreshFonts(){if(_fontCatalog is null)return;var selected=SelectedFont?.Descriptor.Family;Fonts.Clear();foreach(var font in _fontCatalog.Fonts.Where(x=>(string.IsNullOrWhiteSpace(FontSearch)||x.Descriptor.Family.Contains(FontSearch,StringComparison.OrdinalIgnoreCase))&&(FontKindFilter=="All"||x.Descriptor.Kind.ToString()==FontKindFilter)))Fonts.Add(font);SelectedFont=Fonts.FirstOrDefault(x=>x.Descriptor.Family==selected)??Fonts.FirstOrDefault();}
    public void CreateJob(JobSettings settings)
    {
        ClearPlayback();
        Document.Job = settings;
        Document.Entities.Clear(); Document.Layers = [new()];Document.ActiveLayerId=Document.Layers[0].Id; CamJob = new() { Machine = SelectedMachine?.Snapshot(), SafetySetupIncomplete = SelectedMachine is null };NestingJob=new(){SheetTemplate=new(){WidthMm=settings.WidthMm,HeightMm=settings.HeightMm,ThicknessMm=settings.ThicknessMm}};NestParts.Clear();MaterialSheets.Clear(); Program.Operations.Clear(); Operations.Clear(); Fixtures.Clear(); Selection.Clear(); _history.Clear(); UpdateHistoryState();RefreshLayers();
        Program.Name = settings.Name; ProjectPath = null; HasJob = true; IsDirty = true; JobTitle = settings.Name;
        GCodePreview = "Create a profile toolpath to preview G-code.";
        Message = $"Created {settings.WidthMm:0.##} × {settings.HeightMm:0.##} × {settings.ThicknessMm:0.##} mm job";
        RefreshWorkspaceState(); OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary));
    }
    public void UpdateJob(JobSettings settings) { Document.Job = settings; JobTitle = settings.Name; Program.Name = settings.Name; InvalidatePreflight(); IsDirty = true; OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary)); RefreshWorkspaceState(); }
    public void AddEntity(CadEntity entity)
    {
        if(Document.ActiveLayer.IsLocked)throw new InvalidOperationException("The active layer is locked.");
        if(UseDrawingColor)entity.StrokeColor=VectorColor;
        _history.Execute(new AddEntityCommand(Document, entity)); Selection.Set(entity.Id); SelectionChanged(); UpdateHistoryState(); IsDirty = true;
        Message = $"{entity.GetType().Name.Replace("Entity", string.Empty)} created";
        RefreshWorkspaceState();
    }
    public void AddRectangle(RectangleEntity rectangle) => AddEntity(rectangle);
    public VectorImportResult InspectVectors(string fileName,Stream content)=>_vectorImporter.Import(new(fileName,content));
    public VectorImportResult ImportVectors(string fileName,Stream content,double scale=1,Point2D target=default,bool preserveLayers=true)
    {
        var result=_vectorImporter.Import(new(fileName,content,scale,target,preserveLayers));if(!result.Success)throw new InvalidDataException("The file contains no supported 2D vectors.");_history.Execute(new ImportEntitiesCommand(Document,result.Layers,result.Entities));Selection.Set(result.Entities.Select(x=>x.Id));RefreshLayers();SelectionChanged();UpdateHistoryState();IsDirty=true;Message=$"Imported {result.Entities.Count} vectors · {result.Warnings.Count} warnings · {result.Unsupported.Count} unsupported types";RefreshWorkspaceState();return result;
    }
    public void CreatePolygon()=>AddEntity(ParametricShapes.Polygon(new(Point1X,Point1Y),ShapeOuterRadius,ShapeSides,Document.ActiveLayerId));
    public void CreateStar()=>AddEntity(ParametricShapes.Star(new(Point1X,Point1Y),ShapeOuterRadius,ShapeInnerRadius,ShapeSides,Document.ActiveLayerId));
    public void CreateSlot()=>AddEntity(ParametricShapes.Slot(new(Point1X,Point1Y),ShapeWidth,ShapeHeight,Document.ActiveLayerId));
    public void CreateRoundedRectangle()=>AddEntity(ParametricShapes.RoundedRectangle(new(Point1X,Point1Y),ShapeWidth,ShapeHeight,CornerRadius,Document.ActiveLayerId));
    public void CreateText()
    {
        var family=SelectedFont?.Descriptor.Family??TextFont;var result=_textOutlineService.Create(new(TextValue,family,TextFontStyle,TextSize,TextLetterSpacing,TextLineSpacing,TextAlignment,new(Point1X,Point1Y)));var source=VectorPathGeometry.Clone(result.Outlines);var guide=Document.Entities.SingleOrDefault(x=>Selection.Contains(x.Id)&&x is CircleEntity or ArcEntity or EllipseEntity or PolylineEntity or VectorPathEntity);var placement=new TextPathPlacement{GuideEntityId=guide?.Id,StartOffset=TextPathStart,BaselineOffsetMm=TextPathOffset,ReverseDirection=TextPathReverse,FlipSide=TextPathFlip,KeepUpright=TextKeepUpright,FitMode=TextPathFit?TextFitMode.FitSpacing:TextFitMode.Natural,GuideSnapshot=guide is null?[]:[..EntityGeometry.WorldPoints(guide)]};var outlines=guide is null?source:TextPathLayout.Fit(source,placement.GuideSnapshot,placement);AddEntity(new TextEntity{LayerId=Document.ActiveLayerId,Text=TextValue,FontFamily=family,FontStyle=TextFontStyle,SizeMm=TextSize,LetterSpacingMm=TextLetterSpacing,LineSpacing=TextLineSpacing,Alignment=TextAlignment,FontKind=result.Kind,LayoutMode=guide is null?TextLayoutMode.Straight:TextLayoutMode.FollowPath,PathPlacement=placement,GlyphAdvancesMm=[..result.GlyphAdvancesMm??[]],SourceOutlines=source,Outlines=[..outlines],Glyphs=BuildGlyphs(result,outlines)});if(result.Warnings.Count>0)Message=result.Warnings[0];
    }
    public void UpdateSelectedText()
    {
        var text=SelectedEntities().OfType<TextEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one editable text object.");
        var family=SelectedFont?.Descriptor.Family??text.FontFamily;
        var result=_textOutlineService.Create(new(TextValue,family,TextFontStyle,TextSize,TextLetterSpacing,TextLineSpacing,TextAlignment,new(Point1X,Point1Y)));
        var source=VectorPathGeometry.Clone(result.Outlines);
        var placement=new TextPathPlacement{GuideEntityId=text.PathPlacement.GuideEntityId,StartOffset=TextPathStart,BaselineOffsetMm=TextPathOffset,ReverseDirection=TextPathReverse,FlipSide=TextPathFlip,KeepUpright=TextKeepUpright,FitMode=TextPathFit?TextFitMode.FitSpacing:TextFitMode.Natural,GuideSnapshot=[..text.PathPlacement.GuideSnapshot]};
        if(placement.GuideEntityId is{} guideId&&Document.Entities.FirstOrDefault(x=>x.Id==guideId) is{} guide)placement.GuideSnapshot=[..EntityGeometry.WorldPoints(guide)];
        var outlines=placement.GuideEntityId is null?source:TextPathLayout.Fit(source,placement.GuideSnapshot,placement);
        var replacement=(TextEntity)EntityGeometry.Clone(text);replacement.Text=TextValue;replacement.FontFamily=family;replacement.FontStyle=TextFontStyle;replacement.SizeMm=TextSize;replacement.LetterSpacingMm=TextLetterSpacing;replacement.LineSpacing=TextLineSpacing;replacement.Alignment=TextAlignment;replacement.FontKind=result.Kind;replacement.LayoutMode=placement.GuideEntityId is null?TextLayoutMode.Straight:TextLayoutMode.FollowPath;replacement.PathPlacement=placement;replacement.GlyphAdvancesMm=[..result.GlyphAdvancesMm??[]];replacement.SourceOutlines=source;replacement.Outlines=[..outlines];replacement.Glyphs=BuildGlyphs(result,outlines);
        _history.Execute(new EditTextEntityCommand(text,replacement));AfterGeometryCommand([text.Id],"Editable text updated");if(result.Warnings.Count>0)Message=result.Warnings[0];
    }
    private static List<TextGlyphGeometry> BuildGlyphs(TextOutlineResult result,IReadOnlyList<VectorPathFigure> placed){var output=new List<TextGlyphGeometry>();var index=0;foreach(var glyph in result.Glyphs??[]){var count=glyph.Figures.Count;output.Add(new(){Cluster=glyph.Cluster,AdvanceMm=glyph.AdvanceMm,Order=glyph.Order,SourceFigures=VectorPathGeometry.Clone(glyph.Figures),PlacedFigures=VectorPathGeometry.Clone(placed.Skip(index).Take(count))});index+=count;}return output;}
    public void CenterTextOnPath(){TextPathStart=.5;TextPathFit=false;UpdateSelectedText();}
    public void ReverseTextPath(){TextPathReverse=!TextPathReverse;UpdateSelectedText();}
    public void FlipTextPath(){TextPathFlip=!TextPathFlip;UpdateSelectedText();}
    public void ResetTextSpacing(){TextLetterSpacing=0;TextPathFit=false;UpdateSelectedText();}
    public void ConvertSelectedTextToVectors(){var texts=SelectedEntities().OfType<TextEntity>().ToArray();if(texts.Length==0)throw new InvalidOperationException("Select editable text first.");var additions=new List<CadEntity>();foreach(var text in texts){var group=Guid.NewGuid();var glyphs=text.Glyphs.Count>0?text.Glyphs.OrderBy(x=>x.Order).Select(x=>x.PlacedFigures):text.Outlines.Select(x=>(IEnumerable<VectorPathFigure>)[x]);foreach(var figures in glyphs)if(figures.Any())additions.Add(new VectorPathEntity{LayerId=text.LayerId,MaterialSheetId=text.MaterialSheetId,GroupId=group,StrokeColor=text.StrokeColor,Transform=text.Transform,Figures=VectorPathGeometry.Clone(figures)});}var ids=texts.Select(x=>x.Id).ToArray();_history.Execute(new ReplaceEntitiesCommand(Document,ids,additions,"Convert text to vectors"));Selection.Set(additions.Select(x=>x.Id));AfterGeometryCommand(ids.Concat(additions.Select(x=>x.Id)),"Converted text to grouped glyph vectors");}
    public void ApplyVectorColor(){if(!HasSelection)throw new InvalidOperationException("Select vectors to color.");if(!System.Text.RegularExpressions.Regex.IsMatch(VectorColor,"^#[0-9a-fA-F]{6}$"))throw new InvalidOperationException("Enter a color as #RRGGBB.");VectorColor=VectorColor.ToUpperInvariant();var ids=Selection.Ids.ToArray();_history.Execute(new SetEntityColorCommand(Document,ids,VectorColor));if(!RecentColors.Contains(VectorColor)){RecentColors.Insert(0,VectorColor);while(RecentColors.Count>8)RecentColors.RemoveAt(RecentColors.Count-1);}AfterGeometryCommand(ids,"Vector color changed");}
    public void ChoosePaletteColor(string color){VectorColor=color;UseDrawingColor=true;if(HasSelection)ApplyVectorColor();}
    public void PickPrimaryColor(){var entity=Selection.PrimaryId is{} id?Document.Entities.FirstOrDefault(x=>x.Id==id):null;if(entity is null)throw new InvalidOperationException("Select a visible unlocked vector to sample its color.");var layer=Document.Layers.FirstOrDefault(x=>x.Id==entity.LayerId);VectorColor=entity.StrokeColor??layer?.Color??"#65C9FF";UseDrawingColor=true;Message=$"Picked {VectorColor}";}
    public void UseLayerColor(){if(!HasSelection)return;var ids=Selection.Ids.ToArray();_history.Execute(new SetEntityColorCommand(Document,ids,null));AfterGeometryCommand(ids,"Vectors now inherit layer color");}
    public void GroupSelection(){if(Selection.Count<2)throw new InvalidOperationException("Select at least two vectors to group.");var ids=Selection.Ids.ToArray();_history.Execute(new SetEntityGroupCommand(Document,ids,Guid.NewGuid()));AfterGeometryCommand(ids,"Vectors grouped");}
    public void UngroupSelection(){var ids=SelectedEntities().Where(x=>x.GroupId is not null).Select(x=>x.Id).ToArray();if(ids.Length==0)throw new InvalidOperationException("Select grouped vectors.");_history.Execute(new SetEntityGroupCommand(Document,ids,null));AfterGeometryCommand(ids,"Vectors ungrouped");}
    public void TrimSelectionAtPoint(){var source=SelectedEntities();if(source.Length<2)throw new InvalidOperationException("Select the line to trim first, then its cutting vectors.");var result=_topologyService.Trim(source[0],source.Skip(1),new(Point1X,Point1Y),RepairTolerance);ReplaceTopology(source[0],result,"Trim vector");}
    public void ApplyInteractiveTrim(TrimCandidate candidate){if(!candidate.Success)throw new InvalidOperationException(candidate.Error);_history.Execute(new ReplaceEntitiesCommand(Document,[candidate.TargetId],candidate.Replacements,"Trim vector"));Selection.Set(candidate.Replacements.Select(x=>x.Id));SelectionChanged();UpdateHistoryState();InvalidateOperations([candidate.TargetId]);IsDirty=true;RefreshWorkspaceState();Message="Vector trimmed";}
    public void ChamferSelection(){var path=SelectedEntities().OfType<PolylineEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one polyline.");ReplaceTopology(path,_topologyService.Chamfer(path,CornerIndex,ChamferDistance),"Chamfer corner");}
    public void FilletSelection(){var path=SelectedEntities().OfType<PolylineEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one polyline.");ReplaceTopology(path,_topologyService.Fillet(path,CornerIndex,FilletRadius),"Fillet corner");}
    private void ReplaceTopology(CadEntity source,VectorEditResult result,string description){if(!result.Success)throw new InvalidOperationException(result.Error);_history.Execute(new ReplaceEntitiesCommand(Document,[source.Id],result.Entities,description));Selection.Set(result.Entities.Select(x=>x.Id));SelectionChanged();AfterGeometryCommand([source.Id],description);}
    public void AnalyzeRepair(){var source=HasSelection?SelectedEntities():Document.Entities.Where(x=>x.IsVisible&&!x.IsLocked).ToArray();_repairAnalysis=_repairService.Analyze(source,new(RepairTolerance,MinimumSegment));RepairIssues.Clear();foreach(var issue in _repairAnalysis.Issues)RepairIssues.Add(issue);Message=$"Repair preview: {RepairIssues.Count} issue(s), {_repairAnalysis.Replacements.Count} proposed replacement(s)";}
    public void ApplyRepair(){if(_repairAnalysis is null)throw new InvalidOperationException("Analyze vectors first.");if(_repairAnalysis.RemoveIds.Count==0&&_repairAnalysis.Replacements.Count==0){Message="No safe repairs to apply";return;}_history.Execute(new ReplaceEntitiesCommand(Document,_repairAnalysis.RemoveIds,_repairAnalysis.Replacements,"Repair vectors"));var ids=_repairAnalysis.RemoveIds.Concat(_repairAnalysis.Replacements.Select(x=>x.Id)).ToArray();Selection.Set(_repairAnalysis.Replacements.Select(x=>x.Id));RepairIssues.Clear();_repairAnalysis=null;AfterGeometryCommand(ids,"Vector repairs applied");}
    public void AddLayer(){var layer=new CadLayer{Name=$"Layer {Document.Layers.Count+1}",Order=Document.Layers.Count};Document.Layers.Add(layer);Document.ActiveLayerId=layer.Id;RefreshLayers();IsDirty=true;}
    public void DeleteSelectedLayer(){var layer=SelectedLayer??throw new InvalidOperationException("Select a layer.");if(Document.Layers.Count==1)throw new InvalidOperationException("A document needs at least one layer.");if(Document.Entities.Any(x=>x.LayerId==layer.Id))throw new InvalidOperationException("Only empty layers can be deleted.");Document.Layers.Remove(layer);Document.ActiveLayerId=Document.Layers[0].Id;RefreshLayers();IsDirty=true;}
    public void ToggleLayerVisibility(){if(SelectedLayer is null)return;SelectedLayer.IsVisible=!SelectedLayer.IsVisible;IsDirty=true;}
    public void ToggleLayerLock(){if(SelectedLayer is null)return;SelectedLayer.IsLocked=!SelectedLayer.IsLocked;IsDirty=true;}
    public void ActivateLayer(){if(SelectedLayer is null||SelectedLayer.IsLocked)throw new InvalidOperationException("Choose an unlocked layer.");Document.ActiveLayerId=SelectedLayer.Id;Message=$"Active layer: {SelectedLayer.Name}";}
    public void SelectLayerContents(){if(SelectedLayer is null)return;Selection.Set(Document.Entities.Where(x=>x.LayerId==SelectedLayer.Id).Select(x=>x.Id));SelectionChanged();}
    public void MoveSelectionToLayer(){if(SelectedLayer is null||SelectedLayer.IsLocked)throw new InvalidOperationException("Choose an unlocked destination layer.");var entities=SelectedEntities();foreach(var entity in entities)entity.LayerId=SelectedLayer.Id;AfterGeometryCommand(entities.Select(x=>x.Id),$"Moved selection to {SelectedLayer.Name}");}
    public void CommitLayerProperties(){if(SelectedLayer is null)return;if(string.IsNullOrWhiteSpace(SelectedLayer.Name))SelectedLayer.Name="Layer";IsDirty=true;RefreshLayers();Message=$"Updated layer {SelectedLayer.Name}";}
    public void MoveSelectedLayer(int direction){if(SelectedLayer is null)return;var ordered=Document.Layers.OrderBy(x=>x.Order).ToList();var index=ordered.IndexOf(SelectedLayer);var target=Math.Clamp(index+direction,0,ordered.Count-1);if(target==index)return;(ordered[index],ordered[target])=(ordered[target],ordered[index]);for(var i=0;i<ordered.Count;i++)ordered[i].Order=i;RefreshLayers();SelectedLayer=ordered[target];IsDirty=true;}
    private void RefreshLayers(){var id=Document.ActiveLayerId;Layers.Clear();foreach(var layer in Document.Layers.OrderBy(x=>x.Order))Layers.Add(layer);SelectedLayer=Layers.FirstOrDefault(x=>x.Id==id)??Layers.FirstOrDefault();}
    public void SelectionChanged()
    {
        var primary=Selection.PrimaryId is{} primaryId?Document.Entities.FirstOrDefault(x=>x.Id==primaryId):null;var layer=primary is null?null:Document.Layers.FirstOrDefault(x=>x.Id==primary.LayerId);SelectionStatus = Selection.Count switch { 0 => "Nothing selected", 1 => $"{primary?.GetType().Name.Replace("Entity","")??"Vector"} · {layer?.Name??"Layer"} · {primary?.StrokeColor??layer?.Color??"default color"}", _ => $"{Selection.Count} entities selected · primary {primary?.GetType().Name.Replace("Entity","")??"vector"}" };
        if(primary is TextEntity text&&Selection.Count==1){TextValue=text.Text;TextFont=text.FontFamily;TextFontStyle=text.FontStyle;TextSize=text.SizeMm;TextLetterSpacing=text.LetterSpacingMm;TextLineSpacing=text.LineSpacing;TextAlignment=text.Alignment;TextPathOffset=text.PathPlacement.BaselineOffsetMm;TextPathStart=text.PathPlacement.StartOffset;TextPathReverse=text.PathPlacement.ReverseDirection;TextPathFlip=text.PathPlacement.FlipSide;TextKeepUpright=text.PathPlacement.KeepUpright;TextPathFit=text.PathPlacement.FitMode==TextFitMode.FitSpacing;SelectedFont=Fonts.FirstOrDefault(x=>x.Descriptor.Family.Equals(text.FontFamily,StringComparison.OrdinalIgnoreCase));}
        HasSelection = Selection.Count > 0;
        CanAlign=Selection.Count>=2;CanDistribute=Selection.Count>=3;
        if (HasSelection) { var bounds = SelectionBounds(); SelectionX = bounds.MinX; SelectionY = bounds.MinY; PivotX=(bounds.MinX+bounds.MaxX)/2; PivotY=(bounds.MinY+bounds.MaxY)/2; }
        RefreshWorkspaceState();
    }
    public void MoveSelection(Vector2D delta, string message = "Selection moved")
    {
        if (Selection.Count == 0 || delta.Length <= 1e-9) return;
        var ids = Selection.Ids.ToArray(); _history.Execute(new MoveEntitiesCommand(Document, ids, delta)); SelectionChanged(); UpdateHistoryState(); InvalidateOperations(ids); IsDirty = true; Message = message;
    }
    public void ApplySelectionPosition() { if (!HasSelection) return; var bounds = SelectionBounds(); MoveSelection(new(SelectionX - bounds.MinX, SelectionY - bounds.MinY), "Selection positioned"); }
    public void CenterSelectionOnMaterial()
    {
        if (!HasSelection) return; var bounds = SelectionBounds();
        MoveSelection(new(Document.Job.WidthMm / 2 - (bounds.MinX + bounds.MaxX) / 2, Document.Job.HeightMm / 2 - (bounds.MinY + bounds.MaxY) / 2), "Selection centered on material");
    }
    public void NudgeSelection(double x, double y) => MoveSelection(new(x, y), "Selection nudged");
    public void CreateExact()
    {
        var layer=Document.ActiveLayerId; CadEntity entity=(ActivePaletteItem?.Tool) switch
        {
            CanvasTool.Line=>new LineEntity{LayerId=layer,Start=new(Point1X,Point1Y),End=ExactUsesLengthAngle?Polar(new(Point1X,Point1Y),ExactLength,ExactAngle):new(Point2X,Point2Y)},
            CanvasTool.Rectangle when ExactWidth>0&&ExactHeight>0=>new RectangleEntity{LayerId=layer,Origin=RectangleFromCenter?new(Point1X-ExactWidth/2,Point1Y-ExactHeight/2):new(Point1X,Point1Y),Width=ExactWidth,Height=ExactHeight},
            CanvasTool.Circle when ExactRadius>0=>new CircleEntity{LayerId=layer,Center=new(Point1X,Point1Y),Radius=CircleUsesDiameter?ExactRadius/2:ExactRadius},
            CanvasTool.Arc=>GeometryAlgorithms.CreateThreePointArc(new(Point1X,Point1Y),new(Point2X,Point2Y),new(Point3X,Point3Y),layer),
            _=>throw new InvalidOperationException("Choose Line, Rectangle, Circle, or Arc for exact creation.")
        };
        if(EntityGeometry.WorldPoints(entity).Distinct().Count()<2)throw new InvalidOperationException("Exact geometry is degenerate."); AddEntity(entity);
    }
    public void AddNumericPolylineNode(){if(_numericPolyline.Count==0)_numericPolyline.Add(new(Point1X,Point1Y));var point=ExactUsesLengthAngle?Polar(_numericPolyline[^1],ExactLength,ExactAngle):new Point2D(Point2X,Point2Y);if(_numericPolyline[^1].DistanceTo(point)>1e-9)_numericPolyline.Add(point);Message=$"{_numericPolyline.Count} numeric polyline nodes";}
    public void FinishNumericPolyline(bool close){if(_numericPolyline.Count<(close?3:2))throw new InvalidOperationException(close?"A closed path needs at least three nodes.":"A path needs at least two nodes.");var entity=new PolylineEntity{LayerId=Document.ActiveLayerId,Points=[.._numericPolyline],IsClosed=close};_numericPolyline.Clear();AddEntity(entity);}
    public void TransformSelection(Matrix2D transform,string description){if(!HasSelection)return;var ids=Selection.Ids.ToArray();_history.Execute(new TransformEntitiesCommand(Document,ids,transform,description));AfterGeometryCommand(ids,description);}
    public void RotateSelection()=>TransformSelection(Matrix2D.Around(new(PivotX,PivotY),Matrix2D.Rotation(TransformAngle)),"Selection rotated");
    public void ScaleSelection(){if(Math.Abs(ScaleX)<1e-9||Math.Abs(ScaleY)<1e-9)throw new InvalidOperationException("Scale cannot be zero.");TransformSelection(Matrix2D.Around(new(PivotX,PivotY),Matrix2D.Scale(ScaleX,ScaleY)),"Selection scaled");}
    public void MirrorSelection(bool horizontal)=>TransformSelection(Matrix2D.Around(new(PivotX,PivotY),Matrix2D.Scale(horizontal?-1:1,horizontal?1:-1)),horizontal?"Selection mirrored horizontally":"Selection mirrored vertically");
    public void AlignSelection(string mode)
    {
        var entities=SelectedEntities();if(entities.Length<2)return;var all=SelectionBounds();var moves=entities.Select(e=>{var b=e.GetBounds();var delta=mode switch{"Left"=>new Vector2D(all.MinX-b.MinX,0),"HCenter"=>new Vector2D((all.MinX+all.MaxX-b.MinX-b.MaxX)/2,0),"Right"=>new Vector2D(all.MaxX-b.MaxX,0),"Bottom"=>new Vector2D(0,all.MinY-b.MinY),"VCenter"=>new Vector2D(0,(all.MinY+all.MaxY-b.MinY-b.MaxY)/2),_=>new Vector2D(0,all.MaxY-b.MaxY)};return(e,delta);}).ToArray();
        _history.Execute(new MoveEntitiesIndividuallyCommand(moves,$"Align {mode}"));AfterGeometryCommand(entities.Select(e=>e.Id),$"Aligned {mode}");
    }
    public void DistributeSelection(bool horizontal)
    {
        var entities=SelectedEntities();if(entities.Length<3)return;var ordered=(horizontal?entities.OrderBy(e=>e.GetBounds().MinX):entities.OrderBy(e=>e.GetBounds().MinY)).ToArray();var first=ordered[0].GetBounds();var last=ordered[^1].GetBounds();var start=horizontal?first.MinX:first.MinY;var end=horizontal?last.MinX:last.MinY;var moves=ordered.Select((e,i)=>{var b=e.GetBounds();var target=start+(end-start)*i/(ordered.Length-1);return(e,horizontal?new Vector2D(target-b.MinX,0):new Vector2D(0,target-b.MinY));});_history.Execute(new MoveEntitiesIndividuallyCommand(moves,"Distribute selection"));AfterGeometryCommand(entities.Select(e=>e.Id),horizontal?"Distributed horizontally":"Distributed vertically");
    }
    public void OffsetSelection()
    {
        var source=SelectedEntities();var result=_offsetService.Offset(source,OffsetDistance,OffsetJoin);if(!result.Success)throw new InvalidOperationException(result.Error);_history.Execute(new AddEntitiesCommand(Document,result.Entities,"Offset vectors"));Selection.Set(result.Entities.Select(e=>e.Id));SelectionChanged();UpdateHistoryState();IsDirty=true;Message=$"Created {result.Entities.Count} offset vector(s)";RefreshWorkspaceState();
    }
    public void SetPathClosed(bool close)
    {
        var paths=SelectedEntities().OfType<PolylineEntity>().ToArray();if(paths.Length==0)throw new InvalidOperationException("Select one or more polylines.");if(close&&paths.Any(p=>p.Points.Count<3))throw new InvalidOperationException("A closed path needs at least three nodes.");var ids=paths.Select(p=>p.Id).ToArray();_history.Execute(new CompositeCommand(paths.Select(p=>(IUndoableCommand)new PolylineNodesCommand(p,p.Points,close,close?"Close path":"Open path")),close?"Close paths":"Open paths"));SelectionChanged();UpdateHistoryState();InvalidateOperations(ids);IsDirty=true;Message=close?"Paths closed":"Paths opened";
    }
    public void JoinSelection(double tolerance=.01)
    {
        var source=SelectedEntities();var paths=source.Select(e=>e switch{LineEntity l=>EntityGeometry.WorldPoints(l).ToList(),PolylineEntity{IsClosed:false} p=>EntityGeometry.WorldPoints(p).ToList(),_=>throw new InvalidOperationException("Join supports selected lines and open polylines.")}).ToList();if(paths.Count<2)throw new InvalidOperationException("Select at least two open paths.");var joined=paths[0];paths.RemoveAt(0);while(paths.Count>0){var index=paths.FindIndex(p=>joined[^1].DistanceTo(p[0])<=tolerance||joined[^1].DistanceTo(p[^1])<=tolerance);if(index<0)throw new InvalidOperationException("Selected endpoints do not connect within tolerance.");var next=paths[index];if(joined[^1].DistanceTo(next[^1])<=tolerance)next.Reverse();joined.AddRange(next.Skip(1));paths.RemoveAt(index);}var result=new PolylineEntity{LayerId=source[0].LayerId,Points=joined};var ids=source.Select(e=>e.Id).ToArray();_history.Execute(new ReplaceEntitiesCommand(Document,ids,[result],"Join paths"));Selection.Set(result.Id);SelectionChanged();UpdateHistoryState();InvalidateOperations(ids);IsDirty=true;Message="Paths joined";
    }
    public void MoveNode(CadEntity entity,int index,Point2D worldPoint){_history.Execute(new SetNodeCommand(entity,index,worldPoint));_activeNodeEntity=entity;_activeNodeIndex=index;NodeX=worldPoint.X;NodeY=worldPoint.Y;HasActiveNode=true;AfterGeometryCommand([entity.Id],"Node moved");}
    public void ApplyNodePosition(){if(_activeNodeEntity is null)return;MoveNode(_activeNodeEntity,_activeNodeIndex,new(NodeX,NodeY));}
    public void InsertNode(){if(_activeNodeEntity is not PolylineEntity p)throw new InvalidOperationException("Select a polyline node first.");var local=p.Transform.Inverse().Transform(new Point2D(NodeX,NodeY));var points=p.Points.ToList();points.Insert(Math.Min(_activeNodeIndex+1,points.Count),local);_history.Execute(new PolylineNodesCommand(p,points,p.IsClosed,"Insert node"));_activeNodeIndex++;AfterGeometryCommand([p.Id],"Node inserted");}
    public void DeleteNode(){if(_activeNodeEntity is not PolylineEntity p)throw new InvalidOperationException("Select a polyline node first.");var minimum=p.IsClosed?3:2;if(p.Points.Count<=minimum)throw new InvalidOperationException($"Path requires at least {minimum} nodes.");var points=p.Points.ToList();points.RemoveAt(_activeNodeIndex);_history.Execute(new PolylineNodesCommand(p,points,p.IsClosed,"Delete node"));HasActiveNode=false;_activeNodeEntity=null;AfterGeometryCommand([p.Id],"Node deleted");}
    public void MakeStartNode(){if(_activeNodeEntity is not PolylineEntity{IsClosed:true} p)throw new InvalidOperationException("Select a node on a closed polyline.");var points=p.Points.Skip(_activeNodeIndex).Concat(p.Points.Take(_activeNodeIndex)).ToArray();_history.Execute(new PolylineNodesCommand(p,points,true,"Change start node"));_activeNodeIndex=0;AfterGeometryCommand([p.Id],"Start node changed");}
    private CadEntity[] SelectedEntities(){var entities=Document.Entities.Where(e=>Selection.Contains(e.Id)).ToArray();if(entities.Any(e=>e.IsLocked||Document.Layers.FirstOrDefault(l=>l.Id==e.LayerId)?.IsLocked==true))throw new InvalidOperationException("Locked-layer geometry cannot be edited.");return entities;}
    private static Point2D Polar(Point2D start,double length,double angle){if(length<=0)throw new InvalidOperationException("Length must be greater than zero.");var radians=angle*Math.PI/180;return start+new Vector2D(Math.Cos(radians)*length,Math.Sin(radians)*length);}
    private void AfterGeometryCommand(IEnumerable<Guid> ids,string message){SelectionChanged();UpdateHistoryState();InvalidateOperations(ids);IsDirty=true;Message=message;}
    public void CopySelection()
    {
        _clipboard.Clear(); _clipboard.AddRange(Document.Entities.Where(e => Selection.Contains(e.Id)).Select(EntityGeometry.Clone)); _pasteCount = 0;
        HasClipboard = _clipboard.Count > 0; Message = _clipboard.Count == 0 ? "Nothing selected to copy" : $"Copied {_clipboard.Count} entities";
    }
    public void Paste()
    {
        if (_clipboard.Count == 0) return; _pasteCount++;
        var entities = _clipboard.Select(EntityGeometry.Clone).ToList(); var offset = new Vector2D(10 * _pasteCount, 10 * _pasteCount);
        foreach (var entity in entities) EntityGeometry.Translate(entity, offset);
        _history.Execute(new AddEntitiesCommand(Document, entities, "Paste entities")); Selection.Set(entities.Select(e => e.Id)); SelectionChanged(); UpdateHistoryState(); IsDirty = true; Message = $"Pasted {entities.Count} entities";
    }
    public void DuplicateSelection()
    {
        var entities = Document.Entities.Where(e => Selection.Contains(e.Id)).Select(EntityGeometry.Clone).ToList(); if (entities.Count == 0) return;
        foreach (var entity in entities) EntityGeometry.Translate(entity, new(10, 10));
        _history.Execute(new AddEntitiesCommand(Document, entities, "Duplicate entities")); Selection.Set(entities.Select(e => e.Id)); SelectionChanged(); UpdateHistoryState(); IsDirty = true; Message = $"Duplicated {entities.Count} entities";
    }
    public void UpdateInteraction(SnapResult snap, CncCad.Rendering.Canvas.DrawingMeasurement measurement)
    {
        var snapText = snap.Type == SnapType.None ? "Snap: none" : $"Snap: {snap.Type}";
        InteractionStatus = measurement.IsVisible ? $"{snapText}   {measurement.Summary}" : snapText;
    }
    private BoundingBox SelectionBounds() => BoundingBox.FromPoints(Document.Entities.Where(e => Selection.Contains(e.Id)).SelectMany(e => { var b = e.GetBounds(); return new[] { new Point2D(b.MinX, b.MinY), new Point2D(b.MaxX, b.MaxY) }; }));
    private void InvalidateOperations(IEnumerable<Guid> ids)
    {
        var values = ids.ToArray();var linked=RefreshLinkedText(values);values=[..values.Concat(linked).Distinct()]; CamJob.MarkGeometryChanged(values, values.Any(id => Document.Entities.All(entity => entity.Id != id)));if(NestingJob.Parts.Any(p=>values.Contains(p.OuterGeometryId)||p.InnerGeometryIds.Any(values.Contains))||NestingJob.AcceptedLayout?.Placements.Any(p=>p.InstanceGeometryIds.Any(values.Contains))==true)NestingJob.Status=NestingLayoutStatus.Stale; RebuildProgram(); RefreshOperations(); ClearPlayback(); InvalidatePreflight(); GCodePreview = "Geometry changed — recalculate stale toolpaths.";
    }
    private IReadOnlyList<Guid> RefreshLinkedText(IReadOnlyCollection<Guid> changed){var affected=new List<Guid>();foreach(var text in Document.Entities.OfType<TextEntity>().Where(x=>x.LayoutMode==TextLayoutMode.FollowPath&&x.PathPlacement.GuideEntityId is{} id&&changed.Contains(id))){var guide=Document.Entities.FirstOrDefault(x=>x.Id==text.PathPlacement.GuideEntityId);if(guide is null){text.PathPlacement.GuideEntityId=null;continue;}text.PathPlacement.GuideSnapshot=[..EntityGeometry.WorldPoints(guide)];text.Outlines=[..TextPathLayout.Fit(text.SourceOutlines,text.PathPlacement.GuideSnapshot,text.PathPlacement)];var index=0;foreach(var glyph in text.Glyphs.OrderBy(x=>x.Order)){glyph.PlacedFigures=VectorPathGeometry.Clone(text.Outlines.Skip(index).Take(glyph.SourceFigures.Count));index+=glyph.SourceFigures.Count;}affected.Add(text.Id);}return affected;}
    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var ids = Selection.Ids.ToArray();var commands=new List<IUndoableCommand>();foreach(var text in Document.Entities.OfType<TextEntity>().Where(x=>x.PathPlacement.GuideEntityId is{} guide&&ids.Contains(guide)&&!ids.Contains(x.Id))){var detached=(TextEntity)EntityGeometry.Clone(text);detached.PathPlacement.GuideEntityId=null;detached.LayoutMode=TextLayoutMode.Straight;detached.SourceOutlines=VectorPathGeometry.Clone(detached.Outlines);foreach(var glyph in detached.Glyphs)glyph.SourceFigures=VectorPathGeometry.Clone(glyph.PlacedFigures);commands.Add(new EditTextEntityCommand(text,detached));}commands.Add(new DeleteEntitiesCommand(Document,ids));_history.Execute(new CompositeCommand(commands,"Delete vectors and detach linked text"));Selection.Clear(); SelectionChanged(); UpdateHistoryState(); InvalidateOperations(ids); IsDirty = true; Message = "Selection deleted";
    }
    public void Undo() { if (!_history.CanUndo) return; var command = _history.Undo(); Selection.Clear(); SelectionChanged(); UpdateHistoryState(); if (command is IEntityAffectingCommand affected) InvalidateOperations(affected.AffectedEntityIds); IsDirty = true; Message = "Undo"; }
    public void Redo() { if (!_history.CanRedo) return; var command = _history.Redo(); Selection.Clear(); SelectionChanged(); UpdateHistoryState(); if (command is IEntityAffectingCommand affected) InvalidateOperations(affected.AffectedEntityIds); IsDirty = true; Message = "Redo"; }
    private void UpdateHistoryState() { CanUndo = _history.CanUndo; CanRedo = _history.CanRedo; }
    public void UpdatePointer(Point2D point, double zoom) { PointerStatus = $"X {point.X:0.000}   Y {point.Y:0.000}"; ZoomStatus = $"{zoom * 100:0}%"; }
    public void AddNestPartsFromSelection()
    {
        var selected=Document.Entities.Where(x=>Selection.Contains(x.Id)&&x.MaterialSheetId is null).ToArray();if(selected.Length==0)throw new InvalidOperationException("Select one or more source closed contours.");if(selected.Any(x=>!IsProfileContour(x)))throw new InvalidOperationException("Nesting accepts closed rectangles, circles, polylines, or single-figure vector paths.");var outers=selected.Where(entity=>{var b=entity.GetBounds();var center=new Point2D((b.MinX+b.MaxX)/2,(b.MinY+b.MaxY)/2);return !selected.Any(other=>other.Id!=entity.Id&&other.GetBounds().Width*other.GetBounds().Height>b.Width*b.Height&&other.GetBounds().Contains(center));}).ToArray();foreach(var entity in outers){if(NestingJob.Parts.Any(x=>x.OuterGeometryId==entity.Id))continue;var part=new NestPartDefinition{Name=$"Part {NestingJob.Parts.Count+1}",OuterGeometryId=entity.Id,Quantity=Math.Max(1,NestQuantity),AllowQuarterTurn=NestAllowQuarterTurn};var outer=entity.GetBounds();part.InnerGeometryIds.AddRange(selected.Where(x=>x.Id!=entity.Id&&outer.Contains(new((x.GetBounds().MinX+x.GetBounds().MaxX)/2,(x.GetBounds().MinY+x.GetBounds().MaxY)/2))).Select(x=>x.Id));NestingJob.Parts.Add(part);}RefreshNesting();NestingJob.Status=NestingLayoutStatus.Stale;IsDirty=true;Message=$"Defined {NestingJob.Parts.Count} nesting part(s)";
    }
    public void RemoveSelectedNestPart(){if(SelectedNestPart is null)return;NestingJob.Parts.Remove(SelectedNestPart);RefreshNesting();NestingJob.Status=NestingLayoutStatus.Stale;IsDirty=true;}
    public NestingResult OptimizeNest()
    {
        NestingJob.PartSpacingMm=NestSpacing;NestingJob.EdgeMarginMm=NestEdgeMargin;NestingJob.SheetTemplate.WidthMm=Document.Job.WidthMm;NestingJob.SheetTemplate.HeightMm=Document.Job.HeightMm;NestingJob.SheetTemplate.ThicknessMm=Document.Job.ThicknessMm;var result=_nestingOptimizer.Optimize(new(NestingJob,Document.Entities.Where(x=>x.MaterialSheetId is null).ToDictionary(x=>x.Id)));NestingJob.AcceptedLayout=result;NestingJob.Status=NestingLayoutStatus.Stale;RefreshNesting();NestingSummary=$"Preview: {result.Placements.Count} parts · {result.Sheets.Count} sheets · {result.Score.WasteAreaMm2:0} mm² waste{(result.UnplacedQuantities.Count>0?$" · {result.UnplacedQuantities.Values.Sum()} unplaced":"")}";IsDirty=true;return result;
    }
    public void AcceptNest()
    {
        var result=NestingJob.AcceptedLayout??throw new InvalidOperationException("Optimize the nest first.");if(result.Placements.Count==0)throw new InvalidOperationException("The nesting result contains no placed parts.");var layers=new List<CadLayer>();var additions=new List<CadEntity>();foreach(var sheet in result.Sheets){var layer=new CadLayer{Name=sheet.Name,Order=Document.Layers.Count+layers.Count,Color="#75D6A5"};layers.Add(layer);foreach(var placement in result.Placements.Where(x=>x.SheetId==sheet.Id)){var part=NestingJob.Parts.First(x=>x.Id==placement.PartId);var sources=new[]{part.OuterGeometryId}.Concat(part.InnerGeometryIds);foreach(var id in sources){var source=Document.Entities.First(x=>x.Id==id);var clone=EntityGeometry.Clone(source);var b=source.GetBounds();var center=new Point2D((b.MinX+b.MaxX)/2,(b.MinY+b.MaxY)/2);var transform=Matrix2D.Then(Matrix2D.Around(center,Matrix2D.Rotation(placement.RotationDegrees)),Matrix2D.Translation(placement.Xmm,placement.Ymm));clone.Transform=Matrix2D.Then(clone.Transform,transform);clone.LayerId=layer.Id;clone.MaterialSheetId=sheet.Id;additions.Add(clone);placement.InstanceGeometryIds.Add(clone.Id);}}}_history.Execute(new ImportEntitiesCommand(Document,layers,additions));NestingJob.Status=NestingLayoutStatus.Current;MaterialSheets.Clear();foreach(var sheet in result.Sheets)MaterialSheets.Add(sheet);SelectedMaterialSheet=MaterialSheets.FirstOrDefault();Selection.Set(additions.Select(x=>x.Id));SelectionChanged();UpdateHistoryState();IsDirty=true;Message=$"Accepted nest onto {result.Sheets.Count} material sheet(s)";
    }
    public void SelectMaterialSheet(MaterialSheetDefinition? sheet){SelectedMaterialSheet=sheet;Selection.Clear();SelectionChanged();RebuildProgram();RefreshOperations();Message=sheet is null?"Showing source design":$"Active {sheet.Name}";}
    private void RefreshNesting(){var selected=SelectedNestPart;NestParts.Clear();foreach(var part in NestingJob.Parts)NestParts.Add(part);SelectedNestPart=selected??NestParts.FirstOrDefault();MaterialSheets.Clear();if(NestingJob.AcceptedLayout is{} layout)foreach(var sheet in layout.Sheets)MaterialSheets.Add(sheet);NestingSummary=NestingJob.AcceptedLayout is{} r?$"{r.Placements.Count} placed · {r.Sheets.Count} sheet(s) · {r.Score.WasteAreaMm2:0} mm² waste":"Select closed vectors to define parts.";}
    public ProfileOperationDefinition CreateProfileDraft()
    {
        if (Selection.Count == 0) throw new InvalidOperationException("Select one or more closed rectangles, polylines, or circles first.");
        var selected = Document.Entities.Where(entity => Selection.Contains(entity.Id)).ToArray();
        var invalid = selected.FirstOrDefault(entity => !IsProfileContour(entity));
        if (invalid is not null) throw new InvalidOperationException($"{invalid.GetType().Name.Replace("Entity", string.Empty)} is not a closed profile contour. Select rectangles, circles, or closed polylines.");
        var tool = SelectedTool ?? throw new InvalidOperationException("Select a tool first.");
        var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create a profile from one material sheet at a time.");return new ProfileOperationDefinition { Name = $"Profile {CamJob.Operations.Count + 1}", GeometryIds = [.. Selection.Ids], Tool = ToolSnapshot.FromTool(tool), Order = CamJob.Operations.Count, CutDepthMm = Document.Job.ThicknessMm, PassDepthMm = tool.StepDownMm, SafeZMm = Document.Job.SafeZMm, FeedRateMmPerMin = tool.FeedRateMmPerMin, PlungeRateMmPerMin = tool.PlungeRateMmPerMin, SpindleRpm = tool.SpindleRpm,MaterialSheetId=sheets[0] };
    }
    public EngraveOperationDefinition CreateEngraveDraft(){if(Selection.Count==0)throw new InvalidOperationException("Select one or more vectors or centerline text first.");var selected=Document.Entities.Where(x=>Selection.Contains(x.Id)).ToArray();if(selected.Any(x=>x is not LineEntity and not PolylineEntity and not ArcEntity and not CircleEntity and not EllipseEntity and not VectorPathEntity and not TextEntity))throw new InvalidOperationException("The selection contains geometry that cannot be engraved.");var tool=SelectedTool??throw new InvalidOperationException("Select an engraving tool.");var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create engraving for one material sheet at a time.");return new(){Name=$"Engrave {CamJob.Operations.Count+1}",GeometryIds=selected.Select(x=>x.Id).ToList(),Tool=ToolSnapshot.FromTool(tool),Order=CamJob.Operations.Count,CutDepthMm=Math.Min(1,Document.Job.ThicknessMm),PassDepthMm=Math.Min(1,Math.Max(.1,tool.StepDownMm)),SafeZMm=Document.Job.SafeZMm,FeedRateMmPerMin=tool.FeedRateMmPerMin,PlungeRateMmPerMin=tool.PlungeRateMmPerMin,SpindleRpm=tool.SpindleRpm,MaterialSheetId=sheets[0]};}
    public VCarveOperationDefinition CreateVCarveDraft(){if(Selection.Count==0)throw new InvalidOperationException("Select one or more closed outline vectors.");var selected=SelectedEntities();var tool=SelectedTool??throw new InvalidOperationException("Select a V-bit.");if(tool.Type is not ToolType.VBit and not ToolType.EngravingBit||tool.VBitAngleDegrees is not (>0 and <180))throw new InvalidOperationException("Choose a V-bit with a valid included angle.");var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create V-carving for one material sheet at a time.");return new(){Name=$"V-Carve {CamJob.Operations.Count+1}",GeometryIds=selected.Select(x=>x.Id).ToList(),Tool=ToolSnapshot.FromTool(tool),Order=CamJob.Operations.Count,MaximumDepthMm=Math.Min(3,Document.Job.ThicknessMm),OffsetStepOverMm=Math.Max(.1,(tool.TipDiameterMm??.2)*.5),SafeZMm=Document.Job.SafeZMm,FeedRateMmPerMin=tool.FeedRateMmPerMin,PlungeRateMmPerMin=tool.PlungeRateMmPerMin,SpindleRpm=tool.SpindleRpm,MaterialSheetId=sheets[0]};}
    public void AddOperation(ProfileOperationDefinition operation) { ClearPlayback(); InvalidatePreflight(); CamJob.Operations.Add(operation); CamJob.NormalizeOrder(); CalculateOperation(operation); RefreshOperations(); SelectedOperation = operation; IsDirty = true; RefreshWorkspaceState(); }
    public void AddOperation(EngraveOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void AddOperation(VCarveOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void UpdateOperation(ProfileOperationDefinition target, ProfileOperationDefinition values)
    {
        target.Name = values.Name; target.GeometryIds = [.. values.GeometryIds]; target.Tool = values.Tool; target.IsEnabled = values.IsEnabled; target.Side = values.Side; target.Direction = values.Direction; target.StartDepthMm = values.StartDepthMm; target.CutDepthMm = values.CutDepthMm; target.PassDepthMm = values.PassDepthMm; target.SafeZMm = values.SafeZMm; target.FeedRateMmPerMin = values.FeedRateMmPerMin; target.PlungeRateMmPerMin = values.PlungeRateMmPerMin; target.SpindleRpm = values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;target.Tabs=values.Tabs.Copy();
        ClearPlayback(); InvalidatePreflight(); CalculateOperation(target); RefreshOperations(); IsDirty = true; RefreshWorkspaceState();
    }
    public void UpdateOperation(EngraveOperationDefinition target,EngraveOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.Direction=values.Direction;target.StartDepthMm=values.StartDepthMm;target.CutDepthMm=values.CutDepthMm;target.PassDepthMm=values.PassDepthMm;target.SafeZMm=values.SafeZMm;target.FeedRateMmPerMin=values.FeedRateMmPerMin;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
    public void UpdateOperation(VCarveOperationDefinition target,VCarveOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.MaximumDepthMm=values.MaximumDepthMm;target.ClearFlatRegions=values.ClearFlatRegions;target.OffsetStepOverMm=values.OffsetStepOverMm;target.SafeZMm=values.SafeZMm;target.FeedRateMmPerMin=values.FeedRateMmPerMin;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
    public void AddManualTabAt(Point2D point)
    {
        if(SelectedOperation is not ProfileOperationDefinition operation)throw new InvalidOperationException("Select a profile operation first.");var entity=operation.GeometryIds.Select(id=>Document.Entities.FirstOrDefault(x=>x.Id==id)).Where(x=>x is not null).MinBy(x=>DistanceToContour(point,EntityGeometry.WorldPoints(x!)))??throw new InvalidOperationException("The operation geometry is missing.");var path=EntityGeometry.WorldPoints(entity);var total=Enumerable.Range(0,path.Count).Sum(i=>path[i].DistanceTo(path[(i+1)%path.Count]));var current=0d;var bestDistance=double.MaxValue;var bestAlong=0d;for(var i=0;i<path.Count;i++){var a=path[i];var b=path[(i+1)%path.Count];var segment=b-a;var length=segment.Length;var q=point-a;var t=length<1e-12?0:Math.Clamp((q.X*segment.X+q.Y*segment.Y)/(length*length),0,1);var projected=new Point2D(a.X+segment.X*t,a.Y+segment.Y*t);var distance=point.DistanceTo(projected);if(distance<bestDistance){bestDistance=distance;bestAlong=current+length*t;}current+=length;}operation.Tabs.Enabled=true;operation.Tabs.PlacementMode=TabPlacementMode.Manual;operation.Tabs.ManualPlacements.Add(new(Guid.NewGuid(),bestAlong/total,entity.Id));ClearPlayback();InvalidatePreflight();CalculateOperation(operation);RefreshOperations();IsDirty=true;Message=$"Added manual tab to {operation.Name}";
    }
    public void DeleteLastManualTab(){if(SelectedOperation is not ProfileOperationDefinition operation||operation.Tabs.ManualPlacements.Count==0)return;operation.Tabs.ManualPlacements.RemoveAt(operation.Tabs.ManualPlacements.Count-1);CalculateOperation(operation);InvalidatePreflight();IsDirty=true;Message="Removed manual tab";}
    private static double DistanceToContour(Point2D point,IReadOnlyList<Point2D> path)=>path.Count<2?double.MaxValue:Enumerable.Range(0,path.Count).Min(i=>{var a=path[i];var b=path[(i+1)%path.Count];var d=b-a;var length=d.Length;var q=point-a;var t=length<1e-12?0:Math.Clamp((q.X*d.X+q.Y*d.Y)/(length*length),0,1);return point.DistanceTo(new(a.X+d.X*t,a.Y+d.Y*t));});
    public void DeleteOperation(CamOperationDefinition operation) { ClearPlayback(); InvalidatePreflight(); CamJob.Operations.Remove(operation); CamJob.NormalizeOrder(); RefreshOperations(); RebuildProgram(); IsDirty = true; RefreshWorkspaceState(); }
    public void DuplicateOperation(ProfileOperationDefinition operation) { ClearPlayback(); InvalidatePreflight(); var copy = operation.Duplicate(); CamJob.Operations.Insert(Math.Min(operation.Order + 1, CamJob.Operations.Count), copy); CamJob.NormalizeOrder(); CalculateOperation(copy); RefreshOperations(); SelectedOperation = copy; IsDirty = true; RefreshWorkspaceState(); }
    public void DuplicateOperation(EngraveOperationDefinition operation) { ClearPlayback();InvalidatePreflight();var copy=operation.Duplicate();CamJob.Operations.Insert(Math.Min(operation.Order+1,CamJob.Operations.Count),copy);CamJob.NormalizeOrder();CalculateOperation(copy);RefreshOperations();SelectedOperation=copy;IsDirty=true;RefreshWorkspaceState(); }
    public void DuplicateOperation(VCarveOperationDefinition operation) { ClearPlayback();InvalidatePreflight();var copy=operation.Duplicate();CamJob.Operations.Insert(Math.Min(operation.Order+1,CamJob.Operations.Count),copy);CamJob.NormalizeOrder();CalculateOperation(copy);RefreshOperations();SelectedOperation=copy;IsDirty=true;RefreshWorkspaceState(); }
    public void ToggleOperation(CamOperationDefinition operation) { ClearPlayback(); InvalidatePreflight(); operation.IsEnabled = !operation.IsEnabled; operation.Status = operation.IsEnabled ? OperationStatus.Stale : OperationStatus.Disabled; if (operation.IsEnabled) CalculateOperation(operation); RefreshOperations(); RebuildProgram(); IsDirty = true; }
    public void MoveOperation(CamOperationDefinition operation, int delta) { var old = CamJob.Operations.IndexOf(operation); var next = Math.Clamp(old + delta, 0, CamJob.Operations.Count - 1); if (old == next) return; ClearPlayback(); InvalidatePreflight(); CamJob.Operations.RemoveAt(old); CamJob.Operations.Insert(next, operation); CamJob.NormalizeOrder(); RefreshOperations(); RebuildProgram(); SelectedOperation = operation; IsDirty = true; }
    public void CalculateOperation(CamOperationDefinition operation)
    {
        if (!operation.IsEnabled) { operation.Status = OperationStatus.Disabled; operation.CalculatedToolpath = null; RebuildProgram(); return; }
        var result = _operationCalculator.Calculate(operation, Document.Entities.ToDictionary(e => e.Id)); operation.CalculatedToolpath = result.Toolpath; operation.Error = result.Errors.Count == 0 ? null : string.Join(" ", result.Errors.Select(e => e.Message)); operation.Status = result.Success ? OperationStatus.Current : OperationStatus.Invalid;
        if (result.Toolpath is not null) operation.EstimatedTime = MachineTimeEstimator.Estimate(new ToolpathProgram { Operations = [result.Toolpath] }, 5000); RebuildProgram();
    }
    public void RecalculateAll() { foreach (var operation in CamJob.Operations) CalculateOperation(operation); RefreshOperations(); RebuildProgram(); }
    public string PreviewGCode()
    {
        RecalculateAll(); var problem = CamJob.Operations.FirstOrDefault(o => o.IsEnabled && MatchesActiveSheet(o) && o.Status != OperationStatus.Current); if (problem is not null) throw new InvalidOperationException($"{problem.Name}: {problem.Error ?? "Toolpath is not current."}");
        if (Program.Operations.Count == 0) throw new InvalidOperationException("Create at least one enabled toolpath first.");
        var safety = RunPreflight(); if (!safety.CanExport) throw new InvalidOperationException($"Preflight blocked export: {safety.ErrorCount} error(s). {safety.Findings.First(x => x.Severity == SafetySeverity.Error).Message}");
        var origin=CncCad.Core.Transforms.MachineCoordinateTransform.OriginInDocument(Document.Job);var machine=CamJob.Machine!;GCodePreview = _postProcessor.Generate(Program, new MachineProfile { SafeZMm = Math.Max(Document.Job.SafeZMm,machine.DefaultClearanceMm),RapidRateMmPerMin=machine.RapidRateMmPerMin,MaximumSpindleRpm=machine.MaximumSpindleRpm,ParkXmm=machine.ParkXmm,ParkYmm=machine.ParkYmm,WorkZeroDocumentXmm=origin.X,WorkZeroDocumentYmm=origin.Y,WorkZeroZOffsetMm=Document.Job.ZZero==ZZeroPosition.MachineBed?Document.Job.ThicknessMm:0 }); return GCodePreview;
    }
    public IReadOnlyList<(MaterialSheetDefinition Sheet,string GCode,int WarningCount)> GenerateSheetPrograms()
    {
        if(MaterialSheets.Count==0)throw new InvalidOperationException("Accept a multi-sheet nesting layout first.");RecalculateAll();var machine=CamJob.Machine??throw new InvalidOperationException("Select a complete machine snapshot before export.");var origin=CncCad.Core.Transforms.MachineCoordinateTransform.OriginInDocument(Document.Job);var profile=new MachineProfile{SafeZMm=Math.Max(Document.Job.SafeZMm,machine.DefaultClearanceMm),RapidRateMmPerMin=machine.RapidRateMmPerMin,MaximumSpindleRpm=machine.MaximumSpindleRpm,ParkXmm=machine.ParkXmm,ParkYmm=machine.ParkYmm,WorkZeroDocumentXmm=origin.X,WorkZeroDocumentYmm=origin.Y,WorkZeroZOffsetMm=Document.Job.ZZero==ZZeroPosition.MachineBed?Document.Job.ThicknessMm:0};var output=new List<(MaterialSheetDefinition,string,int)>();foreach(var sheet in MaterialSheets){var operations=CamJob.Operations.Where(x=>x.IsEnabled&&(x is ProfileOperationDefinition p&&p.MaterialSheetId==sheet.Id||x is EngraveOperationDefinition e&&e.MaterialSheetId==sheet.Id||x is VCarveOperationDefinition v&&v.MaterialSheetId==sheet.Id)).OrderBy(x=>x.Order).ToArray();var invalid=operations.FirstOrDefault(x=>x.CalculatedToolpath is null||x.Status!=OperationStatus.Current);if(invalid is not null)throw new InvalidOperationException($"{sheet.Name} · {invalid.Name}: {invalid.Error??"toolpath is not current"}");if(operations.Length==0)throw new InvalidOperationException($"{sheet.Name} has no enabled operations.");var program=new ToolpathProgram{Name=$"{Document.Job.Name} - {sheet.Name}",Operations=operations.Select(x=>x.CalculatedToolpath!).ToList()};var safety=RunPreflight(operations,program);if(!safety.CanExport)throw new InvalidOperationException($"{sheet.Name} preflight blocked export: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");output.Add((sheet,_postProcessor.Generate(program,profile),safety.WarningCount));}return output;
    }
    public void LoadSelectedPlayback()
    {
        var operation = SelectedOperation ?? throw new InvalidOperationException("Select a toolpath from the list first.");
        if (!operation.IsEnabled) throw new InvalidOperationException($"{operation.Name} is disabled. Enable it before previewing.");
        CalculateOperation(operation); if (operation.CalculatedToolpath is null) throw new InvalidOperationException($"{operation.Name}: {operation.Error ?? "Toolpath could not be calculated."}");
        var selectedProgram=new ToolpathProgram{Name=operation.Name,Operations=[operation.CalculatedToolpath]};var safety=RunPreflight([operation],selectedProgram);if(!safety.CanPreview)throw new InvalidOperationException($"Preflight blocked preview: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");
        Playback.Load([new PlaybackProgramItem(operation.CalculatedToolpath, operation.Tool.DiameterMm)],CamJob.Machine?.RapidRateMmPerMin??5000); PlaybackSessionLabel = $"Selected · {operation.Name}"; FinishPlaybackLoad();
    }
    public void LoadAllPlayback()
    {
        RecalculateAll(); var enabled = CamJob.Operations.OrderBy(operation => operation.Order).Where(operation => operation.IsEnabled&&MatchesActiveSheet(operation)).ToArray();
        if (enabled.Length == 0) throw new InvalidOperationException("Create or enable at least one toolpath first.");
        var invalid = enabled.FirstOrDefault(operation => operation.CalculatedToolpath is null); if (invalid is not null) throw new InvalidOperationException($"{invalid.Name}: {invalid.Error ?? "Toolpath could not be calculated."}");
        var previewProgram=new ToolpathProgram{Name=Document.Job.Name,Operations=enabled.Select(x=>x.CalculatedToolpath!).ToList()};var safety=RunPreflight(enabled,previewProgram);if(!safety.CanPreview)throw new InvalidOperationException($"Preflight blocked preview: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");
        Playback.Load(enabled.Select(operation => new PlaybackProgramItem(operation.CalculatedToolpath!, operation.Tool.DiameterMm)),CamJob.Machine?.RapidRateMmPerMin??5000); PlaybackSessionLabel = $"All enabled · {enabled.Length} operation{(enabled.Length == 1 ? "" : "s")}"; FinishPlaybackLoad();
    }
    public void UpdatePlaybackStatus()
    {
        var frame = Playback.Frame; HasPlayback = frame.State != PlaybackState.Empty; PlaybackProgress = frame.Progress * 100; PlaybackAction = frame.State == PlaybackState.Playing ? "Pause" : "Play";
        ShowPlaybackControls=IsToolpathPreview&&HasPlayback;
        PlaybackTime = $"{FormatTime(frame.Elapsed)} / {FormatTime(frame.Total)}"; PlaybackPosition = $"{frame.ActiveOperationName ?? "—"}   X {frame.Position.X:0.000}  Y {frame.Position.Y:0.000}  Z {frame.Z:0.000}";
        var thickness = Math.Max(0.001, Document.Job.ThicknessMm); var depth = Math.Max(0, -frame.Z); var percent = depth / thickness * 100;
        PlaybackDepth = frame.Z > 0.0001 ? $"Above material · Z {frame.Z:0.###}" : frame.Z < -thickness ? $"Over-depth · {depth:0.###} mm · {percent:0.#}%" : $"Depth {depth:0.###} mm · {percent:0.#}%";
    }
    public void ClearPlayback() { Playback.Clear(); PlaybackSessionLabel = "No preview loaded"; UpdatePlaybackStatus(); }
    public SafetyPreflightResult RunPreflight(IReadOnlyList<CamOperationDefinition>? operations=null,ToolpathProgram? program=null)
    {
        var activeOperations=operations??CamJob.Operations.Where(MatchesActiveSheet).ToArray();LastPreflight=_safetyPreflight.Check(new SafetyPreflightRequest(Document.Job,CamJob.Machine,CamJob.Fixtures,activeOperations,program??Program));
        SafetyFindings.Clear();foreach(var finding in LastPreflight.Findings)SafetyFindings.Add(finding);
        PreflightStatus=LastPreflight.Status;HasPreflightErrors=LastPreflight.ErrorCount>0;HasPreflightWarnings=LastPreflight.WarningCount>0;
        PreflightSummary=$"{LastPreflight.ErrorCount} errors · {LastPreflight.WarningCount} warnings · deepest {LastPreflight.DeepestCutMm:0.###} mm"+(LastPreflight.MinimumFixtureClearanceMm is { } clearance?$" · minimum clearance {clearance:0.###} mm":"");
        return LastPreflight;
    }
    public void InvalidatePreflight(){LastPreflight=SafetyPreflightResult.NotChecked;SafetyFindings.Clear();PreflightStatus="Not checked";PreflightSummary="Run preflight before preview or export.";HasPreflightErrors=false;HasPreflightWarnings=false;}
    public void SelectMachineSnapshot(){if(SelectedMachine is null)throw new InvalidOperationException("Select a machine profile.");CamJob.Machine=SelectedMachine.Snapshot();CamJob.SafetySetupIncomplete=false;InvalidatePreflight();IsDirty=true;Message=$"Machine snapshot updated from {SelectedMachine.Name}";}
    public async Task SaveSelectedMachineAsync(){if(SelectedMachine is null||_machineProfiles is null)throw new InvalidOperationException("Select a machine profile.");if(!SelectedMachine.Snapshot().IsComplete)throw new InvalidOperationException("Complete all required machine limits, rates, clearance and holder values.");await _machineProfiles.SaveAsync(SelectedMachine);SelectMachineSnapshot();Message=$"Saved {SelectedMachine.Name} and updated this job's snapshot";}
    public void AddDefaultFixture(){var index=CamJob.Fixtures.Count+1;var fixture=new FixtureDefinition{Name=$"Clamp {index}",MinXmm=10+(index-1)*40,MinYmm=10,MaxXmm=40+(index-1)*40,MaxYmm=40,TopZmm=10,RequiredClearanceMm=2};CamJob.Fixtures.Add(fixture);Fixtures.Add(fixture);SelectedFixture=fixture;InvalidatePreflight();IsDirty=true;}
    public void FixtureChanged(){InvalidatePreflight();IsDirty=true;Message="Fixture updated — run preflight again";}
    public void DeleteSelectedFixture(){if(SelectedFixture is null)return;CamJob.Fixtures.Remove(SelectedFixture);Fixtures.Remove(SelectedFixture);SelectedFixture=null;InvalidatePreflight();IsDirty=true;}
    private void FinishPlaybackLoad() { Playback.SetSpeed(PlaybackSpeed); UpdatePlaybackStatus(); }
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    private void RebuildProgram() { Program.Name = Document.Job.Name; Program.Operations = CamJob.Operations.OrderBy(o => o.Order).Where(o => o.IsEnabled && o.Status == OperationStatus.Current && o.CalculatedToolpath is not null&&MatchesActiveSheet(o)).Select(o => o.CalculatedToolpath!).ToList(); }
    private bool MatchesActiveSheet(CamOperationDefinition operation){var sheet=operation switch{ProfileOperationDefinition p=>p.MaterialSheetId,EngraveOperationDefinition e=>e.MaterialSheetId,VCarveOperationDefinition v=>v.MaterialSheetId,_=>null};return SelectedMaterialSheet is null?sheet is null:sheet==SelectedMaterialSheet.Id;}
    private void RefreshOperations() { var selected = SelectedOperation; Operations.Clear(); foreach (var operation in CamJob.Operations.OrderBy(o => o.Order).Where(MatchesActiveSheet)) Operations.Add(operation); SelectedOperation = selected is not null&&Operations.Contains(selected)?selected:null; }
    public async Task AddToolAsync() { var tool = new CncTool { Name = "6mm End Mill", ToolNumber = Tools.Count + 1, DiameterMm = 6, CuttingLengthMm=24, OverallLengthMm=50, ShankDiameterMm=6, StickOutMm=30 }; await _toolLibrary.SaveAsync(tool); Tools.Add(tool); SelectedTool = tool; Message = "Tool added to the persistent library"; }
    public async Task SaveProjectAsync(string path)
    {
        var fullPath=Path.GetFullPath(path);var directory=Path.GetDirectoryName(fullPath)??Directory.GetCurrentDirectory();Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,$".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try{await using(var stream=File.Create(temporary)){await _projectSerializer.SaveAsync(new CncProject { Document = Document, CamJob = CamJob,Nesting=NestingJob }, stream);await stream.FlushAsync();}File.Move(temporary,fullPath,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        ProjectPath = fullPath; IsDirty = false; JobTitle = Document.Job.Name; Message = $"Saved {Path.GetFileName(path)}";
        await _recentProjectStore.AddAsync(ProjectPath);
        await RefreshRecentProjectsAsync();
    }
    public async Task OpenProjectAsync(string path)
    {
        ClearPlayback();
        await using var stream = File.OpenRead(path);
        var loaded = await _projectSerializer.LoadAsync(stream); var document = loaded.Document;
        Document.Version = document.Version; Document.Job = document.Job; Document.Layers = document.Layers;Document.ActiveLayerId=document.ActiveLayerId; Document.Entities = document.Entities; CamJob = loaded.CamJob;NestingJob=loaded.Nesting;RefreshNesting(); Fixtures.Clear();foreach(var fixture in CamJob.Fixtures)Fixtures.Add(fixture);Selection.Clear(); _history.Clear(); UpdateHistoryState(); SelectionChanged();InvalidatePreflight();RefreshLayers();
        Program.Name = document.Job.Name; RecalculateAll();
        ProjectPath = Path.GetFullPath(path); HasJob = true; IsDirty = false; JobTitle = document.Job.Name;
        GCodePreview = "Toolpaths are recalculated after opening a project."; Message = $"Opened {Path.GetFileName(path)}";
        RefreshWorkspaceState(); OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary));
        await _recentProjectStore.AddAsync(ProjectPath);
        await RefreshRecentProjectsAsync();
    }
    public async Task RemoveRecentProjectAsync(string path) { await _recentProjectStore.RemoveAsync(path); await RefreshRecentProjectsAsync(); }
    public async Task ClearRecentProjectsAsync() { await _recentProjectStore.ClearAsync(); await RefreshRecentProjectsAsync(); }
    private async Task RefreshRecentProjectsAsync()
    {
        RecentProjects.Clear();
        foreach (var project in await _recentProjectStore.GetAsync()) RecentProjects.Add(project);
    }
    public void SetWorkspaceFocus(WorkspaceFocus focus) { WorkspaceFocus = focus; RefreshWorkflowStage(); }
    public void SetWorkspaceView(WorkspaceView view) { ActiveWorkspaceView = view; IsToolpathPreview=view==WorkspaceView.ToolpathPreview; ShowPlaybackControls=IsToolpathPreview&&HasPlayback; RefreshWorkflowStage(); }
    public void SetActiveToolGuidance(CanvasTool tool)
    {
        IsSelectActive = tool == CanvasTool.Select;
        ShowExactLine=tool==CanvasTool.Line;ShowExactPolyline=tool==CanvasTool.Polyline;ShowExactRectangle=tool==CanvasTool.Rectangle;ShowExactCircle=tool==CanvasTool.Circle;ShowExactArc=tool==CanvasTool.Arc;
        DesignGuidance = tool switch
        {
            CanvasTool.Select => HasSelection ? $"{SelectionStatus}. Drag to move. Corner handles resize proportionally; hold Alt/Option for free resize." : "Click an object or drag a selection box. Hold Ctrl/Shift to add to the selection.",
            CanvasTool.Line => "Click a start and end point. Escape cancels the unfinished line.",
            CanvasTool.Polyline => "Click each point. Enter or double-click finishes; C closes; Escape cancels.",
            CanvasTool.Rectangle => "Drag from one corner to the opposite corner. Escape cancels.",
            CanvasTool.Circle => "Click the centre, then click the radius. Escape cancels.",
            CanvasTool.Trim => "Scissors: hover the portion to remove, then click. Cutting vectors remain unchanged.",
            _ => "Click the start, a point on the arc, then the end. Escape cancels."
        };
    }
    private void RefreshWorkspaceState()
    {
        HasGeometry = Document.Entities.Count > 0; HasOperations = CamJob.Operations.Count > 0; ShowEmptyDesign = HasJob && !HasGeometry;
        RefreshWorkflowStage();
        if (!HasJob) { CanCreateProfile = false; ProfileGuidance = "Create a job, then select closed vectors."; }
        else if (!HasSelection) { CanCreateProfile = false; ProfileGuidance = "Select one or more rectangles, circles, or closed polylines."; }
        else
        {
            var selected = Document.Entities.Where(entity => Selection.Contains(entity.Id)).ToArray();
            var invalid = selected.FirstOrDefault(entity => !IsProfileContour(entity));
            CanCreateProfile = invalid is null && selected.Length > 0;
            ProfileGuidance = CanCreateProfile ? $"{selected.Length} closed contour{(selected.Length == 1 ? "" : "s")} ready for profiling." : $"{invalid!.GetType().Name.Replace("Entity", string.Empty)} is open or unsupported. Select only closed contours.";
        }
        if (ActivePaletteItem is { } item) SetActiveToolGuidance(item.Tool);
    }
    private void RefreshWorkflowStage()=>WorkflowStage=!HasJob?"1  Job Setup":IsToolpathPreview?"4  Preview & Export":WorkspaceFocus==WorkspaceFocus.Toolpaths?"3  Toolpaths":"2  Design";
    private static bool IsProfileContour(CadEntity entity)=>entity is RectangleEntity or CircleEntity or PolylineEntity{IsClosed:true,Points.Count:>=3}||entity is VectorPathEntity{Figures.Count:1} path&&path.Figures[0].IsClosed;
}
