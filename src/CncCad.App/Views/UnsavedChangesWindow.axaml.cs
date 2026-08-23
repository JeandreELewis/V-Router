using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CncCad.App.Views;

public enum UnsavedChoice { Cancel, Save, Discard }

public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow() => InitializeComponent();
    public UnsavedChangesWindow(string jobName) : this() => MessageText.Text = $"“{jobName}” has unsaved changes.";
    private void Save_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Save);
    private void Discard_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Discard);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(UnsavedChoice.Cancel);
}
