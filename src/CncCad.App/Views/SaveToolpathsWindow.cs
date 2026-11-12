using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncCad.App.ViewModels;

namespace CncCad.App.Views;

public sealed class SaveToolpathsWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ComboBox _scope;
    private readonly TextBox _preview;
    private readonly TextBlock _status;
    private readonly Button _save;
    public SaveToolpathsWindow(MainWindowViewModel viewModel)
    {
        _viewModel=viewModel;Title="Save Toolpaths";Width=760;Height=650;MinWidth=620;MinHeight=500;CanResize=true;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.Parse("#181E23"));
        var choices=new ScopeChoice[]{new(ToolpathExportScope.SelectedToolpath,"Selected toolpath"),new(ToolpathExportScope.VisibleSeparateFiles,"Visible toolpaths — safe files by cutter (recommended)"),new(ToolpathExportScope.VisibleCombined,"Visible toolpaths — one file (same cutter only)")};
        _scope=new ComboBox{ItemsSource=choices,SelectedItem=choices[1]};_scope.SelectionChanged+=(_,_)=>RefreshPreview();
        _preview=new TextBox{AcceptsReturn=true,IsReadOnly=true,FontFamily=new FontFamily("Menlo,Consolas"),FontSize=11,TextWrapping=TextWrapping.NoWrap};
        _status=new TextBlock{Foreground=new SolidColorBrush(Color.Parse("#D8C477")),TextWrapping=TextWrapping.Wrap};_save=new Button{Content="Choose Destination…",HorizontalAlignment=HorizontalAlignment.Right,Classes={"primary"}};var cancel=new Button{Content="Cancel"};_save.Click+=(_,_)=>{if(_scope.SelectedItem is ScopeChoice choice)Close(choice.Value);};cancel.Click+=(_,_)=>Close(null);
        var machine=viewModel.CamJob.Machine;Content=new Grid{Margin=new Thickness(20),RowDefinitions=new("Auto,Auto,Auto,*,Auto"),RowSpacing=10,Children={new TextBlock{Text="SAVE TOOLPATHS",FontSize=20,FontWeight=FontWeight.Bold},new Grid{[Grid.RowProperty]=1,ColumnDefinitions=new("*,*"),ColumnSpacing=10,Children={new StackPanel{Spacing=4,Children={new TextBlock{Text="Scope",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},_scope}},new StackPanel{[Grid.ColumnProperty]=1,Spacing=4,Children={new TextBlock{Text="Machine / postprocessor",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},new TextBlock{Text=$"{machine?.Name??"No machine"} · {machine?.PostProcessorId??"No post"}",VerticalAlignment=VerticalAlignment.Center}}}}},new StackPanel{[Grid.RowProperty]=2,Spacing=4,Children={new TextBlock{Text="Safety rule: every runnable machine file contains one cutter only. Different cutters are always exported to separate files.",TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.Parse("#FFBE73"))},_status,new TextBlock{Text="Generated program preview",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))}}},_preview,new StackPanel{[Grid.RowProperty]=4,Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Children={cancel,_save}}}};
        RefreshPreview();
    }
    private void RefreshPreview()
    {
        try{var scope=(_scope.SelectedItem as ScopeChoice)?.Value??ToolpathExportScope.VisibleSeparateFiles;var files=_viewModel.GenerateExportPrograms(scope);_preview.Text=string.Join($"{Environment.NewLine}{Environment.NewLine}----- NEXT FILE -----{Environment.NewLine}{Environment.NewLine}",files.Select(x=>x.Text));_status.Text=$"{files.Count} safe machine file{(files.Count==1?"":"s")} · {files.Sum(x=>x.WarningCount)} preflight warning(s) · atomic write after confirmation";_save.IsEnabled=true;}
        catch(Exception exception){_preview.Text="";_status.Text=exception.Message;_save.IsEnabled=false;}
    }

    private sealed record ScopeChoice(ToolpathExportScope Value,string Label)
    {
        public override string ToString()=>Label;
    }
}
