using System.Collections.Concurrent;
using System.Text.Json;
using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

/// <summary>Local, bounded completion history. Contains no code, parameter values, output or secrets.</summary>
public sealed class RunHistoryStore
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;
    private readonly object _gate;
    private volatile ScriptRunRecord[] _records;
    public RunHistoryStore(string dataRoot)
    {
        _path = Path.GetFullPath(Path.Combine(dataRoot, "run-history.json"));
        _gate = Gates.GetOrAdd(_path, _ => new object());
        _records = Read();
    }
    public IReadOnlyList<ScriptRunRecord> Recent => Array.AsReadOnly(_records);
    public ScriptRunRecord? LatestFor(Guid id) => _records.FirstOrDefault(x => x.ScriptId == id);
    private ScriptRunRecord[] Read()
    {
        try
        {
            if (!File.Exists(_path)) return Array.Empty<ScriptRunRecord>();
            return (JsonSerializer.Deserialize<ScriptRunRecord[]>(File.ReadAllText(_path)) ?? Array.Empty<ScriptRunRecord>())
                .Where(x => x is not null && x.ScriptId != Guid.Empty && Enum.IsDefined(x.Outcome) && x.Duration >= TimeSpan.Zero)
                .OrderByDescending(x => x.FinishedAt).Take(100).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Array.Empty<ScriptRunRecord>(); }
    }
    public Task RecordAsync(ScriptRunRecord record) => Task.Run(() =>
    {
        lock (_gate)
        {
            var records = Read().Prepend(record).OrderByDescending(x => x.FinishedAt).Take(100).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(records));
                File.Move(temporary, _path, overwrite: true);
                _records = records;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    });
}
