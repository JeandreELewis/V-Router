using System.Collections.ObjectModel;
using CncCad.Cam.Profile;
using CncCad.Cam.Pocket;
using CncCad.Cam.Drilling;
using CncCad.Cam.Surface;
using CncCad.Cam.Operations;
using CncCad.Cam.Tools;
using CncCad.Cam.Toolpaths;
using CncCad.Cam.Safety;
using CncCad.Cam.Nesting;
using CncCad.Core.Documents;
using CncCad.Core.Commands;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Preview;
using CncCad.Core.Selection;
using CncCad.Core.Snapping;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using CncCad.IO.Projects;
using CncCad.IO.Import;
using CncCad.PostProcessors.Models;
using CncCad.PostProcessors.GCode;
using CncCad.PostProcessors.Uccnc;
using CncCad.Rendering.Canvas;
using CncCad.Simulation.MachineTime;
using CncCad.Simulation.Playback;
using CncCad.Simulation.Stock;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncCad.App.ViewModels;

public enum WorkspaceFocus { Balanced, Design, Toolpaths }
public enum WorkspaceView { Design, ToolpathPreview }
public enum LibraryExchangeFormat { CompleteJson, ToolsCsv, MaterialsCsv, MachinesCsv, PresetsCsv }

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IToolLibrary _toolLibrary;
    private readonly IRecentProjectStore _recentProjectStore;
    private readonly IMachineProfileRepository? _machineProfiles;
    private readonly ISafetyPreflightService _safetyPreflight = new SafetyPreflightService();
    private readonly ICamOperationCalculator _operationCalculator = new CompositeOperationCalculator(new ProfileOperationCalculator(),new PocketOperationCalculator(),new DrillOperationCalculator(),new EngraveOperationCalculator(),new VCarveOperationCalculator(),new SurfaceOperationCalculator());
    private readonly ICadOffsetService _offsetService = new CadOffsetService();
    private readonly IPostProcessorRegistry _postProcessors;
    private readonly IStockSimulationService _stockSimulationService = new CpuStockSimulationService();
    private readonly IRecoveryStore? _recoveryStore;
    private readonly IMaterialLibrary? _materialLibrary;
    private readonly IToolCuttingPresetRepository? _presetRepository;
    private readonly LibraryExchangeService? _libraryExchange;
    private readonly string _recoveryJobKey=Guid.NewGuid().ToString("N");
    private DateTimeOffset _dirtySinceUtc=DateTimeOffset.UtcNow;
    private DateTimeOffset _lastRecoveryUtc=DateTimeOffset.MinValue;
    private readonly JsonProjectSerializer _projectSerializer = new();
    private readonly IVectorImportService _vectorImporter = new CompositeVectorImportService([new SvgVectorImporter(),new DxfVectorImporter()]);
    private readonly IVectorRepairService _repairService = new VectorRepairService();
    private readonly IVectorTopologyService _topologyService = new VectorTopologyService();
    private readonly IRouterVectorPreparationService _routerPreparationService = new RouterVectorPreparationService();
    private readonly ITextOutlineService _textOutlineService = new PortableFallbackTextOutlineService();
    private readonly IFontCatalog? _fontCatalog;
    private readonly INestingOptimizer _nestingOptimizer=new DeterministicNestingOptimizer();
    private readonly CommandHistory _history = new();
    private readonly List<CadEntity> _clipboard = [];
    private readonly HashSet<Guid> _installedNestingEntityIds=[];
    private readonly HashSet<Guid> _installedNestingLayerIds=[];
    private readonly Dictionary<ReplaceImportedLayoutCommand,NestingHistoryState> _nestingHistory=[];
    private NestingResult? _installedNestingLayout;
    private int _pasteCount;
    public CadDocument Document { get; } = new();
    public CamJob CamJob { get; private set; } = new();
    public NestingJobDefinition NestingJob { get; private set; }=new();
    public ToolpathProgram Program { get; } = new() { Name = "Untitled job" };
    public ObservableCollection<CncTool> Tools { get; } = [];
    public ObservableCollection<CncTool> FilteredTools { get; } = [];
    public ObservableCollection<MachineProfileDefinition> FilteredMachines { get; } = [];
    public ObservableCollection<MaterialDefinition> FilteredMaterials { get; } = [];
    public ObservableCollection<ToolCuttingPreset> FilteredToolPresets { get; } = [];
    public IReadOnlyList<string> ToolCategories { get; }=["All",..Enum.GetNames<ToolType>()];
    public ObservableCollection<MachineProfileDefinition> Machines { get; } = [];
    public ObservableCollection<FixtureDefinition> Fixtures { get; } = [];
    public ObservableCollection<SafetyFinding> SafetyFindings { get; } = [];
    public ObservableCollection<CamOperationDefinition> Operations { get; } = [];
    public ObservableCollection<RecentProject> RecentProjects { get; } = [];
    public ObservableCollection<RecoverySnapshotInfo> RecoverableProjects { get; } = [];
    public ObservableCollection<MaterialDefinition> Materials { get; } = [];
    public ObservableCollection<ToolCuttingPreset> ToolPresets { get; } = [];
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
    public ShellWorkspaceViewModel Shell { get; }
    public DesignWorkspaceViewModel DesignWorkspace { get; }
    public ToolpathWorkspaceViewModel ToolpathWorkspace { get; private set; }
    public PreviewWorkspaceViewModel PreviewWorkspace { get; }
    public ExportWorkspaceViewModel ExportWorkspace { get; }
    public IReadOnlyList<double> PlaybackSpeeds => ToolpathPlaybackSession.SupportedSpeeds;
    public IReadOnlyList<GCodeExportFormat> ExportFormats => GCodeExportFormat.All;
    public IReadOnlyList<LibraryExchangeFormat> LibraryExchangeFormats { get; }=Enum.GetValues<LibraryExchangeFormat>();
    [ObservableProperty] private CncTool? selectedTool;
    [ObservableProperty] private string toolSearch="";
    [ObservableProperty] private string selectedToolCategory="All";
    [ObservableProperty] private string machineSearch="";
    [ObservableProperty] private string materialSearch="";
    [ObservableProperty] private string presetSearch="";
    [ObservableProperty] private CadLayer? selectedLayer;
    [ObservableProperty] private string layerNameDraft="";
    [ObservableProperty] private string layerColorDraft="#61C8FF";
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
    [ObservableProperty] private WorkspaceMode workspaceMode = WorkspaceMode.Startup;
    [ObservableProperty] private ViewportLayout viewportLayout = ViewportLayout.Design2D;
    [ObservableProperty] private ResponsiveLayoutState responsiveLayout = ResponsiveLayoutState.Regular;
    [ObservableProperty] private DesignTaskPage designTaskPage = DesignTaskPage.Palette;
    [ObservableProperty] private ToolpathTaskPage toolpathTaskPage = ToolpathTaskPage.Palette;
    [ObservableProperty] private StockHeightfield? stockSimulation;
    [ObservableProperty] private ProjectPreviewSettings previewSettings = ProjectPreviewSettings.LegacyDefault;
    [ObservableProperty] private MaterialDefinition? selectedMaterial;
    [ObservableProperty] private ToolCuttingPreset? selectedToolPreset;
    [ObservableProperty] private LibraryExchangeFormat selectedLibraryExchangeFormat;
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
    [ObservableProperty] private CamOperationDefinition? operationEditorDraft;
    [ObservableProperty] private CamOperationDefinition? operationEditorTarget;
    [ObservableProperty] private bool isOperationEditorOpen;
    [ObservableProperty] private bool isNewOperationEditor;
    [ObservableProperty] private bool isAdvancedMode;
    [ObservableProperty] private string workflowStage = "1  Job Setup";
    [ObservableProperty] private string designGuidance = "Create a job to begin drawing.";
    [ObservableProperty] private string profileGuidance = "Create a job, then select closed vectors.";
    [ObservableProperty] private bool canCreateProfile;
    [ObservableProperty] private bool hasGeometry;
    [ObservableProperty] private bool hasOperations;
    [ObservableProperty] private bool hasClipboard;
    [ObservableProperty] private WorkspaceFocus workspaceFocus = WorkspaceFocus.Design;
    [ObservableProperty] private WorkspaceView activeWorkspaceView;
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
    [ObservableProperty] private NestGrainDirection nestGrain=NestGrainDirection.AlongX;
    [ObservableProperty] private NestGrainDirection nestSheetGrain=NestGrainDirection.AlongX;
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
    [ObservableProperty] private double textPathStart=.5;
    [ObservableProperty] private bool textPathReverse;
    [ObservableProperty] private bool textPathFlip;
    [ObservableProperty] private bool textPathFit;
    [ObservableProperty] private bool textKeepUpright=true;
    public IReadOnlyList<TextAlignment> TextAlignments=>Enum.GetValues<TextAlignment>();
    public IReadOnlyList<ProfileSide> ProfileSides { get; } = Enum.GetValues<ProfileSide>();
    public IReadOnlyList<CutDirection> CutDirections { get; } = Enum.GetValues<CutDirection>();
    public IReadOnlyList<PocketEntryMode> PocketEntryModes { get; } = Enum.GetValues<PocketEntryMode>();
    public IReadOnlyList<PocketStrategy> PocketStrategies { get; } = Enum.GetValues<PocketStrategy>();
    public IReadOnlyList<ProfileLeadStyle> ProfileLeadStyles { get; } = Enum.GetValues<ProfileLeadStyle>();
    public IReadOnlyList<ProfileEntryMode> ProfileEntryModes { get; } = Enum.GetValues<ProfileEntryMode>();
    public IReadOnlyList<TabStyle> TabStyles { get; } = Enum.GetValues<TabStyle>();
    public IReadOnlyList<SurfaceStrategy> SurfaceStrategies { get; } = Enum.GetValues<SurfaceStrategy>();
    public IReadOnlyList<NestGrainDirection> NestGrainDirections { get; } = Enum.GetValues<NestGrainDirection>();
    public IReadOnlyList<ControllerFamily> ControllerFamilies { get; } = Enum.GetValues<ControllerFamily>();
    public IReadOnlyList<ToolType> ToolTypes { get; } = Enum.GetValues<ToolType>();
    public IReadOnlyList<MeasurementUnit> MeasurementUnits { get; } = Enum.GetValues<MeasurementUnit>();
    public IReadOnlyList<MachineToolChangeMode> MachineToolChangeModes { get; } = Enum.GetValues<MachineToolChangeMode>();
    public IReadOnlyList<string> PostProcessorIds => _postProcessors.Descriptors.Select(x=>x.Id).ToArray();
    public IReadOnlyList<DrillCycle> DrillCycles { get; } = Enum.GetValues<DrillCycle>();
    public IReadOnlyList<DrillOrdering> DrillOrderings { get; } = Enum.GetValues<DrillOrdering>();
    public IReadOnlyList<EngraveDirection> EngraveDirections { get; } = Enum.GetValues<EngraveDirection>();
    public IReadOnlyList<TabPlacementMode> TabPlacementModes { get; } = Enum.GetValues<TabPlacementMode>();
    [ObservableProperty] private string vectorColor="#61C8FF";
    [ObservableProperty] private bool useDrawingColor;
    [ObservableProperty] private int cornerIndex=1;
    [ObservableProperty] private double chamferDistance=5;
    [ObservableProperty] private double filletRadius=5;
    [ObservableProperty] private double cornerReliefToolDiameter=6;
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
    public bool IsRegularLayout=>ResponsiveLayout==ResponsiveLayoutState.Regular;
    public bool IsCompactLayout=>ResponsiveLayout==ResponsiveLayoutState.Compact;
    public bool ShowRegularPreviewAppearance=>IsToolpathPreview&&IsRegularLayout;
    public bool IsDesignMode=>WorkspaceMode==CncCad.Infrastructure.Settings.WorkspaceMode.Design;
    public bool IsToolpathsMode=>WorkspaceMode==CncCad.Infrastructure.Settings.WorkspaceMode.Toolpaths;
    public bool IsDesignPalette=>DesignTaskPage==DesignTaskPage.Palette;
    public bool IsDesignConstructionTask=>DesignTaskPage==DesignTaskPage.Construction;
    public bool IsDesignPositionTask=>DesignTaskPage==DesignTaskPage.Position;
    public bool IsDesignTransformTask=>DesignTaskPage==DesignTaskPage.Transform;
    public bool IsDesignEditTask=>DesignTaskPage==DesignTaskPage.Edit;
    public bool IsDesignOffsetTask=>DesignTaskPage==DesignTaskPage.OffsetLayout;
    public bool IsDesignTextTask=>DesignTaskPage==DesignTaskPage.TextShapes;
    public bool IsDesignAppearanceTask=>DesignTaskPage==DesignTaskPage.Appearance;
    public bool IsDesignRepairTask=>DesignTaskPage==DesignTaskPage.Repair;
    public bool IsDesignSnappingTask=>DesignTaskPage==DesignTaskPage.Snapping;
    public bool IsDesignLayersTask=>DesignTaskPage==DesignTaskPage.Layers;
    public bool IsDesignNestingTask=>DesignTaskPage==DesignTaskPage.Nesting;
    public bool IsDesignJobTask=>DesignTaskPage==DesignTaskPage.Job;
    public bool IsToolpathPalette=>ToolpathTaskPage==ToolpathTaskPage.Palette;
    public bool IsToolLibraryTask=>ToolpathTaskPage==ToolpathTaskPage.ToolLibrary;
    public bool IsMachineFixturesTask=>ToolpathTaskPage==ToolpathTaskPage.MachineFixtures;
    public string DesignTaskTitle=>DesignTaskPage switch{DesignTaskPage.Palette=>"DESIGN",DesignTaskPage.Construction=>"NUMERIC CONSTRUCTION",DesignTaskPage.Position=>"POSITION",DesignTaskPage.Transform=>"TRANSFORM & ALIGN",DesignTaskPage.Edit=>"EDIT VECTORS",DesignTaskPage.OffsetLayout=>"OFFSET & LAYOUT",DesignTaskPage.TextShapes=>"SHAPES & TEXT",DesignTaskPage.Appearance=>"VECTOR APPEARANCE",DesignTaskPage.Repair=>"REPAIR & NODE EDIT",DesignTaskPage.Snapping=>"SNAPPING",DesignTaskPage.Layers=>"LAYERS",DesignTaskPage.Nesting=>"MATERIAL NESTING",_=>"JOB SETUP"};
    public string ToolpathTaskTitle=>ToolpathTaskPage switch{ToolpathTaskPage.Palette=>"TOOLPATHS",ToolpathTaskPage.OperationEditor=>OperationEditorDraft?.Name?.ToUpperInvariant()??"TOOLPATH EDITOR",ToolpathTaskPage.ToolLibrary=>"TOOL & MATERIAL LIBRARY",_=>"MACHINE & FIXTURES"};
    public bool IsMaterialPreview=>PreviewSettings.Style==PreviewRenderStyle.Material;
    public bool CanUseChamferFillet=>Selection.Count==1&&Document.Entities.SingleOrDefault(entity=>Selection.Contains(entity.Id)) is PolylineEntity{Points.Count:>=3} path&&!path.IsLocked&&Document.Layers.FirstOrDefault(layer=>layer.Id==path.LayerId)?.IsLocked!=true;
    private bool ActiveNodeEditable=>_activeNodeEntity is{IsLocked:false} entity&&Document.Layers.FirstOrDefault(layer=>layer.Id==entity.LayerId)?.IsLocked!=true;
    public bool CanInsertNode=>ActiveNodeEditable&&_activeNodeIndex>=0&&(_activeNodeEntity is PolylineEntity polyline&&(polyline.IsClosed||_activeNodeIndex<polyline.Points.Count-1)||_activeNodeEntity is VectorPathEntity vector&&VectorPathNodes.CanInsertAfter(vector,_activeNodeIndex));
    public bool CanDeleteNode=>ActiveNodeEditable&&_activeNodeIndex>=0&&(_activeNodeEntity is PolylineEntity polyline&&polyline.Points.Count>(polyline.IsClosed?3:2)||_activeNodeEntity is VectorPathEntity vector&&VectorPathNodes.CanDelete(vector,_activeNodeIndex));
    public bool CanMakeStartNode=>ActiveNodeEditable&&_activeNodeIndex>=0&&(_activeNodeEntity is PolylineEntity{IsClosed:true}&&_activeNodeIndex>0||_activeNodeEntity is VectorPathEntity vector&&VectorPathNodes.CanMakeStart(vector,_activeNodeIndex));
    public bool CanApplyNodePosition=>ActiveNodeEditable&&_activeNodeIndex>=0&&_activeNodeEntity is not null&&(_activeNodeEntity is not VectorPathEntity vector||VectorPathNodes.CanMove(vector,_activeNodeIndex));
    public bool CanApplyRepair=>_repairAnalysis is not null&&(_repairAnalysis.RemoveIds.Count>0||_repairAnalysis.Replacements.Count>0);
    public bool CanAddCornerRelief=>CornerReliefEligibility().Success;
    public bool CanUseAdvancedTextSettings=>true;
    public bool CanCreateVCarve=>HasJob&&HasSelection&&SelectedTool is{Type:ToolType.VBit or ToolType.EngravingBit,VBitAngleDegrees:>0 and <180}&&SelectedEntities().All(IsVCarveContour)&&SelectedEntities().Select(entity=>entity.MaterialSheetId).Distinct().Count()==1;
    public bool CanAcceptNest=>NestingJob.Status==NestingLayoutStatus.Stale&&NestingJob.AcceptedLayout?.Success==true;
    public bool CanOffsetSelection=>HasSelection&&Document.Entities.Where(entity=>Selection.Contains(entity.Id)).All(IsOffsetEligible);
    public bool CanSaveToolpaths
    {
        get
        {
            var postId=CamJob.Machine?.PostProcessorId??SelectedMachine?.PostProcessorId;
            return HasOperations&&postId is not null&&_postProcessors.Descriptors.Any(x=>x.Id.Equals(postId,StringComparison.OrdinalIgnoreCase)&&x.CertificationStatus==PostCertificationStatus.Certified);
        }
    }
    public string StabilizationNotice=>"Unavailable until its geometry and safety conformance tests pass.";
    public string VCarveGuidance=>CanCreateVCarve?"Closed outlines and a valid V-bit are ready for variable-depth carving.":"Select only closed outline vectors on one sheet and choose a V-bit with a valid included angle.";
    public string CornerEditGuidance=>CanUseChamferFillet?"Uses world-millimetre distances. Fillets remain exact circular arcs.":"Select one unlocked polyline with at least three nodes.";
    public string CornerReliefGuidance{get{var result=CornerReliefEligibility();return result.Success?"Selected inside corner is ready. Cutter diameter is applied in world millimetres.":result.Error??"Select one inside corner on a closed polyline.";}}
    public string ExportAvailabilityMessage=>CanSaveToolpaths?"Certified machine output is available.":"Runnable G-code saving is disabled while the selected controller post is uncertified. Preview and preflight remain available.";
    public string PreviewAppearanceSummary=>PreviewSettings.Style==PreviewRenderStyle.Technical?"Technical shaded preview":$"{PreviewSettings.ResolveMaterial(CamJob.Material?.Name,CamJob.Material?.Category)} · {(PreviewSettings.GrainDirection==GrainDirection.AlongStockY?"grain along Y":"grain along X")}";

    public void TogglePreviewStyle()=>UpdatePreviewSettings(PreviewSettings with{Style=PreviewSettings.Style==PreviewRenderStyle.Technical?PreviewRenderStyle.Material:PreviewRenderStyle.Technical});
    public void SetPreviewMaterial(MaterialAppearancePreset preset)=>UpdatePreviewSettings(PreviewSettings with{Style=PreviewRenderStyle.Material,MaterialPreset=preset});
    public void SetPreviewGrain(GrainDirection direction)=>UpdatePreviewSettings(PreviewSettings with{GrainDirection=direction});
    public void SetPreviewTint(string? tint)=>UpdatePreviewSettings(PreviewSettings with{TintHex=tint});
    public void ResetPreviewAppearance()=>UpdatePreviewSettings(ProjectPreviewSettings.NewWoodJobDefault);
    private void UpdatePreviewSettings(ProjectPreviewSettings settings)
    {
        settings=settings.Normalize();if(PreviewSettings==settings)return;PreviewSettings=settings;IsDirty=true;Message=$"Preview appearance: {PreviewAppearanceSummary}";
    }
    partial void OnPreviewSettingsChanged(ProjectPreviewSettings value){OnPropertyChanged(nameof(IsMaterialPreview));OnPropertyChanged(nameof(PreviewAppearanceSummary));}

    public MainWindowViewModel(IToolLibrary toolLibrary, IRecentProjectStore recentProjectStore, IMachineProfileRepository? machineProfiles = null,ITextOutlineService? textOutlineService=null,IFontCatalog? fontCatalog=null,IRecoveryStore? recoveryStore=null,IMaterialLibrary? materialLibrary=null,IToolCuttingPresetRepository? presetRepository=null,IPostProcessorRegistry? postProcessors=null) { _toolLibrary = toolLibrary; _recentProjectStore = recentProjectStore; _machineProfiles = machineProfiles;_recoveryStore=recoveryStore;_materialLibrary=materialLibrary;_presetRepository=presetRepository;if(machineProfiles is not null&&materialLibrary is not null&&presetRepository is not null)_libraryExchange=new(toolLibrary,machineProfiles,materialLibrary,presetRepository);_postProcessors=postProcessors??new PostProcessorRegistry(CertifiedPostDefinitions.All.Select(x=>new DeclarativeGCodePostProcessor(x)));if(textOutlineService is not null)_textOutlineService=textOutlineService;_fontCatalog=fontCatalog;Shell=new(SetWorkspaceMode,SetViewportLayout);DesignWorkspace=new(Document,Selection,Layers);ToolpathWorkspace=new(CamJob,Operations,Tools);PreviewWorkspace=new(Playback,()=>StockSimulation);ExportWorkspace=new(_postProcessors); SetActiveToolGuidance(CanvasTool.Select); }
    public async Task LoadAsync()
    {
        await ReloadLibrariesAsync();SelectedTool = Tools.FirstOrDefault(t => t.Name.StartsWith("6mm End Mill")) ?? Tools.FirstOrDefault();
        Fonts.Clear();if(_fontCatalog is not null)foreach(var font in _fontCatalog.Fonts)Fonts.Add(font);SelectedFont=Fonts.FirstOrDefault(x=>x.Descriptor.Family.Equals("Inter",StringComparison.OrdinalIgnoreCase))??Fonts.FirstOrDefault();if(SelectedFont is not null)TextFont=SelectedFont.Descriptor.Family;
        await RefreshRecentProjectsAsync();
        await RefreshRecoveriesAsync();
        RefreshLayers();
    }
    private async Task ReloadLibrariesAsync()
    {
        var toolId=SelectedTool?.Id;var machineId=SelectedMachine?.Id;var materialId=SelectedMaterial?.Id;var presetId=SelectedToolPreset?.Id;Tools.Clear();foreach(var tool in await _toolLibrary.GetAllAsync())Tools.Add(tool);RefreshToolFilter();SelectedTool=Tools.FirstOrDefault(value=>value.Id==toolId)??FilteredTools.FirstOrDefault();if(_machineProfiles is not null){Machines.Clear();foreach(var machine in await _machineProfiles.GetAllAsync())Machines.Add(machine);RefreshMachineFilter();SelectedMachine=Machines.FirstOrDefault(value=>value.Id==machineId)??FilteredMachines.FirstOrDefault();}if(_materialLibrary is not null){Materials.Clear();foreach(var material in await _materialLibrary.GetAllAsync())Materials.Add(material);RefreshMaterialFilter();SelectedMaterial=Materials.FirstOrDefault(value=>value.Id==materialId)??FilteredMaterials.FirstOrDefault();}if(_presetRepository is not null){ToolPresets.Clear();foreach(var preset in await _presetRepository.GetAllAsync())ToolPresets.Add(preset);RefreshPresetFilter();SelectedToolPreset=ToolPresets.FirstOrDefault(value=>value.Id==presetId)??FilteredToolPresets.FirstOrDefault();}
    }
    partial void OnFontSearchChanged(string value)=>RefreshFonts();partial void OnFontKindFilterChanged(string value)=>RefreshFonts();private void RefreshFonts(){if(_fontCatalog is null)return;var selected=SelectedFont?.Descriptor.Family;Fonts.Clear();foreach(var font in _fontCatalog.Fonts.Where(x=>(string.IsNullOrWhiteSpace(FontSearch)||x.Descriptor.Family.Contains(FontSearch,StringComparison.OrdinalIgnoreCase))&&(FontKindFilter=="All"||x.Descriptor.Kind.ToString()==FontKindFilter)))Fonts.Add(font);SelectedFont=Fonts.FirstOrDefault(x=>x.Descriptor.Family==selected)??Fonts.FirstOrDefault();}
    partial void OnToolSearchChanged(string value)=>RefreshToolFilter();partial void OnSelectedToolCategoryChanged(string value)=>RefreshToolFilter();private void RefreshToolFilter(){var selected=SelectedTool;FilteredTools.Clear();foreach(var tool in Tools.Where(x=>(string.IsNullOrWhiteSpace(ToolSearch)||x.Name.Contains(ToolSearch,StringComparison.OrdinalIgnoreCase))&&(SelectedToolCategory=="All"||x.Type.ToString()==SelectedToolCategory)))FilteredTools.Add(tool);SelectedTool=selected is not null&&FilteredTools.Contains(selected)?selected:FilteredTools.FirstOrDefault();}
    partial void OnMachineSearchChanged(string value)=>RefreshMachineFilter();private void RefreshMachineFilter(){var selected=SelectedMachine;FilteredMachines.Clear();foreach(var machine in Machines.Where(value=>string.IsNullOrWhiteSpace(MachineSearch)||$"{value.Name} {value.ControllerFamily} {value.PostProcessorId}".Contains(MachineSearch,StringComparison.OrdinalIgnoreCase)))FilteredMachines.Add(machine);SelectedMachine=selected is not null&&FilteredMachines.Contains(selected)?selected:FilteredMachines.FirstOrDefault();}
    partial void OnMaterialSearchChanged(string value)=>RefreshMaterialFilter();private void RefreshMaterialFilter(){var selected=SelectedMaterial;FilteredMaterials.Clear();foreach(var material in Materials.Where(value=>string.IsNullOrWhiteSpace(MaterialSearch)||$"{value.Name} {value.Category} {value.Notes}".Contains(MaterialSearch,StringComparison.OrdinalIgnoreCase)))FilteredMaterials.Add(material);SelectedMaterial=selected is not null&&FilteredMaterials.Contains(selected)?selected:FilteredMaterials.FirstOrDefault();}
    partial void OnPresetSearchChanged(string value)=>RefreshPresetFilter();private void RefreshPresetFilter(){var selected=SelectedToolPreset;FilteredToolPresets.Clear();foreach(var preset in ToolPresets.Where(value=>string.IsNullOrWhiteSpace(PresetSearch)||PresetText(value).Contains(PresetSearch,StringComparison.OrdinalIgnoreCase)))FilteredToolPresets.Add(preset);SelectedToolPreset=selected is not null&&FilteredToolPresets.Contains(selected)?selected:FilteredToolPresets.FirstOrDefault();}
    private string PresetText(ToolCuttingPreset preset)=>$"{Tools.FirstOrDefault(value=>value.Id==preset.ToolId)?.Name} {Machines.FirstOrDefault(value=>value.Id==preset.MachineId)?.Name} {Materials.FirstOrDefault(value=>value.Id==preset.MaterialId)?.Name} {preset.FeedRateMmPerMin} {preset.SpindleRpm}";
    partial void OnSelectedMaterialChanged(MaterialDefinition? value){RefreshPresetFilter();if(value is not null&&HasJob){CamJob.Material=new(){SourceMaterialId=value.Id,Name=value.Name,Category=value.Category};IsDirty=true;OnPropertyChanged(nameof(PreviewAppearanceSummary));}}
    partial void OnSelectedMachineChanged(MachineProfileDefinition? value)=>RefreshPresetFilter();
    partial void OnSelectedToolChanged(CncTool? value){RefreshPresetFilter();OnPropertyChanged(nameof(CanCreateVCarve));OnPropertyChanged(nameof(VCarveGuidance));}
    public void CreateJob(JobSettings settings)
    {
        ClearPlayback();
        PreviewSettings=ProjectPreviewSettings.NewWoodJobDefault;
        Document.Job = settings;
        Document.Entities.Clear(); Document.Layers = [new()];Document.ActiveLayerId=Document.Layers[0].Id; CamJob = new() { Machine = SelectedMachine?.Snapshot(), SafetySetupIncomplete = SelectedMachine is null };ToolpathWorkspace=new(CamJob,Operations,Tools);OnPropertyChanged(nameof(ToolpathWorkspace));NestingJob=new(){SheetTemplate=new(){WidthMm=settings.WidthMm,HeightMm=settings.HeightMm,ThicknessMm=settings.ThicknessMm}};_installedNestingEntityIds.Clear();_installedNestingLayerIds.Clear();_installedNestingLayout=null;_nestingHistory.Clear();NestParts.Clear();MaterialSheets.Clear(); Program.Operations.Clear(); Operations.Clear(); Fixtures.Clear(); Selection.Clear(); _history.Clear(); UpdateHistoryState();RefreshLayers();
        Program.Name = settings.Name; ProjectPath = null; HasJob = true; IsDirty = true; JobTitle = settings.Name;WorkspaceMode=WorkspaceMode.Design;
        if(SelectedMaterial is not null)CamJob.Material=new(){SourceMaterialId=SelectedMaterial.Id,Name=SelectedMaterial.Name,Category=SelectedMaterial.Category};
        GCodePreview = "Create a profile toolpath to preview G-code.";
        Message = $"Created {settings.WidthMm:0.##} × {settings.HeightMm:0.##} × {settings.ThicknessMm:0.##} mm job";
        RefreshWorkspaceState(); OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary));
    }
    public void UpdateJob(JobSettings settings)
    {
        if(SetJobSettingsCommand.Equivalent(Document.Job,settings))return;
        _history.Execute(new SetJobSettingsCommand(Document,settings));
        UpdateHistoryState();RefreshJobPresentation();InvalidateAllOperationsForJobSetup();IsDirty=true;Message="Job setup updated";
    }
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
        var family=SelectedFont?.Descriptor.Family??TextFont;var result=_textOutlineService.Create(new(TextValue,family,TextFontStyle,TextSize,TextLetterSpacing,TextLineSpacing,TextAlignment,new(Point1X,Point1Y)));var source=VectorPathGeometry.Clone(result.Outlines);var guide=Document.Entities.SingleOrDefault(x=>Selection.Contains(x.Id)&&x is CircleEntity or ArcEntity or EllipseEntity or PolylineEntity or VectorPathEntity);var placement=new TextPathPlacement{GuideEntityId=guide?.Id,StartOffset=TextPathStart,BaselineOffsetMm=TextPathOffset,ReverseDirection=TextPathReverse,FlipSide=TextPathFlip,KeepUpright=TextKeepUpright,FitMode=TextPathFit?TextFitMode.FitSpacing:TextFitMode.Natural,GuideSnapshot=guide is null?[]:[..EntityGeometry.WorldPoints(guide)]};var layout=PlaceGlyphs(result,placement,guide is not null);AddEntity(new TextEntity{LayerId=Document.ActiveLayerId,Text=TextValue,FontFamily=family,FontStyle=TextFontStyle,SizeMm=TextSize,LetterSpacingMm=TextLetterSpacing,LineSpacing=TextLineSpacing,Alignment=TextAlignment,FontKind=result.Kind,LayoutMode=guide is null?TextLayoutMode.Straight:TextLayoutMode.FollowPath,PathPlacement=placement,GlyphAdvancesMm=[..result.GlyphAdvancesMm??[]],SourceOutlines=source,Outlines=layout.Outlines,Glyphs=layout.Glyphs});if(result.Warnings.Count>0)Message=result.Warnings[0];
    }
    public void UpdateSelectedText()
    {
        var text=SelectedEntities().OfType<TextEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one editable text object.");
        var family=SelectedFont?.Descriptor.Family??text.FontFamily;
        var result=_textOutlineService.Create(new(TextValue,family,TextFontStyle,TextSize,TextLetterSpacing,TextLineSpacing,TextAlignment,new(Point1X,Point1Y)));
        var source=VectorPathGeometry.Clone(result.Outlines);
        var placement=new TextPathPlacement{GuideEntityId=text.PathPlacement.GuideEntityId,StartOffset=TextPathStart,BaselineOffsetMm=TextPathOffset,ReverseDirection=TextPathReverse,FlipSide=TextPathFlip,KeepUpright=TextKeepUpright,FitMode=TextPathFit?TextFitMode.FitSpacing:TextFitMode.Natural,GuideSnapshot=[..text.PathPlacement.GuideSnapshot]};
        if(placement.GuideEntityId is{} guideId&&Document.Entities.FirstOrDefault(x=>x.Id==guideId) is{} guide)placement.GuideSnapshot=[..EntityGeometry.WorldPoints(guide)];
        var layout=PlaceGlyphs(result,placement,placement.GuideEntityId is not null);
        var replacement=(TextEntity)EntityGeometry.Clone(text);replacement.Text=TextValue;replacement.FontFamily=family;replacement.FontStyle=TextFontStyle;replacement.SizeMm=TextSize;replacement.LetterSpacingMm=TextLetterSpacing;replacement.LineSpacing=TextLineSpacing;replacement.Alignment=TextAlignment;replacement.FontKind=result.Kind;replacement.LayoutMode=placement.GuideEntityId is null?TextLayoutMode.Straight:TextLayoutMode.FollowPath;replacement.PathPlacement=placement;replacement.GlyphAdvancesMm=[..result.GlyphAdvancesMm??[]];replacement.SourceOutlines=source;replacement.Outlines=layout.Outlines;replacement.Glyphs=layout.Glyphs;
        _history.Execute(new EditTextEntityCommand(text,replacement));AfterGeometryCommand([text.Id],"Editable text updated");if(result.Warnings.Count>0)Message=result.Warnings[0];
    }
    private static (List<VectorPathFigure> Outlines,List<TextGlyphGeometry> Glyphs) PlaceGlyphs(TextOutlineResult result,TextPathPlacement placement,bool followPath){var source=(result.Glyphs??[]).OrderBy(glyph=>glyph.Order).ToArray();if(source.Length==0){var outlines=followPath?TextPathLayout.Fit(result.Outlines,placement.GuideSnapshot,placement):VectorPathGeometry.Clone(result.Outlines);return([..outlines],[]);}var groups=source.Select(glyph=>glyph.Figures).ToArray();var placed=followPath?TextPathLayout.FitGlyphs(groups,placement.GuideSnapshot,placement):groups.Select(VectorPathGeometry.Clone).Cast<IReadOnlyList<VectorPathFigure>>().ToArray();var glyphs=source.Select((glyph,index)=>new TextGlyphGeometry{Cluster=glyph.Cluster,AdvanceMm=glyph.AdvanceMm,Order=glyph.Order,SourceFigures=VectorPathGeometry.Clone(glyph.Figures),PlacedFigures=VectorPathGeometry.Clone(placed[index])}).ToList();return([..placed.SelectMany(group=>group)],glyphs);}
    public void CenterTextOnPath(){TextPathStart=.5;TextPathFit=false;UpdateSelectedText();}
    public void ReverseTextPath(){TextPathReverse=!TextPathReverse;UpdateSelectedText();}
    public void FlipTextPath(){TextPathFlip=!TextPathFlip;UpdateSelectedText();}
    public void ResetTextSpacing(){TextLetterSpacing=0;TextPathFit=false;UpdateSelectedText();}
    public void ConvertSelectedTextToVectors(){var texts=SelectedEntities().OfType<TextEntity>().ToArray();if(texts.Length==0)throw new InvalidOperationException("Select editable text first.");var additions=new List<CadEntity>();foreach(var text in texts){var group=Guid.NewGuid();var glyphs=text.Glyphs.Count>0?text.Glyphs.OrderBy(x=>x.Order).Select(x=>x.PlacedFigures):text.Outlines.Select(x=>(IEnumerable<VectorPathFigure>)[x]);foreach(var figures in glyphs)if(figures.Any())additions.Add(new VectorPathEntity{LayerId=text.LayerId,MaterialSheetId=text.MaterialSheetId,GroupId=group,StrokeColor=text.StrokeColor,Transform=text.Transform,Figures=VectorPathGeometry.Clone(figures)});}var ids=texts.Select(x=>x.Id).ToArray();_history.Execute(new ReplaceEntitiesCommand(Document,ids,additions,"Convert text to vectors"));Selection.Set(additions.Select(x=>x.Id));AfterGeometryCommand(ids.Concat(additions.Select(x=>x.Id)),"Converted text to grouped glyph vectors");}
    public void ApplyVectorColor(){if(!HasSelection)throw new InvalidOperationException("Select vectors to color.");if(!System.Text.RegularExpressions.Regex.IsMatch(VectorColor,"^#[0-9a-fA-F]{6}$"))throw new InvalidOperationException("Enter a color as #RRGGBB.");VectorColor=VectorColor.ToUpperInvariant();var ids=Selection.Ids.ToArray();_history.Execute(new SetEntityColorCommand(Document,ids,VectorColor));if(!RecentColors.Contains(VectorColor)){RecentColors.Insert(0,VectorColor);while(RecentColors.Count>8)RecentColors.RemoveAt(RecentColors.Count-1);}AfterGeometryCommand(ids,"Vector color changed");}
    public void ChoosePaletteColor(string color){VectorColor=color;UseDrawingColor=true;if(HasSelection)ApplyVectorColor();}
    public void PickPrimaryColor(){var entity=Selection.PrimaryId is{} id?Document.Entities.FirstOrDefault(x=>x.Id==id):null;if(entity is null)throw new InvalidOperationException("Select a visible unlocked vector to sample its color.");var layer=Document.Layers.FirstOrDefault(x=>x.Id==entity.LayerId);VectorColor=entity.StrokeColor??layer?.Color??"#65C9FF";UseDrawingColor=true;Message=$"Picked {VectorColor}";}
    public void UseLayerColor(){if(!HasSelection)return;var ids=Selection.Ids.ToArray();_history.Execute(new SetEntityColorCommand(Document,ids,null));AfterGeometryCommand(ids,"Vectors now inherit layer color");}
    public void GroupSelection(){var entities=SelectedEntities();if(entities.Length<2)throw new InvalidOperationException("Select at least two unlocked vectors to group.");var ids=entities.Select(entity=>entity.Id).ToArray();_history.Execute(new SetEntityGroupCommand(Document,ids,Guid.NewGuid()));AfterGeometryCommand(ids,"Vectors grouped");}
    public void UngroupSelection(){var ids=SelectedEntities().Where(x=>x.GroupId is not null).Select(x=>x.Id).ToArray();if(ids.Length==0)throw new InvalidOperationException("Select grouped vectors.");_history.Execute(new SetEntityGroupCommand(Document,ids,null));AfterGeometryCommand(ids,"Vectors ungrouped");}
    public void TrimSelectionAtPoint(){var source=SelectedEntities();if(source.Length<2)throw new InvalidOperationException("Select the line to trim first, then its cutting vectors.");var result=_topologyService.Trim(source[0],source.Skip(1),new(Point1X,Point1Y),RepairTolerance);ReplaceTopology(source[0],result,"Trim vector");}
    public void ApplyInteractiveTrim(TrimCandidate candidate){if(!candidate.Success)throw new InvalidOperationException(candidate.Error);_history.Execute(new ReplaceEntitiesCommand(Document,[candidate.TargetId],candidate.Replacements,"Trim vector"));Selection.Set(candidate.Replacements.Select(x=>x.Id));SelectionChanged();UpdateHistoryState();InvalidateOperations([candidate.TargetId]);IsDirty=true;RefreshWorkspaceState();Message="Vector trimmed";}
    public void ChamferSelection(){var path=SelectedEntities().OfType<PolylineEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one polyline.");ReplaceTopology(path,_topologyService.Chamfer(path,CornerIndex,ChamferDistance),"Chamfer corner");}
    public void FilletSelection(){var path=SelectedEntities().OfType<PolylineEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one polyline.");ReplaceTopology(path,_topologyService.Fillet(path,CornerIndex,FilletRadius),"Fillet corner");}
    public void AddCornerRelief(CornerReliefStyle style){var path=SelectedEntities().OfType<PolylineEntity>().SingleOrDefault()??throw new InvalidOperationException("Select one closed polyline.");ReplaceTopology(path,_routerPreparationService.AddCornerRelief(path,[CornerIndex],CornerReliefToolDiameter,style),style==CornerReliefStyle.Dogbone?"Add dogbone relief":"Add T-bone relief");}
    private VectorEditResult CornerReliefEligibility()
    {
        var path=SelectedEntities().OfType<PolylineEntity>().SingleOrDefault();if(path is null||Selection.Count!=1)return VectorEditResult.Fail("Select one closed polyline.");if(path.IsLocked||Document.Layers.FirstOrDefault(layer=>layer.Id==path.LayerId)?.IsLocked==true)return VectorEditResult.Fail("Unlock the vector and its layer before adding corner relief.");return _routerPreparationService.AddCornerRelief(path,[CornerIndex],CornerReliefToolDiameter,CornerReliefStyle.Dogbone);
    }
    private void ReplaceTopology(CadEntity source,VectorEditResult result,string description){if(!result.Success)throw new InvalidOperationException(result.Error);_history.Execute(new ReplaceEntitiesCommand(Document,[source.Id],result.Entities,description));Selection.Set(result.Entities.Select(x=>x.Id));SelectionChanged();AfterGeometryCommand([source.Id],description);}
    public void AnalyzeRepair(){var source=HasSelection?SelectedEntities():Document.Entities.Where(x=>x.IsVisible&&!x.IsLocked).ToArray();_repairAnalysis=_repairService.Analyze(source,new(RepairTolerance,MinimumSegment));RepairIssues.Clear();foreach(var issue in _repairAnalysis.Issues)RepairIssues.Add(issue);OnPropertyChanged(nameof(CanApplyRepair));Message=$"Repair preview: {RepairIssues.Count} issue(s), {_repairAnalysis.Replacements.Count} proposed replacement(s)";}
    public void ApplyRepair(){if(_repairAnalysis is null)throw new InvalidOperationException("Analyze vectors first.");if(_repairAnalysis.RemoveIds.Count==0&&_repairAnalysis.Replacements.Count==0){Message="No unambiguous repairs to apply; review the diagnostics.";return;}var analysis=_repairAnalysis;_history.Execute(new ReplaceEntitiesCommand(Document,analysis.RemoveIds,analysis.Replacements,"Repair vectors"));var ids=analysis.RemoveIds.Concat(analysis.Replacements.Select(x=>x.Id)).ToArray();Selection.Set(analysis.Replacements.Select(x=>x.Id));ClearRepairAnalysis();AfterGeometryCommand(ids,"Vector repairs applied");}
    public void AddLayer(){var layer=new CadLayer{Name=$"Layer {Document.Layers.Count+1}",Order=Document.Layers.Count};_history.Execute(new AddLayerCommand(Document,layer));RefreshLayers();UpdateHistoryState();IsDirty=true;}
    public void DeleteSelectedLayer(){var layer=SelectedLayer??throw new InvalidOperationException("Select a layer.");if(Document.Layers.Count==1)throw new InvalidOperationException("A document needs at least one layer.");_history.Execute(new DeleteLayerCommand(Document,layer));RefreshLayers();UpdateHistoryState();IsDirty=true;}
    public void ToggleLayerVisibility(){if(SelectedLayer is null)return;_history.Execute(new SetLayerBooleanCommand(SelectedLayer,LayerBooleanProperty.Visibility,!SelectedLayer.IsVisible));UpdateHistoryState();IsDirty=true;}
    public void ToggleLayerLock(){if(SelectedLayer is null)return;_history.Execute(new SetLayerBooleanCommand(SelectedLayer,LayerBooleanProperty.Lock,!SelectedLayer.IsLocked));UpdateHistoryState();IsDirty=true;}
    public void ActivateLayer(){if(SelectedLayer is null||SelectedLayer.IsLocked)throw new InvalidOperationException("Choose an unlocked layer.");if(Document.ActiveLayerId==SelectedLayer.Id)return;_history.Execute(new SetActiveLayerCommand(Document,SelectedLayer.Id));RefreshLayers();UpdateHistoryState();IsDirty=true;Message=$"Active layer: {SelectedLayer.Name}";}
    public void SelectLayerContents(){if(SelectedLayer is null)return;Selection.Set(Document.Entities.Where(x=>x.LayerId==SelectedLayer.Id).Select(x=>x.Id));SelectionChanged();}
    public void MoveSelectionToLayer(){if(SelectedLayer is null||SelectedLayer.IsLocked)throw new InvalidOperationException("Choose an unlocked destination layer.");var entities=SelectedEntities();_history.Execute(new MoveEntitiesToLayerCommand(Document,entities.Select(entity=>entity.Id),SelectedLayer.Id));AfterGeometryCommand(entities.Select(x=>x.Id),$"Moved selection to {SelectedLayer.Name}");}
    public void CommitLayerProperties(){if(SelectedLayer is null)return;if(string.IsNullOrWhiteSpace(LayerColorDraft)||!LayerColorDraft.StartsWith('#')||LayerColorDraft.Length is not (4 or 7 or 9))throw new InvalidOperationException("Layer colour must use #RGB, #RRGGBB, or #AARRGGBB format.");if(SelectedLayer.Name==LayerNameDraft.Trim()&&SelectedLayer.Color.Equals(LayerColorDraft,StringComparison.OrdinalIgnoreCase))return;_history.Execute(new SetLayerPropertiesCommand(SelectedLayer,LayerNameDraft,LayerColorDraft));UpdateHistoryState();IsDirty=true;RefreshLayers();Message=$"Updated layer {SelectedLayer.Name}";}
    public void MoveSelectedLayer(int direction){if(SelectedLayer is null)return;var ordered=Document.Layers.OrderBy(x=>x.Order).ToList();var index=ordered.IndexOf(SelectedLayer);var target=Math.Clamp(index+direction,0,ordered.Count-1);if(target==index)return;var selected=SelectedLayer;_history.Execute(new ReorderLayersCommand(ordered,selected,target));RefreshLayers();SelectedLayer=selected;UpdateHistoryState();IsDirty=true;}
    private void RefreshLayers(){var id=Document.ActiveLayerId;Layers.Clear();foreach(var layer in Document.Layers.OrderBy(x=>x.Order))Layers.Add(layer);SelectedLayer=Layers.FirstOrDefault(x=>x.Id==id)??Layers.FirstOrDefault();}
    public void SelectionChanged()
    {
        if(_activeNodeEntity is not null&&!Selection.Contains(_activeNodeEntity.Id)){_activeNodeEntity=null;_activeNodeIndex=-1;HasActiveNode=false;NotifyNodeCapabilities();}
        var primary=Selection.PrimaryId is{} primaryId?Document.Entities.FirstOrDefault(x=>x.Id==primaryId):null;var layer=primary is null?null:Document.Layers.FirstOrDefault(x=>x.Id==primary.LayerId);SelectionStatus = Selection.Count switch { 0 => "Nothing selected", 1 => $"{primary?.GetType().Name.Replace("Entity","")??"Vector"} · {layer?.Name??"Layer"} · {primary?.StrokeColor??layer?.Color??"default color"}", _ => $"{Selection.Count} entities selected · primary {primary?.GetType().Name.Replace("Entity","")??"vector"}" };
        if(primary is TextEntity text&&Selection.Count==1){TextValue=text.Text;TextFont=text.FontFamily;TextFontStyle=text.FontStyle;TextSize=text.SizeMm;TextLetterSpacing=text.LetterSpacingMm;TextLineSpacing=text.LineSpacing;TextAlignment=text.Alignment;TextPathOffset=text.PathPlacement.BaselineOffsetMm;TextPathStart=text.PathPlacement.StartOffset;TextPathReverse=text.PathPlacement.ReverseDirection;TextPathFlip=text.PathPlacement.FlipSide;TextKeepUpright=text.PathPlacement.KeepUpright;TextPathFit=text.PathPlacement.FitMode==TextFitMode.FitSpacing;SelectedFont=Fonts.FirstOrDefault(x=>x.Descriptor.Family.Equals(text.FontFamily,StringComparison.OrdinalIgnoreCase));}
        HasSelection = Selection.Count > 0;
        OnPropertyChanged(nameof(CanOffsetSelection));OnPropertyChanged(nameof(CanUseChamferFillet));OnPropertyChanged(nameof(CornerEditGuidance));OnPropertyChanged(nameof(CanAddCornerRelief));OnPropertyChanged(nameof(CornerReliefGuidance));OnPropertyChanged(nameof(CanCreateVCarve));OnPropertyChanged(nameof(VCarveGuidance));
        CanAlign=Selection.Count>=2;CanDistribute=Selection.Count>=3;
        if (HasSelection) { var bounds = SelectionBounds(); SelectionX = bounds.MinX; SelectionY = bounds.MinY; PivotX=(bounds.MinX+bounds.MaxX)/2; PivotY=(bounds.MinY+bounds.MaxY)/2; }
        RefreshWorkspaceState();
    }
    public void MoveSelection(Vector2D delta, string message = "Selection moved")
    {
        if(!GeometryValidation.IsFinite(delta))throw new InvalidOperationException("Movement must contain finite coordinates.");if (Selection.Count == 0 || delta.Length <= GeometryValidation.CoordinateToleranceMm) return;
        var ids = Selection.Ids.ToArray(); _history.Execute(new MoveEntitiesCommand(Document, ids, delta)); SelectionChanged(); UpdateHistoryState(); InvalidateOperations(ids); IsDirty = true; Message = message;
    }
    public void ApplySelectionPosition() { if (!HasSelection) return;if(!double.IsFinite(SelectionX)||!double.IsFinite(SelectionY))throw new InvalidOperationException("Position must contain finite coordinates."); var bounds = SelectionBounds(); MoveSelection(new(SelectionX - bounds.MinX, SelectionY - bounds.MinY), "Selection positioned"); }
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
        var points=EntityGeometry.WorldPoints(entity);if(points.Any(point=>!double.IsFinite(point.X)||!double.IsFinite(point.Y)))throw new InvalidOperationException("Exact geometry coordinates must be finite.");if(points.Distinct().Count()<2)throw new InvalidOperationException("Exact geometry is degenerate."); AddEntity(entity);
    }
    public void AddNumericPolylineNode(){var start=new Point2D(Point1X,Point1Y);if(!Finite(start))throw new InvalidOperationException("Polyline coordinates must be finite.");if(_numericPolyline.Count==0)_numericPolyline.Add(start);var point=ExactUsesLengthAngle?Polar(_numericPolyline[^1],ExactLength,ExactAngle):new Point2D(Point2X,Point2Y);if(!Finite(point))throw new InvalidOperationException("Polyline coordinates must be finite.");if(_numericPolyline[^1].DistanceTo(point)>1e-9)_numericPolyline.Add(point);Message=$"{_numericPolyline.Count} numeric polyline nodes";}
    public void FinishNumericPolyline(bool close){if(_numericPolyline.Count<(close?3:2))throw new InvalidOperationException(close?"A closed path needs at least three nodes.":"A path needs at least two nodes.");if(_numericPolyline.Any(point=>!Finite(point)))throw new InvalidOperationException("Polyline coordinates must be finite.");var entity=new PolylineEntity{LayerId=Document.ActiveLayerId,Points=[.._numericPolyline],IsClosed=close};_numericPolyline.Clear();AddEntity(entity);}
    public void CancelNumericPolyline(){var count=_numericPolyline.Count;_numericPolyline.Clear();Message=count==0?"No numeric polyline is in progress.":"Numeric polyline cancelled";}
    public void TransformSelection(Matrix2D transform,string description){if(!GeometryValidation.IsInvertible(transform))throw new InvalidOperationException("Transform must contain finite values and remain invertible.");if(!HasSelection||GeometryValidation.IsIdentity(transform))return;var ids=Selection.Ids.ToArray();_history.Execute(new TransformEntitiesCommand(Document,ids,transform,description));AfterGeometryCommand(ids,description);}
    public void RotateSelection(){if(!double.IsFinite(PivotX)||!double.IsFinite(PivotY)||!double.IsFinite(TransformAngle))throw new InvalidOperationException("Pivot and angle must be finite.");TransformSelection(Matrix2D.Around(new(PivotX,PivotY),Matrix2D.Rotation(TransformAngle)),"Selection rotated");}
    public void ScaleSelection(){if(!double.IsFinite(PivotX)||!double.IsFinite(PivotY)||!double.IsFinite(ScaleX)||!double.IsFinite(ScaleY))throw new InvalidOperationException("Pivot and scale must be finite.");if(Math.Abs(ScaleX)<GeometryValidation.CoordinateToleranceMm||Math.Abs(ScaleY)<GeometryValidation.CoordinateToleranceMm)throw new InvalidOperationException("Scale cannot be zero.");TransformSelection(Matrix2D.Around(new(PivotX,PivotY),Matrix2D.Scale(ScaleX,ScaleY)),"Selection scaled");}
    public void MirrorSelection(bool horizontal){if(!double.IsFinite(PivotX)||!double.IsFinite(PivotY))throw new InvalidOperationException("Pivot must be finite.");TransformSelection(Matrix2D.Around(new(PivotX,PivotY),Matrix2D.Scale(horizontal?-1:1,horizontal?1:-1)),horizontal?"Selection mirrored horizontally":"Selection mirrored vertically");}
    public void AlignSelection(string mode)
    {
        if(mode is not ("Left" or "HCenter" or "Right" or "Bottom" or "VCenter" or "Top"))throw new InvalidOperationException("Alignment mode is not supported.");var entities=SelectedEntities();if(entities.Length<2)return;var all=SelectionBounds();var moves=entities.Select(e=>{var b=e.GetBounds();var delta=mode switch{"Left"=>new Vector2D(all.MinX-b.MinX,0),"HCenter"=>new Vector2D((all.MinX+all.MaxX-b.MinX-b.MaxX)/2,0),"Right"=>new Vector2D(all.MaxX-b.MaxX,0),"Bottom"=>new Vector2D(0,all.MinY-b.MinY),"VCenter"=>new Vector2D(0,(all.MinY+all.MaxY-b.MinY-b.MaxY)/2),_=>new Vector2D(0,all.MaxY-b.MaxY)};return(e,delta);}).Where(move=>move.delta.Length>GeometryValidation.CoordinateToleranceMm).ToArray();if(moves.Length==0)return;
        _history.Execute(new MoveEntitiesIndividuallyCommand(moves,$"Align {mode}"));AfterGeometryCommand(entities.Select(e=>e.Id),$"Aligned {mode}");
    }
    public void DistributeSelection(bool horizontal)
    {
        var entities=SelectedEntities();if(entities.Length<3)return;var ordered=(horizontal?entities.OrderBy(e=>e.GetBounds().MinX).ThenBy(e=>e.Id):entities.OrderBy(e=>e.GetBounds().MinY).ThenBy(e=>e.Id)).ToArray();var bounds=ordered.Select(entity=>entity.GetBounds()).ToArray();var start=horizontal?bounds[0].MinX:bounds[0].MinY;var end=horizontal?bounds[^1].MaxX:bounds[^1].MaxY;var total=bounds.Sum(bound=>horizontal?bound.Width:bound.Height);var gap=(end-start-total)/(ordered.Length-1);var cursor=start;var moves=new List<(CadEntity Entity,Vector2D Delta)>();for(var index=0;index<ordered.Length;index++){var bound=bounds[index];var minimum=horizontal?bound.MinX:bound.MinY;var target=index==0?start:index==ordered.Length-1?end-(horizontal?bound.Width:bound.Height):cursor;var delta=horizontal?new Vector2D(target-minimum,0):new Vector2D(0,target-minimum);if(delta.Length>GeometryValidation.CoordinateToleranceMm)moves.Add((ordered[index],delta));cursor=target+(horizontal?bound.Width:bound.Height)+gap;}if(moves.Count==0)return;_history.Execute(new MoveEntitiesIndividuallyCommand(moves,"Distribute selection"));AfterGeometryCommand(entities.Select(e=>e.Id),horizontal?"Distributed horizontally":"Distributed vertically");
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
        if(!double.IsFinite(tolerance)||tolerance<=0)throw new InvalidOperationException("Join tolerance must be positive and finite.");var source=SelectedEntities();var paths=source.Select(e=>(Entity:e,Points:e switch{LineEntity l=>EntityGeometry.WorldPoints(l).ToList(),PolylineEntity{IsClosed:false} p=>EntityGeometry.WorldPoints(p).ToList(),_=>throw new InvalidOperationException("Join supports selected lines and open polylines.")})).ToList();if(paths.Count<2)throw new InvalidOperationException("Select at least two open paths.");var joined=paths[0].Points;paths.RemoveAt(0);
        while(paths.Count>0)
        {
            var candidates=paths.SelectMany((path,index)=>new[]{(Index:index,Mode:0,Distance:joined[^1].DistanceTo(path.Points[0])),(Index:index,Mode:1,Distance:joined[^1].DistanceTo(path.Points[^1])),(Index:index,Mode:2,Distance:joined[0].DistanceTo(path.Points[^1])),(Index:index,Mode:3,Distance:joined[0].DistanceTo(path.Points[0]))}).Where(candidate=>candidate.Distance<=tolerance).OrderBy(candidate=>candidate.Distance).ThenBy(candidate=>candidate.Index).ThenBy(candidate=>candidate.Mode).ToArray();if(candidates.Length==0)throw new InvalidOperationException("Selected endpoints do not form one continuous chain within tolerance.");var candidate=candidates[0];var next=paths[candidate.Index].Points.ToList();switch(candidate.Mode){case 0:joined.AddRange(next.Skip(1));break;case 1:next.Reverse();joined.AddRange(next.Skip(1));break;case 2:next.RemoveAt(next.Count-1);next.AddRange(joined);joined=next;break;case 3:next.Reverse();next.RemoveAt(next.Count-1);next.AddRange(joined);joined=next;break;}paths.RemoveAt(candidate.Index);
        }
        var closed=joined.Count>=3&&joined[0].DistanceTo(joined[^1])<=tolerance;if(closed)joined.RemoveAt(joined.Count-1);var first=source[0];var result=new PolylineEntity{Id=first.Id,LayerId=first.LayerId,MaterialSheetId=first.MaterialSheetId,GroupId=first.GroupId,StrokeColor=first.StrokeColor,IsVisible=first.IsVisible,IsLocked=first.IsLocked,Points=joined,IsClosed=closed};var ids=source.Select(e=>e.Id).ToArray();_history.Execute(new ReplaceEntitiesCommand(Document,ids,[result],"Join paths"));Selection.Set(result.Id);SelectionChanged();UpdateHistoryState();InvalidateOperations(ids);IsDirty=true;Message=closed?"Paths joined and closed":"Paths joined";
    }
    public void SelectNode(CadEntity entity,int index,Point2D worldPoint){_activeNodeEntity=entity;_activeNodeIndex=index;NodeX=worldPoint.X;NodeY=worldPoint.Y;HasActiveNode=true;NotifyNodeCapabilities();Message=$"Node {index} selected";}
    public void MoveNode(CadEntity entity,int index,Point2D worldPoint){if(entity.IsLocked||Document.Layers.FirstOrDefault(layer=>layer.Id==entity.LayerId)?.IsLocked==true)throw new InvalidOperationException("Unlock the vector and its layer before editing nodes.");_history.Execute(new SetNodeCommand(entity,index,worldPoint));SelectNode(entity,index,worldPoint);AfterGeometryCommand([entity.Id],"Node moved");}
    public void ApplyNodePosition(){if(_activeNodeEntity is null)return;if(!CanApplyNodePosition)throw new InvalidOperationException("This exact-arc anchor cannot be moved. Split the arc or edit its radius instead.");MoveNode(_activeNodeEntity,_activeNodeIndex,new(NodeX,NodeY));}
    public void InsertNode()
    {
        if(_activeNodeEntity is PolylineEntity p){var next=(_activeNodeIndex+1)%p.Points.Count;if(!p.IsClosed&&_activeNodeIndex==p.Points.Count-1)throw new InvalidOperationException("The final node of an open path has no following segment to split.");var worldA=p.Transform.Transform(p.Points[_activeNodeIndex]);var worldB=p.Transform.Transform(p.Points[next]);var local=p.Transform.Inverse().Transform(new((worldA.X+worldB.X)/2,(worldA.Y+worldB.Y)/2));var points=p.Points.ToList();points.Insert(_activeNodeIndex+1,local);_history.Execute(new PolylineNodesCommand(p,points,p.IsClosed,"Insert node"));_activeNodeIndex++;var world=p.Transform.Transform(local);NodeX=world.X;NodeY=world.Y;NotifyNodeCapabilities();AfterGeometryCommand([p.Id],"Node inserted");return;}
        if(_activeNodeEntity is VectorPathEntity vector){var edit=VectorPathNodes.InsertAfter(vector,_activeNodeIndex);_history.Execute(new VectorPathFiguresCommand(vector,edit.Figures,"Insert vector node"));_activeNodeIndex=edit.SelectedNodeIndex;var world=vector.Transform.Transform(VectorPathNodes.Get(vector,_activeNodeIndex));NodeX=world.X;NodeY=world.Y;NotifyNodeCapabilities();AfterGeometryCommand([vector.Id],"Curve segment split without changing its shape");return;}
        throw new InvalidOperationException("Select a path anchor with a following segment.");
    }
    public void DeleteNode()
    {
        if(_activeNodeEntity is PolylineEntity p){var minimum=p.IsClosed?3:2;if(p.Points.Count<=minimum)throw new InvalidOperationException($"Path requires at least {minimum} nodes.");var points=p.Points.ToList();points.RemoveAt(_activeNodeIndex);_history.Execute(new PolylineNodesCommand(p,points,p.IsClosed,"Delete node"));ClearActiveNode();AfterGeometryCommand([p.Id],"Node deleted");return;}
        if(_activeNodeEntity is VectorPathEntity vector){var edit=VectorPathNodes.Delete(vector,_activeNodeIndex);_history.Execute(new VectorPathFiguresCommand(vector,edit.Figures,"Delete vector node"));ClearActiveNode();AfterGeometryCommand([vector.Id],"Vector node deleted; unaffected native segments were preserved");return;}
        throw new InvalidOperationException("Select a removable path anchor.");
    }
    public void MakeStartNode()
    {
        if(_activeNodeEntity is PolylineEntity{IsClosed:true} p){var points=p.Points.Skip(_activeNodeIndex).Concat(p.Points.Take(_activeNodeIndex)).ToArray();_history.Execute(new PolylineNodesCommand(p,points,true,"Change start node"));_activeNodeIndex=0;var world=p.Transform.Transform(points[0]);NodeX=world.X;NodeY=world.Y;NotifyNodeCapabilities();AfterGeometryCommand([p.Id],"Start node changed");return;}
        if(_activeNodeEntity is VectorPathEntity vector){var edit=VectorPathNodes.MakeStart(vector,_activeNodeIndex);_history.Execute(new VectorPathFiguresCommand(vector,edit.Figures,"Change vector start node"));_activeNodeIndex=edit.SelectedNodeIndex;var world=vector.Transform.Transform(VectorPathNodes.Get(vector,_activeNodeIndex));NodeX=world.X;NodeY=world.Y;NotifyNodeCapabilities();AfterGeometryCommand([vector.Id],"Start node changed without flattening curves");return;}
        throw new InvalidOperationException("Select another anchor on a closed path.");
    }
    private void ClearActiveNode(){HasActiveNode=false;_activeNodeEntity=null;_activeNodeIndex=-1;NotifyNodeCapabilities();}
    private void NotifyNodeCapabilities(){OnPropertyChanged(nameof(CanInsertNode));OnPropertyChanged(nameof(CanDeleteNode));OnPropertyChanged(nameof(CanMakeStartNode));OnPropertyChanged(nameof(CanApplyNodePosition));}
    private CadEntity[] SelectedEntities(){var entities=Document.Entities.Where(e=>Selection.Contains(e.Id)).ToArray();if(entities.Any(e=>e.IsLocked||Document.Layers.FirstOrDefault(l=>l.Id==e.LayerId)?.IsLocked==true))throw new InvalidOperationException("Locked-layer geometry cannot be edited.");return entities;}
    private static Point2D Polar(Point2D start,double length,double angle){if(length<=0)throw new InvalidOperationException("Length must be greater than zero.");var radians=angle*Math.PI/180;return start+new Vector2D(Math.Cos(radians)*length,Math.Sin(radians)*length);}
    private static bool Finite(Point2D point)=>double.IsFinite(point.X)&&double.IsFinite(point.Y);
    private void AfterGeometryCommand(IEnumerable<Guid> ids,string message){SelectionChanged();UpdateHistoryState();InvalidateOperations(ids);IsDirty=true;Message=message;}
    public void CopySelection()
    {
        _clipboard.Clear(); _clipboard.AddRange(CloneEntityGraph(Document.Entities.Where(e => Selection.Contains(e.Id)))); _pasteCount = 0;
        HasClipboard = _clipboard.Count > 0; Message = _clipboard.Count == 0 ? "Nothing selected to copy" : $"Copied {_clipboard.Count} entities";
    }
    public void Paste()
    {
        if (_clipboard.Count == 0) return; _pasteCount++;
        var entities = CloneEntityGraph(_clipboard, new(10 * _pasteCount, 10 * _pasteCount));
        _history.Execute(new AddEntitiesCommand(Document, entities, "Paste entities")); Selection.Set(entities.Select(e => e.Id)); SelectionChanged(); UpdateHistoryState(); IsDirty = true; Message = $"Pasted {entities.Count} entities";
    }
    public void DuplicateSelection()
    {
        var entities = CloneEntityGraph(Document.Entities.Where(e => Selection.Contains(e.Id)),new(10,10)); if (entities.Count == 0) return;
        _history.Execute(new AddEntitiesCommand(Document, entities, "Duplicate entities")); Selection.Set(entities.Select(e => e.Id)); SelectionChanged(); UpdateHistoryState(); IsDirty = true; Message = $"Duplicated {entities.Count} entities";
    }
    private static List<CadEntity> CloneEntityGraph(IEnumerable<CadEntity> source,Vector2D? translation=null)
    {
        var originals=source.ToArray();var clones=originals.Select(EntityGeometry.Clone).ToList();var entityIds=originals.Zip(clones,(original,clone)=>(SourceId:original.Id,CloneId:clone.Id)).ToDictionary(pair=>pair.SourceId,pair=>pair.CloneId);var groups=new Dictionary<Guid,Guid>();
        foreach(var clone in clones)
        {
            if(clone.GroupId is{} group){if(!groups.TryGetValue(group,out var replacement)){replacement=Guid.NewGuid();groups[group]=replacement;}clone.GroupId=replacement;}
            if(clone is TextEntity text&&text.PathPlacement.GuideEntityId is{} guide&&entityIds.TryGetValue(guide,out var replacementGuide))text.PathPlacement.GuideEntityId=replacementGuide;
            if(translation is{} delta)EntityGeometry.Translate(clone,delta);
        }
        return clones;
    }
    public void UpdateInteraction(SnapResult snap, CncCad.Rendering.Canvas.DrawingMeasurement measurement)
    {
        var snapText = snap.Type == SnapType.None ? "Snap: none" : $"Snap: {snap.Type}";
        InteractionStatus = measurement.IsVisible ? $"{snapText}   {measurement.Summary}" : snapText;
    }
    private BoundingBox SelectionBounds() => BoundingBox.FromPoints(Document.Entities.Where(e => Selection.Contains(e.Id)).SelectMany(e => { var b = e.GetBounds(); return new[] { new Point2D(b.MinX, b.MinY), new Point2D(b.MaxX, b.MaxY) }; }));
    private void InvalidateOperations(IEnumerable<Guid> ids)
    {
        ClearRepairAnalysis();var values = ids.ToArray();var linked=RefreshLinkedText(values);values=[..values.Concat(linked).Distinct()];var deleted=values.Where(id=>Document.Entities.All(entity=>entity.Id!=id)).ToArray();var surviving=values.Except(deleted).ToArray();CamJob.MarkGeometryChanged(surviving);CamJob.MarkGeometryChanged(deleted,true);if(NestingJob.Parts.Any(p=>values.Contains(p.OuterGeometryId)||p.InnerGeometryIds.Any(values.Contains))||NestingJob.AcceptedLayout?.Placements.Any(p=>p.InstanceGeometryIds.Any(values.Contains))==true){NestingJob.Status=NestingLayoutStatus.Stale;NestingJob.AcceptedLayout=null;RefreshNesting();OnPropertyChanged(nameof(CanAcceptNest));} RebuildProgram(); RefreshOperations(); ClearPlayback(); InvalidatePreflight(); GCodePreview = "Geometry changed — recalculate stale toolpaths.";
    }
    private void InvalidateAllOperationsForJobSetup()
    {
        ClearRepairAnalysis();
        foreach(var operation in CamJob.Operations){operation.Status=OperationStatus.Stale;operation.Error=null;operation.CalculatedToolpath=null;operation.EstimatedTime=TimeSpan.Zero;}
        NestingJob.SheetTemplate.WidthMm=Document.Job.WidthMm;NestingJob.SheetTemplate.HeightMm=Document.Job.HeightMm;NestingJob.SheetTemplate.ThicknessMm=Document.Job.ThicknessMm;NestingJob.Status=NestingLayoutStatus.Stale;NestingJob.AcceptedLayout=null;
        RefreshNesting();OnPropertyChanged(nameof(CanAcceptNest));RebuildProgram();RefreshOperations();ClearPlayback();InvalidatePreflight();GCodePreview="Job setup changed — recalculate every toolpath.";RefreshWorkspaceState();
    }
    private void RefreshJobPresentation(){JobTitle=Document.Job.Name;Program.Name=Document.Job.Name;OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary));}
    private void ClearRepairAnalysis(){if(_repairAnalysis is null&&RepairIssues.Count==0)return;_repairAnalysis=null;RepairIssues.Clear();OnPropertyChanged(nameof(CanApplyRepair));}
    private IReadOnlyList<Guid> RefreshLinkedText(IReadOnlyCollection<Guid> changed){var affected=new List<Guid>();foreach(var text in Document.Entities.OfType<TextEntity>().Where(x=>x.LayoutMode==TextLayoutMode.FollowPath&&x.PathPlacement.GuideEntityId is{} id&&changed.Contains(id))){var guide=Document.Entities.FirstOrDefault(x=>x.Id==text.PathPlacement.GuideEntityId);if(guide is null){text.PathPlacement.GuideEntityId=null;continue;}text.PathPlacement.GuideSnapshot=[..EntityGeometry.WorldPoints(guide)];if(text.Glyphs.Count>0){var glyphs=text.Glyphs.OrderBy(x=>x.Order).ToArray();var placed=TextPathLayout.FitGlyphs(glyphs.Select(glyph=>glyph.SourceFigures),text.PathPlacement.GuideSnapshot,text.PathPlacement);for(var index=0;index<glyphs.Length;index++)glyphs[index].PlacedFigures=VectorPathGeometry.Clone(placed[index]);text.Outlines=[..placed.SelectMany(group=>group)];}else text.Outlines=[..TextPathLayout.Fit(text.SourceOutlines,text.PathPlacement.GuideSnapshot,text.PathPlacement)];affected.Add(text.Id);}return affected;}
    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var selected=SelectedEntities();var ids=selected.Select(entity=>entity.Id).ToArray();var linked=Document.Entities.OfType<TextEntity>().Where(x=>x.PathPlacement.GuideEntityId is{} guide&&ids.Contains(guide)&&!ids.Contains(x.Id)).ToArray();if(linked.Any(text=>text.IsLocked||Document.Layers.FirstOrDefault(layer=>layer.Id==text.LayerId)?.IsLocked==true))throw new InvalidOperationException("Unlock text linked to the selected guide before deleting it.");var commands=new List<IUndoableCommand>();foreach(var text in linked){var detached=(TextEntity)EntityGeometry.Clone(text);detached.PathPlacement.GuideEntityId=null;detached.LayoutMode=TextLayoutMode.Straight;detached.SourceOutlines=VectorPathGeometry.Clone(detached.Outlines);foreach(var glyph in detached.Glyphs)glyph.SourceFigures=VectorPathGeometry.Clone(glyph.PlacedFigures);commands.Add(new EditTextEntityCommand(text,detached));}commands.Add(new DeleteEntitiesCommand(Document,ids));var command=new CompositeCommand(commands,"Delete vectors and detach linked text");_history.Execute(command);Selection.Clear(); SelectionChanged(); UpdateHistoryState(); InvalidateOperations(command.AffectedEntityIds); IsDirty = true; Message = "Selection deleted";
    }
    public void Undo() { if (!_history.CanUndo) return; var command = _history.Undo(); Selection.Clear(); SelectionChanged();RefreshLayers(); UpdateHistoryState(); if(command is ReplaceImportedLayoutCommand nesting&&_nestingHistory.TryGetValue(nesting,out var state))RestoreNestingHistory(state,false);else if(command is IJobSettingsAffectingCommand){RefreshJobPresentation();InvalidateAllOperationsForJobSetup();}else if (command is IEntityAffectingCommand affected) InvalidateOperations(affected.AffectedEntityIds); IsDirty = true; Message = "Undo"; }
    public void Redo() { if (!_history.CanRedo) return; var command = _history.Redo(); Selection.Clear(); SelectionChanged();RefreshLayers(); UpdateHistoryState(); if(command is ReplaceImportedLayoutCommand nesting&&_nestingHistory.TryGetValue(nesting,out var state))RestoreNestingHistory(state,true);else if(command is IJobSettingsAffectingCommand){RefreshJobPresentation();InvalidateAllOperationsForJobSetup();}else if (command is IEntityAffectingCommand affected) InvalidateOperations(affected.AffectedEntityIds); IsDirty = true; Message = "Redo"; }
    private void UpdateHistoryState() { CanUndo = _history.CanUndo; CanRedo = _history.CanRedo; }
    public void UpdatePointer(Point2D point, double zoom) { PointerStatus = $"X {point.X:0.000}   Y {point.Y:0.000}"; ZoomStatus = $"{zoom * 100:0}%"; }
    public void AddNestPartsFromSelection()
    {
        var selected=Document.Entities.Where(x=>Selection.Contains(x.Id)&&x.MaterialSheetId is null).ToArray();if(selected.Length==0)throw new InvalidOperationException("Select one or more source closed contours.");if(selected.Any(x=>!IsProfileContour(x)))throw new InvalidOperationException("Nesting accepts closed rectangles, circles, polylines, or single-figure vector paths.");var outers=selected.Where(entity=>{var b=entity.GetBounds();var center=new Point2D((b.MinX+b.MaxX)/2,(b.MinY+b.MaxY)/2);return !selected.Any(other=>other.Id!=entity.Id&&other.GetBounds().Width*other.GetBounds().Height>b.Width*b.Height&&other.GetBounds().Contains(center));}).ToArray();foreach(var entity in outers){if(NestingJob.Parts.Any(x=>x.OuterGeometryId==entity.Id))continue;var part=new NestPartDefinition{Name=$"Part {NestingJob.Parts.Count+1}",OuterGeometryId=entity.Id,Quantity=Math.Max(1,NestQuantity),AllowQuarterTurn=NestAllowQuarterTurn,Grain=NestGrain};var outer=entity.GetBounds();part.InnerGeometryIds.AddRange(selected.Where(x=>x.Id!=entity.Id&&outer.Contains(new((x.GetBounds().MinX+x.GetBounds().MaxX)/2,(x.GetBounds().MinY+x.GetBounds().MaxY)/2))).Select(x=>x.Id));NestingJob.Parts.Add(part);}NestingJob.AcceptedLayout=null;RefreshNesting();NestingJob.Status=NestingLayoutStatus.Stale;OnPropertyChanged(nameof(CanAcceptNest));IsDirty=true;Message=$"Defined {NestingJob.Parts.Count} nesting part(s)";
    }
    public void RemoveSelectedNestPart(){if(SelectedNestPart is null)return;NestingJob.Parts.Remove(SelectedNestPart);NestingJob.AcceptedLayout=null;RefreshNesting();NestingJob.Status=NestingLayoutStatus.Stale;OnPropertyChanged(nameof(CanAcceptNest));IsDirty=true;}
    public NestingResult OptimizeNest()
    {
        if(NestingJob.Status==NestingLayoutStatus.Current)CaptureInstalledNestingLayout();NestingJob.PartSpacingMm=NestSpacing;NestingJob.EdgeMarginMm=NestEdgeMargin;NestingJob.SheetTemplate.WidthMm=Document.Job.WidthMm;NestingJob.SheetTemplate.HeightMm=Document.Job.HeightMm;NestingJob.SheetTemplate.ThicknessMm=Document.Job.ThicknessMm;NestingJob.SheetTemplate.Grain=NestSheetGrain;var result=_nestingOptimizer.Optimize(new(NestingJob,Document.Entities.Where(x=>x.MaterialSheetId is null).ToDictionary(x=>x.Id)));NestingJob.AcceptedLayout=result;NestingJob.Status=NestingLayoutStatus.Stale;RefreshNesting();NestingSummary=$"Preview: {result.Placements.Count} parts · {result.Sheets.Count} sheets · {result.Score.WasteAreaMm2:0} mm² waste{(result.UnplacedQuantities.Count>0?$" · {result.UnplacedQuantities.Values.Sum()} unplaced":"")}";OnPropertyChanged(nameof(CanAcceptNest));IsDirty=true;return result;
    }
    public void AcceptNest()
    {
        var result=NestingJob.AcceptedLayout??throw new InvalidOperationException("Optimize the nest first.");if(!result.Success)throw new InvalidOperationException($"Place every requested part before accepting the nest. {result.UnplacedQuantities.Values.Sum()} part(s) remain unplaced.");var layers=new List<CadLayer>();var additions=new List<CadEntity>();foreach(var sheet in result.Sheets){var layer=new CadLayer{Name=sheet.Name,Order=Document.Layers.Count+layers.Count,Color="#75D6A5"};layers.Add(layer);foreach(var placement in result.Placements.Where(x=>x.SheetId==sheet.Id)){placement.InstanceGeometryIds.Clear();var part=NestingJob.Parts.First(x=>x.Id==placement.PartId);var sources=new[]{part.OuterGeometryId}.Concat(part.InnerGeometryIds);foreach(var id in sources){var source=Document.Entities.First(x=>x.Id==id);var clone=EntityGeometry.Clone(source);var b=source.GetBounds();var center=new Point2D((b.MinX+b.MaxX)/2,(b.MinY+b.MaxY)/2);var transform=Matrix2D.Then(Matrix2D.Around(center,Matrix2D.Rotation(placement.RotationDegrees)),Matrix2D.Translation(placement.Xmm,placement.Ymm));clone.Transform=Matrix2D.Then(clone.Transform,transform);clone.LayerId=layer.Id;clone.MaterialSheetId=sheet.Id;additions.Add(clone);placement.InstanceGeometryIds.Add(clone.Id);}}}var command=new ReplaceImportedLayoutCommand(Document,_installedNestingEntityIds,_installedNestingLayerIds,layers,additions);var state=new NestingHistoryState(_installedNestingLayout,[.._installedNestingEntityIds],[.._installedNestingLayerIds],result,[..additions.Select(entity=>entity.Id)],[..layers.Select(layer=>layer.Id)]);_history.Execute(command);_nestingHistory[command]=state;RestoreNestingHistory(state,true);Selection.Set(additions.Select(x=>x.Id));SelectionChanged();UpdateHistoryState();IsDirty=true;Message=$"Accepted nest onto {result.Sheets.Count} material sheet(s)";
    }
    public void SelectMaterialSheet(MaterialSheetDefinition? sheet){SelectedMaterialSheet=sheet;Selection.Clear();SelectionChanged();RebuildProgram();RefreshOperations();Message=sheet is null?"Showing source design":$"Active {sheet.Name}";}
    private void RefreshNesting(){var selected=SelectedNestPart;NestParts.Clear();foreach(var part in NestingJob.Parts)NestParts.Add(part);SelectedNestPart=selected??NestParts.FirstOrDefault();MaterialSheets.Clear();if(NestingJob.AcceptedLayout is{} layout)foreach(var sheet in layout.Sheets)MaterialSheets.Add(sheet);NestingSummary=NestingJob.AcceptedLayout is{} r?$"{r.Placements.Count} placed · {r.Sheets.Count} sheet(s) · {r.Score.WasteAreaMm2:0} mm² waste":"Select closed vectors to define parts.";}
    private void CaptureInstalledNestingLayout(){_installedNestingEntityIds.Clear();if(NestingJob.AcceptedLayout is{} layout){_installedNestingLayout=layout;_installedNestingEntityIds.UnionWith(layout.Placements.SelectMany(placement=>placement.InstanceGeometryIds));}else _installedNestingLayout=null;_installedNestingLayerIds.Clear();_installedNestingLayerIds.UnionWith(Document.Entities.Where(entity=>_installedNestingEntityIds.Contains(entity.Id)).Select(entity=>entity.LayerId));}
    private void RestoreNestingHistory(NestingHistoryState state,bool useNew){var layout=useNew?state.NewLayout:state.OldLayout;_installedNestingLayout=layout;_installedNestingEntityIds.Clear();_installedNestingEntityIds.UnionWith(useNew?state.NewEntityIds:state.OldEntityIds);_installedNestingLayerIds.Clear();_installedNestingLayerIds.UnionWith(useNew?state.NewLayerIds:state.OldLayerIds);NestingJob.AcceptedLayout=layout;NestingJob.Status=layout is null?NestingLayoutStatus.Stale:NestingLayoutStatus.Current;RefreshNesting();OnPropertyChanged(nameof(CanAcceptNest));InvalidatePreflight();}
    private sealed record NestingHistoryState(NestingResult? OldLayout,Guid[] OldEntityIds,Guid[] OldLayerIds,NestingResult NewLayout,Guid[] NewEntityIds,Guid[] NewLayerIds);
    public ProfileOperationDefinition CreateProfileDraft()
    {
        if (Selection.Count == 0) throw new InvalidOperationException("Select one or more closed rectangles, polylines, or circles first.");
        var selected = Document.Entities.Where(entity => Selection.Contains(entity.Id)).ToArray();
        var invalid = selected.FirstOrDefault(entity => !IsProfileContour(entity));
        if (invalid is not null) throw new InvalidOperationException($"{invalid.GetType().Name.Replace("Entity", string.Empty)} is not a closed profile contour. Select rectangles, circles, or closed polylines.");
        var tool = SelectedTool ?? throw new InvalidOperationException("Select a tool first.");
        var setup=SelectedCuttingSetup(tool);var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create a profile from one material sheet at a time.");return new ProfileOperationDefinition { Name = $"Profile {CamJob.Operations.Count + 1}", GeometryIds = [.. Selection.Ids], Tool = setup.Snapshot, Order = CamJob.Operations.Count, CutDepthMm = Document.Job.ThicknessMm, PassDepthMm = setup.Snapshot.StepDownMm, SafeZMm = Document.Job.SafeZMm, FeedRateMmPerMin = setup.Snapshot.FeedRateMmPerMin, PlungeRateMmPerMin = setup.Snapshot.PlungeRateMmPerMin, SpindleRpm = setup.Snapshot.SpindleRpm,MaterialSheetId=sheets[0] };
    }
    public EngraveOperationDefinition CreateEngraveDraft(){if(Selection.Count==0)throw new InvalidOperationException("Select one or more vectors or centerline text first.");var selected=Document.Entities.Where(x=>Selection.Contains(x.Id)).ToArray();if(selected.Any(x=>x is not LineEntity and not PolylineEntity and not RectangleEntity and not ArcEntity and not CircleEntity and not EllipseEntity and not VectorPathEntity and not TextEntity))throw new InvalidOperationException("The selection contains geometry that cannot be engraved.");var tool=SelectedTool??throw new InvalidOperationException("Select an engraving tool.");var setup=SelectedCuttingSetup(tool);var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create engraving for one material sheet at a time.");return new(){Name=$"Engrave {CamJob.Operations.Count+1}",GeometryIds=selected.Select(x=>x.Id).ToList(),Tool=setup.Snapshot,Order=CamJob.Operations.Count,CutDepthMm=Math.Min(1,Document.Job.ThicknessMm),PassDepthMm=Math.Min(1,Math.Max(.1,setup.Snapshot.StepDownMm)),SafeZMm=Document.Job.SafeZMm,FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin,PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin,SpindleRpm=setup.Snapshot.SpindleRpm,MaterialSheetId=sheets[0]};}
    public VCarveOperationDefinition CreateVCarveDraft(){if(Selection.Count==0)throw new InvalidOperationException("Select one or more closed outline vectors.");var selected=SelectedEntities();if(selected.Any(x=>!IsVCarveContour(x)))throw new InvalidOperationException("V-carving requires closed outline vectors. Open or single-line vectors should use engraving.");var tool=SelectedTool??throw new InvalidOperationException("Select a V-bit.");if(tool.Type is not ToolType.VBit and not ToolType.EngravingBit||tool.VBitAngleDegrees is not (>0 and <180))throw new InvalidOperationException("Choose a V-bit with a valid included angle.");var setup=SelectedCuttingSetup(tool);var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create V-carving for one material sheet at a time.");return new(){Name=$"V-Carve {CamJob.Operations.Count+1}",GeometryIds=selected.Select(x=>x.Id).ToList(),Tool=setup.Snapshot,Order=CamJob.Operations.Count,MaximumDepthMm=Math.Min(3,Document.Job.ThicknessMm),OffsetStepOverMm=Math.Max(.1,(tool.TipDiameterMm??.2)*.5),SafeZMm=Document.Job.SafeZMm,FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin,PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin,SpindleRpm=setup.Snapshot.SpindleRpm,MaterialSheetId=sheets[0]};}
    public PocketOperationDefinition CreatePocketDraft(){if(Selection.Count==0)throw new InvalidOperationException("Select one or more closed pocket boundaries.");var selected=SelectedEntities();if(selected.Any(x=>!IsProfileContour(x)))throw new InvalidOperationException("Pocket boundaries must be closed vectors.");var tool=SelectedTool??throw new InvalidOperationException("Select a pocket tool.");var setup=SelectedCuttingSetup(tool);var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create a pocket from one material sheet at a time.");return new(){Name=$"Pocket {CamJob.Operations.Count+1}",GeometryIds=selected.Select(x=>x.Id).ToList(),Tool=setup.Snapshot,Order=CamJob.Operations.Count,CutDepthMm=Math.Min(6,Document.Job.ThicknessMm),PassDepthMm=Math.Min(setup.Snapshot.StepDownMm,Document.Job.ThicknessMm),StepOverMm=Math.Max(.1,tool.DiameterMm*setup.StepOverPercent/100),SafeZMm=Document.Job.SafeZMm,FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin,PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin,SpindleRpm=setup.Snapshot.SpindleRpm,MaterialSheetId=sheets[0]};}
    public DrillOperationDefinition CreateDrillDraft(){if(Selection.Count==0)throw new InvalidOperationException("Select circle centers or explicit drill points.");var selected=SelectedEntities();if(selected.Any(x=>x is not CircleEntity and not DrillPointEntity))throw new InvalidOperationException("Drilling supports circles and explicit drill points.");var tool=SelectedTool??throw new InvalidOperationException("Select a drill tool.");var setup=SelectedCuttingSetup(tool);var sheets=selected.Select(x=>x.MaterialSheetId).Distinct().ToArray();if(sheets.Length>1)throw new InvalidOperationException("Create drilling from one material sheet at a time.");return new(){Name=$"Drill {CamJob.Operations.Count+1}",GeometryIds=selected.Select(x=>x.Id).ToList(),Tool=setup.Snapshot,Order=CamJob.Operations.Count,CutDepthMm=Math.Min(Document.Job.ThicknessMm,tool.CuttingLengthMm??Document.Job.ThicknessMm),PeckDepthMm=Math.Max(.1,setup.Snapshot.StepDownMm),SafeZMm=Document.Job.SafeZMm,RetractZMm=Math.Min(1,Document.Job.SafeZMm/2),PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin,SpindleRpm=setup.Snapshot.SpindleRpm,MaterialSheetId=sheets[0]};}
    public SurfaceOperationDefinition CreateSurfaceDraft(){var tool=SelectedTool??throw new InvalidOperationException("Select a surfacing tool.");if(tool.Type!=ToolType.SurfacingBit)throw new InvalidOperationException("Choose a surfacing bit for stock facing.");var selected=Selection.Count==0?[]:SelectedEntities();if(selected.Length>1||selected.Length==1&&!IsProfileContour(selected[0]))throw new InvalidOperationException("Select one simple closed boundary for regional surfacing, or clear the selection to face the whole stock.");var bounds=selected.Length==1?selected[0].GetBounds():new BoundingBox(0,0,Document.Job.WidthMm,Document.Job.HeightMm);var sheets=selected.Select(entity=>entity.MaterialSheetId).Distinct().ToArray();var setup=SelectedCuttingSetup(tool);return new(){Name=$"Surface {CamJob.Operations.Count+1}",GeometryIds=[..selected.Select(entity=>entity.Id)],Tool=setup.Snapshot,Order=CamJob.Operations.Count,OriginXmm=bounds.MinX,OriginYmm=bounds.MinY,WidthMm=bounds.Width,HeightMm=bounds.Height,CutDepthMm=Math.Min(.5,Document.Job.ThicknessMm),PassDepthMm=Math.Min(.5,setup.Snapshot.StepDownMm),StepOverMm=Math.Max(.1,tool.DiameterMm*setup.StepOverPercent/100),OvertravelMm=tool.DiameterMm/2,SafeZMm=Document.Job.SafeZMm,FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin,PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin,SpindleRpm=setup.Snapshot.SpindleRpm,MaterialSheetId=sheets.Length==1?sheets[0]:SelectedMaterialSheet?.Id};}
    private (ToolSnapshot Snapshot,double StepOverPercent) SelectedCuttingSetup(CncTool tool)
    {
        var machineId=SelectedMachine?.Id;var materialId=SelectedMaterial?.Id;var preset=SelectedToolPreset is{} selected&&selected.ToolId==tool.Id&&selected.MachineId==machineId&&selected.MaterialId==materialId?selected:ToolPresets.FirstOrDefault(value=>value.ToolId==tool.Id&&value.MachineId==machineId&&value.MaterialId==materialId);return preset is null?(ToolSnapshot.FromTool(tool),tool.StepOverPercent):(preset.Apply(tool),preset.StepOverPercent);
    }
    public void AddOperation(ProfileOperationDefinition operation) { ClearPlayback(); InvalidatePreflight(); CamJob.Operations.Add(operation); CamJob.NormalizeOrder(); CalculateOperation(operation); RefreshOperations(); SelectedOperation = operation; IsDirty = true; RefreshWorkspaceState(); }
    public void AddOperation(EngraveOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void AddOperation(VCarveOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void AddOperation(PocketOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void AddOperation(DrillOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void AddOperation(SurfaceOperationDefinition operation) { ClearPlayback();InvalidatePreflight();CamJob.Operations.Add(operation);CamJob.NormalizeOrder();CalculateOperation(operation);RefreshOperations();SelectedOperation=operation;IsDirty=true;RefreshWorkspaceState(); }
    public void BeginOperationEditor(CamOperationDefinition draft,bool isNew)
    {
        OperationEditorTarget=isNew?null:draft;OperationEditorDraft=isNew?draft:CopyOperation(draft);IsNewOperationEditor=isNew;IsOperationEditorOpen=true;ToolpathTaskPage=ToolpathTaskPage.OperationEditor;SetWorkspaceMode(CncCad.Infrastructure.Settings.WorkspaceMode.Toolpaths);OnPropertyChanged(nameof(ToolpathTaskTitle));
    }
    public void SelectVectorsForEditor(){if(OperationEditorDraft is null)throw new InvalidOperationException("Open a toolpath editor first.");if(Selection.Count==0)throw new InvalidOperationException("Select vectors in the 2D view first.");OperationEditorDraft.GeometryIds=[..Selection.Ids];OnPropertyChanged(nameof(OperationEditorDraft));Message=$"Selected {Selection.Count} vector(s) for {OperationEditorDraft.Name}";}
    public void ApplySelectedToolToEditor(){if(OperationEditorDraft is null||SelectedTool is null)throw new InvalidOperationException("Select a library tool first.");var setup=SelectedCuttingSetup(SelectedTool);OperationEditorDraft.Tool=setup.Snapshot;switch(OperationEditorDraft){case ProfileOperationDefinition value:value.PassDepthMm=setup.Snapshot.StepDownMm;value.FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin;value.PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin;value.SpindleRpm=setup.Snapshot.SpindleRpm;break;case PocketOperationDefinition value:value.PassDepthMm=setup.Snapshot.StepDownMm;value.StepOverMm=Math.Max(.1,setup.Snapshot.DiameterMm*setup.StepOverPercent/100);value.FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin;value.PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin;value.SpindleRpm=setup.Snapshot.SpindleRpm;break;case DrillOperationDefinition value:value.PeckDepthMm=setup.Snapshot.StepDownMm;value.PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin;value.SpindleRpm=setup.Snapshot.SpindleRpm;break;case EngraveOperationDefinition value:value.PassDepthMm=setup.Snapshot.StepDownMm;value.FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin;value.PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin;value.SpindleRpm=setup.Snapshot.SpindleRpm;break;case VCarveOperationDefinition value:value.FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin;value.PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin;value.SpindleRpm=setup.Snapshot.SpindleRpm;break;case SurfaceOperationDefinition value:value.PassDepthMm=setup.Snapshot.StepDownMm;value.StepOverMm=Math.Max(.1,setup.Snapshot.DiameterMm*setup.StepOverPercent/100);value.FeedRateMmPerMin=setup.Snapshot.FeedRateMmPerMin;value.PlungeRateMmPerMin=setup.Snapshot.PlungeRateMmPerMin;value.SpindleRpm=setup.Snapshot.SpindleRpm;break;}OnPropertyChanged(nameof(OperationEditorDraft));}
    public void ApplyOperationEditor()
    {
        if(OperationEditorDraft is null)return;var draft=OperationEditorDraft;var validation=_operationCalculator.Calculate(draft,Document.Entities.ToDictionary(x=>x.Id));if(!validation.Success)throw new InvalidOperationException(string.Join(" ",validation.Errors.Select(x=>x.Message)));var target=OperationEditorTarget;if(target is null){switch(draft){case ProfileOperationDefinition value:AddOperation(value);break;case PocketOperationDefinition value:AddOperation(value);break;case DrillOperationDefinition value:AddOperation(value);break;case EngraveOperationDefinition value:AddOperation(value);break;case VCarveOperationDefinition value:AddOperation(value);break;case SurfaceOperationDefinition value:AddOperation(value);break;}}else switch(target,draft){case(ProfileOperationDefinition t,ProfileOperationDefinition v):UpdateOperation(t,v);break;case(PocketOperationDefinition t,PocketOperationDefinition v):UpdateOperation(t,v);break;case(DrillOperationDefinition t,DrillOperationDefinition v):UpdateOperation(t,v);break;case(EngraveOperationDefinition t,EngraveOperationDefinition v):UpdateOperation(t,v);break;case(VCarveOperationDefinition t,VCarveOperationDefinition v):UpdateOperation(t,v);break;case(SurfaceOperationDefinition t,SurfaceOperationDefinition v):UpdateOperation(t,v);break;default:throw new InvalidOperationException("The operation editor type changed unexpectedly.");}IsOperationEditorOpen=false;OperationEditorDraft=null;OperationEditorTarget=null;ToolpathTaskPage=ToolpathTaskPage.Palette;
    }
    public void CancelOperationEditor(){IsOperationEditorOpen=false;OperationEditorDraft=null;OperationEditorTarget=null;ToolpathTaskPage=ToolpathTaskPage.Palette;}
    public void UpdateOperation(ProfileOperationDefinition target, ProfileOperationDefinition values)
    {
        target.Name = values.Name; target.GeometryIds = [.. values.GeometryIds]; target.Tool = values.Tool; target.IsEnabled = values.IsEnabled; target.Side = values.Side; target.Direction = values.Direction; target.StartDepthMm = values.StartDepthMm; target.CutDepthMm = values.CutDepthMm; target.PassDepthMm = values.PassDepthMm; target.AllowanceMm=values.AllowanceMm;target.StartPointFraction=values.StartPointFraction;target.LeadInLengthMm=values.LeadInLengthMm;target.LeadOutLengthMm=values.LeadOutLengthMm;target.LeadStyle=values.LeadStyle;target.EntryMode=values.EntryMode;target.RampLengthMm=values.RampLengthMm;target.OnionSkinMm=values.OnionSkinMm;target.FinishPassEnabled=values.FinishPassEnabled;target.FinishAllowanceMm=values.FinishAllowanceMm;target.LastPassDepthMm=values.LastPassDepthMm; target.SafeZMm = values.SafeZMm; target.FeedRateMmPerMin = values.FeedRateMmPerMin; target.PlungeRateMmPerMin = values.PlungeRateMmPerMin; target.SpindleRpm = values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;target.Tabs=values.Tabs.Copy();
        ClearPlayback(); InvalidatePreflight(); CalculateOperation(target); RefreshOperations(); IsDirty = true; RefreshWorkspaceState();
    }
    public void UpdateOperation(EngraveOperationDefinition target,EngraveOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.Direction=values.Direction;target.StartDepthMm=values.StartDepthMm;target.CutDepthMm=values.CutDepthMm;target.PassDepthMm=values.PassDepthMm;target.SafeZMm=values.SafeZMm;target.FeedRateMmPerMin=values.FeedRateMmPerMin;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
    public void UpdateOperation(VCarveOperationDefinition target,VCarveOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.MaximumDepthMm=values.MaximumDepthMm;target.ClearFlatRegions=values.ClearFlatRegions;target.OffsetStepOverMm=values.OffsetStepOverMm;target.SafeZMm=values.SafeZMm;target.FeedRateMmPerMin=values.FeedRateMmPerMin;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
    public void UpdateOperation(PocketOperationDefinition target,PocketOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.StartDepthMm=values.StartDepthMm;target.CutDepthMm=values.CutDepthMm;target.PassDepthMm=values.PassDepthMm;target.StepOverMm=values.StepOverMm;target.AllowanceMm=values.AllowanceMm;target.RoughAllowanceMm=values.RoughAllowanceMm;target.FinishPassEnabled=values.FinishPassEnabled;target.Direction=values.Direction;target.Strategy=values.Strategy;target.EntryMode=values.EntryMode;target.RampLengthMm=values.RampLengthMm;target.SafeZMm=values.SafeZMm;target.FeedRateMmPerMin=values.FeedRateMmPerMin;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
    public void UpdateOperation(DrillOperationDefinition target,DrillOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.Cycle=values.Cycle;target.Ordering=values.Ordering;target.CutDepthMm=values.CutDepthMm;target.PeckDepthMm=values.PeckDepthMm;target.SafeZMm=values.SafeZMm;target.RetractZMm=values.RetractZMm;target.DwellSeconds=values.DwellSeconds;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
    public void UpdateOperation(SurfaceOperationDefinition target,SurfaceOperationDefinition values){target.Name=values.Name;target.GeometryIds=[..values.GeometryIds];target.Tool=values.Tool;target.IsEnabled=values.IsEnabled;target.OriginXmm=values.OriginXmm;target.OriginYmm=values.OriginYmm;target.WidthMm=values.WidthMm;target.HeightMm=values.HeightMm;target.StartDepthMm=values.StartDepthMm;target.CutDepthMm=values.CutDepthMm;target.PassDepthMm=values.PassDepthMm;target.StepOverMm=values.StepOverMm;target.OvertravelMm=values.OvertravelMm;target.Strategy=values.Strategy;target.SafeZMm=values.SafeZMm;target.FeedRateMmPerMin=values.FeedRateMmPerMin;target.PlungeRateMmPerMin=values.PlungeRateMmPerMin;target.SpindleRpm=values.SpindleRpm;target.MaterialSheetId=values.MaterialSheetId;ClearPlayback();InvalidatePreflight();CalculateOperation(target);RefreshOperations();IsDirty=true;RefreshWorkspaceState();}
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
    public void DuplicateOperation(PocketOperationDefinition operation) { ClearPlayback();InvalidatePreflight();var copy=operation.Duplicate();CamJob.Operations.Insert(Math.Min(operation.Order+1,CamJob.Operations.Count),copy);CamJob.NormalizeOrder();CalculateOperation(copy);RefreshOperations();SelectedOperation=copy;IsDirty=true;RefreshWorkspaceState(); }
    public void DuplicateOperation(DrillOperationDefinition operation) { ClearPlayback();InvalidatePreflight();var copy=operation.Duplicate();CamJob.Operations.Insert(Math.Min(operation.Order+1,CamJob.Operations.Count),copy);CamJob.NormalizeOrder();CalculateOperation(copy);RefreshOperations();SelectedOperation=copy;IsDirty=true;RefreshWorkspaceState(); }
    public void DuplicateOperation(SurfaceOperationDefinition operation) { ClearPlayback();InvalidatePreflight();var copy=operation.Duplicate();CamJob.Operations.Insert(Math.Min(operation.Order+1,CamJob.Operations.Count),copy);CamJob.NormalizeOrder();CalculateOperation(copy);RefreshOperations();SelectedOperation=copy;IsDirty=true;RefreshWorkspaceState(); }
    public void ToggleOperation(CamOperationDefinition operation) { ClearPlayback(); InvalidatePreflight(); operation.IsEnabled = !operation.IsEnabled; operation.Status = operation.IsEnabled ? OperationStatus.Stale : OperationStatus.Disabled; if (operation.IsEnabled) CalculateOperation(operation); RefreshOperations(); RebuildProgram(); IsDirty = true; }
    public void MoveOperation(CamOperationDefinition operation, int delta) { var old = CamJob.Operations.IndexOf(operation); var next = Math.Clamp(old + delta, 0, CamJob.Operations.Count - 1); if (old == next) return; ClearPlayback(); InvalidatePreflight(); CamJob.Operations.RemoveAt(old); CamJob.Operations.Insert(next, operation); CamJob.NormalizeOrder(); RefreshOperations(); RebuildProgram(); SelectedOperation = operation; IsDirty = true; }
    public void CalculateOperation(CamOperationDefinition operation)
    {
        if (!operation.IsEnabled) { operation.Status = OperationStatus.Disabled; operation.CalculatedToolpath = null; RebuildProgram(); return; }
        var result = _operationCalculator.Calculate(operation, Document.Entities.ToDictionary(e => e.Id)); operation.CalculatedToolpath = result.Toolpath; operation.Error = result.Errors.Count == 0 ? null : string.Join(" ", result.Errors.Select(e => e.Message)); operation.Status = result.Success ? OperationStatus.Current : OperationStatus.Invalid;
        if (result.Toolpath is not null) operation.EstimatedTime = MachineTimeEstimator.Estimate(new ToolpathProgram { Operations = [result.Toolpath] },CamJob.Machine?.RapidRateMmPerMin??5000,CamJob.Machine?.ToolChangeSeconds??0);else operation.EstimatedTime=TimeSpan.Zero; RebuildProgram();
    }
    public void RecalculateAll() { foreach (var operation in CamJob.Operations) CalculateOperation(operation); RefreshOperations(); RebuildProgram(); }
    public string PreviewGCode()
    {
        RecalculateAll(); var problem = CamJob.Operations.FirstOrDefault(o => o.IsEnabled && MatchesActiveSheet(o) && o.Status != OperationStatus.Current); if (problem is not null) throw new InvalidOperationException($"{problem.Name}: {problem.Error ?? "Toolpath is not current."}");
        if (Program.Operations.Count == 0) throw new InvalidOperationException("Create at least one enabled toolpath first.");
        var safety = RunPreflight(); if (!safety.CanExport) throw new InvalidOperationException($"Preflight blocked export: {safety.ErrorCount} error(s). {safety.Findings.First(x => x.Severity == SafetySeverity.Error).Message}");
        var machine=CamJob.Machine!;var result=ProcessPost(Program,machine,SafeExportScope(Program));GCodePreview=string.Join(Environment.NewLine+Environment.NewLine,result.Files.Select(x=>$"--- {x.FileName}{x.Extension} ---{Environment.NewLine}{x.Content}"));return GCodePreview;
    }
    public IReadOnlyList<GeneratedToolpathFile> GenerateExportPrograms(ToolpathExportScope scope)
    {
        RecalculateAll();var machine=CamJob.Machine??throw new InvalidOperationException("Select a complete machine snapshot before export.");var post=_postProcessors.GetRequired(machine.PostProcessorId);var candidates=CamJob.Operations.Where(x=>x.IsEnabled&&MatchesActiveSheet(x)).OrderBy(x=>x.Order).ToArray();
        if(scope==ToolpathExportScope.SelectedToolpath)candidates=SelectedOperation is{IsEnabled:true} selected&&MatchesActiveSheet(selected)?[selected]:throw new InvalidOperationException("Select a visible enabled toolpath first.");
        if(candidates.Length==0)throw new InvalidOperationException("There are no visible enabled toolpaths to export.");var invalid=candidates.FirstOrDefault(x=>x.Status!=OperationStatus.Current||x.CalculatedToolpath is null);if(invalid is not null)throw new InvalidOperationException($"{invalid.Name}: {invalid.Error??"toolpath is not current"}");
        var name=GCodeExportFormat.SafeFileStem(scope==ToolpathExportScope.SelectedToolpath?candidates[0].Name:Document.Job.Name);
        var postScope=scope==ToolpathExportScope.VisibleSeparateFiles?PostExportScope.SeparateByTool:PostExportScope.Combined;
        return EnsureUniquePrograms(Generate(candidates,name,postScope));
        IReadOnlyList<GeneratedToolpathFile> Generate(IReadOnlyList<CamOperationDefinition> definitions,string name,PostExportScope exportScope){var program=new ToolpathProgram{Name=name,Operations=definitions.Select(x=>x.CalculatedToolpath!).ToList()};var safety=RunPreflight(definitions,program);if(!safety.CanExport)throw new InvalidOperationException($"Preflight blocked export: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");var result=ProcessPost(program,machine,exportScope);return result.Files.Select(file=>new GeneratedToolpathFile(file.FileName,file.Extension,file.Content,safety.WarningCount+result.Warnings.Count)).ToArray();}
    }
    private static IReadOnlyList<GeneratedToolpathFile> EnsureUniquePrograms(IReadOnlyList<GeneratedToolpathFile> files)
    {
        var duplicate=files.GroupBy(file=>file.FileStem+file.Extension,StringComparer.OrdinalIgnoreCase).FirstOrDefault(group=>group.Count()>1);
        if(duplicate is not null)throw new InvalidOperationException($"Generated toolpaths contain a duplicate filename: {duplicate.Key}. Rename the operations before export.");
        return files;
    }
    public IReadOnlyList<(MaterialSheetDefinition Sheet,string FileStem,string Extension,string GCode,int WarningCount)> GenerateSheetPrograms()
    {
        if(MaterialSheets.Count==0)throw new InvalidOperationException("Accept a multi-sheet nesting layout first.");RecalculateAll();var machine=CamJob.Machine??throw new InvalidOperationException("Select a complete machine snapshot before export.");var output=new List<(MaterialSheetDefinition,string,string,string,int)>();foreach(var sheet in MaterialSheets){var operations=CamJob.Operations.Where(x=>x.IsEnabled&&OperationSheet(x)==sheet.Id).OrderBy(x=>x.Order).ToArray();var invalid=operations.FirstOrDefault(x=>x.CalculatedToolpath is null||x.Status!=OperationStatus.Current);if(invalid is not null)throw new InvalidOperationException($"{sheet.Name} · {invalid.Name}: {invalid.Error??"toolpath is not current"}");if(operations.Length==0)throw new InvalidOperationException($"{sheet.Name} has no enabled operations.");var program=new ToolpathProgram{Name=$"{Document.Job.Name} - {sheet.Name}",Operations=operations.Select(x=>x.CalculatedToolpath!).ToList()};var safety=RunPreflight(operations,program);if(!safety.CanExport)throw new InvalidOperationException($"{sheet.Name} preflight blocked export: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");var result=ProcessPost(program,machine,SafeExportScope(program));foreach(var file in result.Files)output.Add((sheet,file.FileName,file.Extension,file.Content,safety.WarningCount+result.Warnings.Count));}var duplicate=output.GroupBy(item=>item.Item2+item.Item3,StringComparer.OrdinalIgnoreCase).FirstOrDefault(group=>group.Count()>1);if(duplicate is not null)throw new InvalidOperationException($"Generated sheet programs contain a duplicate filename: {duplicate.Key}.");return output;
    }
    public void LoadSelectedPlayback()
    {
        var operation = SelectedOperation ?? throw new InvalidOperationException("Select a toolpath from the list first.");
        if (!operation.IsEnabled) throw new InvalidOperationException($"{operation.Name} is disabled. Enable it before previewing.");
        CalculateOperation(operation); if (operation.CalculatedToolpath is null) throw new InvalidOperationException($"{operation.Name}: {operation.Error ?? "Toolpath could not be calculated."}");
        var selectedProgram=new ToolpathProgram{Name=operation.Name,Operations=[operation.CalculatedToolpath]};var safety=RunPreflight([operation],selectedProgram);if(!safety.CanPreview)throw new InvalidOperationException($"Preflight blocked preview: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");
        Playback.Load([new PlaybackProgramItem(operation.CalculatedToolpath, operation.Tool.DiameterMm)],CamJob.Machine?.RapidRateMmPerMin??5000,CamJob.Machine?.ToolChangeSeconds??0); PlaybackSessionLabel = $"Selected · {operation.Name}"; FinishPlaybackLoad([operation]);
    }
    public void LoadAllPlayback()
    {
        RecalculateAll(); var enabled = CamJob.Operations.OrderBy(operation => operation.Order).Where(operation => operation.IsEnabled&&MatchesActiveSheet(operation)).ToArray();
        if (enabled.Length == 0) throw new InvalidOperationException("Create or enable at least one toolpath first.");
        var invalid = enabled.FirstOrDefault(operation => operation.CalculatedToolpath is null); if (invalid is not null) throw new InvalidOperationException($"{invalid.Name}: {invalid.Error ?? "Toolpath could not be calculated."}");
        var previewProgram=new ToolpathProgram{Name=Document.Job.Name,Operations=enabled.Select(x=>x.CalculatedToolpath!).ToList()};var safety=RunPreflight(enabled,previewProgram);if(!safety.CanPreview)throw new InvalidOperationException($"Preflight blocked preview: {safety.Findings.First(x=>x.Severity==SafetySeverity.Error).Message}");
        Playback.Load(enabled.Select(operation => new PlaybackProgramItem(operation.CalculatedToolpath!, operation.Tool.DiameterMm)),CamJob.Machine?.RapidRateMmPerMin??5000,CamJob.Machine?.ToolChangeSeconds??0); PlaybackSessionLabel = $"All enabled · {enabled.Length} operation{(enabled.Length == 1 ? "" : "s")}"; FinishPlaybackLoad(enabled);
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
    public void SelectMachineSnapshot(){if(SelectedMachine is null)throw new InvalidOperationException("Select a machine profile.");CamJob.Machine=SelectedMachine.Snapshot();CamJob.SafetySetupIncomplete=false;InvalidatePreflight();IsDirty=true;OnPropertyChanged(nameof(CanSaveToolpaths));OnPropertyChanged(nameof(ExportAvailabilityMessage));Message=$"Machine snapshot updated from {SelectedMachine.Name}";}
    public async Task SaveSelectedMachineAsync(){if(SelectedMachine is null||_machineProfiles is null)throw new InvalidOperationException("Select a machine profile.");if(!SelectedMachine.Snapshot().IsComplete)throw new InvalidOperationException("Complete all required machine limits, rates, clearance and holder values.");await _machineProfiles.SaveAsync(SelectedMachine);RefreshMachineFilter();SelectMachineSnapshot();Message=$"Saved {SelectedMachine.Name} and updated this job's snapshot";}
    public void AddMachineProfile(){var index=Machines.Count+1;var machine=new MachineProfileDefinition{Name=$"CNC Router {index}"};Machines.Add(machine);RefreshMachineFilter();SelectedMachine=machine;Message="New machine profile — complete and save all safety values.";}
    public async Task DeleteSelectedMachineAsync(){if(SelectedMachine is null||_machineProfiles is null)return;var removed=SelectedMachine;if(ToolPresets.Any(value=>value.MachineId==removed.Id))throw new InvalidOperationException("Delete or reassign cutting presets that reference this machine first.");await _machineProfiles.DeleteAsync(removed.Id);Machines.Remove(removed);RefreshMachineFilter();Message=$"Deleted machine library profile {removed.Name}. Existing job snapshots were not changed.";}
    public void AddDefaultFixture(){var index=CamJob.Fixtures.Count+1;var fixture=new FixtureDefinition{Name=$"Clamp {index}",MinXmm=10+(index-1)*40,MinYmm=10,MaxXmm=40+(index-1)*40,MaxYmm=40,TopZmm=10,RequiredClearanceMm=2};CamJob.Fixtures.Add(fixture);Fixtures.Add(fixture);SelectedFixture=fixture;InvalidatePreflight();IsDirty=true;}
    public void FixtureChanged(){InvalidatePreflight();IsDirty=true;Message="Fixture updated — run preflight again";}
    public void DeleteSelectedFixture(){if(SelectedFixture is null)return;CamJob.Fixtures.Remove(SelectedFixture);Fixtures.Remove(SelectedFixture);SelectedFixture=null;InvalidatePreflight();IsDirty=true;}
    private void FinishPlaybackLoad(IEnumerable<CamOperationDefinition> operations) { Playback.SetSpeed(PlaybackSpeed);StockSimulation=_stockSimulationService.Simulate(Document.Job.WidthMm,Document.Job.HeightMm,Document.Job.ThicknessMm,operations); UpdatePlaybackStatus(); }
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    private void RebuildProgram() { Program.Name = Document.Job.Name; Program.Operations = CamJob.Operations.OrderBy(o => o.Order).Where(o => o.IsEnabled && o.Status == OperationStatus.Current && o.CalculatedToolpath is not null&&MatchesActiveSheet(o)).Select(o => o.CalculatedToolpath!).ToList(); }
    private static Guid? OperationSheet(CamOperationDefinition operation)=>operation switch{ProfileOperationDefinition p=>p.MaterialSheetId,EngraveOperationDefinition e=>e.MaterialSheetId,VCarveOperationDefinition v=>v.MaterialSheetId,PocketOperationDefinition p=>p.MaterialSheetId,DrillOperationDefinition d=>d.MaterialSheetId,SurfaceOperationDefinition s=>s.MaterialSheetId,_=>null};
    private bool MatchesActiveSheet(CamOperationDefinition operation){var sheet=OperationSheet(operation);return SelectedMaterialSheet is null?sheet is null:sheet==SelectedMaterialSheet.Id;}
    private MachineProfile BuildPostMachine(MachineSnapshot machine){var origin=CncCad.Core.Transforms.MachineCoordinateTransform.OriginInDocument(Document.Job);return new(){PostProcessor=machine.PostProcessorId,SafeZMm=Math.Max(Document.Job.SafeZMm,machine.DefaultClearanceMm),RapidRateMmPerMin=machine.RapidRateMmPerMin,MaximumSpindleRpm=machine.MaximumSpindleRpm,ParkXmm=machine.ParkXmm,ParkYmm=machine.ParkYmm,ParkEnabled=machine.ParkEnabled,WorkZeroDocumentXmm=origin.X,WorkZeroDocumentYmm=origin.Y,WorkZeroZOffsetMm=Document.Job.ZZero==ZZeroPosition.MachineBed?Document.Job.ThicknessMm:0,ControllerFamily=machine.ControllerFamily,SupportsArcs=machine.SupportsArcs,ToolChangeMode=machine.ToolChangeMode};}
    private static PostExportScope SafeExportScope(ToolpathProgram program)
    {
        var toolCount=program.Operations.SelectMany(operation=>operation.Moves).OfType<ToolChange>().Select(change=>change.ToolNumber).Distinct().Count();
        return toolCount<=1?PostExportScope.Combined:PostExportScope.SeparateByTool;
    }
    private PostProcessResult ProcessPost(ToolpathProgram program,MachineSnapshot machine,PostExportScope scope){var post=_postProcessors.GetRequired(machine.PostProcessorId);var result=post.Process(new(program,BuildPostMachine(machine),machine.OutputUnit,post.Definition,scope));if(!result.Success)throw new InvalidOperationException(string.Join(" ",result.Errors.Select(x=>x.Message)));return result;}
    private void RefreshOperations() { var selected = SelectedOperation; Operations.Clear(); foreach (var operation in CamJob.Operations.OrderBy(o => o.Order).Where(MatchesActiveSheet)) Operations.Add(operation); SelectedOperation = selected is not null&&Operations.Contains(selected)?selected:null; }
    public async Task AddToolAsync() { var tool = new CncTool { Name = "New 6mm End Mill", ToolNumber = Tools.Count + 1, DiameterMm = 6, CuttingLengthMm=24, OverallLengthMm=50, ShankDiameterMm=6, StickOutMm=30 }; await _toolLibrary.SaveAsync(tool); Tools.Add(tool);RefreshToolFilter(); SelectedTool = tool; Message = "Tool added to the persistent library"; }
    public async Task SaveSelectedToolAsync(){if(SelectedTool is null)throw new InvalidOperationException("Select a tool.");await _toolLibrary.SaveAsync(SelectedTool);RefreshToolFilter();Message=$"Saved {SelectedTool.Name}";}
    public async Task DeleteSelectedToolAsync(){if(SelectedTool is null)return;var tool=SelectedTool;if(ToolPresets.Any(value=>value.ToolId==tool.Id))throw new InvalidOperationException("Delete or reassign cutting presets that reference this tool first.");await _toolLibrary.DeleteAsync(tool.Id);Tools.Remove(tool);RefreshToolFilter();Message=$"Deleted {tool.Name}";}
    public async Task AddMaterialAsync(){if(_materialLibrary is null)throw new InvalidOperationException("Material library is unavailable.");var material=new MaterialDefinition{Name=$"New material {Materials.Count+1}",Category="General"};await _materialLibrary.SaveAsync(material);Materials.Add(material);RefreshMaterialFilter();SelectedMaterial=material;Message="Material added to the persistent library";}
    public async Task SaveSelectedMaterialAsync(){if(SelectedMaterial is null||_materialLibrary is null)throw new InvalidOperationException("Select a material.");if(string.IsNullOrWhiteSpace(SelectedMaterial.Name))throw new InvalidOperationException("Material name is required.");await _materialLibrary.SaveAsync(SelectedMaterial);RefreshMaterialFilter();Message=$"Saved {SelectedMaterial.Name}";}
    public async Task DeleteSelectedMaterialAsync(){if(SelectedMaterial is null||_materialLibrary is null)return;var material=SelectedMaterial;if(ToolPresets.Any(value=>value.MaterialId==material.Id))throw new InvalidOperationException("Delete or reassign cutting presets that reference this material first.");await _materialLibrary.DeleteAsync(material.Id);Materials.Remove(material);RefreshMaterialFilter();Message=$"Deleted {material.Name}";}
    public async Task AddPresetAsync(){if(_presetRepository is null)throw new InvalidOperationException("Cutting preset library is unavailable.");var tool=SelectedTool??throw new InvalidOperationException("Select a tool for the preset.");var machine=SelectedMachine??throw new InvalidOperationException("Select a machine for the preset.");var material=SelectedMaterial??throw new InvalidOperationException("Select a material for the preset.");var preset=new ToolCuttingPreset{ToolId=tool.Id,MachineId=machine.Id,MaterialId=material.Id,FeedRateMmPerMin=tool.FeedRateMmPerMin,PlungeRateMmPerMin=tool.PlungeRateMmPerMin,SpindleRpm=tool.SpindleRpm,PassDepthMm=tool.StepDownMm,StepOverPercent=tool.StepOverPercent};await _presetRepository.SaveAsync(preset);ToolPresets.Add(preset);RefreshPresetFilter();SelectedToolPreset=preset;Message="Cutting preset added for the selected tool, machine and material";}
    public async Task SaveSelectedPresetAsync(){if(SelectedToolPreset is null||_presetRepository is null)throw new InvalidOperationException("Select a cutting preset.");var errors=SelectedToolPreset.Validate();if(errors.Count>0)throw new InvalidOperationException(string.Join(" ",errors));if(Tools.All(value=>value.Id!=SelectedToolPreset.ToolId)||Machines.All(value=>value.Id!=SelectedToolPreset.MachineId)||Materials.All(value=>value.Id!=SelectedToolPreset.MaterialId))throw new InvalidOperationException("The preset references a missing tool, machine, or material.");await _presetRepository.SaveAsync(SelectedToolPreset);RefreshPresetFilter();Message="Cutting preset saved";}
    public async Task DeleteSelectedPresetAsync(){if(SelectedToolPreset is null||_presetRepository is null)return;var preset=SelectedToolPreset;await _presetRepository.DeleteAsync(preset.Id);ToolPresets.Remove(preset);RefreshPresetFilter();Message="Cutting preset deleted";}
    public async Task ExportLibraryAsync(Stream destination,CancellationToken cancellationToken=default)
    {
        var exchange=_libraryExchange??throw new InvalidOperationException("Complete library exchange is unavailable.");switch(SelectedLibraryExchangeFormat){case LibraryExchangeFormat.CompleteJson:await exchange.ExportJsonAsync(destination,cancellationToken);break;case LibraryExchangeFormat.ToolsCsv:await exchange.ExportToolsCsvAsync(destination,cancellationToken);break;case LibraryExchangeFormat.MaterialsCsv:await exchange.ExportMaterialsCsvAsync(destination,cancellationToken);break;case LibraryExchangeFormat.MachinesCsv:await exchange.ExportMachinesCsvAsync(destination,cancellationToken);break;case LibraryExchangeFormat.PresetsCsv:await exchange.ExportPresetsCsvAsync(destination,cancellationToken);break;default:throw new ArgumentOutOfRangeException();}Message=$"Exported {SelectedLibraryExchangeFormat} library";
    }
    public async Task<LibraryImportResult> ImportLibraryAsync(Stream source,CancellationToken cancellationToken=default)
    {
        var exchange=_libraryExchange??throw new InvalidOperationException("Complete library exchange is unavailable.");var result=SelectedLibraryExchangeFormat switch{LibraryExchangeFormat.CompleteJson=>await exchange.ImportJsonAsync(source,cancellationToken),LibraryExchangeFormat.ToolsCsv=>await exchange.ImportToolsCsvAsync(source,cancellationToken),LibraryExchangeFormat.MaterialsCsv=>await exchange.ImportMaterialsCsvAsync(source,cancellationToken),LibraryExchangeFormat.MachinesCsv=>await exchange.ImportMachinesCsvAsync(source,cancellationToken),LibraryExchangeFormat.PresetsCsv=>await exchange.ImportPresetsCsvAsync(source,cancellationToken),_=>throw new ArgumentOutOfRangeException()};await ReloadLibrariesAsync();Message=$"Imported {result.Tools+result.Machines+result.Materials+result.Presets} library record(s) · {result.Warnings.Count} warning(s)";return result;
    }
    public string LibraryExchangeExtension=>SelectedLibraryExchangeFormat==LibraryExchangeFormat.CompleteJson?"json":"csv";
    public async Task SaveProjectAsync(string path)
    {
        var fullPath=NormalizeProjectPath(path);var directory=Path.GetDirectoryName(fullPath)??Directory.GetCurrentDirectory();Directory.CreateDirectory(directory);var temporary=Path.Combine(directory,$".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.WriteThrough)){await _projectSerializer.SaveAsync(new CncProject { Document = Document, CamJob = CamJob,Nesting=NestingJob,PreviewSettings=PreviewSettings }, stream);await stream.FlushAsync();stream.Flush(true);}
            File.Move(temporary,fullPath,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        ProjectPath = fullPath; IsDirty = false; JobTitle = Document.Job.Name; Message = $"Saved {Path.GetFileName(fullPath)}";
        if(_recoveryStore is not null){await _recoveryStore.DeleteJobAsync(_recoveryJobKey);await RefreshRecoveriesAsync();}
        try{await _recentProjectStore.AddAsync(ProjectPath);await RefreshRecentProjectsAsync();}
        catch{Message=$"Saved {Path.GetFileName(fullPath)} (recent projects could not be updated)";}
    }
    public static string NormalizeProjectPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path))throw new ArgumentException("Choose a project filename.",nameof(path));
        const string extension=".cnccad";
        var fullPath=Path.GetFullPath(path.Trim());
        var directory=Path.GetDirectoryName(fullPath)??Directory.GetCurrentDirectory();
        var fileName=Path.GetFileName(fullPath);
        var hadProjectExtension=false;
        while(fileName.EndsWith(extension,StringComparison.OrdinalIgnoreCase))
        {
            fileName=fileName[..^extension.Length];
            hadProjectExtension=true;
        }
        if(string.IsNullOrWhiteSpace(fileName))throw new ArgumentException("Choose a project filename.",nameof(path));
        var normalized=hadProjectExtension?fileName+extension:Path.ChangeExtension(fileName,extension);
        return Path.GetFullPath(Path.Combine(directory,normalized));
    }
    public async Task OpenProjectAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var loaded = await _projectSerializer.LoadAsync(stream); var document = loaded.Document;
        ClearPlayback();
        Document.Version = document.Version; Document.Job = document.Job; Document.Layers = document.Layers;Document.ActiveLayerId=document.ActiveLayerId; Document.Entities = document.Entities; CamJob = loaded.CamJob;PreviewSettings=loaded.PreviewSettings;ToolpathWorkspace=new(CamJob,Operations,Tools);OnPropertyChanged(nameof(ToolpathWorkspace));NestingJob=loaded.Nesting;CaptureInstalledNestingLayout();RefreshNesting(); Fixtures.Clear();foreach(var fixture in CamJob.Fixtures)Fixtures.Add(fixture);Selection.Clear(); _history.Clear(); UpdateHistoryState(); SelectionChanged();InvalidatePreflight();RefreshLayers();
        Program.Name = document.Job.Name; RecalculateAll();
        ProjectPath = Path.GetFullPath(path); HasJob = true; IsDirty = false; JobTitle = document.Job.Name;WorkspaceMode=CncCad.Infrastructure.Settings.WorkspaceMode.Design;
        GCodePreview = "Toolpaths are recalculated after opening a project."; Message = $"Opened {Path.GetFileName(path)}";
        RefreshWorkspaceState(); OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary));
        try{await _recentProjectStore.AddAsync(ProjectPath);await RefreshRecentProjectsAsync();}
        catch{Message=$"Opened {Path.GetFileName(path)} (recent projects could not be updated)";}
    }
    public async Task RemoveRecentProjectAsync(string path) { await _recentProjectStore.RemoveAsync(path); await RefreshRecentProjectsAsync(); }
    public async Task ClearRecentProjectsAsync() { await _recentProjectStore.ClearAsync(); await RefreshRecentProjectsAsync(); }
    private async Task RefreshRecentProjectsAsync()
    {
        RecentProjects.Clear();
        foreach (var project in await _recentProjectStore.GetAsync()) RecentProjects.Add(project);
    }
    private async Task RefreshRecoveriesAsync(){RecoverableProjects.Clear();if(_recoveryStore is null)return;foreach(var snapshot in await _recoveryStore.GetRecoverableAsync())RecoverableProjects.Add(snapshot);}
    public async Task SaveRecoveryIfDueAsync()
    {
        if(_recoveryStore is null||!HasJob||!IsDirty)return;var now=DateTimeOffset.UtcNow;if(now-_dirtySinceUtc<TimeSpan.FromSeconds(30)||now-_lastRecoveryUtc<TimeSpan.FromSeconds(30))return;
        await _recoveryStore.SaveAsync(_recoveryJobKey,Document.Job.Name,ProjectPath,new CncProject{Document=Document,CamJob=CamJob,Nesting=NestingJob,PreviewSettings=PreviewSettings});_lastRecoveryUtc=now;await RefreshRecoveriesAsync();Message="Recovery snapshot saved";
    }
    public async Task OpenRecoveryAsync(RecoverySnapshotInfo snapshot)
    {
        if(_recoveryStore is null)throw new InvalidOperationException("Recovery storage is unavailable.");var loaded=await _recoveryStore.LoadAsync(snapshot);ApplyProject(loaded);ProjectPath=snapshot.ProjectPath;HasJob=true;IsDirty=true;JobTitle=loaded.Document.Job.Name+" (Recovered)";WorkspaceMode=CncCad.Infrastructure.Settings.WorkspaceMode.Design;Message=$"Recovered {snapshot.DisplayName} — save to keep it";
    }
    public async Task DiscardRecoveryAsync(RecoverySnapshotInfo snapshot){if(_recoveryStore is null)return;await _recoveryStore.DeleteJobAsync(snapshot.JobKey);await RefreshRecoveriesAsync();}
    public async Task DiscardCurrentRecoveryAsync(){if(_recoveryStore is null)return;await _recoveryStore.DeleteJobAsync(_recoveryJobKey);await RefreshRecoveriesAsync();}
    partial void OnIsDirtyChanged(bool value){if(value){_dirtySinceUtc=DateTimeOffset.UtcNow;if(_lastRecoveryUtc>_dirtySinceUtc)_lastRecoveryUtc=DateTimeOffset.MinValue;}}
    public void SetWorkspaceFocus(WorkspaceFocus focus) { WorkspaceFocus = focus; RefreshWorkflowStage(); }
    public void SetWorkspaceView(WorkspaceView view) { ActiveWorkspaceView = view; IsToolpathPreview=view==WorkspaceView.ToolpathPreview; ShowPlaybackControls=IsToolpathPreview&&HasPlayback; RefreshWorkflowStage(); }
    public void SetWorkspaceMode(WorkspaceMode mode){WorkspaceMode=mode;Shell.Mode=mode;WorkspaceFocus=mode==CncCad.Infrastructure.Settings.WorkspaceMode.Toolpaths?WorkspaceFocus.Toolpaths:WorkspaceFocus.Design;RefreshWorkflowStage();}
    public void SetViewportLayout(ViewportLayout layout){ViewportLayout=layout;Shell.Layout=layout;IsToolpathPreview=layout!=CncCad.Infrastructure.Settings.ViewportLayout.Design2D;ActiveWorkspaceView=IsToolpathPreview?WorkspaceView.ToolpathPreview:WorkspaceView.Design;ShowPlaybackControls=IsToolpathPreview&&HasPlayback;RefreshWorkflowStage();}
    public void SetResponsiveLayout(double width){ResponsiveLayout=WorkspaceLayoutMetrics.ForWidth(width);Shell.ResponsiveLayout=ResponsiveLayout;}
    public void OpenDesignTask(DesignTaskPage page)=>DesignTaskPage=page;
    public void CloseDesignTask()=>DesignTaskPage=DesignTaskPage.Palette;
    public void OpenToolpathTask(ToolpathTaskPage page)=>ToolpathTaskPage=page;
    public void CloseToolpathTask(){if(IsOperationEditorOpen)CancelOperationEditor();else ToolpathTaskPage=ToolpathTaskPage.Palette;}
    partial void OnResponsiveLayoutChanged(ResponsiveLayoutState value){OnPropertyChanged(nameof(IsRegularLayout));OnPropertyChanged(nameof(IsCompactLayout));OnPropertyChanged(nameof(ShowRegularPreviewAppearance));}
    partial void OnIsToolpathPreviewChanged(bool value)=>OnPropertyChanged(nameof(ShowRegularPreviewAppearance));
    partial void OnWorkspaceModeChanged(WorkspaceMode value){OnPropertyChanged(nameof(IsDesignMode));OnPropertyChanged(nameof(IsToolpathsMode));}
    partial void OnDesignTaskPageChanged(DesignTaskPage value){OnPropertyChanged(nameof(IsDesignPalette));OnPropertyChanged(nameof(IsDesignConstructionTask));OnPropertyChanged(nameof(IsDesignPositionTask));OnPropertyChanged(nameof(IsDesignTransformTask));OnPropertyChanged(nameof(IsDesignEditTask));OnPropertyChanged(nameof(IsDesignOffsetTask));OnPropertyChanged(nameof(IsDesignTextTask));OnPropertyChanged(nameof(IsDesignAppearanceTask));OnPropertyChanged(nameof(IsDesignRepairTask));OnPropertyChanged(nameof(IsDesignSnappingTask));OnPropertyChanged(nameof(IsDesignLayersTask));OnPropertyChanged(nameof(IsDesignNestingTask));OnPropertyChanged(nameof(IsDesignJobTask));OnPropertyChanged(nameof(DesignTaskTitle));}
    partial void OnToolpathTaskPageChanged(ToolpathTaskPage value){OnPropertyChanged(nameof(IsToolpathPalette));OnPropertyChanged(nameof(IsToolLibraryTask));OnPropertyChanged(nameof(IsMachineFixturesTask));OnPropertyChanged(nameof(ToolpathTaskTitle));}
    partial void OnSelectedLayerChanged(CadLayer? value){LayerNameDraft=value?.Name??"";LayerColorDraft=value?.Color??"#61C8FF";}
    partial void OnCornerIndexChanged(int value){OnPropertyChanged(nameof(CanAddCornerRelief));OnPropertyChanged(nameof(CornerReliefGuidance));}
    partial void OnCornerReliefToolDiameterChanged(double value){OnPropertyChanged(nameof(CanAddCornerRelief));OnPropertyChanged(nameof(CornerReliefGuidance));}
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
            CanvasTool.DrillPoint=>"Click to place an explicit drilling point.",
            CanvasTool.Trim => "Scissors: hover the portion to remove, then click. Cutting vectors remain unchanged.",
            _ => "Click the start, a point on the arc, then the end. Escape cancels."
        };
    }
    private void RefreshWorkspaceState()
    {
        HasGeometry = Document.Entities.Count > 0; HasOperations = CamJob.Operations.Count > 0;
        OnPropertyChanged(nameof(CanSaveToolpaths));OnPropertyChanged(nameof(ExportAvailabilityMessage));OnPropertyChanged(nameof(CanOffsetSelection));OnPropertyChanged(nameof(CanCreateVCarve));OnPropertyChanged(nameof(VCarveGuidance));
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
    private static bool IsVCarveContour(CadEntity entity)=>entity switch{RectangleEntity=>true,CircleEntity{Radius:>0}=>true,PolylineEntity{IsClosed:true,Points.Count:>=3}=>true,VectorPathEntity path when path.Figures.Count>0&&path.Figures.All(figure=>figure.IsClosed&&figure.Segments.Count>0)=>true,TextEntity{FontKind:not FontKind.SingleLine} text when text.Outlines.Count>0&&text.Outlines.All(figure=>figure.IsClosed&&figure.Segments.Count>0)=>true,_=>false};
    private static bool IsOffsetEligible(CadEntity entity)=>entity switch{LineEntity=>true,ArcEntity=>true,RectangleEntity=>true,CircleEntity{Radius:>0}=>true,PolylineEntity{IsClosed:false,Points.Count:>=2}=>true,PolylineEntity{IsClosed:true,Points.Count:>=3}=>true,VectorPathEntity{Figures.Count:1} path when path.Figures[0].Segments.Count>0=>true,VectorPathEntity path when path.Figures.Count>0&&path.Figures.All(figure=>!figure.IsClosed&&figure.Segments.Count>0)=>true,_=>false};
    private static CamOperationDefinition CopyOperation(CamOperationDefinition value)
    {
        CamOperationDefinition copy=value switch{ProfileOperationDefinition operation=>operation.Duplicate(),PocketOperationDefinition operation=>operation.Duplicate(),DrillOperationDefinition operation=>operation.Duplicate(),EngraveOperationDefinition operation=>operation.Duplicate(),VCarveOperationDefinition operation=>operation.Duplicate(),SurfaceOperationDefinition operation=>operation.Duplicate(),_=>throw new NotSupportedException(value.GetType().Name)};copy.Name=value.Name;copy.Order=value.Order;return copy;
    }
    private void ApplyProject(CncProject loaded)
    {
        var document=loaded.Document;ClearPlayback();Document.Version=document.Version;Document.Job=document.Job;Document.Layers=document.Layers;Document.ActiveLayerId=document.ActiveLayerId;Document.Entities=document.Entities;CamJob=loaded.CamJob;PreviewSettings=loaded.PreviewSettings;ToolpathWorkspace=new(CamJob,Operations,Tools);OnPropertyChanged(nameof(ToolpathWorkspace));NestingJob=loaded.Nesting;CaptureInstalledNestingLayout();RefreshNesting();Fixtures.Clear();foreach(var fixture in CamJob.Fixtures)Fixtures.Add(fixture);Selection.Clear();_history.Clear();UpdateHistoryState();SelectionChanged();InvalidatePreflight();RefreshLayers();Program.Name=document.Job.Name;RecalculateAll();RefreshWorkspaceState();OnPropertyChanged(nameof(MaterialSummary));OnPropertyChanged(nameof(OriginSummary));
    }
}
