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

    [Fact] public async Task ModernDoor_ReopensAndExportsSelectedCombinedAndSeparateUccncPrograms()
    {
        var path=Path.Combine(Path.GetTempPath(),$"modern-door-{Guid.NewGuid():N}.cnccad");
        try
        {
            await using(var stream=File.Create(path))await new JsonProjectSerializer().SaveAsync(ModernDoorAcceptanceProject.Create(),stream);
            var vm=ViewModel();await vm.OpenProjectAsync(path);vm.SelectedOperation=vm.Operations[0];
            var selected=vm.GenerateExportPrograms(ToolpathExportScope.SelectedToolpath);var combined=vm.GenerateExportPrograms(ToolpathExportScope.VisibleCombined);var separate=vm.GenerateExportPrograms(ToolpathExportScope.VisibleSeparateFiles);
            Assert.Single(selected);Assert.Single(combined);Assert.Equal(5,separate.Count);Assert.All([..selected,..combined,..separate],file=>{Assert.Equal(".nc",file.Extension);Assert.Contains("M30",file.Text);});
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
