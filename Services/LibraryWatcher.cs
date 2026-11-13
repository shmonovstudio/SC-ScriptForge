namespace SC.ScriptForge.Services;

/// <summary>Coalesces external edits; consumers marshal the event onto their UI dispatcher.</summary>
public sealed class LibraryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _timer;
    private volatile bool _disposed;
    public event Action? Changed;
    public LibraryWatcher(string scriptsRoot)
    {
        _timer = new System.Threading.Timer(_ => { if (!_disposed) Changed?.Invoke(); }, null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(scriptsRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            EnableRaisingEvents = false
        };
        _watcher.Changed += OnChanged; _watcher.Created += OnChanged; _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged; _watcher.Error += (_, _) => Schedule();
        _watcher.EnableRaisingEvents = true;
    }
    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (e is RenamedEventArgs || e.ChangeType == WatcherChangeTypes.Deleted || Directory.Exists(e.FullPath)
            || e.FullPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || e.FullPath.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)
            || !Path.HasExtension(e.FullPath)) Schedule();
    }
    private void Schedule()
    {
        if (_disposed) return;
        try { _timer.Change(650, Timeout.Infinite); } catch (ObjectDisposedException) { }
    }
    public void Dispose() { _disposed = true; _watcher.Dispose(); _timer.Dispose(); }
}
