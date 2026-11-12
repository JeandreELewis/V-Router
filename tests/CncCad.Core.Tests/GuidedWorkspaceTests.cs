using CncCad.App.ViewModels;
using CncCad.Cam.Operations;
using CncCad.Cam.Tools;
using CncCad.Cam.Safety;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using CncCad.IO.Projects;

namespace CncCad.Core.Tests;

public class GuidedWorkspaceTests
{
    [Fact] public void GuidedMode_IsTheDefaultAndAdvancedModeIsExplicit()
    {
        var vm=ViewModel();Assert.False(vm.IsAdvancedMode);vm.IsAdvancedMode=true;Assert.True(vm.IsAdvancedMode);
    }

    [Fact] public void Workflow_AdvancesFromSetupThroughPreview()
    {
        var vm = ViewModel(); Assert.StartsWith("1", vm.WorkflowStage);
        vm.CreateJob(new JobSettings { Name = "Flow", WidthMm = 600, HeightMm = 400, ThicknessMm = 18 }); Assert.StartsWith("2", vm.WorkflowStage);
        var rectangle = new RectangleEntity { LayerId = vm.Document.ActiveLayerId, Origin = new(20,20), Width = 200, Height = 100 }; vm.AddEntity(rectangle); Assert.StartsWith("2", vm.WorkflowStage);
        vm.SetWorkspaceFocus(WorkspaceFocus.Toolpaths);Assert.StartsWith("3",vm.WorkflowStage);vm.AddOperation(vm.CreateProfileDraft());vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);Assert.StartsWith("4", vm.WorkflowStage); Assert.True(vm.HasOperations);
    }

    [Fact] public void ProfileReadiness_ExplainsSelectionCompatibility()
    {
        var vm = ViewModel(); vm.CreateJob(new JobSettings()); Assert.False(vm.CanCreateProfile); Assert.Contains("Select", vm.ProfileGuidance);
        var line = new LineEntity { LayerId = vm.Document.ActiveLayerId, Start = new(0,0), End = new(10,10) }; vm.AddEntity(line); Assert.False(vm.CanCreateProfile); Assert.Contains("unsupported", vm.ProfileGuidance);
        var rectangle = new RectangleEntity { LayerId = vm.Document.ActiveLayerId, Width = 100, Height = 50 }; vm.AddEntity(rectangle); Assert.True(vm.CanCreateProfile); Assert.Contains("ready", vm.ProfileGuidance);
        vm.Selection.Add(line.Id); vm.SelectionChanged(); Assert.False(vm.CanCreateProfile);
    }

    [Fact] public void CenterSelection_IsUndoable()
    {
        var vm = ViewModel(); vm.CreateJob(new JobSettings { WidthMm = 600, HeightMm = 400 }); var rectangle = new RectangleEntity { LayerId = vm.Document.ActiveLayerId, Origin = new(0,0), Width = 100, Height = 40 }; vm.AddEntity(rectangle);
        vm.CenterSelectionOnMaterial(); Assert.Equal(new Point2D(250,180), rectangle.Origin);
        vm.Undo(); Assert.Equal(new Point2D(0,0), rectangle.Origin);
    }

    [Fact] public void JoinChainsBothEndsInAnySelectionOrderAndIsUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var group=Guid.NewGuid();var middle=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(10,0),End=new(20,0),GroupId=group,StrokeColor="#FF0000"};var tail=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(20,0),End=new(30,0)};var head=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(0,0),End=new(10,0)};vm.AddEntity(middle);vm.AddEntity(tail);vm.AddEntity(head);vm.Selection.Set([middle.Id,tail.Id,head.Id]);vm.SelectionChanged();vm.JoinSelection();
        var joined=Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(middle.Id,joined.Id);Assert.Equal([new Point2D(0,0),new Point2D(10,0),new Point2D(20,0),new Point2D(30,0)],joined.Points);Assert.Equal(group,joined.GroupId);Assert.Equal("#FF0000",joined.StrokeColor);vm.Undo();Assert.Equal(3,vm.Document.Entities.Count);
    }

    [Fact] public void ChamferAndExactFilletAreUndoableAndInvalidateDependentToolpaths()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=12});var contour=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,10),new(70,10),new(70,60),new(10,60)],IsClosed=true};vm.AddEntity(contour);var operation=vm.CreateProfileDraft();vm.AddOperation(operation);Assert.Equal(OperationStatus.Current,operation.Status);Assert.True(vm.CanUseChamferFillet);
        vm.CornerIndex=1;vm.ChamferDistance=4;vm.ChamferSelection();var chamfered=Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));Assert.Equal(5,chamfered.Points.Count);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));
        vm.Selection.Set(contour.Id);vm.SelectionChanged();vm.FilletRadius=4;vm.FilletSelection();var filleted=Assert.IsType<VectorPathEntity>(Assert.Single(vm.Document.Entities));Assert.Contains(filleted.Figures[0].Segments,segment=>segment is ArcPathSegment);Assert.Equal(OperationStatus.Stale,operation.Status);vm.Undo();Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities));
    }

    [Fact] public void SignedOpenOffsetIsAvailableAndUndoableForAnOpenVector()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100});var source=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,10),new(50,10),new(50,40)]};vm.AddEntity(source);Assert.True(vm.CanOffsetSelection);vm.OffsetDistance=3;vm.OffsetJoin=CncCad.Cam.Profile.CadOffsetJoin.Miter;vm.OffsetSelection();var offset=Assert.IsType<PolylineEntity>(vm.Document.Entities.Single(entity=>entity.Id!=source.Id));Assert.Equal([new Point2D(10,13),new Point2D(47,13),new Point2D(47,40)],offset.Points);vm.Undo();Assert.Single(vm.Document.Entities);Assert.Same(source,vm.Document.Entities[0]);
    }

    [Fact] public void PocketHelixIsExposedOnlyThroughTheContainmentCheckedCalculator()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=100,ThicknessMm=12});var boundary=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,10),Width=80,Height=60};vm.AddEntity(boundary);Assert.Contains(PocketEntryMode.Helix,vm.PocketEntryModes);var pocket=vm.CreatePocketDraft();pocket.EntryMode=PocketEntryMode.Helix;pocket.RampLengthMm=10;pocket.CutDepthMm=2;pocket.PassDepthMm=2;vm.AddOperation(pocket);Assert.Equal(OperationStatus.Current,pocket.Status);Assert.NotEmpty(pocket.CalculatedToolpath!.Moves.OfType<CncCad.Cam.Toolpaths.ArcMove>());
    }

    [Fact] public void VCarveUnlockRequiresClosedOutlinesAndAValidVBit()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=100,ThicknessMm=12});var outline=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,10),Width=80,Height=60};vm.AddEntity(outline);Assert.False(vm.CanCreateVCarve);vm.SelectedTool=new(){Name="60 degree V-bit",Type=ToolType.VBit,DiameterMm=8,VBitAngleDegrees=60,TipDiameterMm=.2,FeedRateMmPerMin=800,PlungeRateMmPerMin=200,SpindleRpm=16000,StepDownMm=2};Assert.True(vm.CanCreateVCarve);var carve=vm.CreateVCarveDraft();vm.AddOperation(carve);Assert.Equal(OperationStatus.Current,carve.Status);Assert.NotEmpty(carve.CalculatedToolpath!.Moves);var open=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(0,0),End=new(10,0)};vm.AddEntity(open);Assert.False(vm.CanCreateVCarve);Assert.Contains("closed",vm.VCarveGuidance,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void PolylineNodeSelectionInsertionDeletionAndStartChangesAreTruthfulAndUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=12});var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Transform=Matrix2D.Translation(5,7),Points=[new(0,0),new(20,0),new(20,10),new(0,10)],IsClosed=true};vm.AddEntity(path);vm.SelectNode(path,1,path.Transform.Transform(path.Points[1]));Assert.True(vm.HasActiveNode);Assert.True(vm.CanInsertNode);Assert.True(vm.CanDeleteNode);Assert.True(vm.CanMakeStartNode);vm.InsertNode();Assert.Equal(5,path.Points.Count);Assert.Equal(new Point2D(20,5),path.Points[2]);vm.Undo();Assert.Equal(4,path.Points.Count);
        vm.Selection.Set(path.Id);vm.SelectionChanged();vm.SelectNode(path,2,path.Transform.Transform(path.Points[2]));vm.MakeStartNode();Assert.Equal(new Point2D(20,10),path.Points[0]);vm.Undo();Assert.Equal(new Point2D(0,0),path.Points[0]);vm.Selection.Set(path.Id);vm.SelectionChanged();vm.SelectNode(path,1,path.Transform.Transform(path.Points[1]));vm.DeleteNode();Assert.Equal(3,path.Points.Count);Assert.False(vm.HasActiveNode);Assert.False(vm.CanDeleteNode);vm.Undo();Assert.Equal(4,path.Points.Count);
    }

    [Fact] public void BezierHandlePositionIsEditableWhileUnsupportedTopologyActionsStayDisabled()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var path=new VectorPathEntity{LayerId=vm.Document.ActiveLayerId,Figures=[new(){Start=new(0,0),Segments=[new CubicPathSegment(new(5,10),new(15,10),new(20,0))]}]};vm.AddEntity(path);vm.SelectNode(path,1,new(5,10));Assert.True(vm.HasActiveNode);Assert.False(vm.CanInsertNode);Assert.False(vm.CanDeleteNode);Assert.False(vm.CanMakeStartNode);vm.NodeX=6;vm.NodeY=12;vm.ApplyNodePosition();Assert.Equal(new Point2D(6,12),Assert.IsType<CubicPathSegment>(path.Figures[0].Segments[0]).Control1);vm.Undo();Assert.Equal(new Point2D(5,10),Assert.IsType<CubicPathSegment>(path.Figures[0].Segments[0]).Control1);
    }

    [Fact] public void RepairApplyIsEnabledOnlyForCurrentUnambiguousPreviewAndIsUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(0,0),new(.001,0),new(10,0)]};vm.AddEntity(path);Assert.False(vm.CanApplyRepair);vm.MinimumSegment=.01;vm.AnalyzeRepair();Assert.True(vm.CanApplyRepair);vm.ApplyRepair();Assert.False(vm.CanApplyRepair);Assert.Equal(2,Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count);vm.Undo();Assert.Equal(3,Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count);
        vm.Selection.Set(path.Id);vm.SelectionChanged();vm.AnalyzeRepair();Assert.True(vm.CanApplyRepair);vm.NudgeSelection(1,0);Assert.False(vm.CanApplyRepair);Assert.Empty(vm.RepairIssues);
    }

    [Fact] public void LayerCreationFlagsOrderingAndEntityMovesAreUndoable()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings());var original=vm.Document.ActiveLayerId;var entity=new LineEntity{LayerId=original,End=new(10,0)};vm.AddEntity(entity);vm.AddLayer();var added=vm.Document.ActiveLayerId;Assert.NotEqual(original,added);vm.MoveSelectionToLayer();Assert.Equal(added,entity.LayerId);vm.Undo();Assert.Equal(original,entity.LayerId);vm.ToggleLayerVisibility();Assert.False(vm.SelectedLayer!.IsVisible);vm.Undo();Assert.True(vm.Document.Layers.Single(layer=>layer.Id==added).IsVisible);vm.Undo();Assert.DoesNotContain(vm.Document.Layers,layer=>layer.Id==added);vm.Redo();Assert.Contains(vm.Document.Layers,layer=>layer.Id==added);vm.SelectedLayer=vm.Document.Layers.Single(layer=>layer.Id==added);vm.LayerNameDraft="Cut vectors";vm.LayerColorDraft="#123456";vm.CommitLayerProperties();Assert.Equal("Cut vectors",vm.SelectedLayer.Name);vm.Undo();Assert.NotEqual("Cut vectors",vm.Document.Layers.Single(layer=>layer.Id==added).Name);
    }

    [Fact] public void ReacceptingANestReplacesTheInstalledLayoutInsteadOfDuplicatingIt()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=120,HeightMm=120,ThicknessMm=18});
        var source=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(5,5),Width=40,Height=30};vm.AddEntity(source);vm.NestQuantity=2;vm.AddNestPartsFromSelection();
        var first=vm.OptimizeNest();Assert.True(first.Success);Assert.True(vm.CanAcceptNest);vm.AcceptNest();Assert.False(vm.CanAcceptNest);
        var firstIds=vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).Select(entity=>entity.Id).ToHashSet();Assert.Equal(2,firstIds.Count);
        var second=vm.OptimizeNest();Assert.True(second.Success);vm.AcceptNest();
        var installed=vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).ToArray();var secondIds=installed.Select(entity=>entity.Id).ToHashSet();Assert.Equal(2,installed.Length);Assert.DoesNotContain(installed,entity=>firstIds.Contains(entity.Id));Assert.Single(vm.Document.Layers,layer=>layer.Name.StartsWith("Sheet ",StringComparison.Ordinal));
        vm.Undo();Assert.Equal(firstIds,vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).Select(entity=>entity.Id).ToHashSet());Assert.Equal(CncCad.Cam.Nesting.NestingLayoutStatus.Current,vm.NestingJob.Status);
        vm.Redo();Assert.Equal(secondIds,vm.Document.Entities.Where(entity=>entity.MaterialSheetId is not null).Select(entity=>entity.Id).ToHashSet());Assert.Equal(CncCad.Cam.Nesting.NestingLayoutStatus.Current,vm.NestingJob.Status);
    }

    [Fact] public void FocusAndViewChoices_AreExplicitState()
    {
        var vm = ViewModel(); Assert.Equal(WorkspaceFocus.Design, vm.WorkspaceFocus); vm.SetWorkspaceFocus(WorkspaceFocus.Toolpaths); vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);
        Assert.Equal(WorkspaceFocus.Toolpaths, vm.WorkspaceFocus); Assert.Equal(WorkspaceView.ToolpathPreview, vm.ActiveWorkspaceView);
    }
    [Fact] public void ModeAndFourViewportLayouts_AreExplicitState()
    {
        var vm=ViewModel();Assert.Equal(WorkspaceMode.Startup,vm.WorkspaceMode);vm.CreateJob(new JobSettings());Assert.Equal(WorkspaceMode.Design,vm.WorkspaceMode);vm.SetWorkspaceMode(WorkspaceMode.Toolpaths);Assert.Equal(WorkspaceFocus.Toolpaths,vm.WorkspaceFocus);foreach(var layout in Enum.GetValues<ViewportLayout>()){vm.SetViewportLayout(layout);Assert.Equal(layout,vm.ViewportLayout);Assert.Equal(layout!=ViewportLayout.Design2D,vm.IsToolpathPreview);}
    }
    [Theory]
    [InlineData(1100, ResponsiveLayoutState.Compact)]
    [InlineData(1280, ResponsiveLayoutState.Compact)]
    [InlineData(1359, ResponsiveLayoutState.Compact)]
    [InlineData(1360, ResponsiveLayoutState.Regular)]
    [InlineData(1920, ResponsiveLayoutState.Regular)]
    public void ResponsiveLayout_UsesTheDocumentedBreakpoint(double width,ResponsiveLayoutState expected)
    {
        var vm=ViewModel();vm.SetResponsiveLayout(width);Assert.Equal(expected,vm.ResponsiveLayout);Assert.Equal(expected,vm.Shell.ResponsiveLayout);
    }
    [Theory]
    [InlineData(1100, WorkspaceMode.Design, 884)]
    [InlineData(1100, WorkspaceMode.Toolpaths, 796)]
    [InlineData(1280, WorkspaceMode.Toolpaths, 976)]
    [InlineData(1440, WorkspaceMode.Design, 1224)]
    [InlineData(1920, WorkspaceMode.Design, 1704)]
    public void ShellWidths_KeepTheCanvasAboveItsMinimum(double width,WorkspaceMode mode,double expected)
    {
        var actual=WorkspaceLayoutMetrics.CanvasWidth(width,mode);Assert.Equal(expected,actual);Assert.True(actual>=WorkspaceLayoutMetrics.MinimumCanvasWidth);
    }
    [Fact] public void FocusedTaskPages_ReturnToTheirCompactPalettes()
    {
        var vm=ViewModel();vm.OpenDesignTask(DesignTaskPage.Layers);Assert.True(vm.IsDesignLayersTask);Assert.False(vm.IsDesignPalette);vm.CloseDesignTask();Assert.True(vm.IsDesignPalette);
        vm.OpenToolpathTask(ToolpathTaskPage.ToolLibrary);Assert.True(vm.IsToolLibraryTask);Assert.False(vm.IsToolpathPalette);vm.CloseToolpathTask();Assert.True(vm.IsToolpathPalette);
    }
    [Fact] public void InlineToolpathEditor_AppliesAndCancelsWithoutMutatingTarget()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=200,HeightMm=100,ThicknessMm=12});var rectangle=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=80,Height=40};vm.AddEntity(rectangle);var draft=vm.CreateProfileDraft();vm.BeginOperationEditor(draft,true);Assert.True(vm.IsOperationEditorOpen);vm.ApplyOperationEditor();var saved=Assert.IsType<ProfileOperationDefinition>(Assert.Single(vm.Operations));vm.BeginOperationEditor(saved,false);Assert.NotSame(saved,vm.OperationEditorDraft);((ProfileOperationDefinition)vm.OperationEditorDraft!).CutDepthMm=5;vm.CancelOperationEditor();Assert.Equal(12,saved.CutDepthMm);
    }
    [Fact] public void Switching_To_Design_Hides_Playback_Ui_But_Preserves_The_Session()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18});var shape=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=50,Height=50};vm.AddEntity(shape);vm.AddOperation(vm.CreateProfileDraft());vm.LoadAllPlayback();vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);
        var segmentCount=vm.Playback.Frame.Segments.Count;Assert.True(vm.ShowPlaybackControls);vm.SetWorkspaceView(WorkspaceView.Design);Assert.False(vm.ShowPlaybackControls);Assert.Equal(segmentCount,vm.Playback.Frame.Segments.Count);Assert.StartsWith("2",vm.WorkflowStage);vm.SetWorkspaceView(WorkspaceView.ToolpathPreview);Assert.True(vm.ShowPlaybackControls);Assert.Equal(segmentCount,vm.Playback.Frame.Segments.Count);
    }
    [Fact] public void PlaybackSelectedAndAll_UseTheRequestedOperationsInVisibleOrder()
    {
        var vm = ViewModel(); vm.CreateJob(new JobSettings { WidthMm = 600, HeightMm = 400, ThicknessMm = 18 });
        var firstShape = new RectangleEntity { LayerId=vm.Document.ActiveLayerId, Width=100, Height=100 }; vm.AddEntity(firstShape); var first=vm.CreateProfileDraft(); first.Name="First"; vm.AddOperation(first);
        var secondShape = new CircleEntity { LayerId=vm.Document.ActiveLayerId, Center=new(200,100), Radius=30 }; vm.AddEntity(secondShape); var second=vm.CreateProfileDraft(); second.Name="Second"; vm.AddOperation(second);
        vm.SelectedOperation=second; vm.LoadSelectedPlayback(); Assert.All(vm.Playback.Frame.Segments, segment => Assert.Equal(second.Id, segment.OperationId));
        first.IsEnabled=false; vm.LoadAllPlayback(); Assert.All(vm.Playback.Frame.Segments, segment => Assert.Equal(second.Id, segment.OperationId)); Assert.StartsWith("All enabled", vm.PlaybackSessionLabel);
    }
    [Fact] public async Task Saved_Job_Reopens_With_Its_Toolpaths_Ready()
    {
        var path=Path.Combine(Path.GetTempPath(),$"cnccad-{Guid.NewGuid():N}.cnccad");
        try
        {
            var source=ViewModel();source.CreateJob(new JobSettings{Name="Persistent CAM",WidthMm=600,HeightMm=400,ThicknessMm=18});var rectangle=new RectangleEntity{LayerId=source.Document.ActiveLayerId,Width=100,Height=60};source.AddEntity(rectangle);var operation=source.CreateProfileDraft();operation.Name="Saved outline";source.AddOperation(operation);await source.SaveProjectAsync(path);
            var reopened=ViewModel();await reopened.OpenProjectAsync(path);var saved=Assert.Single(reopened.Operations);Assert.Equal("Saved outline",saved.Name);Assert.Equal(rectangle.Id,Assert.Single(saved.GeometryIds));Assert.Equal(OperationStatus.Current,saved.Status);Assert.NotNull(saved.CalculatedToolpath);Assert.True(reopened.HasOperations);
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
    [Fact] public async Task Save_Normalizes_Project_Extension_And_Remains_Successful_When_Recent_List_Fails()
    {
        var basePath=Path.Combine(Path.GetTempPath(),$"cnccad-{Guid.NewGuid():N}.wrong");var expected=Path.ChangeExtension(basePath,".cnccad");
        try
        {
            var vm=new MainWindowViewModel(new MemoryTools(),new FailingRecent());vm.CreateJob(new JobSettings{Name="Reliable save"});await vm.SaveProjectAsync(basePath);
            Assert.Equal(Path.GetFullPath(expected),vm.ProjectPath);Assert.True(File.Exists(expected));Assert.False(vm.IsDirty);Assert.Contains("recent projects",vm.Message,StringComparison.OrdinalIgnoreCase);
        }
        finally{if(File.Exists(expected))File.Delete(expected);}
    }
    [Theory]
    [InlineData("sample.cnccad", "sample.cnccad")]
    [InlineData("sample.cnccad.cnccad", "sample.cnccad")]
    [InlineData("sample.CNCCAD.CNCCAD", "sample.cnccad")]
    [InlineData("sample.wrong", "sample.cnccad")]
    public void Project_Path_Normalization_Produces_Exactly_One_Extension(string input,string expected)
    {
        var directory=Path.Combine(Path.GetTempPath(),"cnccad-extension-tests");
        var actual=MainWindowViewModel.NormalizeProjectPath(Path.Combine(directory,input));
        Assert.Equal(Path.Combine(directory,expected),actual);
    }

    [Theory]
    [InlineData("program.nc.nc", ".nc", "program.nc")]
    [InlineData("program.txt.txt", ".txt", "program.txt")]
    public void Export_Path_Normalization_Produces_Exactly_One_Extension(string input,string extension,string expected)
    {
        var directory=Path.Combine(Path.GetTempPath(),"cnccad-extension-tests");
        var actual=GCodeExportFormat.NormalizePath(Path.Combine(directory,input),extension);
        Assert.Equal(Path.Combine(directory,expected),actual);
    }
    [Fact] public async Task Failed_Open_Does_Not_Clear_The_Current_Playback()
    {
        var vm=ViewModel();vm.CreateJob(new JobSettings{WidthMm=100,HeightMm=100,ThicknessMm=18});var shape=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Width=50,Height=50};vm.AddEntity(shape);vm.AddOperation(vm.CreateProfileDraft());vm.LoadAllPlayback();var segmentCount=vm.Playback.Frame.Segments.Count;
        var missing=Path.Combine(Path.GetTempPath(),$"missing-{Guid.NewGuid():N}.cnccad");await Assert.ThrowsAsync<FileNotFoundException>(()=>vm.OpenProjectAsync(missing));Assert.Equal(segmentCount,vm.Playback.Frame.Segments.Count);
    }

    [Fact] public async Task ModernDoor_ReopensAndBlocksCombinedMultiToolOutputForManualRouter()
    {
        var path=Path.Combine(Path.GetTempPath(),$"modern-door-{Guid.NewGuid():N}.cnccad");
        try
        {
            await using(var stream=File.Create(path))await new JsonProjectSerializer().SaveAsync(ModernDoorAcceptanceProject.Create(),stream);
            var vm=ViewModel();await vm.OpenProjectAsync(path);vm.SelectedOperation=vm.Operations[0];
            var selected=vm.GenerateExportPrograms(ToolpathExportScope.SelectedToolpath);var blocked=Assert.Throws<InvalidOperationException>(()=>vm.GenerateExportPrograms(ToolpathExportScope.VisibleCombined));var separate=vm.GenerateExportPrograms(ToolpathExportScope.VisibleSeparateFiles);
            Assert.Contains("separate files",blocked.Message,StringComparison.OrdinalIgnoreCase);Assert.Single(selected);Assert.Equal(5,separate.Count);Assert.All([..selected,..separate],file=>{Assert.Equal(".tap",file.Extension);Assert.Contains("M30",file.Text);});
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }

    private static MainWindowViewModel ViewModel()
    {
        var vm = new MainWindowViewModel(new MemoryTools(), new MemoryRecent()); vm.SelectedTool = new CncTool { Name = "6mm End Mill", DiameterMm = 6, StepDownMm = 3, CuttingLengthMm=30, OverallLengthMm=50, ShankDiameterMm=6, StickOutMm=35 };vm.Machines.Add(new MachineProfileDefinition());vm.SelectedMachine=vm.Machines[0]; return vm;
    }
    private sealed class MemoryTools : IToolLibrary
    {
        public Task<IReadOnlyList<CncTool>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CncTool>>([]);
        public Task SaveAsync(CncTool tool, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class MemoryRecent : IRecentProjectStore
    {
        public Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecentProject>>([]);
        public Task AddAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FailingRecent : IRecentProjectStore
    {
        public Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken = default)=>throw new IOException("Settings unavailable");
        public Task AddAsync(string path,CancellationToken cancellationToken=default)=>throw new IOException("Settings unavailable");
        public Task RemoveAsync(string path,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
}
