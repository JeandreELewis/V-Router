using Avalonia.Controls;using Avalonia.Interactivity;using CncCad.App.ViewModels;using CncCad.Cam.Operations;using CncCad.Cam.Tools;
namespace CncCad.App.Views;
public partial class EngraveOperationWindow:Window
{
    private bool _loaded;private EngraveOperationEditorViewModel ViewModel=>(EngraveOperationEditorViewModel)DataContext!;public EngraveOperationWindow(EngraveOperationDefinition operation,IReadOnlyList<CncTool> tools){InitializeComponent();DataContext=new EngraveOperationEditorViewModel(operation,tools);Opened+=(_,_)=>_loaded=true;}public EngraveOperationWindow(){InitializeComponent();}
    private void ToolPicker_SelectionChanged(object? sender,SelectionChangedEventArgs e){if(_loaded&&sender is ComboBox{SelectedItem:CncTool tool})ViewModel.ApplyTool(tool);}private void Cancel_Click(object? sender,RoutedEventArgs e)=>Close(null);private void Save_Click(object? sender,RoutedEventArgs e){if(ViewModel.Validate())Close(ViewModel.Draft);}
}
