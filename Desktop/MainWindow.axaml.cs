using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

public sealed partial class MainWindow : Window
{
    private readonly ScriptRepository _repository;
    private readonly LibraryViewModel _library;
    private readonly ComputerViewModel _computer;
    private readonly PreferencesStore _store;
    private readonly AppPreferences _preferences;
    private readonly RunHistoryStore _history;
    private readonly DiagnosticLog _log;
    private readonly AiAssistantViewModel _ai;
    private readonly AiSettingsStore _aiSettings;
    private readonly DeletionHistory _deletions;
    private readonly Dictionary<Guid, ScriptRunWindow> _runs = new();
    private readonly Dictionary<string, Button> _nav = new();
    private LibraryWatcher? _watcher;
    private string _section = "home";
    private string _inventoryTab = "overview";
    private bool _busy, _closed;
    private bool _compact;
    private readonly TransitioningContentControl _workspace;
    private readonly CrossFade _navigationTransition = new(TimeSpan.FromMilliseconds(340)) { FadeInEasing = new CubicEaseOut(), FadeOutEasing = new CubicEaseOut() };
    private readonly CrossFade _instantTransition = new(TimeSpan.Zero);
    private readonly Dictionary<string, string> _sectionTitles = new() { ["trash"] = "Корзина" };
    public LibraryViewModel Library => _library;
    public MainWindow(ScriptRepository repository, LibraryViewModel library, ComputerViewModel computer, PreferencesStore store,
        AppPreferences preferences, RunHistoryStore history, DiagnosticLog log, IPrivilegeService privilege, AiAssistantViewModel ai, AiSettingsStore aiSettings)
    {
        AvaloniaXamlLoader.Load(this);
        _repository = repository; _library = library; _computer = computer; _store = store; _preferences = preferences; _history = history; _log = log;
        LibraryLocalization.Language = preferences.LibraryLanguage;
        _ai = ai; _aiSettings = aiSettings; _ai.IsScriptRunning = id => _runs.TryGetValue(id,out var run) && (run.ViewModel.IsRunning || run.ViewModel.IsChangingPolicy);
        _deletions = new(repository);
        _workspace = this.FindControl<TransitioningContentControl>("Workspace")!;
        Width = double.IsFinite(preferences.Width) ? Math.Clamp(preferences.Width, 960, 1920) : 1370;
        Height = double.IsFinite(preferences.Height) ? Math.Clamp(preferences.Height, 640, 1200) : 880;
        if (preferences.Maximized) WindowState = WindowState.Maximized;
        if (preferences.DesignVersion < 2) { preferences.Theme = "Light"; preferences.DesignVersion = 2; }
        ApplyTheme();
        PropertyChanged += (_, args) => { if (args.Property.Name == nameof(ActualThemeVariant)) UpdateThemeButton(); };
        Ui.ReduceMotion = preferences.ReduceMotion;
        SizeChanged += (_, e) =>
        {
            this.FindControl<Border>("TitleStatus")!.IsVisible = e.NewSize.Width >= 1180;
            this.FindControl<Button>("TitleSearch")!.Width = e.NewSize.Width < 1100 ? 240 : 340;
            var compact = e.NewSize.Width < 1180; if (_compact != compact) { _compact = compact; if (_section != "ai") Render(); }
        };
        try { ToolTip.SetTip(this.FindControl<TextBlock>("TokenStatus")!, privilege.IsProcessElevated() ? "Повышенный токен" : "Обычные права"); }
        catch (Exception e) { _log.Failure("token.read", e); }
        var nav = this.FindControl<StackPanel>("Navigation")!;
        var utilityNav = this.FindControl<StackPanel>("UtilityNavigation")!;
        var toolsNav = this.FindControl<StackPanel>("ToolsNavigation")!;
        foreach (var (id, label, icon, group) in new[] {
            ("home", "Дашборд", "home", 0), ("library", "Каталог скриптов", "grid", 0),
            ("favorites", "Избранное", "star", 0), ("history", "История запусков", "clock", 0),
            ("myscripts", "Мои скрипты", "document", 1), ("automation", "Автоматизация", "clock", 1),
            ("ai", "ИИ-помощник", "ai", 1), ("computer", "Мой компьютер", "computer", 1),
            ("settings", "Настройки", "settings", 2) })
        {
            var button = Ui.Action(label, () => Navigate(id), "nav"); button.Name = "Nav_" + id;
            var content = new Grid { ColumnDefinitions = new("18,*,Auto"), ColumnSpacing = 8 };
            content.Children.Add(Ui.Icon(icon, 17)); content.Children.Add(ReferenceUi.Ellipsis(label, size: 12.5).Place(0, 1));
            if (id == "library") { var count = Ui.Bound(nameof(LibraryViewModel.Entries) + ".Count", "muted"); count.Name = "NavLibraryCount"; count.DataContext = _library; count.FontSize = 10; content.Children.Add(count.Place(0,2)); }
            button.Content = content; _nav[id] = button; _sectionTitles[id] = label;
            (group == 0 ? nav : group == 1 ? toolsNav : utilityNav).Children.Add(button);
        }
        this.FindControl<Button>("SidebarRefresh")!.Click += async (_, _) => await GuardAsync("refresh", async () => { await _library.RefreshAsync(); Notify("Библиотека обновлена."); });
        _ai.ScriptCreated += entry => Dispatcher.UIThread.Post(() => Notify($"«{entry.DisplayName}» сохранён в библиотеку · категория {entry.Category}. Откройте «Скрипты», чтобы запустить его."));
        this.FindControl<Button>("ThemeButton")!.Click += (_, _) => ToggleTheme();
        this.FindControl<Button>("TitleSearch")!.Click += (_, _) => FocusSearch();
        foreach (var name in new[] { "ThemeButton", "TitleSearch", "NoticeClose", "SidebarRefresh" }) Ui.ConfigureMotion(this.FindControl<Button>(name)!);
        this.FindControl<Button>("NoticeClose")!.Click += (_, _) => this.FindControl<Border>("Notice")!.IsVisible = false;
        KeyDown += OnKeyDown;
        _library.PropertyChanged += LibraryChanged;
        _library.OperationFailed += (_, e) => ShowError("library", e);
        _computer.PropertyChanged += ComputerChanged;
        Opened += async (_, _) => await InitializeAsync();
        Closing += (_, e) => { if (_busy || _ai.IsApplying || _runs.Values.Any(w => w.ViewModel.IsRunning || w.ViewModel.IsChangingPolicy)) { e.Cancel = true; Notify("Дождитесь завершения действия или остановите запущенный скрипт."); } };
        Closed += (_, _) => { _closed = true; _ai.Dispose(); SavePreferences(); _watcher?.Dispose(); _computer.Dispose(); _library.PropertyChanged -= LibraryChanged; _computer.PropertyChanged -= ComputerChanged; foreach (var run in _runs.Values.ToArray()) run.Close(); };
        InitializeAutomation();
        Navigate("home");
    }
    public async Task InitializeAsync()
    {
        await _library.RefreshAsync();
        if (_closed) return;
        _watcher ??= new LibraryWatcher(_repository.ScriptsRoot);
        _watcher.Changed += () => Dispatcher.UIThread.Post(async () => { if (!_closed && !_busy) await _library.RefreshAsync(); });
        _log.Event("application.start");
        await _computer.RefreshAsync();
    }
    private void ApplyTheme()
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = _preferences.Theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        UpdateThemeButton();
        if (_section == "settings" && _workspace.Content is not null) Render();
    }
    private void ToggleTheme() { _preferences.Theme = ActualThemeVariant == ThemeVariant.Light ? "Dark" : "Light"; ApplyTheme(); SavePreferences(); }
    private void UpdateThemeButton()
    {
        var light = Application.Current?.RequestedThemeVariant != ThemeVariant.Dark;
        this.FindControl<Lucide.Avalonia.LucideIcon>("ThemeIcon")!.Kind = light ? Lucide.Avalonia.LucideIconKind.Moon : Lucide.Avalonia.LucideIconKind.Sun;
        ToolTip.SetTip(this.FindControl<Button>("ThemeButton")!, light ? "Тёмная тема" : "Светлая тема");
    }
    private void FocusSearch()
    {
        if (_section is not ("library" or "favorites" or "myscripts")) Navigate("library");
        // The page is rendered through a transition; focus once its controls are attached.
        Dispatcher.UIThread.Post(() =>
        {
            if (_workspace.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "PageSearch") is not { } search) return;
            search.Focus(); search.SelectAll();
        }, DispatcherPriority.Loaded);
    }
    private void SavePreferences()
    {
        _preferences.Sort = _library.Sort; _preferences.Category = _library.Category; _preferences.Status = _library.Status;
        if (WindowState == WindowState.Normal) { _preferences.Width = Width; _preferences.Height = Height; }
        _preferences.Maximized = WindowState == WindowState.Maximized;
        try { _store.Save(_preferences); } catch (Exception e) { _log.Failure("preferences.save", e); }
    }
    private void LibraryChanged(object? sender, PropertyChangedEventArgs e)
    {
        this.FindControl<TextBlock>("StatusText")!.Text = _library.IsLoading ? "Обновляем библиотеку…" : _library.IsSearching ? "Ищем…" : $"Скриптов: {_library.Entries.Count} · v1.5.1";
        this.FindControl<Border>("StatusDot")!.Background = Brush.Parse(_library.IsLoading || _library.IsSearching ? "#E0A93B" : "#2AC5AC");
        if (e.PropertyName == nameof(LibraryViewModel.Entries) && _section is "home" or "categories") Render();
    }
    private void ComputerChanged(object? sender, PropertyChangedEventArgs e)
    { if (_section == "computer" && e.PropertyName is nameof(ComputerViewModel.Snapshot) or nameof(ComputerViewModel.IsBusy) or nameof(ComputerViewModel.Message)) Render(); }
    public void Navigate(string section)
    {
        // Старые разделы «Категории» и «Инструменты» объединены со «Скриптами».
        if (section is "categories" or "tools") section = "library";
        _section = section;
        _library.Section = section switch { "favorites" => LibrarySection.Favorites, "home" => LibrarySection.Home, _ => LibrarySection.Library };
        var highlighted = section;
        foreach (var item in _nav) item.Value.Classes.Set("active", item.Key == highlighted);
        this.FindControl<TextBlock>("TitleSection")!.Text = _sectionTitles.GetValueOrDefault(section, "Дашборд");
        Render(animate: true);
    }
    private Task ShowAiSettingsAsync() => AiSettingsWindow.ShowAsync(this, _ai, _aiSettings);
    private void Render(bool animate = false)
    {
        // Keep the transition path even for an immediate refresh: a presenter previously
        // faded out can retain Opacity=0 if PageTransition is switched to null.
        _workspace.PageTransition = animate && !Motion.Reduced ? _navigationTransition : _instantTransition;
        _workspace.Content = _section switch
        {
            "home" => HomeView(), "computer" => PaddedPage(ComputerView()), "library" or "favorites" or "myscripts" => LibraryView(), "automation" => AutomationView(),
            "trash" => PaddedPage(TrashView()), "history" => HistoryView(), "settings" => SettingsView(), "ai" => new AiAssistantView(this,_ai,ShowAiSettingsAsync), _ => HomeView()
        };
        if (animate && _workspace.Content is Control page) Motion.Enter(page, cascade: true, animateRoot: false);
    }
    private Button Command(string label, Func<Task> action, string? style = null) => Ui.Button(label, () => GuardAsync(label, action), style);
    private async Task GuardAsync(string operation, Func<Task> work)
    {
        if (_busy) return; _busy = true;
        try { await work(); }
        catch (OperationCanceledException) { Notify("Действие отменено."); }
        catch (Exception e) { ShowError(operation, e); }
        finally { _busy = false; }
    }
    private void ShowError(string operation, Exception e) { _log.Failure(operation, e); var error = OperationError.From(e); Notify(error.Message + " " + error.Action); }
    public void Notify(string message) { this.FindControl<TextBlock>("NoticeText")!.Text = message; var notice = this.FindControl<Border>("Notice")!; notice.IsVisible = true; _ = Motion.RevealAsync(notice, 180, 5); }
    private Control Header(string title, string description, params Control[] actions)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 20, Margin = new(0, 0, 0, 16) };
        var heading = Ui.Stack(Ui.Text(title, "heading"), Ui.Text(description, "muted", 12)); heading.Spacing = 7; grid.Children.Add(heading);
        var row = Ui.Row(actions); Grid.SetColumn(row, 1); row.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(row); return grid;
    }
    private string Value(string category, string key) => _computer.Find(category, key)?.Display ?? (_computer.IsBusy ? "Получаем данные…" : "Недоступно");
    private Control Summary(string label, string value, string icon)
    {
        var layout = new Grid { ColumnDefinitions = new("*,42"), ColumnSpacing = 12 };
        var text = Ui.Stack(Ui.Text(label, "muted", 12), Ui.Text(value, "section")); text.Spacing = 9; layout.Children.Add(text); layout.Children.Add(Ui.Icon(icon, 18).Place(0, 1)); return Ui.Card(layout);
    }
    private Control ComputerView()
    {
        var root = Ui.Stack(Header("Мой компьютер", Environment.MachineName + "  ·  Характеристики и состояние системы", Ui.Action("Обновить данные", () => _computer.RefreshCommand.Execute(null), "primary"), Command("Экспорт", ExportInventoryAsync)));
        var summary = new UniformGrid { Columns = _compact ? 2 : 4 };
        foreach (var card in new[] { Summary("Система", Value("os", "name"), "computer"), Summary("Процессор", Value("cpu", "model"), "cpu"), Summary("Память", Value("memory", "installed"), "memory"), Summary("Время работы", Value("os", "uptime"), "history") }) { card.Margin = new(0, 0, 12, 12); summary.Children.Add(card); }
        root.Children.Add(summary);
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (id, title) in new[] { ("overview", "Обзор"), ("cpu-memory", "Процессор и память"), ("disks", "Диски"), ("gpu", "Видеокарта"), ("network", "Сеть"), ("bios", "Система и BIOS") })
        { var button = Ui.Action(title, () => { _inventoryTab = id; Render(animate: true); }, "tab"); button.Classes.Set("active", _inventoryTab == id); button.Margin = new(0, 0, 4, 4); tabs.Children.Add(button); }
        root.Children.Add(tabs);
        var statusIcon = Ui.Icon("status",13); Ui.Resource(statusIcon,Lucide.Avalonia.LucideIcon.ForegroundProperty,"ScAccentText");
        root.Children.Add(Ui.Row(statusIcon, Ui.Text(_computer.Message, "muted", 11), _computer.IsBusy ? Ui.Action("Отменить", () => _computer.CancelCommand.Execute(null), "ghost") : Ui.Text("")));
        if (_computer.IsBusy) root.Children.Add(new ProgressBar { IsIndeterminate = true });
        if (_computer.Snapshot is { } snapshot)
        {
            var ids = _inventoryTab switch { "overview" => new[] { "os" }, "cpu-memory" => ["cpu", "memory"], _ => [_inventoryTab] };
            foreach (var category in snapshot.Categories.Where(c => ids.Contains(c.Id)))
            {
                if (category.Error is { } error) root.Children.Add(Ui.Card(Ui.Stack(Ui.Text(category.Title, "section"), Ui.Text(error.Message + " " + error.Action, "muted"))));
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var record in category.Records)
                {
                    var categoryIcon = category.Id switch { "os" => "computer", "cpu" => "cpu", "memory" => "memory", "disks" => "disk", "network" => "network", "gpu" => "gpu", "bios" => "bios", _ => "settings" };
                    var caption = Ui.Row(Ui.Icon(categoryIcon, 17), Ui.Text(record.Title, "section")); caption.Spacing = 10;
                    var fields = Ui.Stack(caption);
                    foreach (var field in record.Fields)
                    {
                        var value = Ui.Text(field.Display, field.Status == DataStatus.Success ? null : "muted", 12); ToolTip.SetTip(value, $"{field.StatusText}\n{field.Source}\n{field.CapturedAt.LocalDateTime:g}\n{field.Detail}");
                        var row = new Grid { ColumnDefinitions = new("108,*"), ColumnSpacing = 12, Margin = new(0, 4) };
                        row.Children.Add(Ui.Text(field.Label, "muted", 12)); row.Children.Add(value.Place(0, 1)); fields.Children.Add(row);
                        if (field.Status == DataStatus.Success && field.Unit == "%") fields.Children.Add(new ProgressBar { Value = Math.Clamp(field.Number ?? 0, 0, 100) });
                    }
                    var card = Ui.Card(fields); card.Width = 320; card.VerticalAlignment = VerticalAlignment.Top; card.Margin = new(0, 0, 14, 14); wrap.Children.Add(card);
                }
                root.Children.Add(wrap);
            }
        }
        root.Children.Add(Ui.Text("Источник и время получения — в подсказке к значению", "muted", 10));
        return Ui.Scroll(root);
    }
    private Control TrashView()
    {
        var root = Ui.Stack(Header("Корзина", "Скрипты можно восстановить вместе с метаданными", Command("Отменить удаление", async () => { _deletions.Undo(); await _library.RefreshAsync(); Render(); })));
        var entries = _repository.GetTrash(); if (entries.Count == 0) root.Children.Add(Ui.Card(Ui.Stack(Ui.Text("Корзина пуста", "section"), Ui.Text("Удалённые скрипты появятся здесь. Их можно будет восстановить.", "muted", 13))));
        var rows = Ui.Stack(); rows.Spacing = 0;
        foreach (var entry in entries)
        {
            var restore = Command("Восстановить", async () => { await Task.Run(() => _repository.RestoreFromTrash(entry)); await _library.RefreshAsync(); Render(); });
            var delete = Command("Удалить навсегда", async () => { if (await Dialogs.ConfirmAsync(this, "Удалить навсегда?", entry.Name + " будет удалён из корзины без возможности отмены.")) { await Task.Run(() => _repository.PermanentlyDelete(entry)); Render(); } }, "danger");
            var row = Ui.Stack(Ui.Text(entry.Display, size: 14), Ui.Row(restore, delete)); row.Spacing = 10;
            var item = new Border { Child = row, Padding = new(18), BorderThickness = new(0,0,0,1) }; Ui.Resource(item, Border.BorderBrushProperty, "ScLine"); rows.Children.Add(item);
        }
        if (entries.Count > 0) root.Children.Add(new Border { Child = rows, Classes = { "libraryPane" } });
        return Ui.Scroll(root);
    }
    private async Task CreateAsync()
    {
        var values = await Dialogs.MetadataAsync(this, _repository.Categories, null); if (values is null) return;
        var entry = await Task.Run(() => _repository.Create(values.Name, values.Category, values.Description, values.Notes, values.Tags, values.Favorite, values.Status));
        await _library.RefreshAsync(entry.Id); Navigate("library"); await EditCodeAsync(entry);
    }
    private async Task ImportAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = true, Title = "Импорт скриптов", FileTypeFilter = [new("PowerShell") { Patterns = ["*.ps1"] }] });
        await ImportPathsAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }
    private async Task ImportPathsAsync(IEnumerable<string> paths)
    {
        var category = _library.Category == "Все категории" ? "Other" : _library.Category; Guid? last = null; var count = 0;
        foreach (var path in paths) { last = (await Task.Run(() => _repository.Import(path, category))).Id; count++; }
        if (count == 0) return; await _library.RefreshAsync(last); Navigate("library"); Notify($"Импортировано: {count}");
    }
    private bool Running(ScriptEntry entry) => (_ai.IsApplying && _ai.SelectedEntry?.Id == entry.Id) || (_runs.TryGetValue(entry.Id, out var window) && (window.ViewModel.IsRunning || window.ViewModel.IsChangingPolicy));
    private async Task EditCodeAsync(ScriptEntry entry)
    {
        if (Running(entry)) { Notify("Остановите запуск перед редактированием."); return; }
        var code = await Task.Run(() => ScriptRepository.ReadCode(entry.FilePath));
        var edited = await Dialogs.CodeAsync(this, entry.DisplayName, code); if (edited is null || edited == code) return;
        await Task.Run(() => _repository.WriteCode(entry, edited)); await _library.RefreshAsync(entry.Id);
        if (_runs.TryGetValue(entry.Id, out var run)) await run.ViewModel.ReloadAsync();
        Notify("Сохранено. Предыдущая версия доступна в истории.");
    }
    private async Task MetadataAsync(ScriptEntry entry)
    {
        if (Running(entry)) { Notify("Остановите запуск перед изменением скрипта."); return; }
        var values = await Dialogs.MetadataAsync(this, _repository.Categories, entry); if (values is null) return;
        await Task.Run(() => _repository.EditMetadata(entry, values.Name, values.Category, values.Description, values.Notes, values.Tags, values.Favorite, values.Status));
        if (_runs.Remove(entry.Id, out var run)) run.Close(); await _library.RefreshAsync(entry.Id); Render();
    }
    private async Task VersionsAsync(ScriptEntry entry)
    { if (Running(entry)) { Notify("Остановите запуск перед восстановлением версии."); return; } await Dialogs.VersionsAsync(this, _repository, entry); await _library.RefreshAsync(entry.Id); if (_runs.TryGetValue(entry.Id, out var run)) await run.ViewModel.ReloadAsync(); }
    private async Task DeleteAsync(ScriptEntry entry)
    {
        if (Running(entry)) { Notify("Остановите запуск перед удалением."); return; }
        if (!await Dialogs.ConfirmAsync(this, "Переместить в корзину?", entry.DisplayName + " можно будет восстановить.")) return;
        await Task.Run(() => _deletions.Delete(entry)); if (_runs.Remove(entry.Id, out var run)) run.Close(); await _library.RefreshAsync(); Notify("Скрипт в корзине · Ctrl+Z для отмены.");
    }
    private async Task SyntaxAsync(ScriptEntry entry)
    {
        var result = await new PowerShellSyntaxService().CheckAsync(entry.FilePath);
        var choice = await SyntaxDialog.ShowAsync(this, entry.DisplayName, result);
        if (choice == SyntaxDialogChoice.Edit) await EditCodeAsync(entry);
        else if (choice == SyntaxDialogChoice.AskAi)
        {
            if (_ai.IsBusy) { Notify("Дождитесь ответа SC AI или остановите генерацию."); return; }
            Navigate("ai"); await _ai.FixAsync(entry, "Ошибки синтаксиса PowerShell:\n" + result.Message);
        }
    }
    private void OpenRun(ScriptEntry entry)
    {
        if (_ai.IsApplying && _ai.SelectedEntry?.Id == entry.Id) { Notify("Дождитесь применения предложения ИИ."); return; }
        if (_runs.TryGetValue(entry.Id, out var existing)) { existing.Activate(); return; }
        var window = new ScriptRunWindow(entry, _repository.DataRoot, _history, _log,(script,error) =>
        {
            if (_ai.IsBusy) { Notify("Дождитесь ответа ИИ или остановите генерацию."); return false; }
            Navigate("ai"); Activate(); _ = _ai.FixAsync(script, error); return true;
        }); _runs[entry.Id] = window;
        var previousRun = _history.LatestFor(entry.Id);
        window.Closed += (_, _) =>
        {
            _runs.Remove(entry.Id);
            // Closing a child window does not change the catalog. Retain its controls,
            // selection and scroll position; only completed runs require a history refresh.
            if (!_closed && _section == "history" && _history.LatestFor(entry.Id) != previousRun) Render();
        };
        window.Show(this);
    }
    private void OpenAi(ScriptEntry entry)
    {
        if (_ai.IsBusy) { Notify("Дождитесь ответа SC AI или остановите генерацию."); return; }
        _ai.SelectedEntry = entry; _ai.AutoDetect = true; Navigate("ai");
    }
    private async Task ExportScriptAsync(ScriptEntry entry)
    { var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = entry.FileName, DefaultExtension = "ps1" }); if (file?.TryGetLocalPath() is { } path) await Task.Run(() => File.Copy(entry.FilePath, path, true)); }
    private async Task ExportInventoryAsync()
    {
        if (_computer.Snapshot is not { } snapshot) { Notify("Сначала получите характеристики компьютера."); return; }
        var options = await Dialogs.InventoryExportAsync(this); if (options is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "SC-Computer-" + DateTime.Now.ToString("yyyyMMdd") + (options.Json ? ".json" : ".txt"), DefaultExtension = options.Json ? "json" : "txt" });
        if (file?.TryGetLocalPath() is not { } path) return;
        var selected = InventoryExport.Select(snapshot, options.Categories, options.Sensitive);
        await File.WriteAllTextAsync(path, options.Json ? InventoryExport.Json(selected) : InventoryExport.Text(selected)); Notify("Характеристики экспортированы.");
    }
    private async Task BackupAsync()
    { var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "SC-ScriptForge-" + DateTime.Now.ToString("yyyyMMdd") + ".zip", DefaultExtension = "zip" }); if (file?.TryGetLocalPath() is { } path) { await Task.Run(() => _repository.CreateBackup(path)); Notify("Резервная копия создана."); } }
    private static Task OpenFolderAsync(string path) { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return Task.CompletedTask; }
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        switch (e.Key)
        {
            case Key.K: case Key.F: FocusSearch(); break;
            case Key.T when e.Source is not TextBox: ToggleTheme(); break;
            case Key.N: await GuardAsync("create", CreateAsync); break;
            case Key.I: await GuardAsync("import", ImportAsync); break;
            case Key.R: if (_section == "computer") await _computer.RefreshAsync(); else await _library.RefreshAsync(); break;
            case Key.Z when e.Source is not TextBox: await GuardAsync("undo", async () => { _deletions.Undo(); await _library.RefreshAsync(); Render(); }); break;
            case Key.D1: Navigate("home"); break; case Key.D2: Navigate("library"); break; case Key.D3: Navigate("ai"); break; case Key.D4: Navigate("computer"); break;
            case Key.E when _library.SelectedEntry is { } edit: await GuardAsync("edit", () => EditCodeAsync(edit)); break;
            case Key.D when _library.SelectedEntry is { } duplicate: await GuardAsync("duplicate", async () => { var copy = await Task.Run(() => _repository.Duplicate(duplicate)); await _library.RefreshAsync(copy.Id); }); break;
            case Key.Enter when _section == "ai":
                if (_workspace.Content is AiAssistantView aiView && aiView.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(t => t.Name == "AiTabs")?.SelectedIndex == 0) await _ai.GenerateAsync();
                break;
            case Key.Enter when _library.SelectedEntry is { } run: OpenRun(run); break;
            case Key.P: await Dialogs.PaletteAsync(this, new Dictionary<string, Action> { ["Главная"] = () => Navigate("home"), ["Скрипты"] = () => Navigate("library"), ["Избранное"] = () => Navigate("favorites"), ["SC AI · написать скрипт"] = () => Navigate("ai"), ["Мой компьютер"] = () => Navigate("computer"), ["Новый скрипт"] = () => _ = GuardAsync("create", CreateAsync), ["Импорт .ps1"] = () => _ = GuardAsync("import", ImportAsync), ["Модель SC AI"] = () => _ = ShowAiSettingsAsync(), ["Корзина"] = () => Navigate("trash"), ["Журнал запусков"] = () => Navigate("history"), ["Настройки"] = () => Navigate("settings"),
                ["Сменить тему · Ctrl+T"] = ToggleTheme }); break;
            default: return;
        }
        e.Handled = true;
    }
}
internal static class GridPlacement
{
    public static T Place<T>(this T control, int row, int column) where T : Control { Grid.SetRow(control, row); Grid.SetColumn(control, column); return control; }
    public static T AlignBottom<T>(this T control) where T : Control { control.VerticalAlignment = VerticalAlignment.Bottom; control.HorizontalAlignment = HorizontalAlignment.Center; return control; }
}
