using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SC.ScriptForge.Infrastructure;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;

namespace SC.ScriptForge.Presentation;

public enum MessageTone { Neutral, Success, Warning, Danger }
public enum RunState { Idle, Running, Success, Warnings, Stopped, Failed }
public sealed record ResultMessage(string Title, string Text, bool IsWarning)
{
    public string Color => IsWarning ? "#916426" : "#4F625F";
    public string Icon => IsWarning ? "Shield" : "Check";
    public MessageTone Tone { get; init; } = IsWarning ? MessageTone.Warning : MessageTone.Neutral;
    /// <summary>"Ключ: значение" output that reads best as a summary row.</summary>
    public bool IsLabeled { get; init; }
}
/// <summary>An error line from the script; stack trace lines that follow it are folded into Details.</summary>
public sealed record ResultError(string Text, string Details = "");
public sealed record ResultFile(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}
public sealed class ResultTable : ObservableObject
{
    private readonly string _language;
    private string L(string ru, string en) => LibraryLocalization.Text(ru, en, _language);
    private readonly DataTable _data = new();
    public ResultTable(IReadOnlyList<ScriptResultField> fields, int index, string? language = null)
    {
        _language = LibraryLocalization.Normalize(language ?? LibraryLocalization.Language);
        Signature = string.Join('\u001f', fields.Select(x => x.Name));
        Title = index == 1 ? L("Данные результата", "Result data") : L("Данные результата · ", "Result data · ") + index;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var label = RunResultPresentation.Label(field.Name, _language);
            var unique = label; var suffix = 2;
            while (!used.Add(unique)) unique = label + " " + suffix++;
            _data.Columns.Add(unique);
        }
    }
    public string Signature { get; }
    public string Language => _language;
    public string Title { get; }
    public DataView Rows => _data.DefaultView;
    public int Count => _data.Rows.Count;
    public string CountLabel => L("Записей: ", "Rows: ") + Count;
    public void Add(IReadOnlyList<ScriptResultField> fields)
    {
        _data.Rows.Add(fields.Select(x => (object)RunResultPresentation.DisplayValue(x, _language)).ToArray());
        Raise(nameof(CountLabel));
    }
}

