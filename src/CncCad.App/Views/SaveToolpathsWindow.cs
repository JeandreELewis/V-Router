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
    public SaveToolpathsWindow(MainWindowViewModel viewModel)
    {
        _viewModel=viewModel;Title="Save Toolpaths";Width=760;Height=650;MinWidth=620;MinHeight=500;CanResize=true;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.Parse("#181E23"));
        _scope=new ComboBox{ItemsSource=Enum.GetValues<ToolpathExportScope>(),SelectedItem=ToolpathExportScope.VisibleCombined};_scope.SelectionChanged+=(_,_)=>RefreshPreview();
        _preview=new TextBox{AcceptsReturn=true,IsReadOnly=true,FontFamily=new FontFamily("Menlo,Consolas"),FontSize=11,TextWrapping=TextWrapping.NoWrap};
        _status=new TextBlock{Foreground=new SolidColorBrush(Color.Parse("#D8C477")),TextWrapping=TextWrapping.Wrap};var save=new Button{Content="Choose Destination…",HorizontalAlignment=HorizontalAlignment.Right,Classes={"primary"}};var cancel=new Button{Content="Cancel"};save.Click+=(_,_)=>{if(_scope.SelectedItem is ToolpathExportScope value)Close(value);};cancel.Click+=(_,_)=>Close(null);
        var machine=viewModel.CamJob.Machine;Content=new Grid{Margin=new Thickness(20),RowDefinitions=new("Auto,Auto,Auto,*,Auto"),RowSpacing=10,Children={new TextBlock{Text="SAVE TOOLPATHS",FontSize=20,FontWeight=FontWeight.Bold},new Grid{[Grid.RowProperty]=1,ColumnDefinitions=new("*,*"),ColumnSpacing=10,Children={new StackPanel{Spacing=4,Children={new TextBlock{Text="Scope",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},_scope}},new StackPanel{[Grid.ColumnProperty]=1,Spacing=4,Children={new TextBlock{Text="Machine / postprocessor",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))},new TextBlock{Text=$"{machine?.Name??"No machine"} · {machine?.PostProcessorId??"No post"}",VerticalAlignment=VerticalAlignment.Center}}}}},new StackPanel{[Grid.RowProperty]=2,Spacing=4,Children={_status,new TextBlock{Text="Generated program preview",FontSize=10,Foreground=new SolidColorBrush(Color.Parse("#91A1AD"))}}},_preview,new StackPanel{[Grid.RowProperty]=4,Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Children={cancel,save}}}};
        RefreshPreview();
    }
    private void RefreshPreview()
    {
        try{var scope=(ToolpathExportScope)(_scope.SelectedItem??ToolpathExportScope.VisibleCombined);var files=_viewModel.GenerateExportPrograms(scope);_preview.Text=string.Join($"{Environment.NewLine}{Environment.NewLine}----- NEXT FILE -----{Environment.NewLine}{Environment.NewLine}",files.Select(x=>x.Text));_status.Text=$"{files.Count} file{(files.Count==1?"":"s")} · {files.Sum(x=>x.WarningCount)} preflight warning(s) · atomic write after confirmation";}
        catch(Exception exception){_preview.Text="";_status.Text=exception.Message;}
    }
}
