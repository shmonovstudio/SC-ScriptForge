using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

/// <summary>Session-local undo history. The trash remains persistent across application restarts.</summary>
public sealed class DeletionHistory(ScriptRepository repository)
{
    private readonly Stack<(TrashEntry Trash, Guid Id)> _history = new();
    private readonly object _sync = new();
    public bool CanUndo { get { lock (_sync) return _history.Any(x => File.Exists(Path.Combine(x.Trash.Folder, "script.ps1"))); } }
    public void Delete(ScriptEntry entry) { lock (_sync) _history.Push((repository.MoveToTrash(entry), entry.Id)); }
    public Guid? Undo()
    {
        lock (_sync)
        {
            while (_history.TryPeek(out var last))
            {
                if (!File.Exists(Path.Combine(last.Trash.Folder, "script.ps1"))) { _history.Pop(); continue; }
                repository.RestoreFromTrash(last.Trash);
                _history.Pop();
                return last.Id;
            }
            return null;
        }
    }
}