/// <summary>Deterministic presentation of actual script output. Raw output stays in diagnostics.</summary>
public sealed class RunResultPresentation : ObservableObject
{
    private string _language;
    private string L(string ru, string en) => LibraryLocalization.Text(ru, en, _language);
    public RunResultPresentation(string? language = null) => _language = LibraryLocalization.Normalize(language ?? LibraryLocalization.Language);
    private string _firstError = "";
    private string _workingDirectory = "";
    private int _received;
    private int _warnings;
    private int _errors;
    private int _omitted;
    private long? _processed;
    private bool _started;
    private bool _complete;
    private RunOutcome _outcome;
    public ObservableCollection<ResultTable> Tables { get; } = new();
    public ObservableCollection<ResultMessage> Messages { get; } = new();
    public ObservableCollection<ResultFile> Files { get; } = new();
    public ObservableCollection<ResultError> Errors { get; } = new();
    public int ErrorCount => _errors;
    public int ReceivedCount => _received;
    public RunState State => !_started ? RunState.Idle : !_complete ? RunState.Running : _outcome switch
    { RunOutcome.Success when _warnings == 0 => RunState.Success, RunOutcome.Success => RunState.Warnings, RunOutcome.Stopped => RunState.Stopped, _ => RunState.Failed };
    public IEnumerable<ResultMessage> WarningMessages => Messages.Where(x => x.IsWarning);
    public IEnumerable<ResultMessage> VisibleMessages => Messages.Where(x => !x.IsWarning && !Files.Any(f => f.Path.Equals(x.Text, StringComparison.OrdinalIgnoreCase)));
    public bool HasTables => Tables.Count > 0;
    public bool HasMessages => Messages.Count > 0;
    public bool HasFiles => Files.Count > 0;
    public bool HasWarnings => _warnings > 0;
    public bool HasOmitted => _omitted > 0;
    public bool IsRunning => _started && !_complete;
    public string RecordCount => (_received > 0 ? _received : _processed)?.ToString("N0", CultureInfo.GetCultureInfo(_language == "en" ? "en-US" : "ru-RU")) ?? "—";
    public string RecordLabel => _received > 0 ? L("записей в результате", "result rows") : _processed.HasValue ? L("строк обработано", "rows processed") : L("записей получено", "rows received");
    public string FileCount => Files.Count.ToString();
    public string WarningCount => _warnings.ToString();
    public string OmittedText => L("Показана часть большого результата. Полный принятый вывод доступен в диагностике и журнале.", "Only part of this large result is shown. The full received output is available in diagnostics and the log.");
    public string Color => !_started ? "#59716B" : !_complete ? "#287970" : _outcome switch
    { RunOutcome.Success when _warnings == 0 => "#28705B", RunOutcome.Success => "#916426", RunOutcome.Stopped => "#6A706F", _ => "#B4483E" };
    public string Background => !_complete ? "#E9EFE9" : _outcome switch
    { RunOutcome.Failed => "#F8EBE5", RunOutcome.Stopped => "#ECEDE7", RunOutcome.Success when _warnings > 0 => "#F6EFDF", _ => "#E9EFE9" };
    public string BorderColor => !_complete ? "#CAD8D0" : _outcome switch
    { RunOutcome.Failed => "#E8C9BD", RunOutcome.Success when _warnings > 0 => "#E3D2A8", _ => "#CAD8D0" };
    public string Icon => !_started ? "Play" : !_complete ? "ClockFilled" : _outcome switch
    { RunOutcome.Success when _warnings == 0 => "Check", RunOutcome.Success => "Shield", RunOutcome.Stopped => "Stop", _ => "Close" };
    public string Title => !_started ? L("Всё готово к работе", "Ready to run") : !_complete ? L("Выполняем задачу", "Running task") : _outcome switch
    { RunOutcome.Success when _warnings == 0 => L("Задача выполнена", "Task completed"), RunOutcome.Success => L("Готово, есть замечания", "Completed with warnings"), RunOutcome.Stopped => L("Выполнение остановлено", "Execution stopped"), _ => L("Не удалось завершить задачу", "Task failed") };
    public string Description => !_started ? L("Проверьте параметры слева и запустите задачу. Здесь появятся данные, сообщения и готовые файлы.", "Review the parameters on the left and run the task. Data, messages and output files will appear here.")
        : !_complete ? L("Результаты появляются по мере поступления. Время работы зависит от задачи.", "Results appear as they arrive. The time required depends on the task.")
        : _outcome == RunOutcome.Stopped ? L("Уже выполненные действия сохраняются. Данные ниже могут быть неполными.", "Completed actions are retained. The data below may be incomplete.")
        : _outcome == RunOutcome.Failed ? FriendlyError(_firstError)
        : _received == 0 && Messages.Count == 0 && Files.Count == 0 ? L("Скрипт завершился без ошибок и не вернул данные для отображения.", "The script completed without errors and returned no data to display.")
        : _warnings > 0 ? L("Скрипт завершился без ошибки. Просмотрите замечания перед использованием результата.", "The script completed without an error. Review the warnings before using the result.")
        : L("Результаты готовы к просмотру. Можно скопировать отчёт или открыть папку с файлами.", "Results are ready. You can copy the report or open the output folder.");
    public string StepText => !_started ? L("01  Параметры   /   02  Выполнение   /   03  Результат", "01  Parameters   /   02  Execution   /   03  Result")
        : !_complete ? L("Параметры проверены  →  Задача выполняется", "Parameters checked  →  Running")
        : _outcome == RunOutcome.Success ? L("Параметры проверены  →  Выполнено  →  Результат готов", "Parameters checked  →  Completed  →  Results ready") : L("Параметры проверены  →  Выполнение завершено", "Parameters checked  →  Execution finished");

