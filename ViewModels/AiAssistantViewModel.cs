using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text;
using SC.ScriptForge.Core;
using SC.ScriptForge.Infrastructure;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using ShmonovStudio.AI;

namespace SC.ScriptForge.ViewModels;

/// <summary>A display-only exchange. Earlier exchanges never retain an applicable proposal.</summary>
public sealed class AiChatTurn(string prompt, string context, string task) : ObservableObject
{
    private string _answer = "", _completion = "Готовлю ответ…";
    private bool _finished;
    public string Prompt { get; } = prompt;
    public string Context { get; } = context;
    public string Task { get; } = task;
    public string Answer { get => _answer; set => Set(ref _answer,value); }
    public string Completion { get => _completion; set => Set(ref _completion,value); }
    public bool Finished { get => _finished; set => Set(ref _finished,value); }
}

public sealed class AiAssistantViewModel : ObservableObject, IDisposable
{
    private readonly IAiRuntime _runtime;
    private readonly ScriptAiService _service;
    private readonly AiSettingsStore _settings;
    private readonly DiagnosticLog _log;
    private readonly List<AiMessage> _conversation = [];
    private readonly List<AiMessage> _fixHistory = [];
    private string _lastFixError = "";
    private CancellationTokenSource? _generation, _preparation;
    private AiScriptContext? _context;
    private AiTaskDefinition _task = AiTaskDefinition.All[0];
    private ScriptEntry? _entry;
    private AiContextChoice? _scope;
    private ScriptVersion? _version;
    private AiProposal? _proposal;
    private AiNewScript? _newScript;
    private AiChatTurn? _currentTurn;
    private string _instruction = "", _error = "", _answer = "", _draft = "", _status = "Опишите задачу — SC AI сам определит, что нужно сделать", _contextStatus = "Без скрипта · общий вопрос PowerShell";
    private bool _busy, _preparing, _disposed, _applying, _auto = true;
    private int _firstLine, _lastLine;
    public LibraryViewModel Library { get; }
    public ObservableCollection<AiChatTurn> Turns { get; } = [];
    public bool HasTurns => Turns.Count > 0;
    public AiAssistantViewModel(IAiRuntime runtime, ScriptAiService service, AiSettingsStore settings, LibraryViewModel library, DiagnosticLog log)
    { _runtime = runtime; _service = service; _settings = settings; Library = library; _log = log; _runtime.StateChanged += RuntimeChanged; Library.PropertyChanged += LibraryChanged; }
    private void LibraryChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(Library.Entries) && !IsBusy) SyncEntry(); }
    private void SyncEntry()
    {
        if (_entry is null) return;
        var current = Library.Entries.FirstOrDefault(e => e.Id == _entry.Id);
        if (current is null) SelectedEntry = null;
        else { _entry = current; Raise(nameof(SelectedEntry)); Raise(nameof(ContextLabel)); }
    }
    public Func<Guid, bool> IsScriptRunning { get; set; } = _ => false;
    public AiRuntimeOptions Options => _settings.Load();
    public static AiHardwareProfile Hardware => AiHardwareProfile.Current;
    public bool IsExternal => Options.IsExternal;
    public string ModelLabel
    {
        get
        {
            var options = Options;
            if (options.IsExternal) return string.IsNullOrWhiteSpace(options.ExternalModel) ? "Сервер ИИ не настроен" : options.ExternalModel;
            return File.Exists(options.ModelPath) ? Path.GetFileNameWithoutExtension(options.ModelPath) : "Модель не установлена";
        }
    }
    public bool HasModel { get { var options = Options; return options.IsExternal ? !string.IsNullOrWhiteSpace(options.ExternalModel) && !string.IsNullOrWhiteSpace(options.Endpoint) : File.Exists(options.ModelPath); } }
    public string ModelDisplayName => HasModel ? ModelLabel.Replace("-Q4_K_M", "", StringComparison.OrdinalIgnoreCase) : "Подключить модель";
    public string ModelStateLabel => !HasModel ? "Установка и подключение" : IsExternal ? (IsModelLoaded ? "Сервер · подключено" : "Сервер · " + OpenAiCompatibleRuntime.Host(Options.Endpoint)) : (IsModelLoaded ? "В памяти · " : "На устройстве · ") + Options.ComputeLabel;
    public string RuntimeLabel => _runtime.State.Message;
    public bool IsModelLoaded => _runtime.State.Loaded;
    public IReadOnlyList<AiTaskDefinition> Tasks => AiTaskDefinition.All;
    public IReadOnlyList<AiContextChoice> Scopes => _context?.Choices ?? [];
    public IReadOnlyList<ScriptVersion> Versions => _context?.Versions ?? [];

    /// <summary>Ручной выбор задачи. Сбрасывает автоопределение до следующей отправки.</summary>
    public AiTaskDefinition SelectedTask
    {
        get => _task;
        set
        {
            if (value is null || _busy) return;
            var changed = Set(ref _task, value); var wasAuto = _auto; _auto = false;
            if (changed || wasAuto) { _conversation.Clear(); ClearResult(); RaiseIntent(); }
        }
    }
    /// <summary>Автоматически определять задачу по тексту запроса.</summary>
    public bool AutoDetect { get => _auto; set { if (!_busy && Set(ref _auto, value)) RaiseIntent(); } }
    /// <summary>Скрипт с ошибкой (вставленной или найденной Parser) без текста запроса — исправляем его, а не просто объясняем.</summary>
    public AiTaskDefinition DetectedTask => string.IsNullOrWhiteSpace(Instruction) && (HasErrorText || SelectedEntry is not null && HasParserErrors)
        ? AiTaskDefinition.Get(SelectedEntry is null ? "error" : "fix")
        : AiTaskDefinition.Get(AiIntentRouter.Detect(Instruction, SelectedEntry is not null, HasNewScript || !string.IsNullOrEmpty(_lastDraft)));
    public bool HasParserErrors => _context is { Inspection.Errors.Length: > 0 };
    public AiTaskDefinition EffectiveTask => _auto ? DetectedTask : _task;
    public string IntentLabel => EffectiveTask.Id == "create" && !string.IsNullOrEmpty(_lastDraft) && SelectedEntry is null ? "Доработать черновик" : EffectiveTask.Name;
    public string IntentIcon => EffectiveTask.Icon;
    private void RaiseIntent() { Raise(nameof(AutoDetect)); Raise(nameof(EffectiveTask)); Raise(nameof(IntentLabel)); Raise(nameof(IntentIcon)); Raise(nameof(IsErrorTask)); Raise(nameof(IsVersionTask)); Raise(nameof(QueryHint)); }
    public bool IsErrorTask => EffectiveTask.Id == "error";
    public bool IsVersionTask => EffectiveTask.Id == "changes";
    public ScriptEntry? SelectedEntry
    {
        get => _entry;
        set
        {
            if (_busy) return;
            // После обновления библиотеки тот же скрипт приходит новым объектом — это не смена контекста.
            if (value is not null && _entry is not null && value.Id == _entry.Id)
            { if (!ReferenceEquals(value,_entry)) { _entry = value; Raise(nameof(SelectedEntry)); Raise(nameof(ContextLabel)); } return; }
            if (Set(ref _entry, value)) { ResetTurns(); ClearResult(); Raise(nameof(ContextLabel)); Raise(nameof(ScopeLabel)); Raise(nameof(HasScript)); RaiseIntent(); Preparation = PrepareAsync(); }
        }
    }
    public bool HasScript => SelectedEntry is not null;
    public Task Preparation { get; private set; } = Task.CompletedTask;
    public AiContextChoice? SelectedScope { get => _scope; set { if (Set(ref _scope, value)) { _conversation.Clear(); ClearProposal(); Raise(nameof(ScopeLabel)); } } }
    public ScriptVersion? SelectedVersion { get => _version; set => Set(ref _version, value); }
    public int FirstLine { get => _firstLine; set { if (Set(ref _firstLine,value)) { _conversation.Clear(); ClearProposal(); Raise(nameof(ScopeLabel)); } } }
    public int LastLine { get => _lastLine; set { if (Set(ref _lastLine,value)) { _conversation.Clear(); ClearProposal(); Raise(nameof(ScopeLabel)); } } }
    public string ContextLabel => SelectedEntry?.Name ?? "Прикрепить скрипт";
    public string ScopeLabel => SelectedEntry is null ? "Общий вопрос" : FirstLine > 0 || LastLine > 0 ? $"Строки {FirstLine}–{LastLine}" : SelectedScope?.Name ?? "Читаем структуру…";
    public string Instruction { get => _instruction; set { if (Set(ref _instruction, value ?? "") && _auto) RaiseIntent(); } }
    public string ErrorText { get => _error; set { if (Set(ref _error, value ?? "")) { Raise(nameof(HasErrorText)); if (_auto) RaiseIntent(); } } }
    public bool HasErrorText => !string.IsNullOrWhiteSpace(ErrorText);
    public string Answer { get => _answer; private set { if (Set(ref _answer, value) && _currentTurn is { Finished: false }) _currentTurn.Answer = value; } }
    public string Draft { get => _draft; private set { if (Set(ref _draft, value)) Raise(nameof(HasDraft)); } }
    public bool HasDraft => !string.IsNullOrEmpty(Draft);
    public string QueryHint => SelectedEntry is not null
        ? "Что сделать со скриптом? Например: «объясни», «найди проблемы», «добавь логирование»…"
        : !string.IsNullOrEmpty(_lastDraft) ? "Что изменить в черновике? Например: «добавь параметр для пути»…"
        : "Опишите задачу: «напиши скрипт, который…» или задайте вопрос по PowerShell";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ContextStatus { get => _contextStatus; private set => Set(ref _contextStatus, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy,value)) { Raise(nameof(CanGenerate)); Raise(nameof(CanEditContext)); Raise(nameof(CanApply)); Raise(nameof(CanSaveNewScript)); Raise(nameof(HasSuggestedFix)); } } }
    public bool IsPreparing { get => _preparing; private set { if (Set(ref _preparing,value)) { Raise(nameof(CanGenerate)); Raise(nameof(CanEditContext)); } } }
    public bool CanGenerate => !IsBusy && !IsPreparing;
    public bool IsApplying { get => _applying; private set => Set(ref _applying,value); }
    public bool CanEditContext => !IsBusy;
    public AiProposal? Proposal { get => _proposal; private set { if (Set(ref _proposal,value)) { Raise(nameof(HasProposal)); Raise(nameof(HasSuggestedFix)); Raise(nameof(CanApply)); Raise(nameof(Diff)); Raise(nameof(Validation)); } } }
    public bool HasProposal => Proposal is not null;
    public bool CanApply => !IsBusy && Proposal?.IsValid == true;
    public string Diff => Proposal?.Diff ?? "";
    public string Validation => Proposal?.Validation ?? NewScript?.Validation ?? "";
    /// <summary>Новый скрипт от ИИ, ещё не сохранённый в библиотеку.</summary>
    public AiNewScript? NewScript { get => _newScript; private set { if (Set(ref _newScript,value)) { Raise(nameof(HasNewScript)); Raise(nameof(CanSaveNewScript)); Raise(nameof(Validation)); RaiseIntent(); } } }
    public bool HasNewScript => NewScript is not null;
    public bool CanSaveNewScript => !IsBusy && NewScript is not null;
    private string _lastDraft = "";
    public event Action<ScriptEntry>? ScriptCreated;
    private void ClearProposal() { Proposal = null; Draft = ""; }
    private void ClearResult() { ClearProposal(); Answer = ""; Status = "Готов к новому запросу"; }
    private void RuntimeChanged(AiRuntimeState state)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_disposed) return; Raise(nameof(RuntimeLabel)); Raise(nameof(IsModelLoaded)); Raise(nameof(ModelStateLabel)); if (state.Loading) Status = state.Message; });
    }
    public async Task PrepareAsync()
    {
        _preparation?.Cancel(); using var cancellation = new CancellationTokenSource(); _preparation = cancellation;
        IsPreparing = true; _context = null; FirstLine = 0; LastLine = 0; SelectedScope = null; SelectedVersion = null;
        try
        {
            if (SelectedEntry is { } entry)
            {
                ContextStatus = "Читаем структуру скрипта · код не выполняется…";
                var context = await _service.ContextAsync(entry,cancellation.Token); cancellation.Token.ThrowIfCancellationRequested();
                _context = context; SelectedScope = context.Choices[0]; SelectedVersion = context.Versions.Skip(1).FirstOrDefault() ?? context.Versions.FirstOrDefault();
                ContextStatus = $"{entry.Name} · {context.Snapshot.Code.Count(c => c == '\n') + 1} строк · функций {context.Inspection.Functions.Length}";
                if (context.Inspection.Errors.Length > 0)
                {
                    ContextStatus += $" · ошибок Parser: {context.Inspection.Errors.Length}";
                    Status = "В скрипте есть синтаксические ошибки. Нажмите «Отправить» — SC AI исправит их и предложит сохранить.";
                }
            }
            else ContextStatus = "Без скрипта · общий вопрос PowerShell";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _log.Failure("ai.context",e); ContextStatus = e.Message; }
        finally { if (ReferenceEquals(_preparation,cancellation)) { _preparation = null; IsPreparing = false; Raise(nameof(Scopes)); Raise(nameof(Versions)); Raise(nameof(HasParserErrors)); RaiseIntent(); } }
    }
    /// <summary>
    /// Ошибка синтаксиса или запуска из другого окна: SC AI сам исправляет код, показывает изменения
    /// и спрашивает, сохранять ли их. Без подключённой модели только готовит запрос.
    /// </summary>
    public async Task FixAsync(ScriptEntry entry, string error)
    {
        if (IsBusy) return;
        // Тот же скрипт перечитываем: файл мог измениться после прошлого чтения.
        var same = _entry?.Id == entry.Id; SelectedEntry = entry; if (same) Preparation = PrepareAsync(); await Preparation;
        if (IsBusy) return;
        SelectedTask = AiTaskDefinition.Get("fix"); Instruction = ""; ErrorText = error;
        if (HasModel) await GenerateAsync();
        else Status = "Подключите модель вверху справа и нажмите «Отправить» — SC AI исправит ошибку и предложит сохранить.";
    }
    /// <summary>Отправить текст сразу, с автоопределением задачи (например, из быстрого поля на главной).</summary>
    public Task AskAsync(string text) { if (!CanGenerate) return Task.CompletedTask; AutoDetect = true; Instruction = text; return GenerateAsync(); }
    public Task GenerateAsync() => GenerateCoreAsync(null,null,null);

    /// <summary>
    /// Ответ содержит исправление для прикреплённого скрипта (разбор ошибки, поиск проблем, вопрос),
    /// но сам файл не меняет. Пользователь видит вопрос «Применить к скрипту?».
    /// </summary>
    public bool HasSuggestedFix => _suggestedFix.Length > 0 && SelectedEntry is not null && !IsBusy && Proposal is null;
    private string _suggestedFix = "";
    private void SetSuggestedFix(string value) { _suggestedFix = value; Raise(nameof(HasSuggestedFix)); }
    private static bool ContainsFix(string answer) => answer.Contains("```", StringComparison.Ordinal) || System.Text.RegularExpressions.Regex.IsMatch(answer, "исправлен|замени|исправь|следует заменить|нужно изменить|fix", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    /// <summary>Превращает исправление из ответа в настоящее предложение: Diff и вопрос о сохранении.</summary>
    public Task ApplySuggestedFixAsync()
    {
        if (!HasSuggestedFix || !CanGenerate) return Task.CompletedTask;
        var answer = _suggestedFix.Length > 6000 ? _suggestedFix[..6000] : _suggestedFix;
        var instruction = "Внеси в код исправление, предложенное в предыдущем ответе. Меняй только то, что нужно для исправления, остальной код сохрани без изменений.\nПредыдущий ответ (данные):\n" + answer;
        return GenerateCoreAsync(AiTaskDefinition.Get("complete"),instruction,"Применить предложенное исправление к скрипту");
    }
    public void DismissSuggestedFix() { SetSuggestedFix(""); Status = "Хорошо, файл не меняем."; }
    public void DiscardProposal() { if (IsBusy || Proposal is null) return; ClearProposal(); Status = "Изменения не сохранены. Файл остался прежним."; }

    private async Task GenerateCoreAsync(AiTaskDefinition? forced,string? instructionOverride,string? display)
    {
        if (!CanGenerate) return;
        using var cancellation = new CancellationTokenSource(); _generation = cancellation; IsBusy = true; ClearResult(); SetSuggestedFix("");
        var task = forced ?? EffectiveTask; var context = _context; var scope = SelectedScope; var firstLine = FirstLine; var lastLine = LastLine;
        var instruction = instructionOverride ?? Instruction; _currentTurn = null;
        // Текст ошибки можно вставить прямо в сообщение — отдельное поле не нужно.
        var error = task.Id == "error" && string.IsNullOrWhiteSpace(ErrorText) ? "" : ErrorText;
        if (task.Id == "fix")
        {
            if (string.IsNullOrWhiteSpace(error)) error = _lastFixError;
            else _lastFixError = error;
        }
        // «Добавь параметр…» после генерации дорабатывает черновик; «напиши новый…» начинает заново.
        var draft = task.Change == AiChangeKind.NewScript && _lastDraft.Length > 0 && !AiIntentRouter.IsFreshRequest(instruction) ? _lastDraft : null;
        // Черновик с ошибками Parser: модель должна знать о них при доработке.
        if (draft is not null && NewScript is { SyntaxValid: false } broken) error = (error + "\n" + broken.Validation).Trim();
        try
        {
            if (task.NeedsScript && SelectedEntry is not null && context is null) throw new InvalidOperationException("Контекст недоступен. Обновите его или выберите другой скрипт.");
            var options = Options; options.Validate();
            if (task.Id == "fix" && SelectedEntry is { } currentEntry)
            {
                // The source can change between attempts or after a manual edit. Always diagnose the current file.
                var fresh = await _service.ContextAsync(currentEntry, cancellation.Token);
                scope = fresh.Choices.FirstOrDefault(c => c.Name == scope?.Name) ?? fresh.Choices[0];
                _context = context = fresh; _scope = scope;
                Raise(nameof(Scopes)); Raise(nameof(SelectedScope)); Raise(nameof(HasParserErrors));
            }
            var maximum = options.MaximumOutputTokens;
            // Новому скрипту нужно больше места, чем короткому ответу.
            if (task.Change is AiChangeKind.NewScript or AiChangeKind.Code) maximum = Math.Max(maximum, options.IsExternal ? 4096 : Math.Min(4096, options.ContextTokens / 2));
            var request = await _service.RequestAsync(task,context,scope,firstLine,lastLine,instruction,error,SelectedVersion,maximum,task.Id == "fix" ? _fixHistory : _conversation,cancellation.Token,draft);
            var contextLabel = task.Id == "create" ? (draft is null ? "Новый скрипт" : "Доработка черновика") : SelectedEntry is null ? "PowerShell · без скрипта" : ContextLabel + " · " + ScopeLabel;
            _currentTurn = new(display ?? (string.IsNullOrWhiteSpace(instruction) ? task.Name : instruction.Trim()), contextLabel, task.Name);
            Turns.Add(_currentTurn); while (Turns.Count > 8) Turns.RemoveAt(0); Raise(nameof(HasTurns)); if (instructionOverride is null) Instruction = "";
            if (error.Length > 0) ErrorText = "";
            Status = options.IsExternal ? "Запрос отправлен на сервер ИИ…" : "Запрос выполняется локально…"; var watch = Stopwatch.StartNew();
            var result = ScriptAiService.Parse(await StreamAsync(request,json =>
            {
                Answer = StreamingJson.StringProperty(json,"answer"); Draft = StreamingJson.StringProperty(json,"replacement");
                Status = (task.Change == AiChangeKind.NewScript ? "Пишу скрипт · " : task.Id == "fix" ? "Исправляю код · " : "Формирую ответ · ") + watch.Elapsed.ToString(@"mm\:ss");
            },cancellation.Token));
            Answer = result.Answer;
            // Код, который не проходит Parser, SC AI исправляет сам, прежде чем предложить сохранение.
            AiNewScript? script = null; AiProposal? proposal = null; var repairs = 0; var notes = new List<string>();
            while (true)
            {
                if (task.Change == AiChangeKind.NewScript) script = await _service.DraftAsync(result,cancellation.Token);
                else proposal = await _service.ProposeAsync(task,context,scope,firstLine,lastLine,result,cancellation.Token);
                if (script is not { SyntaxValid: false } && proposal is not { IsValid: false } || repairs == MaximumRepairs) break;
                var repair = await _service.RepairRequestAsync(task,context,scope,firstLine,lastLine,result.Replacement,maximum,cancellation.Token,proposal?.DependencyErrors,error,instruction);
                if (repair is null) break;
                repairs++; var attempt = repairs;
                var repaired = ScriptAiService.Parse(await StreamAsync(repair,json =>
                {
                    Draft = StreamingJson.StringProperty(json,"replacement");
                    Status = $"Проверка кода нашла ошибки — исправляю (попытка {attempt} из {MaximumRepairs}) · " + watch.Elapsed.ToString(@"mm\:ss");
                },cancellation.Token));
                if (string.IsNullOrWhiteSpace(repaired.Replacement)) break;
                if (!string.IsNullOrWhiteSpace(repaired.Answer)) notes.Add(repaired.Answer.Trim());
                result = result with { Replacement = repaired.Replacement };
            }
            if (repairs > 0)
            {
                var valid = script?.SyntaxValid ?? proposal?.IsValid ?? true;
                Answer = (result.Answer.TrimEnd() + "\n\n**Проверка синтаксиса и вызовов:** " + (valid
                    ? "предварительная проверка первого варианта нашла ошибки — модель доработала предложение. " + string.Join(' ',notes)
                    : "после трёх вариантов проверка всё ещё находит ошибки. Предложение нельзя сохранить; подробности ниже.")).Trim();
            }
            if (task.Change == AiChangeKind.NewScript)
            {
                NewScript = script; Draft = script!.Code; _lastDraft = script.Code;
                if (string.IsNullOrWhiteSpace(Answer)) Answer = script.Summary.Length > 0 ? script.Summary : "Скрипт готов.";
                if (_currentTurn is not null) _currentTurn.Answer = Answer;
            }
            else { Proposal = proposal; if (proposal is null) Draft = ""; }
            if (task.Id == "fix")
            {
                static string Bounded(string value) => value.Length > 1400 ? value[..1400] + "…" : value;
                _fixHistory.Clear();
                _fixHistory.Add(new("user", Bounded("Предыдущая попытка исправления этого же скрипта. Задача: " + instruction + "\nОшибка выполнения (данные): " + error)));
                _fixHistory.Add(new("assistant", Bounded("Предыдущее предложение (сам факт применения и успех запуска не подтверждены):\n" + result.Answer + "\n" + result.Replacement)));
            }
            if (task.Change is AiChangeKind.None or AiChangeKind.NewScript)
            {
                _conversation.Add(new("user",string.IsNullOrWhiteSpace(instruction) ? task.Name : instruction)); _conversation.Add(new("assistant",result.Answer));
                if (_conversation.Count > 4) _conversation.RemoveRange(0,_conversation.Count - 4);
            }
            Status = NewScript is not null && task.Change == AiChangeKind.NewScript ? (NewScript.SyntaxValid ? "Скрипт готов. Посмотрите код и сохраните в библиотеку." : "Скрипт готов, но Parser нашёл ошибки — напишите «исправь ошибки».")
                : Proposal is null ? (task.Id == "fix" ? "Модель не предложила изменений. Причина ошибки выполнения не подтверждена; проверьте объяснение." : "Готово · " + watch.Elapsed.ToString(@"mm\:ss"))
                : Proposal.IsValid ? (task.Id == "fix" ? "Исправление прошло предварительную проверку. Сохранить изменения? Выполнение ещё не проверялось." : "Сохранить изменения в скрипт? Посмотрите их и подтвердите.")
                : "Проверка предложения обнаружила ошибки; сохранение заблокировано. Подробности — в проверке кода.";
            if (task.Id is "error" or "audit" or "ask" && context is not null && ContainsFix(result.Answer))
            { SetSuggestedFix(result.Answer); Status = "SC AI предложил исправление. Применить его к скрипту?"; }
            _currentTurn!.Completion = "Готово · " + watch.Elapsed.ToString(@"mm\:ss");
            _log.Event("ai.completed");
        }
        catch (OperationCanceledException) { ClearProposal(); Status = "Генерация остановлена. Изменения не применены."; if (_currentTurn is not null) _currentTurn.Completion = "Остановлено · ответ неполный"; }
        catch (Exception e) { ClearProposal(); _log.Failure("ai.generate",e); Status = e.Message; if (_currentTurn is not null) _currentTurn.Completion = e.Message; }
        finally
        {
            if (_currentTurn is not null) _currentTurn.Finished = true; if (ReferenceEquals(_generation,cancellation)) _generation = null;
            IsBusy = false; if (!_auto) { _auto = true; RaiseIntent(); } SyncEntry();
            if (NewScript is not null) Draft = NewScript.Code;
        }
    }
    private const int MaximumRepairs = 2;
    /// <summary>Читает поток модели целиком; <paramref name="project"/> не чаще раза в 65 мс показывает частичный ответ.</summary>
    private async Task<string> StreamAsync(AiRequest request,Action<string> project,CancellationToken cancellationToken)
    {
        var text = new StringBuilder(); var watch = Stopwatch.StartNew(); var update = -100L; var truncated = false;
        await foreach (var delta in _runtime.GenerateAsync(request,cancellationToken))
        {
            text.Append(delta.Text); truncated |= delta.Truncated;
            if (watch.ElapsedMilliseconds - update > 65 || delta.Finished) { project(text.ToString()); update = watch.ElapsedMilliseconds; }
        }
        if (truncated) throw new InvalidDataException("Ответ не поместился в лимит. Увеличьте «Длину ответа» в настройках модели или сократите запрос. Незавершённые изменения не применяются.");
        return text.ToString();
    }
    public async Task ApplyAsync(AiProposal reviewed)
    {
        if (!CanApply || !ReferenceEquals(reviewed,Proposal)) return;
        if (IsScriptRunning(reviewed.Snapshot.Id)) { Status = "Остановите скрипт перед применением изменений."; return; }
        IsBusy = true; IsApplying = true;
        try
        {
            var entry = await _service.ApplyAsync(reviewed); Proposal = null; Draft = "";
            await Library.RefreshAsync(entry.Id); _entry = Library.Entries.FirstOrDefault(e => e.Id == entry.Id) ?? entry; Raise(nameof(SelectedEntry)); Raise(nameof(ContextLabel)); await PrepareAsync();
            Status = reviewed.Code is null ? "Сведения сохранены." : "Код сохранён новой версией. Скрипт не запускался.";
        }
        catch (Exception e) { _log.Failure("ai.apply",e); Status = e.Message; }
        finally { IsApplying = false; IsBusy = false; }
    }
    public async Task<ScriptEntry?> SaveNewScriptAsync(AiNewScript script, string name, string category, string description, IEnumerable<string> tags)
    {
        if (!CanSaveNewScript || !ReferenceEquals(script,NewScript)) return null;
        IsBusy = true;
        try
        {
            var entry = await _service.CreateScriptAsync(script,name,category,description,tags);
            await Library.RefreshAsync(entry.Id); NewScript = null; Draft = ""; _lastDraft = "";
            Status = $"«{entry.Name}» сохранён в библиотеку. Скрипт не запускался."; _log.Event("ai.script.created");
            ScriptCreated?.Invoke(entry); return entry;
        }
        catch (Exception e) { _log.Failure("ai.create",e); Status = e.Message; return null; }
        finally { IsBusy = false; RaiseIntent(); }
    }
    public void Cancel() => _generation?.Cancel();
    private void ResetTurns() { _conversation.Clear(); _fixHistory.Clear(); _lastFixError = ""; _currentTurn = null; Turns.Clear(); _lastDraft = ""; NewScript = null; SetSuggestedFix(""); Raise(nameof(HasTurns)); }
    public void NewConversation() { if (IsBusy) return; ResetTurns(); Instruction = ""; ErrorText = ""; ClearResult(); AutoDetect = true; Status = "Опишите задачу — SC AI сам определит, что нужно сделать"; }
    public async Task UnloadAsync() { if (IsBusy) return; await _runtime.UnloadAsync(); }
    public void SaveOptions(AiRuntimeOptions options)
    {
        if (IsBusy) throw new InvalidOperationException("Остановите генерацию.");
        options.Validate(); _settings.Save(options); _runtime.Configure(options);
        RaiseOptions();
    }
    public async Task ActivateInstalledAsync(AiRuntimeOptions options, CancellationToken cancellationToken = default)
    {
        if (IsBusy) throw new InvalidOperationException("Остановите генерацию.");
        options.Validate(); var previous = Options;
        _runtime.Configure(options);
        try
        {
            await _runtime.PrepareAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _settings.Save(options);
        }
        catch { _runtime.Configure(previous); throw; }
        finally { RaiseOptions(); }
    }
    private void RaiseOptions()
    {
        foreach (var name in new[] { nameof(Options),nameof(IsExternal),nameof(ModelLabel),nameof(HasModel),nameof(ModelDisplayName),nameof(ModelStateLabel),nameof(RuntimeLabel),nameof(IsModelLoaded) }) Raise(name);
    }
    public void Dispose() { _disposed = true; _generation?.Cancel(); _preparation?.Cancel(); _runtime.StateChanged -= RuntimeChanged; Library.PropertyChanged -= LibraryChanged; _runtime.Dispose(); }
}
