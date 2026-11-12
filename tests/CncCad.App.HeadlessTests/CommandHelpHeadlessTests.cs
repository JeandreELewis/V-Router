using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CncCad.App.ViewModels;
using CncCad.App.Views;
using CncCad.Cam.Tools;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Preview;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using CncCad.Rendering.Canvas;

namespace CncCad.App.HeadlessTests;

public sealed class CommandHelpHeadlessTests
{
    [AvaloniaFact]
    public void StartupAndWindowChromeUseTheVRouterBrandAssets()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.NotNull(window.Icon);var shell=window.FindControl<Image>("ShellLogo");var startup=window.FindControl<Image>("StartupLogo");Assert.NotNull(shell);Assert.NotNull(startup);Assert.True(shell.IsVisible);Assert.True(startup.IsVisible);Assert.NotNull(shell.Source);Assert.NotNull(startup.Source);Assert.True(shell.Bounds.Width>0&&shell.Bounds.Height>0);Assert.True(startup.Bounds.Width>0&&startup.Bounds.Height>0);Assert.True(shell.Source.Size.Width>0&&startup.Source.Size.Height>0);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void DisabledCommandStillProvidesItsRequirementInTheTooltip()
    {
        var button=new Button{IsEnabled=false};CommandHelp.SetKey(button,"edit.copy");
        var tooltip=Assert.IsType<string>(ToolTip.GetTip(button));Assert.Contains("Select one or more vectors",tooltip);Assert.True(button.GetValue(ToolTip.ShowOnDisabledProperty));
    }

