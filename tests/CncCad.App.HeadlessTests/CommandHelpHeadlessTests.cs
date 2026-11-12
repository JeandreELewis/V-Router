using Avalonia;
using Avalonia.Automation;
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
using CncCad.Cam.Safety;
using CncCad.Cam.Tools;
using CncCad.Core.Documents;
using CncCad.Core.Entities;
using CncCad.Core.Geometry;
using CncCad.Core.Preview;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;
using CncCad.Rendering.Canvas;
using CncCad.Simulation.Playback;

namespace CncCad.App.HeadlessTests;

public sealed class CommandHelpHeadlessTests
{
    public static TheoryData<double,double,ViewportLayout> SupportedViewportLayouts => new()
    {
        {1100,700,ViewportLayout.Design2D},
        {1366,768,ViewportLayout.Preview3D},
        {1600,900,ViewportLayout.HorizontalSplit},
        {1920,1080,ViewportLayout.VerticalSplit}
    };

    [AvaloniaTheory]
    [MemberData(nameof(SupportedViewportLayouts))]
    public void SupportedWindowAndViewportMatrixKeepsActionsNamedAndWorkspaceUsable(double width,double height,ViewportLayout layout)
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Layout matrix",WidthMm=600,HeightMm=400,ThicknessMm=18});var window=new MainWindow{DataContext=vm,Width=width,Height=height};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var layoutButton=window.FindControl<ToggleButton>(layout switch{ViewportLayout.Design2D=>"DesignViewButton",ViewportLayout.Preview3D=>"PreviewViewButton",ViewportLayout.HorizontalSplit=>"HorizontalSplitButton",ViewportLayout.VerticalSplit=>"VerticalSplitButton",_=>throw new ArgumentOutOfRangeException(nameof(layout))});Assert.NotNull(layoutButton);layoutButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();CommandHelp.ApplyAccessibilityTree(window);Assert.Equal(layout,vm.ViewportLayout);
            var workspace=window.FindControl<Grid>("CenterWorkspaceGrid");Assert.NotNull(workspace);Assert.True(workspace.Bounds.Width>=760,$"{width}x{height}/{layout}: workspace width was {workspace.Bounds.Width:0.###}");Assert.True(workspace.Bounds.Height>=500,$"{width}x{height}/{layout}: workspace height was {workspace.Bounds.Height:0.###}");
            var design=window.FindControl<CadCanvas>("Canvas");var preview=window.FindControl<CadCanvas>("PreviewCanvas");Assert.NotNull(design);Assert.NotNull(preview);Assert.Equal(layout!=ViewportLayout.Preview3D,design.IsVisible);Assert.Equal(layout!=ViewportLayout.Design2D,preview.IsVisible);
            var unnamed=window.GetVisualDescendants().OfType<Button>().Where(control=>control.IsVisible&&string.IsNullOrWhiteSpace(AutomationProperties.GetName(control))).ToArray();Assert.Empty(unnamed);
            var named=window.GetVisualDescendants().OfType<Button>().Where(control=>control.IsVisible).ToArray();Assert.NotEmpty(named);Assert.All(named,control=>Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(control))));
        }
        finally{window.Close();}
    }

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
        Assert.Equal("Copy",AutomationProperties.GetName(button));Assert.Equal("Ctrl+C",AutomationProperties.GetAcceleratorKey(button));
        button.IsEnabled=true;Assert.DoesNotContain("Unavailable:",Assert.IsType<string>(ToolTip.GetTip(button)));
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
    public void EditingKeyboardShortcutsCopyPasteDuplicateDeleteUndoAndRedo()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Shortcuts",WidthMm=100,HeightMm=100,ThicknessMm=18});var source=new VectorPathEntity{LayerId=vm.Document.ActiveLayerId,Figures=[new(){Start=new(10,10),Segments=[new CubicPathSegment(new(20,30),new(30,-10),new(40,10))]}]};vm.Document.Entities.Add(source);vm.Selection.Set(source.Id);vm.SelectionChanged();var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            Press(Key.C,KeyModifiers.Control);Assert.True(vm.HasClipboard);Assert.Single(vm.Document.Entities);Press(Key.V,KeyModifiers.Control);Assert.Equal(2,vm.Document.Entities.Count);var pasted=Assert.IsType<VectorPathEntity>(vm.Document.Entities[1]);Assert.IsType<CubicPathSegment>(pasted.Figures[0].Segments[0]);Press(Key.D,KeyModifiers.Control);Assert.Equal(3,vm.Document.Entities.Count);Press(Key.Delete);Assert.Equal(2,vm.Document.Entities.Count);Press(Key.Z,KeyModifiers.Control);Assert.Equal(3,vm.Document.Entities.Count);Press(Key.Z,KeyModifiers.Control|KeyModifiers.Shift);Assert.Equal(2,vm.Document.Entities.Count);
            var locked=vm.Document.Entities[0];locked.IsLocked=true;vm.Selection.Set(locked.Id);vm.SelectionChanged();Press(Key.Delete);Assert.Contains(locked,vm.Document.Entities);Assert.Contains("Locked",vm.Message,StringComparison.OrdinalIgnoreCase);
            void Press(Key key,KeyModifiers modifiers=KeyModifiers.None){window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=key,KeyModifiers=modifiers});Dispatcher.UIThread.RunJobs();}
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void ClickingOneGroupedVectorSelectsTheEntireEditableGroup()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Groups",WidthMm=100,HeightMm=100,ThicknessMm=18});var group=Guid.NewGuid();var first=new LineEntity{LayerId=vm.Document.ActiveLayerId,GroupId=group,Start=new(10,25),End=new(70,25)};var second=new LineEntity{LayerId=vm.Document.ActiveLayerId,GroupId=group,Start=new(10,50),End=new(70,50)};vm.Document.Entities.AddRange([first,second]);vm.Selection.Clear();vm.SelectionChanged();var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<CadCanvas>("Canvas");Assert.NotNull(canvas);canvas.SetTool(CanvasTool.Select);canvas.ZoomToFit();Dispatcher.UIThread.RunJobs();var zoom=Math.Max(.05,Math.Min((canvas.Bounds.Width-80)/100,(canvas.Bounds.Height-80)/100));var panX=(canvas.Bounds.Width-100*zoom)/2;var panY=(canvas.Bounds.Height-100*zoom)/2;var local=new Point(panX+40*zoom,canvas.Bounds.Height-panY-25*zoom);var screen=canvas.TranslatePoint(local,window);Assert.NotNull(screen);window.MouseDown(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();window.MouseUp(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();Assert.Equal(2,vm.Selection.Count);Assert.Contains(first.Id,vm.Selection.Ids);Assert.Contains(second.Id,vm.Selection.Ids);
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
    public void PlaybackControlsExecutePlayPauseSeekSpeedRestartAndStopAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent())
        {
            SelectedMachine=new MachineProfileDefinition(),
            SelectedTool=new CncTool{Name="6mm End Mill",ToolNumber=1,DiameterMm=6,StepDownMm=3,CuttingLengthMm=30,OverallLengthMm=50,ShankDiameterMm=6,StickOutMm=35,FeedRateMmPerMin=1200,PlungeRateMmPerMin=300,SpindleRpm=18000}
        };
        vm.CreateJob(new JobSettings{Name="Playback UI",WidthMm=100,HeightMm=100,ThicknessMm=18});vm.AddEntity(new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(20,20),Width=50,Height=40});vm.AddOperation(vm.CreateProfileDraft());vm.LoadAllPlayback();vm.SetViewportLayout(ViewportLayout.Preview3D);
        var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var play=window.FindControl<Button>("PlayPauseButton");var stop=window.FindControl<Button>("PlaybackStopButton");var restart=window.FindControl<Button>("PlaybackRestartButton");var slider=window.FindControl<Slider>("PlaybackSlider");var speed=window.FindControl<ComboBox>("PlaybackSpeedCombo");Assert.NotNull(play);Assert.NotNull(stop);Assert.NotNull(restart);Assert.NotNull(slider);Assert.NotNull(speed);Assert.True(play.IsVisible);Assert.True(slider.IsVisible);
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(PlaybackState.Playing,vm.Playback.State);Assert.Equal("Pause",vm.PlaybackAction);
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(PlaybackState.Paused,vm.Playback.State);Assert.Equal("Play",vm.PlaybackAction);
            slider.Value=50;Dispatcher.UIThread.RunJobs();Assert.Equal(.5,vm.Playback.Frame.Progress,2);
            speed.SelectedItem=10d;Dispatcher.UIThread.RunJobs();Assert.Equal(10,vm.PlaybackSpeed);
            restart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(PlaybackState.Playing,vm.Playback.State);Assert.InRange(vm.Playback.Frame.Progress,0,.01);
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(PlaybackState.Paused,vm.Playback.State);Assert.Equal(0,vm.Playback.Frame.Progress);
            Assert.Equal("Toolpath playback position",AutomationProperties.GetName(slider));Assert.Equal("Toolpath playback speed",AutomationProperties.GetName(speed));
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
    public void RepairTaskPreviewsAndAppliesClosedSelfIntersectionAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Repair",WidthMm=100,HeightMm=100,ThicknessMm=18});var bowtie=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,IsClosed=true,Points=[new(10,10),new(70,70),new(10,70),new(70,10)]};vm.AddEntity(bowtie);vm.Selection.Set(bowtie.Id);vm.SelectionChanged();vm.RepairTolerance=.001;vm.MinimumSegment=.001;vm.OpenDesignTask(DesignTaskPage.Repair);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var analyze=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Analyze"));var apply=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Apply Safe Repairs"));Assert.False(apply.IsEnabled);analyze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.True(apply.IsEnabled);Assert.Contains(vm.RepairIssues,issue=>issue.Kind==RepairIssueKind.SelfIntersection);apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(2,vm.Document.Entities.Count);Assert.All(vm.Document.Entities,entity=>Assert.True(Assert.IsType<PolylineEntity>(entity).IsClosed));
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void RouterCornerReliefControlsExecuteAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Corner relief",WidthMm=100,HeightMm=100,ThicknessMm=18});var path=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,IsClosed=true,Points=[new(10,10),new(80,10),new(80,70),new(50,70),new(50,40),new(10,40)]};vm.AddEntity(path);vm.CornerIndex=4;vm.CornerReliefToolDiameter=6;vm.OpenDesignTask(DesignTaskPage.Repair);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var dogbone=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Dogbone"));var tBone=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"T-Bone"));Assert.True(dogbone.IsEnabled);Assert.True(tBone.IsEnabled);dogbone.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.True(Assert.IsType<PolylineEntity>(Assert.Single(vm.Document.Entities)).Points.Count>path.Points.Count);Assert.True(vm.CanUndo);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void PathTextCreateAndGroupedVectorConversionWorkAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent(),textOutlineService:new PortableFallbackTextOutlineService());vm.CreateJob(new JobSettings{Name="Path text",WidthMm=120,HeightMm=100,ThicknessMm=18});var guide=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,Points=[new(10,20),new(70,20),new(70,80)]};vm.Document.Entities.Add(guide);vm.Selection.Set(guide.Id);vm.SelectionChanged();vm.TextValue="AB";vm.TextPathStart=.5;vm.OpenDesignTask(DesignTaskPage.TextShapes);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var create=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Create Text"));create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var text=Assert.IsType<TextEntity>(vm.Document.Entities.Single(entity=>entity is TextEntity));Assert.Equal(2,text.Glyphs.Count);var convert=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"To Vectors"));convert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var vectors=vm.Document.Entities.OfType<VectorPathEntity>().ToArray();Assert.Equal(2,vectors.Length);Assert.Single(vectors.Select(vector=>vector.GroupId).Distinct());Assert.True(vm.CanUndo);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void PointerSelectedPartCanBeOptimizedAcceptedAndUndoneAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Nest workflow",WidthMm=100,HeightMm=100,ThicknessMm=18});var source=new RectangleEntity{LayerId=vm.Document.ActiveLayerId,Origin=new(10,10),Width=20,Height=15};vm.Document.Entities.Add(source);vm.NestQuantity=2;vm.NestSpacing=4;vm.NestEdgeMargin=5;var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<CadCanvas>("Canvas");Assert.NotNull(canvas);canvas.SetTool(CanvasTool.Select);canvas.ZoomToFit();Dispatcher.UIThread.RunJobs();var zoom=Math.Max(.05,Math.Min((canvas.Bounds.Width-80)/100,(canvas.Bounds.Height-80)/100));var panX=(canvas.Bounds.Width-100*zoom)/2;var panY=(canvas.Bounds.Height-100*zoom)/2;var local=new Point(panX+10*zoom,canvas.Bounds.Height-panY-17.5*zoom);var screen=canvas.TranslatePoint(local,window);Assert.NotNull(screen);window.MouseDown(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();window.MouseUp(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();Assert.Equal(source.Id,vm.Selection.PrimaryId);
            vm.OpenDesignTask(DesignTaskPage.Nesting);Dispatcher.UIThread.RunJobs();Button Visible(string label)=>window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,label));Visible("Add Selected Parts").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(2,Assert.Single(vm.NestParts).Quantity);Visible("Optimize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var accept=Visible("Accept");Assert.True(accept.IsEnabled);accept.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(2,vm.Document.Entities.Count(entity=>entity.MaterialSheetId is not null));Assert.Equal(CncCad.Cam.Nesting.NestingLayoutStatus.Current,vm.NestingJob.Status);vm.Undo();Assert.DoesNotContain(vm.Document.Entities,entity=>entity.MaterialSheetId is not null);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void LayerCommandsMoveInvalidateAndUndoThroughVisibleControls()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Layers",WidthMm=100,HeightMm=100,ThicknessMm=18});var original=vm.Document.ActiveLayerId;var line=new LineEntity{LayerId=original,Start=new(10,10),End=new(70,10)};vm.AddEntity(line);var operation=new CncCad.Cam.Operations.EngraveOperationDefinition{GeometryIds=[line.Id],Status=CncCad.Cam.Operations.OperationStatus.Current,CalculatedToolpath=new()};vm.CamJob.Operations.Add(operation);vm.OpenDesignTask(DesignTaskPage.Layers);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            Button Visible(string label)=>window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,label));
            Visible("New").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var added=vm.SelectedLayer!;Assert.NotEqual(original,added.Id);Assert.Equal(2,vm.Document.Layers.Count);
            Visible("Move Selection Here").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(added.Id,line.LayerId);Assert.Equal(CncCad.Cam.Operations.OperationStatus.Stale,operation.Status);vm.Undo();Assert.Equal(original,line.LayerId);
            Visible("Lock / Unlock").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.True(added.IsLocked);vm.Undo();Assert.False(added.IsLocked);
            Visible("Move Up").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(0,added.Order);vm.Undo();Assert.Equal(1,added.Order);
            Visible("Delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.DoesNotContain(added,vm.Document.Layers);vm.Undo();Assert.Contains(added,vm.Document.Layers);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void ExactPolylineCancelIsVisibleAtMinimumSizeAndDoesNotCreateHistoryOrGeometry()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Exact polyline",WidthMm=100,HeightMm=100,ThicknessMm=18});vm.ActivePaletteItem=vm.CreationTools.Single(item=>item.Tool==CanvasTool.Polyline);vm.OpenDesignTask(DesignTaskPage.Construction);vm.Point1X=1;vm.Point1Y=2;vm.Point2X=10;vm.Point2Y=2;vm.AddNumericPolylineNode();var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var cancel=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Cancel"));cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Empty(vm.Document.Entities);Assert.False(vm.CanUndo);Assert.Contains("cancelled",vm.Message,StringComparison.OrdinalIgnoreCase);
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
    public void ExactArcAnchorIsSelectableAndSplittableButCannotBeDraggedOrNumericallyMoved()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Arc nodes",WidthMm=100,HeightMm=100,ThicknessMm=18});var path=new VectorPathEntity{LayerId=vm.Document.ActiveLayerId,Figures=[new(){Start=new(70,50),IsClosed=true,Segments=[new ArcPathSegment(new(50,50),90,new(50,70)),new ArcPathSegment(new(50,50),90,new(30,50)),new ArcPathSegment(new(50,50),90,new(50,30)),new ArcPathSegment(new(50,50),90,new(70,50))]}]};vm.AddEntity(path);vm.Selection.Set(path.Id);vm.SelectionChanged();vm.OpenDesignTask(DesignTaskPage.Edit);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var canvas=window.FindControl<CadCanvas>("Canvas");Assert.NotNull(canvas);canvas.NodeEditMode=true;canvas.ZoomToFit();Dispatcher.UIThread.RunJobs();var zoom=Math.Max(.05,Math.Min((canvas.Bounds.Width-80)/100,(canvas.Bounds.Height-80)/100));var panX=(canvas.Bounds.Width-100*zoom)/2;var panY=(canvas.Bounds.Height-100*zoom)/2;var local=new Point(panX+70*zoom,canvas.Bounds.Height-panY-50*zoom);var screen=canvas.TranslatePoint(local,window);Assert.NotNull(screen);window.MouseDown(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();window.MouseUp(screen.Value,MouseButton.Left,RawInputModifiers.None);Dispatcher.UIThread.RunJobs();Assert.True(vm.HasActiveNode);Assert.True(vm.CanInsertNode);Assert.True(vm.CanDeleteNode);Assert.False(vm.CanApplyNodePosition);
            var apply=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Apply Node Position"));var insert=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Insert"));Assert.False(apply.IsEnabled);Assert.True(insert.IsEnabled);insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(5,path.Figures[0].Segments.Count);Assert.All(path.Figures[0].Segments.Take(2),segment=>Assert.IsType<ArcPathSegment>(segment));vm.Undo();Assert.Equal(4,path.Figures[0].Segments.Count);
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
    public void SurfacePaletteUsesSelectedClosedRegionAndCalculatesAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Regional surface",WidthMm=120,HeightMm=100,ThicknessMm=18});var region=new PolylineEntity{LayerId=vm.Document.ActiveLayerId,IsClosed=true,Points=[new(10,10),new(90,10),new(90,35),new(40,35),new(40,80),new(10,80)]};vm.Document.Entities.Add(region);vm.Selection.Set(region.Id);vm.SelectionChanged();vm.SelectedTool=new(){Name="20 mm facing cutter",Type=ToolType.SurfacingBit,DiameterMm=20,StepDownMm=1,StepOverPercent=40,FeedRateMmPerMin=1600,PlungeRateMmPerMin=300,SpindleRpm=16000};vm.SetWorkspaceMode(WorkspaceMode.Toolpaths);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var create=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"S"));create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var draft=Assert.IsType<CncCad.Cam.Operations.SurfaceOperationDefinition>(vm.OperationEditorDraft);Assert.Equal([region.Id],draft.GeometryIds);var apply=window.GetVisualDescendants().OfType<Button>().Single(control=>control.IsVisible&&Equals(control.Content,"Calculate / Apply"));apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var operation=Assert.IsType<CncCad.Cam.Operations.SurfaceOperationDefinition>(Assert.Single(vm.CamJob.Operations));Assert.Equal(CncCad.Cam.Operations.OperationStatus.Current,operation.Status);Assert.NotNull(operation.CalculatedToolpath);Assert.Equal([region.Id],operation.CalculatedToolpath!.SourceGeometryIds);
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

    [AvaloniaFact]
    public void LibraryTaskExposesSearchCrudAndEveryToolSafetyDimensionAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Libraries",WidthMm=100,HeightMm=100,ThicknessMm=18});vm.SelectedMaterial=new(){Name="Birch",Category="Sheet"};vm.SelectedTool=new(){Name="6 mm",DiameterMm=6,CuttingLengthMm=20,OverallLengthMm=50,ShankDiameterMm=6,StickOutMm=30};vm.SelectedToolPreset=new(){ToolId=vm.SelectedTool.Id,MaterialId=vm.SelectedMaterial.Id,MachineId=Guid.NewGuid(),FeedRateMmPerMin=1000,PlungeRateMmPerMin=300,SpindleRpm=18000,PassDepthMm=2,StepOverPercent=40};vm.SetWorkspaceMode(WorkspaceMode.Toolpaths);vm.OpenToolpathTask(ToolpathTaskPage.ToolLibrary);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var placeholders=window.GetVisualDescendants().Where(control=>control.IsVisible).Select(control=>control switch{TextBox text=>text.PlaceholderText,NumericUpDown number=>number.PlaceholderText,_=>null}).Where(value=>value is not null).ToHashSet();foreach(var required in new[]{"Search materials…","Search tools…","Search presets…","Cut length","Overall length","Shank Ø","Stick-out","Feed","Plunge","RPM","Pass depth","Stepover %"})Assert.Contains(required,placeholders);var headings=window.GetVisualDescendants().OfType<TextBlock>().Where(control=>control.IsVisible).Select(control=>control.Text).ToArray();Assert.Contains("MATERIAL LIBRARY",headings);Assert.Contains("TOOL LIBRARY",headings);Assert.Contains("CUTTING PRESETS",headings);Assert.Contains("IMPORT / EXPORT",headings);var buttons=window.GetVisualDescendants().OfType<Button>().Where(control=>control.IsVisible).Select(control=>control.Content?.ToString()).ToArray();Assert.Contains("Import…",buttons);Assert.Contains("Export…",buttons);
        }
        finally{window.Close();}
    }

    [AvaloniaFact]
    public void MachineTaskExposesSearchAndEverySafetyDimensionAtMinimumSize()
    {
        var vm=new MainWindowViewModel(new MemoryTools(),new MemoryRecent());vm.CreateJob(new JobSettings{Name="Machine",WidthMm=100,HeightMm=100,ThicknessMm=18});vm.SelectedMachine=new();vm.SetWorkspaceMode(WorkspaceMode.Toolpaths);vm.OpenToolpathTask(ToolpathTaskPage.MachineFixtures);var window=new MainWindow{DataContext=vm,Width=1100,Height=700};window.Show();Dispatcher.UIThread.RunJobs();
        try{var placeholders=window.GetVisualDescendants().Where(control=>control.IsVisible).Select(control=>control switch{TextBox text=>text.PlaceholderText,NumericUpDown number=>number.PlaceholderText,_=>null}).Where(value=>value is not null).ToHashSet();foreach(var required in new[]{"Search machines…","Min X","Max X","Min Y","Max Y","Min Z","Max Z","Rapid mm/min","Tool change sec","Max RPM","Clearance","Holder Ø","Holder length","Tool change X","Tool change Y","Allowed spoilboard cut","Park X","Park Y"})Assert.Contains(required,placeholders);}
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
