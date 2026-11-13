using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Presentation;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

public sealed class ScriptRunWindow : Window
{
    public ScriptRunViewModel ViewModel { get; }
    private readonly StackPanel _fields = Ui.Stack();
    private readonly ContentControl _results = new() { Name = "RunResults", HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly DiagnosticLog _log;
    private readonly Button? _askAi;
    private bool _initializing;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _previewCount;
    private static readonly FontFamily Mono = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas");
    private readonly SelectableTextBlock _console = new() { FontFamily = Mono, FontSize = 11.5, LineHeight = 19, TextWrapping = TextWrapping.Wrap, Margin = new(14, 10), Inlines = new() };
    private readonly ScrollViewer _consoleScroll;
    private readonly Expander _diagnostics;
    private bool _consoleDirty = true;
    public ScriptRunWindow(ScriptEntry entry, string dataRoot, RunHistoryStore history, DiagnosticLog log, Func<ScriptEntry,string,bool>? explainError = null)
    {
        _log = log; ViewModel = new(entry, dataRoot, history, log: log); DataContext = ViewModel;
        _consoleScroll = new ScrollViewer { Content = _console, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Title = entry.DisplayName + " · SC ScriptForge"; Width = 1180; Height = 840; MinWidth = 900; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var engine = new ComboBox { ItemsSource = ViewModel.Engines, HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<PowerShellEngine>((e, _) => Ui.Text(e?.Name ?? "")) };
        engine.Bind(ComboBox.SelectedItemProperty, new Binding("SelectedEngine") { Mode = BindingMode.TwoWay }); engine.Bind(IsEnabledProperty, new Binding("CanEdit"));
        var sets = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        sets.Bind(ItemsControl.ItemsSourceProperty, new Binding("ParameterSets")); sets.Bind(ComboBox.SelectedItemProperty, new Binding("SelectedParameterSet") { Mode = BindingMode.TwoWay });
        sets.Bind(IsVisibleProperty, new Binding("HasParameterSets")); sets.Bind(IsEnabledProperty, new Binding("CanEdit"));
        var remember = new CheckBox { Content = Ui.Text("Запоминать параметры", size: 12), Margin = new(2, 0) };
        ToolTip.SetTip(remember, "Пароли, токены и другие секретные значения не сохраняются.");
        remember.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("RememberParameters") { Mode = BindingMode.TwoWay });
        var run = Ui.Button("Запустить задачу", () => SafeAsync(async () => { await ViewModel.RunAsync(); _log.Event("script.completed"); }), "primary");
        run.Name = "RunStart"; run.MinWidth = 174; run.MinHeight = 42; run.HorizontalContentAlignment = HorizontalAlignment.Center;
        run.Bind(ContentControl.ContentProperty, new Binding("RunButtonText")); run.Bind(IsEnabledProperty, new Binding("CanRun"));
        var stop = Ui.WithIcon(Ui.Action("Остановить", ViewModel.Stop, "ghost"), "stop"); stop.Name = "RunStop";
        stop.Bind(IsVisibleProperty, new Binding("IsRunning")); stop.Bind(IsEnabledProperty, new Binding("IsRunning"));
        var reload = Ui.Button("Обновить параметры", () => SafeAsync(ViewModel.ReloadAsync)); reload.Bind(IsEnabledProperty, new Binding("CanEdit"));
        var bypass = Ui.Button("Только этот раз · Bypass", () => SafeAsync(async () =>
        {
            if (await Dialogs.ConfirmAsync(this, "Разовый запуск с Bypass", "Выбранный скрипт сможет выполнять действия с вашими правами. Ограничение Execution Policy снимается только для одного процесса; это не песочница. Продолжайте, только если доверяете коду.")) await ViewModel.RunAsync(true);
        })); bypass.Bind(IsEnabledProperty, new Binding("CanBypassOnce"));
        var allow = Ui.Button("Разрешить локальные…", () => SafeAsync(async () =>
        {
            if (await Dialogs.ConfirmAsync(this, "Изменить CurrentUser на RemoteSigned?", "Настройка затронет выбранный PowerShell для вашей учётной записи, в том числе вне SC ScriptForge. Групповые политики сохраняют приоритет. Для скачанных скриптов может потребоваться подпись.")) await ViewModel.AllowLocalScriptsAsync();
        })); allow.Bind(IsEnabledProperty, new Binding("CanAllowLocalScripts"));
        var policyBody = Ui.Stack(Ui.Field("PowerShell", engine), reload, Ui.Bound("PolicySummary", "muted"), Ui.Bound("PolicyDetails", "muted"), bypass, allow); policyBody.Spacing = 12;
        var policy = SectionExpander("Движок и разрешения", policyBody); policy.Name = "RunEngineSettings";
        _fields.Spacing = 12;
        var parameterHeading = Ui.Row(Ui.Icon("settings", 16), Ui.Text("Параметры запуска", "section", 14)); parameterHeading.Spacing = 9;
        var parameterBody = Ui.Stack(sets, _fields); parameterBody.Spacing = 16;
        var parameterCard = Feedback.Panel(parameterBody, 16); parameterCard.Name = "RunParameters";
        var localHint = Ui.Text("Выполняется локально с вашими правами.\nЛимит времени — 10 минут.", "muted", 11); localHint.LineHeight = 17; localHint.Margin = new(3, 0);
        var left = Ui.Stack(parameterCard, remember, policy, localHint); left.Spacing = 16;
        var errorText = new SelectableTextBlock { Name = "RunErrorText", TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19 };
        errorText.Bind(SelectableTextBlock.TextProperty, new Binding("Error"));
        Ui.Resource(errorText, TextBlock.ForegroundProperty, "ScDanger");
        var copyError = Ui.WithIcon(Ui.Button("Копировать ошибку", () => SafeAsync(async () =>
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(ViewModel.Error);
        }), "ghost"), "copy"); copyError.Name = "RunCopyError";
        copyError.FontSize = 11; copyError.MinHeight = 28; copyError.Padding = new(8, 4);
        var errorHeading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        errorHeading.Children.Add(Ui.Text("Ошибка запуска", "section", 13)); errorHeading.Children.Add(copyError.Place(0, 1));
        var retry = Ui.Button("Обновить параметры", () => SafeAsync(ViewModel.ReloadAsync)); retry.Bind(IsEnabledProperty, new Binding("CanEdit"));
        var error = Feedback.Callout(Tone.Danger, "failed", Ui.Stack(errorHeading, errorText, retry));
        error.Name = "RunError"; error.Bind(IsVisibleProperty, new Binding("HasError"));
        var notice = Ui.Bound("Notice", "muted"); notice.Bind(IsVisibleProperty, new Binding("HasNotice"));
        var consoleFrame = new Border { Child = _consoleScroll, Height = 240, CornerRadius = new(10), BorderThickness = new(1), ClipToBounds = true };
        Ui.Resource(consoleFrame, Border.BackgroundProperty, "ScCodeBackground"); Ui.Resource(consoleFrame, Border.BorderBrushProperty, "ScBorder");
        var copyLog = Ui.WithIcon(Ui.Button("Копировать журнал", () => SafeAsync(async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(ViewModel.LogText); }), "ghost"), "copy");
        var copy = Ui.WithIcon(Ui.Button("Копировать отчёт", () => SafeAsync(async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(ViewModel.Result.Report()); })), "copy");
        copy.Name = "RunCopyReport"; copy.Classes.Add("ghost"); copy.FontSize = 12; copy.MinHeight = 32; copy.Padding = new(10, 6);
        void UpdateCopy() => copy.IsVisible = ViewModel.Result.State is not RunState.Idle and not RunState.Running && !ViewModel.IsRunning;
        ViewModel.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(ViewModel.LastRun) or nameof(ViewModel.IsRunning)) UpdateCopy(); }; UpdateCopy();
        var folder = Ui.WithIcon(Ui.Button("Папка журнала", () => SafeAsync(() => { if (File.Exists(ViewModel.LogPath)) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(ViewModel.LogPath)!) { UseShellExecute = true }); return Task.CompletedTask; }), "ghost"), "folderopen");
        var logPath = Ui.Bound("LogPath", "muted"); logPath.FontSize = 11;
        var logActions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 }; logActions.Children.Add(folder); logActions.Children.Add(copyLog);
        _diagnostics = SectionExpander("Расширенная диагностика", Ui.Stack(consoleFrame, logPath, logActions)); _diagnostics.Name = "RunDiagnostics";
        _diagnostics.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty && _diagnostics.IsExpanded) RenderConsole(); };
        var right = Ui.Stack(error, notice, _results, _diagnostics); right.Spacing = 16;
        if (explainError is not null)
        {
            _askAi = Ui.Action("Исправить ошибку в SC AI",() =>
            {
                var text = ViewModel.Error + "\n" + string.Join('\n',ViewModel.LogText.Split('\n').Where(line => line.StartsWith("! ")));
                if (text.Length > 12000) text = text[..12000];
                if (explainError(entry,text.Trim())) Close();
            });
            _askAi.IsVisible = false; _askAi.Bind(IsEnabledProperty,new Binding("CanEdit")); right.Children.Add(_askAi);
        }
        var root = new Grid { Name = "RunWorkspace", RowDefinitions = new("Auto,*,Auto"), RowSpacing = 22, Margin = new(28, 24) };
        var title = Ui.Text(entry.DisplayName, "heading", 24); title.Name = "RunTitle"; title.LineHeight = 32;
        var headingCopy = Ui.Stack(Ui.Text("ЗАПУСК СКРИПТА", "eyebrow", 10), title, Ui.Text("Настройте параметры и просмотрите результат выполнения", "muted", 12)); headingCopy.Spacing = 5;
        var header = new Grid { ColumnDefinitions = new("46,*"), ColumnSpacing = 14 };
        var badge = Ui.IconBadge("script", 21); badge.Width = badge.Height = 46; badge.VerticalAlignment = VerticalAlignment.Top; badge.Margin = new(0, 5, 0, 0);
        header.Children.Add(badge); header.Children.Add(headingCopy.Place(0, 1)); root.Children.Add(header);
        var body = new Grid { Name = "RunBody", ColumnDefinitions = new("320,*"), ColumnSpacing = 24 };
        var parameterColumn = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        parameterHeading.VerticalAlignment = VerticalAlignment.Center;
        parameterColumn.Children.Add(new Border { Name = "RunParametersHeading", Child = parameterHeading, MinHeight = 32 });
        parameterColumn.Children.Add(Ui.Scroll(left).Place(1, 0)); body.Children.Add(parameterColumn);
        var resultColumn = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 12 };
        var resultHeading = new Grid { Name = "RunResultsHeading", ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, MinHeight = 32 };
        resultHeading.Children.Add(Ui.Row(Ui.Icon("list", 16), Ui.Text("Результат выполнения", "section", 14)));
        resultHeading.Children.Add(copy.Place(0, 1)); resultColumn.Children.Add(resultHeading);
        var resultScroll = Ui.Scroll(right); resultScroll.Name = "RunResultScroll"; resultColumn.Children.Add(resultScroll.Place(1, 0));
        body.Children.Add(resultColumn.Place(0, 1)); root.Children.Add(body.Place(1, 0));
        var actions = Ui.Row(run, stop); actions.Spacing = 10;
        var status = Ui.Bound("Status", "muted"); status.FontSize = 12; status.TextTrimming = TextTrimming.CharacterEllipsis; status.TextWrapping = TextWrapping.NoWrap; status.MaxWidth = 300;
        var elapsed = Ui.Bound("Elapsed", "mono"); elapsed.FontSize = 11; Ui.Resource(elapsed, TextBlock.ForegroundProperty, "ScMuted");
        var state = Ui.Row(status, elapsed); state.Spacing = 14; state.HorizontalAlignment = HorizontalAlignment.Right;
        var footer = new Grid { Name = "RunFooter", ColumnDefinitions = new("Auto,*"), ColumnSpacing = 20 }; footer.Children.Add(actions); footer.Children.Add(state.Place(0, 1));
        var footerFrame = new Border { Child = footer, Padding = new(0, 16, 0, 0), BorderThickness = new(0, 1, 0, 0) };
        Ui.Resource(footerFrame, Border.BorderBrushProperty, "ScLine"); root.Children.Add(footerFrame.Place(2, 0)); Content = root;
        SizeChanged += (_, e) => body.ColumnDefinitions[0].Width = new GridLength(e.NewSize.Width < 1040 ? 300 : 320);
        ViewModel.PropertyChanged += VmChanged;
        _previewTimer.Tick += (_, _) => { var count = ViewModel.Result.Tables.Sum(t => t.Count) + ViewModel.Result.Messages.Count + ViewModel.Result.Files.Count + ViewModel.Result.ErrorCount; if (count != _previewCount) { _previewCount = count; RenderResults(); } };
        Opened += async (_, _) => { if (_initializing) return; _initializing = true; await SafeAsync(ViewModel.InitializeAsync); RebuildParameters(); };
        Closing += (_, e) => { if (ViewModel.IsRunning || ViewModel.IsChangingPolicy) { e.Cancel = true; ViewModel.ShowNotice("Остановите скрипт или дождитесь завершения операции перед закрытием."); } };
        Closed += (_, _) => { _previewTimer.Stop(); ViewModel.PropertyChanged -= VmChanged; ViewModel.Dispose(); };
        RenderResults();
        Motion.RevealOnOpen(this);
    }
    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { _log.Failure("script.operation", e); var error = OperationError.From(e); ViewModel.ShowMessage(error.Message + " " + error.Action); }
    }
    private void VmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_askAi is not null && e.PropertyName is nameof(ScriptRunViewModel.Error) or nameof(ScriptRunViewModel.LastRun)) _askAi.IsVisible = ViewModel.HasError || ViewModel.LastRun?.Outcome == RunOutcome.Failed;
        if (e.PropertyName is nameof(ScriptRunViewModel.Parameters) or nameof(ScriptRunViewModel.IsLoading)) RebuildParameters();
        if (e.PropertyName == nameof(ScriptRunViewModel.IsRunning)) { if (ViewModel.IsRunning) { _previewCount = -1; _previewTimer.Start(); } else _previewTimer.Stop(); }
        if (e.PropertyName is nameof(ScriptRunViewModel.IsRunning) or nameof(ScriptRunViewModel.LastRun) or nameof(ScriptRunViewModel.Error)) RenderResults();
        if (e.PropertyName == nameof(ScriptRunViewModel.LogText)) { _consoleDirty = true; if (_diagnostics.IsExpanded) RenderConsole(); }
    }
    /// <summary>Colour-coded diagnostic log: errors, warnings, the run header and the exit summary stand out from plain output.</summary>
    private void RenderConsole()
    {
        if (!_consoleDirty) return; _consoleDirty = false;
        var lines = ViewModel.LogText.Replace("\r", "").TrimEnd('\n').Split('\n');
        var inlines = _console.Inlines!; inlines.Clear();
        if (lines.Length == 1 && lines[0].Length == 0) { inlines.Add(ConsoleRun("Журнал появится после запуска задачи.", "ScMuted")); return; }
        var skipped = Math.Max(0, lines.Length - 600);
        if (skipped > 0) inlines.Add(ConsoleRun("… ранние строки скрыты (" + skipped + "), полный журнал — в папке журнала\n", "ScMuted"));
        foreach (var line in lines.Skip(skipped))
        {
            var summary = System.Text.RegularExpressions.Regex.Match(line, @" · код (-?\d+) · ");
            var run = line.StartsWith("! ") ? ConsoleRun("✕ " + line[2..], "ScDanger")
                : line.StartsWith("⚠ ") ? ConsoleRun(line, "ScWarning")
                : line.StartsWith('▶') ? ConsoleRun(line, "ScAccentText", FontWeight.SemiBold)
                : summary.Success ? ConsoleRun(line, summary.Groups[1].Value == "0" ? "ScSuccess" : "ScDanger", FontWeight.SemiBold)
                : line.StartsWith("Режим:") ? ConsoleRun(line, "ScMuted")
                : ConsoleRun(line, "ScSoft");
            inlines.Add(run); inlines.Add(new Avalonia.Controls.Documents.LineBreak());
        }
        Dispatcher.UIThread.Post(() => _consoleScroll.ScrollToEnd(), DispatcherPriority.Background);
    }
    private static Avalonia.Controls.Documents.Run ConsoleRun(string text, string resource, FontWeight? weight = null)
    {
        var run = new Avalonia.Controls.Documents.Run(text); Ui.Resource(run, Avalonia.Controls.Documents.TextElement.ForegroundProperty, resource);
        if (weight.HasValue) run.FontWeight = weight.Value; return run;
    }
    private static Expander SectionExpander(string title, Control content)
    {
        var expander = new Expander
        {
            Header = Ui.Text(title, "section", 12.5),
            Content = new Border { Child = content, Padding = new(14, 12) },
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new(0), CornerRadius = new(10), BorderThickness = new(1)
        };
        Ui.Resource(expander, Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, "ScSurface");
        Ui.Resource(expander, Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty, "ScLine");
        return expander;
    }
    private void RebuildParameters()
    {
        _fields.Children.Clear();
        if (ViewModel.IsLoading) { _fields.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3, MinHeight = 3 }); return; }
        var optional = Ui.Stack(); optional.Spacing = 16;
        foreach (var field in ViewModel.Parameters)
        {
            var control = ParameterEditor(field); if (field.IsPrimary) _fields.Children.Add(control); else optional.Children.Add(control);
        }
        if (optional.Children.Count > 0)
        {
            var expander = SectionExpander("Дополнительно", optional); expander.Name = "RunOptionalParameters";
            expander.Bind(Expander.IsExpandedProperty, new Binding("ShowOptionalParameters") { Mode = BindingMode.TwoWay }); _fields.Children.Add(expander);
        }
        if (ViewModel.HasNoParameters) _fields.Children.Add(Ui.Text("Для этого скрипта параметры не требуются. Можно сразу запускать.", "muted", 12));
        else if (!ViewModel.CanRun && ViewModel.HasError) _fields.Children.Add(Ui.Text("Параметры недоступны. Исправьте ошибку и нажмите «Обновить параметры».", "muted", 12));
    }
    private Control ParameterEditor(ScriptParameterViewModel field)
    {
        var label = Ui.Text(field.Label + (field.IsRequired ? " *" : ""), "section", 12.5); label.FontWeight = FontWeight.SemiBold;
        ToolTip.SetTip(label, field.Caption + (field.DefaultTooltip.Length > 0 ? "\n" + field.DefaultTooltip : ""));
        var customize = Ui.Action("", field.ToggleDefault, "ghost"); customize.Name = "ParameterCustomize"; customize.Tag = field.Name;
        customize.FontSize = 11; customize.MinHeight = 26; customize.Padding = new(7, 4); customize.Bind(ContentControl.ContentProperty, new Binding("DefaultActionText")); customize.Bind(IsVisibleProperty, new Binding("HasOptionalDefault"));
        AutomationProperties.SetName(customize, "Изменить значение: " + field.Label);
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 }; heading.Children.Add(label); heading.Children.Add(customize.Place(0, 1));
        var body = Ui.Stack(heading); body.DataContext = field; body.Spacing = 9;
        if (!string.IsNullOrWhiteSpace(field.Description)) { var help = Ui.Text(field.Description, "muted", 11); help.LineHeight = 17; body.Children.Add(help); }
        var presetValue = Ui.Bound("DefaultSummary", "soft"); presetValue.FontSize = 12; presetValue.LineHeight = 18;
        var preset = new Border
        {
            Name = "ParameterDefaultSummary", Child = Ui.Stack(Ui.Text("По умолчанию", "muted", 10), presetValue).Also(s => s.Spacing = 3),
            Padding = new(10, 8), CornerRadius = new(7)
        };
        Ui.Resource(preset, Border.BackgroundProperty, "ScQuiet"); preset.Bind(IsVisibleProperty, new Binding("UseDefault")); body.Children.Add(preset);
        var editors = Ui.Stack(); editors.Name = "ParameterCustomEditors"; editors.Spacing = 9; editors.Bind(IsVisibleProperty, new Binding("IsEditable"));
        var text = Ui.Input(multiline: field.IsMultiline); if (field.IsSecret) text.PasswordChar = '●';
        AutomationProperties.SetName(text, field.Label);
        text.Bind(TextBox.TextProperty, new Binding("Value") { Mode = BindingMode.TwoWay }); text.Bind(IsVisibleProperty, new Binding(field.IsSecret ? "IsSecretEditor" : "IsTextEditor")); editors.Children.Add(text);
        var boolean = new CheckBox { Content = "Включено" }; boolean.Bind(Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("BooleanValue") { Mode = BindingMode.TwoWay }); boolean.Bind(IsVisibleProperty, new Binding("IsBooleanEditor")); editors.Children.Add(boolean);
        var choice = new ComboBox { ItemsSource = field.Choices, HorizontalAlignment = HorizontalAlignment.Stretch }; choice.Bind(ComboBox.SelectedItemProperty, new Binding("Value") { Mode = BindingMode.TwoWay }); choice.Bind(IsVisibleProperty, new Binding("IsChoiceEditor")); editors.Children.Add(choice);
        var role = new ComboBox { ItemsSource = field.PathRoles, ItemTemplate = new FuncDataTemplate<FilterOption<ParameterRole>>((r, _) => Ui.Text(r?.Name ?? "")), SelectedItem = field.PathRoles.FirstOrDefault(r => r.Value == field.Role), HorizontalAlignment = HorizontalAlignment.Stretch };
        role.SelectionChanged += (_, _) => { if (role.SelectedItem is FilterOption<ParameterRole> value) field.Role = value.Value; };
        var inputSettings = SectionExpander("Тип ввода", Ui.Stack(Ui.Text(field.Caption, "muted", 10), role)); inputSettings.Bind(IsVisibleProperty, new Binding("CanChooseRole"));
        var browse = Ui.WithIcon(Ui.Button("Выбрать путь…", () => SafeAsync(() => BrowseAsync(field)), "ghost"), "folderopen"); browse.MinHeight = 32; browse.HorizontalAlignment = HorizontalAlignment.Left;
        browse.Bind(IsVisibleProperty, new Binding("CanBrowse")); editors.Children.Add(browse); editors.Children.Add(inputSettings); body.Children.Add(editors);
        var error = Ui.Bound("ValidationError"); error.FontSize = 11; Ui.Resource(error, TextBlock.ForegroundProperty, "ScDanger"); error.Bind(IsVisibleProperty, new Binding("HasError")); body.Children.Add(error);
        var container = new Border { Name = "RunParameter", Tag = field.Name, Child = body };
        container.Bind(IsEnabledProperty, new Binding("CanEdit") { Source = ViewModel }); return container;
    }
    private async Task BrowseAsync(ScriptParameterViewModel field)
    {
        if (field.Role is ParameterRole.InputDirectory or ParameterRole.OutputDirectory)
        { var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = field.Label }); if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) field.Value = path; }
        else if (field.Role == ParameterRole.OutputFile)
        { var file = await StorageProvider.SaveFilePickerAsync(new() { Title = field.Label }); if (file?.TryGetLocalPath() is { } path) field.Value = path; }
        else
        { var files = await StorageProvider.OpenFilePickerAsync(new() { Title = field.Label }); if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) field.Value = path; }
    }
    /// <summary>
    /// Result layout: status banner with counters, then errors, highlighted messages (success / warning / failure wording),
    /// data tables, a key-value summary for "Ключ: значение" output, remaining messages and produced files.
    /// </summary>
    private void RenderResults()
    {
        var result = ViewModel.Result; var root = Ui.Stack(); root.Spacing = 14;
        _results.IsVisible = !(ViewModel.HasError && result.State == RunState.Idle);
        if (!_results.IsVisible) return;
        root.Children.Add(Banner(result));
        if (ViewModel.IsRunning) root.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3, MinHeight = 3 });
        if (result.Errors.Count > 0) root.Children.Add(ErrorsPanel(result));

        // "Сохранено: путь" is already shown as a file card.
        var visible = result.Messages.Where(m => !result.Files.Any(f => f.Path.Equals(m.Text, StringComparison.OrdinalIgnoreCase)
            || m.IsLabeled && Path.GetFileName(m.Text.Trim().Trim('"')).Equals(f.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var tone in new[] { MessageTone.Danger, MessageTone.Warning, MessageTone.Success })
            if (visible.Where(m => m.Tone == tone).ToArray() is { Length: > 0 } group) root.Children.Add(HighlightPanel(tone, group));

        foreach (var table in result.Tables) root.Children.Add(TablePanel(table));
        if (visible.Where(m => m.Tone == MessageTone.Neutral && m.IsLabeled).ToArray() is { Length: > 0 } facts) root.Children.Add(FactsPanel(facts));
        if (visible.Where(m => m.Tone == MessageTone.Neutral && !m.IsLabeled).ToArray() is { Length: > 0 } notes) root.Children.Add(NotesPanel(notes));
        if (result.Files.Count > 0) root.Children.Add(FilesPanel(result.Files));
        if (result.HasOmitted) root.Children.Add(Feedback.Callout(Tone.Neutral, "info", Ui.Text(result.OmittedText, "muted", 12), 12));
        _results.Content = root;
    }
    private Control Banner(RunResultPresentation result)
    {
        var (tone, icon) = result.State switch
        {
            RunState.Running => (Tone.Info, "clock"), RunState.Success => (Tone.Success, "ok"), RunState.Warnings => (Tone.Warning, "warning"),
            RunState.Failed => (Tone.Danger, "failed"), RunState.Stopped => (Tone.Neutral, "stopped"), _ => (Tone.Neutral, "play")
        };
        var title = Ui.Text(result.Title, "heading", 18);
        var description = Ui.Text(result.Description, "soft", 12); description.LineHeight = 18;
        var text = Ui.Stack(title, description); text.Spacing = 5;
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        if (result.State != RunState.Idle)
        {
            if (result.ReceivedCount > 0) chips.Children.Add(Feedback.Chip(Feedback.Plural(result.ReceivedCount, "запись", "записи", "записей") + " в результате", Tone.Info, "grid"));
            else if (result.RecordCount != "—") chips.Children.Add(Feedback.Chip(result.RecordCount + " " + result.RecordLabel, Tone.Info, "grid"));
            if (result.ErrorCount > 0) chips.Children.Add(Feedback.Chip(Feedback.Plural(result.ErrorCount, "ошибка", "ошибки", "ошибок"), Tone.Danger, "failed"));
            if (result.HasWarnings) chips.Children.Add(Feedback.Chip(Feedback.Plural(int.Parse(result.WarningCount), "замечание", "замечания", "замечаний"), Tone.Warning, "warning"));
            if (result.Files.Count > 0) chips.Children.Add(Feedback.Chip(Feedback.Plural(result.Files.Count, "файл", "файла", "файлов"), Tone.Success, "document"));
            if (result.Messages.Count > 0) chips.Children.Add(Feedback.Chip(Feedback.Plural(result.Messages.Count, "сообщение", "сообщения", "сообщений"), Tone.Neutral, "comment"));
            if (!ViewModel.IsRunning && ViewModel.LastRun is not null) chips.Children.Add(Feedback.Chip(ViewModel.Elapsed, Tone.Neutral, "clock"));
            if (chips.Children.Count > 0) text.Children.Add(chips.Also(c => c.Margin = new(0, 6, 0, 0)));
        }
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        grid.Children.Add(Feedback.Badge(icon, tone, 16)); grid.Children.Add(text.Place(0, 1));
        var keys = Feedback.Keys(tone);
        var banner = new Border { Child = grid, CornerRadius = new(12), Padding = new(16), BorderThickness = new(1) };
        Ui.Resource(banner, Border.BackgroundProperty, result.State == RunState.Idle ? "ScSurface" : keys.Back);
        Ui.Resource(banner, Border.BorderBrushProperty, result.State == RunState.Idle ? "ScBorder" : keys.Line);
        return banner;
    }
    private static Control PanelHeader(string icon, string title, string? count = null, Tone tone = Tone.Neutral)
    {
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10 };
        row.Children.Add(Feedback.Icon(icon, tone == Tone.Neutral ? Tone.Info : tone, 16));
        var label = Ui.Text(title, "section", 14); label.FontWeight = FontWeight.SemiBold; row.Children.Add(label.Place(0, 1));
        if (count is not null) row.Children.Add(Ui.Text(count, "muted", 11).Place(0, 2));
        return row;
    }
    private static Control ErrorsPanel(RunResultPresentation result)
    {
        var body = Ui.Stack(Feedback.Toned("Ошибки выполнения", Tone.Danger, 14, FontWeight.SemiBold)); body.Spacing = 10;
        foreach (var error in result.Errors.Take(6))
        {
            var item = Ui.Stack(new SelectableTextBlock { Text = error.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13 }); item.Spacing = 4;
            if (error.Details.Length > 0)
            {
                var details = new SelectableTextBlock { Text = error.Details, TextWrapping = TextWrapping.Wrap, FontFamily = Mono, FontSize = 11, Opacity = .85 };
                Ui.Resource(details, TextBlock.ForegroundProperty, "ScMuted"); item.Children.Add(details);
            }
            body.Children.Add(item);
        }
        if (result.ErrorCount > 6) body.Children.Add(Ui.Text("Показаны первые 6 из " + result.ErrorCount + ". Остальные — в расширенной диагностике.", "muted", 11));
        return Feedback.Callout(Tone.Danger, "bug", body);
    }
    private static Control HighlightPanel(MessageTone tone, IReadOnlyList<ResultMessage> messages)
    {
        var (uiTone, icon) = tone switch { MessageTone.Danger => (Tone.Danger, "failed"), MessageTone.Warning => (Tone.Warning, "warning"), _ => (Tone.Success, "ok") };
        var body = Ui.Stack(); body.Spacing = 6;
        foreach (var message in messages.Take(10))
        {
            var line = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Inlines = new() };
            // "Успех: текст" reads better with the label in the tone colour and the rest as normal text.
            if (message.IsLabeled)
            {
                var label = new Avalonia.Controls.Documents.Run(message.Title + "  ") { FontWeight = FontWeight.SemiBold };
                Ui.Resource(label, Avalonia.Controls.Documents.TextElement.ForegroundProperty, Feedback.Keys(uiTone).Fore); line.Inlines!.Add(label);
            }
            line.Inlines!.Add(new Avalonia.Controls.Documents.Run(message.Text)); body.Children.Add(line);
        }
        if (messages.Count > 10) body.Children.Add(Ui.Text("и ещё " + (messages.Count - 10), "muted", 11));
        return Feedback.Callout(uiTone, icon, body, 14);
    }
    private static Control TablePanel(ResultTable table) => new ResultDataView(table);
    private static Control FactsPanel(IReadOnlyList<ResultMessage> facts)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 18, RowSpacing = 8 };
        var row = 0;
        foreach (var fact in facts.Take(40))
        {
            grid.RowDefinitions.Add(new(GridLength.Auto));
            var key = Ui.Text(fact.Title, "muted", 12); key.MaxWidth = 240; key.VerticalAlignment = VerticalAlignment.Top;
            var value = new SelectableTextBlock { Text = fact.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            grid.Children.Add(key.Place(row, 0)); grid.Children.Add(value.Place(row, 1)); row++;
        }
        var body = Ui.Stack(PanelHeader("list", "Сводка", facts.Count > 40 ? "показано 40 из " + facts.Count : null), grid);
        return Feedback.Panel(body, 16);
    }
    private static Control NotesPanel(IReadOnlyList<ResultMessage> notes)
    {
        var list = Ui.Stack(); list.Spacing = 8;
        foreach (var note in notes.Take(40))
        {
            if (note.Text.Contains('\n'))
            {
                // Multi-line output is usually Format-Table text: keep its alignment.
                var block = new SelectableTextBlock { Text = note.Text, FontFamily = Mono, FontSize = 11.5, TextWrapping = TextWrapping.NoWrap, Margin = new(12, 10) };
                var frame = new Border { Child = new ScrollViewer { Content = block, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, CornerRadius = new(10) };
                Ui.Resource(frame, Border.BackgroundProperty, "ScCodeBackground"); list.Children.Add(frame); continue;
            }
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
            var dot = new Border { Width = 5, Height = 5, CornerRadius = new(3), Margin = new(1, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Ui.Resource(dot, Border.BackgroundProperty, "ScAccentText");
            row.Children.Add(dot); row.Children.Add(new SelectableTextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13 }.Place(0, 1));
            list.Children.Add(row);
        }
        if (notes.Count > 40) list.Children.Add(Ui.Text("и ещё " + (notes.Count - 40) + " — в расширенной диагностике", "muted", 11));
        return Feedback.Panel(Ui.Stack(PanelHeader("comment", "Сообщения задачи", notes.Count > 1 ? notes.Count.ToString() : null), list), 16);
    }
    private Control FilesPanel(IEnumerable<ResultFile> files)
    {
        var list = Ui.Stack(); list.Spacing = 8;
        foreach (var file in files)
        {
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
            row.Children.Add(Feedback.Badge("document", Tone.Success, 15));
            var name = Ui.Text(file.Name, null, 13); name.FontWeight = FontWeight.SemiBold;
            var folder = Ui.Text(file.Folder, "muted", 11); folder.TextTrimming = TextTrimming.CharacterEllipsis; folder.TextWrapping = TextWrapping.NoWrap;
            row.Children.Add(Ui.Stack(name, folder).Also(s => s.Spacing = 2).Place(0, 1));
            row.Children.Add(Ui.WithIcon(Ui.Button("Показать в папке", () => SafeAsync(() =>
            {
                var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; info.ArgumentList.Add("/select,"); info.ArgumentList.Add(file.Path); Process.Start(info); return Task.CompletedTask;
            }), "ghost"), "folderopen").Place(0, 2));
            list.Children.Add(row);
        }
        return Feedback.Panel(Ui.Stack(PanelHeader("folder", "Готовые файлы", null, Tone.Success), list), 16);
    }
}