    [AvaloniaFact]
    public void MinimumWindowKeepsCanvasAtLeast760DipsAndShowsHelpEntryPoint()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Layout",WidthMm=1000,HeightMm=2300,ThicknessMm=50});
        var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<Grid>("CenterWorkspaceGrid");var help=window.FindControl<ToggleButton>("HelpModeToggle");
            Assert.NotNull(canvas);Assert.True(canvas.Bounds.Width>=760,$"Canvas width was {canvas.Bounds.Width:0.###}");Assert.NotNull(help);Assert.NotNull(ToolTip.GetTip(help));
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void ThreeDimensionalViewport_ExposesAndExecutesAppearanceSwitch()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Wood preview",WidthMm=1000,HeightMm=2300,ThicknessMm=50});
        var window=new MainWindow{DataContext=vm,Width=1440,Height=900};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var preview=window.FindControl<ToggleButton>("PreviewViewButton");Assert.NotNull(preview);preview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            var appearance=window.GetVisualDescendants().OfType<Button>().Single(button=>CommandHelp.GetKey(button)=="view.appearance");
            Assert.True(appearance.IsVisible);Assert.Contains("procedural material",CommandHelp.DescriptorFor(appearance)!.Summary,StringComparison.OrdinalIgnoreCase);Assert.Contains("Hardwood",Assert.IsType<string>(ToolTip.GetTip(appearance)));
            Assert.Equal(PreviewRenderStyle.Material,vm.PreviewSettings.Style);appearance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Assert.Equal(PreviewRenderStyle.Technical,vm.PreviewSettings.Style);Assert.True(vm.IsDirty);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void ScissorsClickWithoutPriorPointerMove_CommitsSegmentAndLeavesCuttersUntouched()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());
        vm.CreateJob(new JobSettings{Name="Trim interaction",WidthMm=100,HeightMm=100,ThicknessMm=18});
        var target=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(10,50),End=new(90,50)};
        var left=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(30,20),End=new(30,80)};
        var right=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(70,20),End=new(70,80)};
        vm.Document.Entities.AddRange([target,left,right]);
        var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<CadCanvas>("Canvas");Assert.NotNull(canvas);canvas.SetTool(CanvasTool.Trim);canvas.ZoomToFit();Dispatcher.UIThread.RunJobs();
            var zoom=Math.Max(.05,Math.Min((canvas.Bounds.Width-80)/100,(canvas.Bounds.Height-80)/100));
            var panX=(canvas.Bounds.Width-100*zoom)/2;var panY=(canvas.Bounds.Height-100*zoom)/2;
            var local=new Point(panX+50*zoom,canvas.Bounds.Height-panY-50*zoom);
            var screen=canvas.TranslatePoint(local,window);Assert.NotNull(screen);
            window.MouseDown(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();
            window.MouseUp(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();

            Assert.Contains(vm.Document.Entities,x=>x.Id==target.Id);
            Assert.Contains(left,vm.Document.Entities);Assert.Contains(right,vm.Document.Entities);
            Assert.Equal(4,vm.Document.Entities.Count);
            Assert.Equal("Vector trimmed · scissors remains active",vm.Message);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void FilletControlExecutesTheCertifiedExactArcEdit()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Corner edit",WidthMm=100,HeightMm=100,ThicknessMm=18});var source=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,10),new(70,10),new(70,60)],IsClosed=false};vm.AddEntity(source);vm.CornerIndex=1;vm.FilletRadius=5;vm.OpenDesignTask(DesignTaskPage.Edit);
        var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var button=window.GetVisualDescendants().OfType<Button>().Single(control=>Equals(control.Content,"Fillet"));Assert.True(button.IsEnabled);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var path=Assert.IsType<VectorPathEntity>(Assert.Single(vm.Document.Entities));Assert.Contains(path.Figures[0].Segments,segment=>segment is ArcPathSegment);Assert.True(vm.CanUndo);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void ClickingANodeSelectsItWithoutCreatingANoOpHistoryEntry()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Node selection",WidthMm=100,HeightMm=100,ThicknessMm=18});var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(20,20),new(70,20),new(70,60)],IsClosed=false};vm.AddEntity(path);vm.OpenDesignTask(DesignTaskPage.Edit);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<CadCanvas>("Canvas");Assert.NotNull(canvas);canvas.NodeEditMode=true;canvas.ZoomToFit();Dispatcher.UIThread.RunJobs();var zoom=Math.Max(.05,Math.Min((canvas.Bounds.Width-80)/100,(canvas.Bounds.Height-80)/100));var panX=(canvas.Bounds.Width-100*zoom)/2;var panY=(canvas.Bounds.Height-100*zoom)/2;var local=new Point(panX+20*zoom,canvas.Bounds.Height-panY-20*zoom);var screen=canvas.TranslatePoint(local,window);Assert.NotNull(screen);window.MouseDown(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();window.MouseUp(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();Assert.True(vm.HasActiveNode);Assert.True(vm.CanInsertNode);vm.Undo();Assert.Empty(vm.Document.Entities);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void VCarvePaletteUnlocksForClosedOutlineAndOpensTheValidatedEditor()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="V carve",WidthMm=100,HeightMm=100,ThicknessMm=18});vm.AddEntity(new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,10),Width=60,Height=40});vm.SelectedTool=new(){Name="60 degree V-bit",Type=ToolType.VBit,DiameterMm=8,VBitAngleDegrees=60,TipDiameterMm=.2,FeedRateMmPerMin=800,PlungeRateMmPerMin=200,SpindleRpm=16000};vm.SetWorkspaceMode(WorkspaceMode.Toolpaths);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var button=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"V"));Assert.True(button.IsEnabled);Assert.Contains("ready",Assert.IsType<string>(ToolTip.GetTip(button)),StringComparison.OrdinalIgnoreCase);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.True(vm.IsOperationEditorOpen);Assert.IsType<CncCad.Cam.Operations.VCarveOperationDefinition>(vm.OperationEditorDraft);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void RepeatedClickCyclesOverlappingVectorsWithoutMutatingGeometry()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Overlap selection",WidthMm=100,HeightMm=100,ThicknessMm=18});var lower=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(10,50),End=new(90,50)};var upper=new LineEntity{LayerId=vm.Document.ActiveLayerId,Start=new(10,50),End=new(90,50)};vm.Document.Entities.AddRange([lower,upper]);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<CadCanvas>("Canvas");Assert.NotNull(canvas);canvas.SetTool(CanvasTool.Select);canvas.ZoomToFit();Dispatcher.UIThread.RunJobs();var zoom=Math.Max(.05,Math.Min((canvas.Bounds.Width-80)/100,(canvas.Bounds.Height-80)/100));var panX=(canvas.Bounds.Width-100*zoom)/2;var panY=(canvas.Bounds.Height-100*zoom)/2;var local=new Point(panX+50*zoom,canvas.Bounds.Height-panY-50*zoom);var screen=canvas.TranslatePoint(local,window);Assert.NotNull(screen);
            Click();Assert.Equal(upper.Id,vm.Selection.PrimaryId);Click();Assert.Equal(lower.Id,vm.Selection.PrimaryId);Click();Assert.Equal(upper.Id,vm.Selection.PrimaryId);Assert.Equal(new Point2D(10,50),lower.Start);Assert.Equal(new Point2D(10,50),upper.Start);Assert.False(vm.CanUndo);
            void Click(){window.MouseDown(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();window.MouseUp(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();}
        }
        finally{window.Close();}
    }

    private sealed class MemoryTools:IToolLibrary
    {
        public Task<IReadOnlyList<CncTool>> GetAllAsync(CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<CncTool>>([]);
        public Task SaveAsync(CncTool tool,CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public Task DeleteAsync(Guid id,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
    private sealed class MemoryRecent:IRecentProjectStore
    {
        public Task<IReadOnlyList<RecentProject>> GetAsync(CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<RecentProject>>([]);
        public Task AddAsync(string path,CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public Task RemoveAsync(string path,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
}
