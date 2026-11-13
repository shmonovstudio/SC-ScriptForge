using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SC.ScriptForge.Core;

/// <summary>Bounded JSONL diagnostics. Never writes exception messages, paths, script text or parameter values.</summary>
public sealed class DiagnosticLog(string dataRoot) : ILogger
{
    private readonly object _gate = new();
    public string Path { get; } = System.IO.Path.Combine(dataRoot, "Logs", "application.jsonl");
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > 2_000_000) File.Move(Path, Path + ".previous", true);
                var operation = eventId.Name is { } name && name.Length <= 80 && System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z][a-z0-9.]*$") ? name : "application";
                File.AppendAllText(Path, JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, level = level.ToString(), eventId = eventId.Id, operation, exception = exception?.GetType().Name, code = exception?.HResult }) + "\n");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    public void Failure(string operation, Exception exception) => Log(LogLevel.Error, new EventId(1, operation), "", exception, (_, _) => "");
    public void Event(string operation) => Log(LogLevel.Information, new EventId(0, operation), "", null, (_, _) => "");
}
