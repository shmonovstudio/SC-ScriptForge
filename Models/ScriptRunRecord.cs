using System.Text.Json.Serialization;

namespace SC.ScriptForge.Models;

public enum RunOutcome { Success, Failed, Stopped }

public sealed record ScriptRunRecord(Guid ScriptId, DateTimeOffset FinishedAt, TimeSpan Duration, RunOutcome Outcome, int? ExitCode)
{
    public bool UsedBypass { get; init; }
    [JsonIgnore] public string Label => (Outcome switch { RunOutcome.Success => "Успешно", RunOutcome.Stopped => "Остановлен", _ => "С ошибкой" }) + (UsedBypass ? " · Bypass" : "");
    [JsonIgnore] public string Color => Outcome switch { RunOutcome.Success => "#16845F", RunOutcome.Stopped => "#6D7F97", _ => "#C4485D" };
    public static string FormatDuration(TimeSpan duration) => duration.ToString(duration.TotalDays >= 1 ? @"d\.hh\:mm\:ss" : duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
    [JsonIgnore] public string DurationLabel => FormatDuration(Duration);
    [JsonIgnore] public string Summary => $"{Label} · {FinishedAt.LocalDateTime:dd.MM, HH:mm} · {DurationLabel}";
}
