using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CncCad.App.Views;
public sealed record ShortcutEntry(string Group,string Action,string Gesture);
public partial class KeyboardShortcutsWindow : Window
{
    private readonly ShortcutEntry[] _entries=[new("File","New Job","Cmd/Ctrl+N"),new("File","Open","Cmd/Ctrl+O"),new("File","Save","Cmd/Ctrl+S"),new("File","Save As","Cmd/Ctrl+Shift+S"),new("Edit","Undo","Cmd/Ctrl+Z"),new("Edit","Redo","Cmd/Ctrl+Shift+Z"),new("Edit","Copy / Paste / Duplicate","Cmd/Ctrl+C / V / D"),new("Edit","Delete selection","Delete"),new("View","Zoom to fit","F"),new("Design","Finish current path","Enter"),new("Design","Cancel current action","Escape"),new("Design","Close polyline","C"),new("Design","Nudge selection","Arrow keys"),new("Design","Nudge by 10 mm","Shift+Arrow")];
    public KeyboardShortcutsWindow(){InitializeComponent();ShortcutList.ItemsSource=_entries;}
    private void SearchChanged(object? sender,TextChangedEventArgs e){var q=SearchBox.Text?.Trim()??"";ShortcutList.ItemsSource=string.IsNullOrEmpty(q)?_entries:_entries.Where(x=>x.Group.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Action.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Gesture.Contains(q,StringComparison.OrdinalIgnoreCase));}
    private void Close_Click(object? sender,RoutedEventArgs e)=>Close();
}
