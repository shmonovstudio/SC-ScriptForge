using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using SC.ScriptForge.Infrastructure;
using SC.ScriptForge.Models;
using SC.ScriptForge.Presentation;
using SC.ScriptForge.Services;
using SC.ScriptForge.Core;

namespace SC.ScriptForge.ViewModels;

public sealed class ScriptRunViewModel : ObservableObject, IDisposable
{
    private readonly ScriptEntry _entry;
    private readonly string _language = LibraryLocalization.Language;
    private readonly ScriptParameterService _parameters = new();
    private readonly ScriptExecutionService _execution;
    private readonly RunProfileStore _profiles;
    private readonly RunHistoryStore _history;
    private readonly ExecutionPolicyService _policyService;
    private readonly DiagnosticLog? _logService;
    private ExecutionPolicySnapshot? _policy;
    private bool _policyBlocked;
    private bool _changingPolicy;
    private ScriptRunRecord? _lastRun;
    private bool _showOptional;
    private bool _showTechnical;
    private readonly DispatcherTimer _timer;
    private readonly ConcurrentQueue<ScriptOutput> _output = new();
    private int _droppedOutput;
    private readonly StringBuilder _console = new();
    private List<ScriptParameterViewModel> _allFields = new();
    private IReadOnlyList<ScriptParameterViewModel> _fields = Array.Empty<ScriptParameterViewModel>();
    private ScriptRunProfile _profile = new();
    private ScriptDefinition? _definition;
    private PowerShellEngine? _engine;
    private CancellationTokenSource? _inspectCancellation;
    private CancellationTokenSource? _runCancellation;
    private Task? _initialization;
    private bool _disposed;
    private bool _loading;
    private bool _running;
    private string _error = "";
    private string _notice = "";
    private string _status = "Настройте входные данные";
    private string _set = "";
    private string _log = "";
    private string _logPath = "";
    private bool _remember = true;
    private readonly Stopwatch _elapsed = new();
    public RunResultPresentation Result { get; } = new();
    private sealed class QueueProgress(ConcurrentQueue<ScriptOutput> queue, Action dropped) : IProgress<ScriptOutput>
    {
        public void Report(ScriptOutput value)
        {
            queue.Enqueue(value);
            while (queue.Count > 2_048 && queue.TryDequeue(out _)) dropped();
        }
    }

