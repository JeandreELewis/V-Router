using System.Windows.Input;
using Avalonia.Input;

namespace CncCad.App;

/// <summary>One command contract shared by menus, toolbars, and keyboard routing.</summary>
public sealed class ApplicationCommand(
    string id,
    string label,
    Action execute,
    Func<bool>? canExecute = null,
    KeyGesture? gesture = null) : ICommand
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public KeyGesture? Gesture { get; } = gesture;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
