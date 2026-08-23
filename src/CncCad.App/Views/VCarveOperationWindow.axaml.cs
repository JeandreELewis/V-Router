using Avalonia.Controls;using Avalonia.Interactivity;using CncCad.App.ViewModels;using CncCad.Cam.Operations;using CncCad.Cam.Tools;
namespace CncCad.App.Views;
public partial class VCarveOperationWindow:Window
{
    private VCarveOperationEditorViewModel ViewModel=>(VCarveOperationEditorViewModel)DataContext!;public VCarveOperationWindow(VCarveOperationDefinition operation,IReadOnlyList<CncTool> tools){InitializeComponent();DataContext=new VCarveOperationEditorViewModel(operation,tools);}public VCarveOperationWindow(){InitializeComponent();}
    private void ToolPicker_SelectionChanged(object? sender,SelectionChangedEventArgs e){if(ViewModel.SelectedLibraryTool is{} tool)ViewModel.ApplyTool(tool);}private void Save_Click(object? sender,RoutedEventArgs e){if(!string.IsNullOrEmpty(ViewModel.ValidationMessage))return;Close(ViewModel.Draft);}private void Cancel_Click(object? sender,RoutedEventArgs e)=>Close(null);
}