    public ScriptRunViewModel(ScriptEntry entry, string dataRoot, RunHistoryStore? history = null, ExecutionPolicyService? policyService = null, DiagnosticLog? log = null)
    {
        _entry = entry; ScriptName = entry.DisplayName; _execution = new(dataRoot); _profiles = new(dataRoot);
        _logService = log;
        _history = history ?? new RunHistoryStore(dataRoot);
        _policyService = policyService ?? new ExecutionPolicyService();
        _lastRun = _history.LatestFor(entry.Id);
        Engines = PowerShellHost.InstalledEngines();
        _engine = Engines.FirstOrDefault();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => { DrainOutput(); Raise(nameof(Elapsed)); };
    }
    public string ScriptName { get; }
    public string ScriptPath => _entry.FilePath;
    public string WorkingDirectory => Path.GetDirectoryName(ScriptPath)!;
    public IReadOnlyList<PowerShellEngine> Engines { get; }
    public IReadOnlyList<ScriptParameterViewModel> Parameters => _fields;
    public IReadOnlyList<ScriptParameterViewModel> PrimaryParameters => _fields.Where(x => x.IsPrimary).ToArray();
    public IReadOnlyList<ScriptParameterViewModel> OptionalParameters => _fields.Where(x => !x.IsPrimary).ToArray();
    public bool HasOptionalParameters => OptionalParameters.Count > 0;
    public string OptionalHeader => $"Дополнительные параметры · {OptionalParameters.Count}";
    public bool ShowOptionalParameters { get => _showOptional; set => Set(ref _showOptional, value); }
    public bool ShowTechnicalDetails { get => _showTechnical; set => Set(ref _showTechnical, value); }
    public ScriptParameterViewModel? FirstInvalidParameter => _fields.FirstOrDefault(x => x.HasError);
    public ScriptRunRecord? LastRun { get => _lastRun; private set { if (Set(ref _lastRun, value)) { Raise(nameof(RunButtonText)); Raise(nameof(OutcomeText)); Raise(nameof(StatusColor)); Raise(nameof(OutcomeColor)); } } }
    public string RunButtonText => LastRun is null ? "Запустить задачу" : "Запустить ещё раз";
    public string OutcomeText => IsRunning ? "Скрипт выполняется…" : LastRun?.Summary ?? "Запуск ещё не выполнялся";
    public string StatusColor => IsRunning ? "#087EFF" : HasError ? "#C4485D" : LastRun?.Color ?? "#52749A";
    public string OutcomeColor => IsRunning ? "#087EFF" : LastRun?.Color ?? "#52749A";
    public bool HasLog => LogText.Length > 0;
    public bool HasOutputLocation => Parameters.Any(p => !p.UseDefault && p.Role is ParameterRole.OutputFile or ParameterRole.OutputDirectory && !string.IsNullOrWhiteSpace(p.Value));
    public IReadOnlyList<string> ParameterSets => _definition?.ParameterSets ?? Array.Empty<string>();
    public bool HasParameterSets => ParameterSets.Count > 1;
    public bool HasNoParameters => !IsLoading && _definition is not null && Parameters.Count == 0;
    public string ParameterCount => Parameters.Count + " параметров";
    public bool CanEdit => !IsRunning && !IsLoading && !IsChangingPolicy;
    public bool CanRun => CanEdit && _definition is not null;
    public bool IsChangingPolicy { get => _changingPolicy; private set { if (Set(ref _changingPolicy, value)) StateChanged(); } }
    public bool CanBypassOnce => CanRun && Policy is { IsManaged: false };
    public bool CanAllowLocalScripts => CanEdit && Policy is { IsManaged: false } && !Policy.CurrentUser.Equals("RemoteSigned", StringComparison.OrdinalIgnoreCase);
    public bool PolicyBlocked { get => _policyBlocked; private set => Set(ref _policyBlocked, value); }
    public ExecutionPolicySnapshot? Policy
    {
        get => _policy;
        private set
        {
            if (!Set(ref _policy, value)) return;
            Raise(nameof(PolicySummary)); Raise(nameof(PolicyDetails)); Raise(nameof(PolicyRecoveryText));
            Raise(nameof(CanBypassOnce)); Raise(nameof(CanAllowLocalScripts));
        }
    }
    public string PolicySummary => IsChangingPolicy ? "Применяем RemoteSigned…" : Policy?.Summary ?? "Политика не проверена. Обновите параметры скрипта.";
    public string PolicyDetails => Policy?.Details ?? "Настройки запуска доступны после проверки политики выбранного PowerShell.";
    public string PolicyRecoveryText => Policy is { IsManaged: true } ? Policy.Details
        : "Если вы доверяете скрипту, разрешите один запуск с Bypass или включите локальные скрипты для вашей учётной записи.";
    public bool HasError => Error.Length > 0;
    public bool HasNotice => Notice.Length > 0;
    public string Notice { get => _notice; private set { if (Set(ref _notice, value)) Raise(nameof(HasNotice)); } }
    public string Elapsed => ScriptRunRecord.FormatDuration(_elapsed.Elapsed);
    public string OutputLocation => Parameters.FirstOrDefault(p => !p.UseDefault && p.Role is ParameterRole.OutputFile or ParameterRole.OutputDirectory)?.Value is { Length: > 0 } value ? value : "Путь результата задаётся параметрами скрипта";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Error { get => _error; private set { if (Set(ref _error, value)) { Raise(nameof(HasError)); Raise(nameof(StatusColor)); } } }
    public string LogText { get => _log; private set { if (Set(ref _log, value)) Raise(nameof(HasLog)); } }
    public string LogPath { get => _logPath; private set => Set(ref _logPath, value); }
    public bool RememberParameters { get => _remember; set => Set(ref _remember, value); }
    public bool IsLoading { get => _loading; private set { if (Set(ref _loading, value)) StateChanged(); } }
    public bool IsRunning { get => _running; private set { if (Set(ref _running, value)) StateChanged(); } }
    public PowerShellEngine? SelectedEngine
    {
        get => _engine;
        set { if (Set(ref _engine, value) && _initialization is not null && value is not null) _ = ReloadAsync(); }
    }
    public string SelectedParameterSet
    {
        get => _set;
        set { if (Set(ref _set, value ?? "")) UpdateFields(); }
    }

    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        _profile = await Task.Run(() => _profiles.Load(_entry.Id));
        _engine = Engines.FirstOrDefault(x => x.Id == _profile.EngineId) ?? Engines.FirstOrDefault();
        Raise(nameof(SelectedEngine));
        await ReloadAsync();
    }
    public async Task ReloadAsync()
    {
        if (_disposed || IsRunning) return;
        if (_allFields.Count > 0) _profile.Parameters = _allFields.Where(x => !x.IsSecret).ToDictionary(x => x.Name, x => x.Capture(), StringComparer.OrdinalIgnoreCase);
        _inspectCancellation?.Cancel(); _inspectCancellation?.Dispose();
        var cancellation = _inspectCancellation = new CancellationTokenSource();
        IsLoading = true; Error = ""; Notice = "";
        try
        {
            if (SelectedEngine is null) throw new InvalidOperationException("На компьютере не найден PowerShell.");
            var engine = SelectedEngine;
            var definitionTask = _parameters.InspectAsync(ScriptPath, engine, cancellation.Token);
            var policyTask = ReadPolicySafelyAsync(engine, cancellation.Token);
            await Task.WhenAll(definitionTask, policyTask);
            var definition = await definitionTask;
            if (_disposed || cancellation.IsCancellationRequested) return;
            Policy = await policyTask;
            foreach (var old in _allFields) old.PropertyChanged -= FieldChanged;
            _definition = definition;
            _allFields = definition.Parameters.Select(x => new ScriptParameterViewModel(x, _language)).ToList();
            _set = definition.ParameterSets.Contains(_profile.ParameterSet) ? _profile.ParameterSet
                : definition.ParameterSets.Contains(definition.DefaultSet) ? definition.DefaultSet : definition.ParameterSets.FirstOrDefault() ?? "";
            foreach (var field in _allFields)
            {
                field.SetParameterSet(_set);
                if (_profile.Parameters.TryGetValue(field.Name, out var preset)) field.Restore(preset);
                field.PropertyChanged += FieldChanged;
            }
            UpdateFields();
            Raise(nameof(ParameterSets)); Raise(nameof(HasParameterSets)); Raise(nameof(SelectedParameterSet));
            Status = "Готов к запуску";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            _logService?.Failure("script.inspect", ex);
            if (!cancellation.IsCancellationRequested)
            {
                _definition = null;
                foreach (var field in _allFields) field.PropertyChanged -= FieldChanged;
                _allFields.Clear(); _set = ""; UpdateFields();
                Raise(nameof(ParameterSets)); Raise(nameof(HasParameterSets)); Raise(nameof(SelectedParameterSet));
                Error = "Не удалось подготовить скрипт к запуску.\n" + ex.Message.Trim();
                Status = "Запуск недоступен: ошибка подготовки";
            }
        }
        finally { if (!_disposed && !cancellation.IsCancellationRequested) IsLoading = false; }
    }
    private void UpdateFields()
    {
        foreach (var field in _allFields) field.SetParameterSet(_set);
        _fields = _allFields.Where(x => x.Definition.Sets.Contains("*") || x.Definition.Sets.Contains(_set)).ToArray();
        Raise(nameof(Parameters)); Raise(nameof(ParameterCount)); Raise(nameof(HasNoParameters)); Raise(nameof(OutputLocation));
        Raise(nameof(PrimaryParameters)); Raise(nameof(OptionalParameters)); Raise(nameof(HasOptionalParameters)); Raise(nameof(OptionalHeader)); Raise(nameof(HasOutputLocation));
        if (PrimaryParameters.Count == 0 && HasOptionalParameters) ShowOptionalParameters = true;
    }
    private void FieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptParameterViewModel.Value) or nameof(ScriptParameterViewModel.UseDefault) or nameof(ScriptParameterViewModel.Role)) { Raise(nameof(OutputLocation)); Raise(nameof(HasOutputLocation)); }
    }
    private void StateChanged()
    {
        Raise(nameof(CanEdit)); Raise(nameof(CanRun)); Raise(nameof(HasNoParameters));
        Raise(nameof(OutcomeText)); Raise(nameof(StatusColor));
        Raise(nameof(OutcomeColor));
        Raise(nameof(CanBypassOnce)); Raise(nameof(CanAllowLocalScripts)); Raise(nameof(PolicySummary));
    }

    public ScriptExecutionRequest? PrepareRequest()
    {
        if (_definition is null || SelectedEngine is null) return null;
        var bound = new List<BoundScriptParameter>(); var output = new List<string>(); var valid = true;
        foreach (var field in Parameters)
        {
            if (!field.TryBind(WorkingDirectory, out var parameter, out var directory)) { valid = false; continue; }
            if (parameter is not null) bound.Add(parameter);
            if (directory is not null) output.Add(directory);
        }
        if (!valid)
        {
            if (OptionalParameters.Any(x => x.HasError)) ShowOptionalParameters = true;
            Error = "Проверьте отмеченные поля перед запуском.";
            Raise(nameof(FirstInvalidParameter));
            return null;
        }
        Error = "";
        var reportLanguage = bound.FirstOrDefault(p => p.Name.Equals("Language", StringComparison.OrdinalIgnoreCase))?.Value as string ?? _language;
        return new(ScriptPath, _definition.Fingerprint, SelectedEngine, bound, output) { Language = LibraryLocalization.Normalize(reportLanguage) };
    }
    public async Task SaveProfileAsync()
    {
        _profile = new ScriptRunProfile
        {
            EngineId = SelectedEngine?.Id ?? "windows", ParameterSet = SelectedParameterSet,
            Parameters = _allFields.Where(x => !x.IsSecret).ToDictionary(x => x.Name, x => x.Capture(), StringComparer.OrdinalIgnoreCase)
        };
        await Task.Run(() => _profiles.Save(_entry.Id, _profile));
    }

    private async Task<ExecutionPolicySnapshot?> ReadPolicySafelyAsync(PowerShellEngine engine, CancellationToken token = default)
    {
        try { return await _policyService.ReadAsync(engine, token); }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException) { return null; }
    }
    public async Task AllowLocalScriptsAsync()
    {
        if (!CanAllowLocalScripts || SelectedEngine is null) return;
        IsChangingPolicy = true; Error = ""; Notice = "";
        try
        {
            Policy = await _policyService.AllowLocalScriptsAsync(SelectedEngine);
            Error = "";
            PolicyBlocked = false;
            Notice = Policy.Effective.Equals("RemoteSigned", StringComparison.OrdinalIgnoreCase)
                ? $"RemoteSigned включена для текущего пользователя ({SelectedEngine.Name}). Нажмите «Запустить» для повторной попытки."
                : $"RemoteSigned сохранена для текущего пользователя. Действующая политика: {Policy.Effective}. Для одного доверенного запуска доступен Bypass.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception or System.Text.Json.JsonException)
        {
            Policy = await ReadPolicySafelyAsync(SelectedEngine);
            if (Policy is { IsManaged: false, CurrentUser: "RemoteSigned" })
            {
                Error = "";
                Notice = $"RemoteSigned сохранена. Её перекрывает политика {Policy.Effective}; для одного доверенного запуска доступен Bypass.";
            }
            else { _logService?.Failure("script.policy", ex); Error = "Не удалось применить настройку PowerShell. Проверьте ограничения учётной записи и групповые политики."; }
        }
        finally { IsChangingPolicy = false; }
    }
    public async Task RunAsync(bool bypassOnce = false)
    {
        if (!CanRun) return;
        if (bypassOnce && !CanBypassOnce) { Error = Policy is { IsManaged: true } ? Policy.Details : "Обновите параметры для проверки политики PowerShell."; return; }
        var request = PrepareRequest();
        if (request is null) return;
        request = request with { BypassOnce = bypassOnce };
        PolicyBlocked = false;
        Result.Reset(WorkingDirectory, request.Language);
        IsRunning = true; Error = ""; Notice = ""; Status = "Выполняется…"; _console.Clear(); LogText = ""; LogPath = "";
        while (_output.TryDequeue(out _)) { }
        _droppedOutput = 0;
        _runCancellation = new CancellationTokenSource();
        _elapsed.Restart(); _timer.Start();
        var outcome = RunOutcome.Failed;
        int? exitCode = null;
        _output.Enqueue(new ScriptOutput("▶ " + ScriptName + " · " + SelectedEngine!.Name) { Kind = ScriptOutputKind.Diagnostic });
        if (bypassOnce) _output.Enqueue(new ScriptOutput("Режим: Bypass только для этого процесса. Следующий запуск использует обычную политику.") { Kind = ScriptOutputKind.Diagnostic });
        try
        {
            if (RememberParameters)
            {
                try { await SaveProfileAsync(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "Не удалось запомнить параметры. Запуск продолжается с введёнными значениями."; }
            }
            var result = await _execution.RunAsync(request, new QueueProgress(_output, () => Interlocked.Increment(ref _droppedOutput)), _runCancellation.Token);
            LogPath = result.LogPath;
            Error = result.TimedOut ? "Превышено время выполнения (10 минут). Дочерний процесс остановлен; уже выполненные действия сохраняются." : "";
            PolicyBlocked = result.ExecutionPolicyBlocked;
            if (PolicyBlocked)
            {
                Policy = await ReadPolicySafelyAsync(SelectedEngine!, _runCancellation.Token);
            }
            outcome = result.Cancelled ? RunOutcome.Stopped : result.ExitCode == 0 ? RunOutcome.Success : RunOutcome.Failed;
            exitCode = result.ExitCode;
            Status = result.TimedOut ? "Превышено время выполнения" : result.Cancelled ? "Остановлен" : result.ExitCode == 0 ? "Выполнен успешно" : "Не удалось завершить задачу";
            _output.Enqueue(new ScriptOutput("\n" + Status + " · код " + result.ExitCode + " · " + ScriptRunRecord.FormatDuration(result.Duration)) { Kind = ScriptOutputKind.Diagnostic });
        }
        catch (OperationCanceledException) { Status = "Остановлен"; outcome = RunOutcome.Stopped; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { _logService?.Failure("script.run", ex); Error = "Не удалось запустить скрипт.\n" + ex.Message.Trim(); Status = "Не удалось выполнить"; _output.Enqueue(new ScriptOutput(ex.Message, true)); }
        finally
        {
            _elapsed.Stop(); _timer.Stop(); DrainOutput(drainAll: true);
            if (outcome == RunOutcome.Failed && !HasError)
                Error = PolicyBlocked ? "Запуск заблокирован политикой PowerShell.\n" + (Policy?.Details ?? "Откройте раздел «Движок и разрешения».")
                    : Result.Errors.FirstOrDefault()?.Text ?? $"Скрипт завершился с ошибкой (код {exitCode}). Подробности доступны в диагностике запуска.";
            Result.Complete(outcome, Error);
            if (outcome == RunOutcome.Success && Result.HasWarnings) Status = "Выполнено с замечаниями";
            var record = new ScriptRunRecord(_entry.Id, DateTimeOffset.UtcNow, _elapsed.Elapsed, outcome, exitCode) { UsedBypass = bypassOnce };
            LastRun = record;
            try { await _history.RecordAsync(record); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "Результат готов. Не удалось сохранить историю запусков."; }
            _runCancellation.Dispose(); _runCancellation = null;
            IsRunning = false; Raise(nameof(Elapsed));
        }
    }
    public void Stop() { if (IsRunning) { Status = "Останавливаем…"; _runCancellation?.Cancel(); } }
    public void ShowMessage(string message) => Error = message;
    public void ShowNotice(string message) { Error = ""; Notice = message; }
    private void DrainOutput(bool drainAll = false)
    {
        var changed = false;
        for (var i = 0; i < (drainAll ? 2_049 : 300) && _output.TryDequeue(out var line); i++)
        {
            _console.AppendLine((line.IsError ? "! " : line.Kind == ScriptOutputKind.Warning ? "⚠ " : "") + line.Text);
            Result.Accept(line, notify: false); changed = true;
        }
        if (_console.Length > 120_000) _console.Remove(0, _console.Length - 100_000);
        var dropped = Interlocked.Exchange(ref _droppedOutput, 0);
        if (dropped > 0) Result.MarkOmitted(dropped);
        if (changed) { LogText = _console.ToString(); Result.Refresh(); }
    }
    public void Dispose()
    {
        _disposed = true; _inspectCancellation?.Cancel(); _inspectCancellation?.Dispose();
        _runCancellation?.Cancel(); _timer.Stop();
        foreach (var field in _allFields) field.PropertyChanged -= FieldChanged;
    }
}
