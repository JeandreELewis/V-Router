using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CncCad.App.ViewModels;
using CncCad.App.Views;
using CncCad.Cam.Tools;
using CncCad.Core.Documents;
using CncCad.Core.Preview;
using CncCad.Infrastructure.Persistence;
using CncCad.Infrastructure.Settings;

namespace CncCad.App.HeadlessTests;

public sealed class CommandHelpHeadlessTests
{
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
