namespace CncCad.Core.Commands;

public interface IUndoableCommand { string Description { get; } void Execute(); void Undo(); }
public interface IEntityAffectingCommand { IReadOnlyCollection<Guid> AffectedEntityIds { get; } }
public interface IJobSettingsAffectingCommand { }

public sealed class CommandHistory
{
    private readonly Stack<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Execute(IUndoableCommand command) { command.Execute(); _undo.Push(command); _redo.Clear(); }
    public IUndoableCommand? Undo() { if (!_undo.TryPop(out var command)) return null; command.Undo(); _redo.Push(command); return command; }
    public IUndoableCommand? Redo() { if (!_redo.TryPop(out var command)) return null; command.Execute(); _undo.Push(command); return command; }
    public void Clear() { _undo.Clear(); _redo.Clear(); }
}
