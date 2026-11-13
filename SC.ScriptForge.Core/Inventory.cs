using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SC.ScriptForge.Core;

public enum DataStatus { Success, Unavailable, AccessDenied, Unsupported, Error }
public enum ErrorKind { AccessDenied, SourceUnavailable, Timeout, Unsupported, Cancelled, ApiError, PowerShellRuntime }
public sealed record OperationError(ErrorKind Kind, string Message, string Action)
{
    public static OperationError From(Exception error) => error switch
    {
        UnauthorizedAccessException => new(ErrorKind.AccessDenied, "Недостаточно прав для этого действия.", "Проверьте доступ к ресурсу."),
        TimeoutException => new(ErrorKind.Timeout, "Источник не ответил вовремя.", "Повторите обновление позднее."),
        OperationCanceledException => new(ErrorKind.Cancelled, "Операция отменена.", ""),
        PlatformNotSupportedException or NotSupportedException => new(ErrorKind.Unsupported, "Источник не поддерживается.", "Используйте поддерживаемую версию Windows."),
        System.ComponentModel.Win32Exception e when e.NativeErrorCode == 5 => new(ErrorKind.AccessDenied, "Windows отказала в доступе.", "Проверьте разрешения ресурса."),
        _ => new(ErrorKind.ApiError, "Не удалось завершить действие.", "Повторите попытку. Код ошибки записан в журнал."),
    };
}

// Explicit scalar union: numbers, dates and flags remain typed in exports; no parsing of display text.
public sealed record InventoryField(string Key, string Label, string Source)
{
    public DataStatus Status { get; init; } = DataStatus.Unavailable;
    public string? Text { get; init; }
    public double? Number { get; init; }
    public bool? Flag { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string? Unit { get; init; }
    public string? Detail { get; init; }
    public OperationError? Error { get; init; }
    public bool Sensitive { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public string StatusText => Status switch
    { DataStatus.Success => "Данные получены", DataStatus.AccessDenied => "Недостаточно прав", DataStatus.Unsupported => "Не поддерживается", DataStatus.Error => "Ошибка источника", _ => "Недоступно" };
    [JsonIgnore] public string Display => Status != DataStatus.Success ? StatusText
        : Text ?? (Flag.HasValue ? Flag.Value ? "Да" : "Нет" : Timestamp?.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
        ?? (Number.HasValue ? FormatNumber(Number.Value, Unit) : "Недоступно"));
    public static string FormatNumber(double value, string? unit) => unit switch
    {
        "bytes" => Bytes(value), "seconds" => TimeSpan.FromSeconds(Math.Max(0, value)) is var t ? $"{(int)t.TotalDays} д {t.Hours} ч {t.Minutes} мин" : "",
        "bps" => value >= 1e9 ? $"{value / 1e9:0.##} Гбит/с" : $"{value / 1e6:0.##} Мбит/с",
        _ => value.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")) + (unit is null ? "" : " " + unit)
    };
    public static string Bytes(double bytes)
    {
        var units = new[] { "Б", "КиБ", "МиБ", "ГиБ", "ТиБ" }; var i = 0;
        while (bytes >= 1024 && i < units.Length - 1) { bytes /= 1024; i++; }
        return bytes.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")) + " " + units[i];
    }
}
public sealed record InventoryRecord(string Title, IReadOnlyList<InventoryField> Fields);
public sealed record InventoryCategory(string Id, string Title, IReadOnlyList<InventoryRecord> Records, OperationError? Error = null)
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    { ["os"] = "Операционная система", ["cpu"] = "Процессор", ["memory"] = "Оперативная память", ["disks"] = "Диски", ["gpu"] = "Видеокарта", ["network"] = "Сеть", ["bios"] = "Система и BIOS" };
    public static InventoryCategory Failed(string id, OperationError error) => new(id, Names[id], [], error);
}
public sealed record InventorySnapshot(DateTimeOffset CapturedAt, IReadOnlyList<InventoryCategory> Categories);
public interface IInventoryCategoryService
{
    string CategoryId { get; }
    Task<InventoryCategory> CollectAsync(CancellationToken cancellationToken);
}
public sealed class ComputerInventory(IReadOnlyList<IInventoryCategoryService> services)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<InventorySnapshot> RefreshAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var workers = new SemaphoreSlim(2);
            var tasks = services.Select(async service =>
            {
                await workers.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { return await service.CollectAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { return InventoryCategory.Failed(service.CategoryId, OperationError.From(ex)); }
                finally { workers.Release(); }
            });
            return new(DateTimeOffset.UtcNow, await Task.WhenAll(tasks).ConfigureAwait(false));
        }
        finally { _gate.Release(); }
    }
}
public static class InventoryExport
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static InventorySnapshot Select(InventorySnapshot snapshot, IReadOnlySet<string> categories, bool includeSensitive = false) =>
        snapshot with { Categories = snapshot.Categories.Where(c => categories.Contains(c.Id)).Select(c => c with
        { Records = c.Records.Select(r => r with { Fields = r.Fields.Where(f => includeSensitive || !f.Sensitive).ToArray() }).ToArray() }).ToArray() };
    public static string Json(InventorySnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);
    public static string Text(InventorySnapshot snapshot)
    {
        var text = new StringBuilder("SC ScriptForge · характеристики компьютера\n").AppendLine(snapshot.CapturedAt.ToLocalTime().ToString("g"));
        foreach (var category in snapshot.Categories)
        {
            text.AppendLine().AppendLine(category.Title);
            if (category.Error is { } error) text.AppendLine(error.Message + " " + error.Action);
            foreach (var record in category.Records)
            {
                text.AppendLine(record.Title);
                foreach (var field in record.Fields) text.AppendLine($"  {field.Label}: {field.Display}  [{field.Source}; {field.CapturedAt:O}]");
            }
        }
        return text.ToString();
    }
}