    public void Reset(string workingDirectory, string? language = null)
    {
        if (language is not null) _language = LibraryLocalization.Normalize(language);
        Tables.Clear(); Messages.Clear(); Files.Clear(); Errors.Clear(); _workingDirectory = workingDirectory;
        _received = _warnings = _errors = _omitted = 0; _processed = null; _firstError = ""; _started = true; _complete = false;
        Notify();
    }
    public void Accept(ScriptOutput output, bool notify = true)
    {
        if (output.PreviewLimited) _omitted++;
        if (output.IsError)
        {
            var line = output.Text.TrimEnd();
            if (Errors.Count > 0 && StackLine.IsMatch(line))
            {
                var last = Errors[^1];
                if (last.Details.Length < 4000) Errors[^1] = last with { Details = (last.Details + "\n" + line.Trim()).Trim() };
            }
            else if (line.Trim().Length > 0)
            {
                _errors++;
                if (_firstError.Length == 0) _firstError = output.Text;
                if (Errors.Count < 30) Errors.Add(new(line.Length > 2000 ? line[..2000] + "…" : line));
                else _omitted++;
            }
            if (notify) Notify(); return;
        }
        if (output.Kind == ScriptOutputKind.Diagnostic) return;
        if (output.Kind == ScriptOutputKind.Data && output.Fields.Count > 0)
        {
            _received++;
            var signature = string.Join('\u001f', output.Fields.Select(x => x.Name));
            var table = Tables.FirstOrDefault(x => x.Signature == signature);
            if (table is null && Tables.Count < 12) { table = new(output.Fields, Tables.Count + 1, _language); Tables.Add(table); }
            if (table is not null && table.Count < 250 && Tables.Sum(x => x.Count) < 1000) table.Add(output.Fields);
            else _omitted++;
        }
        else if (!string.IsNullOrWhiteSpace(output.Text))
        {
            var warning = output.Kind == ScriptOutputKind.Warning;
            if (warning) _warnings++;
            if (Messages.Count < 80)
            {
                var text = output.Text.Trim();
                var colon = text.IndexOf(':');
                var labeled = colon is > 0 and < 70 && !text[..colon].Contains('\\') && !(colon == 1 && char.IsLetter(text[0]));
                var processed = Regex.Match(text, @"^(?:Обработано строк|Строк обработано|Rows processed)\s*:\s*(\d+)\s*$", RegexOptions.IgnoreCase);
                if (processed.Success && long.TryParse(processed.Groups[1].Value, out var count)) _processed = count;
                if (text.Length > 2000) { text = text[..2000] + "…"; _omitted++; }
                Messages.Add(new(labeled ? text[..colon] : warning ? L("Обратите внимание", "Warning") : L("Сообщение задачи", "Task message"),
                    labeled ? text[(colon + 1)..].Trim() : text, warning) { Tone = warning ? MessageTone.Warning : Classify(text), IsLabeled = labeled });
            }
            else _omitted++;
            var match = Regex.Match(output.Text, @"^(?:Сохранено|Файл|Отчёт|Saved|Output(?: file)?)\s*:\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success) AddFile(match.Groups[1].Value);
        }
        if (!string.IsNullOrWhiteSpace(output.FilePath)) AddFile(output.FilePath);
        if (notify) Notify();
    }
    private void AddFile(string value)
    {
        try
        {
            var path = Path.GetFullPath(value.Trim().Trim('"'), _workingDirectory);
            if (File.Exists(path) && !Files.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                if (Files.Count < 40) Files.Add(new(path));
                else _omitted++;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { }
    }
    public void Complete(RunOutcome outcome, string error = "")
    {
        _outcome = outcome; _complete = true;
        if (_firstError.Length == 0) _firstError = error;
        // A script can print a filename before the actual file is flushed.
        foreach (var message in Messages.Where(x => x.Title.Equals("Сохранено", StringComparison.OrdinalIgnoreCase))) AddFile(message.Text);
        Notify();
    }
    public void MarkOmitted(int count) { _omitted += count; Notify(); }
    public void Refresh() => Notify();
    public string Report()
    {
        var text = new StringBuilder().AppendLine(Title).AppendLine(Description).AppendLine();
        foreach (var table in Tables)
        {
            text.AppendLine(table.Title);
            text.AppendLine(string.Join('\t', table.Rows.Table!.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
            foreach (DataRowView row in table.Rows) text.AppendLine(string.Join('\t', row.Row.ItemArray.Select(v => Convert.ToString(v)?.Replace("\t", " ").Replace("\r", " ").Replace("\n", " "))));
            text.AppendLine();
        }
        foreach (var error in Errors) text.AppendLine(L("Ошибка: ", "Error: ") + error.Text + (error.Details.Length > 0 ? "\n" + error.Details : ""));
        foreach (var message in Messages) text.AppendLine(message.Title + ": " + message.Text);
        foreach (var file in Files) text.AppendLine(L("Файл: ", "File: ") + file.Path);
        if (HasOmitted) text.AppendLine(OmittedText);
        return text.ToString();
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(State), nameof(ErrorCount), nameof(ReceivedCount), nameof(Title), nameof(Description), nameof(Color), nameof(Background), nameof(BorderColor), nameof(Icon), nameof(StepText), nameof(IsRunning), nameof(RecordCount), nameof(RecordLabel), nameof(FileCount), nameof(WarningCount), nameof(HasTables), nameof(HasMessages), nameof(HasFiles), nameof(HasWarnings), nameof(HasOmitted), nameof(WarningMessages), nameof(VisibleMessages) }) Raise(name);
    }
    private static readonly IReadOnlyDictionary<string, (string Ru, string En)> FieldLabels = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ("Название", "Name"),
        ["displayname"] = ("Отображаемое имя", "Display name"),
        ["status"] = ("Состояние", "Status"),
        ["enabled"] = ("Включено", "Enabled"),
        ["csname"] = ("Компьютер", "Computer"),
        ["computername"] = ("Компьютер", "Computer"),
        ["caption"] = ("Система", "System"),
        ["version"] = ("Версия", "Version"),
        ["lastbootuptime"] = ("Последняя загрузка", "Last boot time"),
        ["manufacturer"] = ("Производитель", "Manufacturer"),
        ["model"] = ("Модель", "Model"),
        ["totalphysicalmemory"] = ("Оперативная память", "Memory"),
        ["deviceid"] = ("Диск", "Drive"),
        ["size"] = ("Объём", "Size"),
        ["freespace"] = ("Свободно", "Free space"),
        ["subject"] = ("Кому выдан", "Subject"),
        ["issuer"] = ("Кем выдан", "Issuer"),
        ["notafter"] = ("Действует до", "Expires at"),
        ["thumbprint"] = ("Отпечаток", "Thumbprint"),
        ["samaccountname"] = ("Учётная запись", "Account"),
        ["mail"] = ("Электронная почта", "Email"),
        ["email"] = ("Электронная почта", "Email"),
        ["count"] = ("Количество", "Count"),
        ["fullname"] = ("Полный путь", "Full path"),
        ["length"] = ("Размер файла", "File size"),
        ["lastwritetime"] = ("Изменён", "Last modified"),
        ["attribute"] = ("Поле", "Attribute"),
        ["issue"] = ("Проблема", "Issue"),
        ["currentvalue"] = ("Текущее значение", "Current value"),
        ["suggestedvalue"] = ("Предложение", "Suggestion"),
        ["lastlogondate"] = ("Последний вход", "Last sign-in"),
        ["created"] = ("Создано", "Created"),
        ["daysinactive"] = ("Дней без входа", "Days inactive"),
        ["expiresat"] = ("Истекает", "Expires at"),
        ["daysremaining"] = ("Осталось дней", "Days remaining"),
        ["missingattributes"] = ("Незаполненные поля", "Missing attributes"),
        ["operatingsystem"] = ("Операционная система", "Operating system"),
        ["serialnumber"] = ("Серийный номер", "Serial number"),
        ["processor"] = ("Процессор", "Processor"),
        ["cores"] = ("Ядра", "Cores"),
        ["logicalprocessors"] = ("Логические процессоры", "Logical processors"),
        ["volumename"] = ("Метка тома", "Volume label"),
        ["freepercent"] = ("Свободно (%)", "Free (%)"),
        ["uptimedays"] = ("Время работы (дни)", "Uptime (days)"),
        ["uptime"] = ("Время работы", "Uptime"),
        ["port"] = ("TCP-порт", "TCP port"),
        ["durationms"] = ("Время (мс)", "Time (ms)"),
        ["detail"] = ("Подробности", "Details"),
        ["dnsname"] = ("DNS-имя", "DNS name"),
        ["recordtype"] = ("Тип записи", "Record type"),
        ["value"] = ("Значение", "Value"),
        ["ttlseconds"] = ("TTL (с)", "TTL (s)"),
        ["preference"] = ("Приоритет", "Preference"),
        ["ipaddress"] = ("IP-адрес", "IP address"),
        ["filecount"] = ("Число файлов", "File count"),
        ["owner"] = ("Владелец", "Owner"),
        ["identity"] = ("Пользователь или группа", "User or group"),
        ["rights"] = ("Разрешения", "Permissions"),
        ["accesstype"] = ("Тип доступа", "Access type"),
        ["isinherited"] = ("Унаследовано", "Inherited"),
        ["inheritanceflags"] = ("Наследование", "Inheritance"),
        ["propagationflags"] = ("Распространение", "Propagation"),
        ["timecreated"] = ("Время события", "Event time"),
        ["username"] = ("Пользователь", "User"),
        ["domain"] = ("Домен", "Domain"),
        ["workstation"] = ("Рабочая станция", "Workstation"),
        ["logontype"] = ("Тип входа", "Logon type"),
        ["substatus"] = ("Дополнительный код", "Substatus"),
        ["groupname"] = ("Группа", "Group"),
        ["objectclass"] = ("Тип объекта", "Object class"),
        ["principalsource"] = ("Источник учётной записи", "Account source"),
        ["sid"] = ("SID", "SID"),
        ["hasprivatekey"] = ("Есть закрытый ключ", "Has private key"),
        ["partner"] = ("Партнёр репликации", "Replication partner"),
        ["failurecount"] = ("Число сбоев", "Failure count"),
        ["firstfailuretime"] = ("Первый сбой", "First failure"),
        ["lasterror"] = ("Код ошибки", "Error code"),
        ["failuretype"] = ("Тип сбоя", "Failure type"),
        ["eventid"] = ("Код события", "Event ID"),
        ["level"] = ("Уровень", "Level"),
        ["provider"] = ("Источник", "Provider"),
        ["message"] = ("Сообщение", "Message"),
    };
    public static string Label(string name, string? language = null) => FieldLabels.TryGetValue(name, out var pair)
        ? LibraryLocalization.Text(pair.Ru, pair.En, language) : Regex.Replace(name, "(?<=[a-zа-я])(?=[A-ZА-Я])|_", " ");
    public static string DisplayValue(ScriptResultField field, string? language = null)
    {
        var english = LibraryLocalization.Normalize(language ?? LibraryLocalization.Language) == "en";
        if (field.Name.ToLowerInvariant() is "totalphysicalmemory" or "size" or "freespace" or "length"
            && double.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0)
        {
            var units = english ? new[] { "B", "KiB", "MiB", "GiB", "TiB" } : new[] { "Б", "КиБ", "МиБ", "ГиБ", "ТиБ" }; var unit = 0;
            while (bytes >= 1024 && unit < units.Length - 1) { bytes /= 1024; unit++; }
            return bytes.ToString("0.##", CultureInfo.GetCultureInfo(english ? "en-US" : "ru-RU")) + " " + units[unit];
        }
        return field.Value;
    }
    private static readonly Regex StackLine = new(@"^\s*(?:at |в |At line:|Строка:|строка:|\+ |\+\s+~|~+\s*$|CategoryInfo|FullyQualifiedErrorId|Категория|Полное имя)", RegexOptions.CultureInvariant);
    private static readonly Regex SuccessText = new(@"^\s*(?:[✓✔✅]|\[(?:OK|ГОТОВО|УСПЕХ)\]|OK\b|Успех|Успешно|Готово|Выполнено|Завершено|Сохранено|Создан[оа]?\b|Success|Done\b|Completed|Finished)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DangerText = new(@"^\s*(?:[✗✘❌×]|\[(?:ERR|ERROR|FAIL|ОШИБКА)\]|Ошибка|Сбой|Не удалось|Невозможно|Error\b|Failed|Failure|FAIL\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WarningText = new(@"^\s*(?:[⚠!]|\[(?:WARN|WARNING|ВНИМАНИЕ)\]|Внимание|Предупреждение|Warning\b|WARN\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    /// <summary>Tone from the wording scripts typically use with Write-Host -ForegroundColor (the colour itself is not transmitted).</summary>
    public static MessageTone Classify(string text) =>
        DangerText.IsMatch(text) ? MessageTone.Danger : WarningText.IsMatch(text) ? MessageTone.Warning : SuccessText.IsMatch(text) ? MessageTone.Success : MessageTone.Neutral;
    private string FriendlyError(string error)
    {
        var text = error.ToLowerInvariant();
        if (text.Contains("access") || text.Contains("доступ") || text.Contains("unauthorized")) return L("Не хватает разрешений для одного из действий. Проверьте доступ к файлу или папке. Подробности доступны в диагностике.", "Permission was denied. Check access to the file or folder. See diagnostics for details.");
        if (text.Contains("cannot find") || text.Contains("не найден") || text.Contains("does not exist")) return L("Не найден нужный файл, папка или команда. Проверьте выбранные пути и наличие необходимых программ. Подробности доступны в диагностике.", "A required file, folder or command was not found. Check the paths and installed prerequisites. See diagnostics for details.");
        if (text.Contains("connect") || text.Contains("соединен") || text.Contains("network")) return L("Не удалось подключиться к ресурсу. Проверьте сеть и доступность сервера, затем повторите запуск.", "Could not connect to the resource. Check the network and server availability, then try again.");
        return L("Скрипт сообщил об ошибке. Проверьте входные данные. Для точной причины откройте расширенную диагностику; результаты ниже могут быть неполными.", "The script reported an error. Check the input and open diagnostics for the exact cause. Results below may be incomplete.");
    }
}
