using Avalonia.Controls;
using Avalonia.Interactivity;
using CncCad.App.ViewModels;
using CncCad.Cam.Operations;
using CncCad.Cam.Tools;

namespace CncCad.App.Views;

public partial class ProfileOperationWindow : Window
{
    private readonly double _materialThickness; private bool _loaded;
    private ProfileOperationEditorViewModel ViewModel => (ProfileOperationEditorViewModel)DataContext!;
    public ProfileOperationWindow(ProfileOperationDefinition operation, IReadOnlyList<CncTool> tools, double materialThickness) { _materialThickness = materialThickness; InitializeComponent(); DataContext = new ProfileOperationEditorViewModel(operation, tools, materialThickness); Opened += (_, _) => _loaded = true; }
    public ProfileOperationWindow() { InitializeComponent(); }
    private void ToolPicker_SelectionChanged(object? sender, SelectionChangedEventArgs e) { if (_loaded && sender is ComboBox { SelectedItem: CncTool tool }) ViewModel.ApplyTool(tool); }
    private void Depth_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) { if (_loaded) ViewModel.RefreshDerived(_materialThickness); }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
    private void Save_Click(object? sender, RoutedEventArgs e) { if (ViewModel.Validate(_materialThickness)) Close(ViewModel.Draft); }
}
