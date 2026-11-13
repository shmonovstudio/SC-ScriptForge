using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

/// <summary>
/// Библиотека — компактный список без постоянного просмотра кода.
/// Действия открываются правой кнопкой мыши или кнопкой «⋯»; двойной щелчок — запуск.
/// </summary>
public sealed partial class MainWindow
{
    private Control LibraryView()
    {
        var favorites = _section == "favorites";
        var root = new Grid { Name = "LibraryPage", ColumnDefinitions = new(_compact ? "*,300" : "*,450") };
        var left = new Grid { RowDefinitions = new("Auto,Auto,Auto,*"), RowSpacing = 24, Margin = new(32,34,28,0) };
        var options = ReferenceUi.IconButton("Действия библиотеки", "list", () => { });
        options.Click += (_,_) =>
        {
            var menu = new ContextMenu();
            void Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_,_) => action(); menu.Items.Add(item); }
            Item("Новый скрипт…", () => _ = GuardAsync("create", CreateAsync));
            Item("Импортировать .ps1…", () => _ = GuardAsync("import", ImportAsync));
            Item("Сбросить фильтры", () => { _library.ResetFilters(); Navigate(_section); });
            foreach (var sort in _library.SortOptions) Item(sort.Name, () => _library.Sort = sort.Value);
            foreach (var status in _library.StatusOptions) Item(status.Name, () => _library.Status = status.Value);
            menu.Open(options);
        };
        left.Children.Add(ReferenceUi.Heading("Простые инструменты для сложных задач", favorites ? "Избранные скрипты" : _section == "myscripts" ? "Мои скрипты" : "Библиотека скриптов",
            "Готовые решения для ежедневных задач администратора", options));
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10, Margin = new(0,8,0,0) };
        toolbar.Children.Add(PageSearch("Найти скрипт, задачу или ключевое слово…"));
        var refresh = Ui.WithIcon(Command("Обновить", async () => await _library.RefreshAsync(), "ink"), "refresh"); refresh.MinHeight = 43;
        toolbar.Children.Add(refresh.Place(0,1)); left.Children.Add(toolbar.Place(1,0));
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var category in _library.Categories.Where(c => c == "Все категории" || c == _library.Category || _library.Entries.Any(e => e.Category == c)))
        {
            var tab = Ui.Action(category, () => { _library.Category = category; foreach (var b in tabs.Children.OfType<Button>()) b.Classes.Set("active", (string?)b.Tag == category); }, "tab");
            tab.Tag = category; tab.Content = category == "Все категории" ? "Все" : ReferenceUi.Category(category); tab.Classes.Set("active", _library.Category == category); tab.Padding = new(12,7); tabs.Children.Add(tab);
        }
        var count = Ui.Bound(nameof(LibraryViewModel.ResultSummary), "muted"); count.DataContext = _library; count.FontSize = 10.5;
        var categoryRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 6 }; categoryRow.Children.Add(tabs); categoryRow.Children.Add(count.Place(0,1));
        left.Children.Add(categoryRow.Place(2,0));
        var details = new ContentControl { Name = "ScriptDetails", Content = ScriptDetails(_library.SelectedEntry), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var list = new ListBox { Name = "ScriptCatalog", DataContext = _library, Classes = { "referenceList" },
            ItemTemplate = new FuncDataTemplate<ScriptEntry>((entry, _) => entry is null ? new Border() : CatalogRow(entry)) };
        AutomationProperties.SetName(list, "Скрипты в библиотеке");
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(LibraryViewModel.FilteredEntries)));
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(LibraryViewModel.SelectedEntry)) { Mode = BindingMode.TwoWay });
        list.SelectionChanged += (_,_) => { details.Content = ScriptDetails(_library.SelectedEntry); if (details.Content is Control view) Motion.Enter(view); };
        list.DoubleTapped += (_, e) => { if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null) return; if (_library.SelectedEntry is { } entry) OpenRun(entry); };
        list.KeyDown += (_, e) =>
        {
            if (_library.SelectedEntry is not { } entry || e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            if (e.Key == Key.Enter) { OpenRun(entry); e.Handled = true; }
            else if (e.Key == Key.Delete) { e.Handled = true; _ = GuardAsync("delete", () => DeleteAsync(entry)); }
            else if ((e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) && list.ContainerFromItem(entry) is Control container) { ScriptMenu(entry).Open(container); e.Handled = true; }
        };
        var reset = Ui.Action("Сбросить фильтры", () => { _library.ResetFilters(); }); reset.Name = "LibraryReset";
        var empty = Ui.Stack(Ui.IconBadge("script"), Ui.Bound(nameof(LibraryViewModel.EmptyTitle), "section"), Ui.Bound(nameof(LibraryViewModel.EmptyDescription), "muted"),
            reset, Ui.WithIcon(Command("Импортировать .ps1", ImportAsync, "primary"), "import"));
        empty.DataContext = _library; empty.Margin = new(24); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Bind(IsVisibleProperty, new Binding(nameof(LibraryViewModel.IsEmpty)));
        var catalog = new Grid(); catalog.Children.Add(list); catalog.Children.Add(empty);
        var catalogBorder = new Border { Child = catalog, BorderThickness = new(0,1,0,0) }; Ui.Resource(catalogBorder, Border.BorderBrushProperty, "ScLine");
        left.Children.Add(catalogBorder.Place(3,0)); root.Children.Add(left); root.Children.Add(ReferenceUi.Side(Ui.Scroll(details)).Place(0,1));
        DragDrop.SetAllowDrop(list, true);
        list.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.TryGetFiles()?.Any(f => Path.GetExtension(f.Name).Equals(".ps1", StringComparison.OrdinalIgnoreCase)) == true ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        list.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(p => Path.GetExtension(p).Equals(".ps1", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
            await GuardAsync("import.drop", () => ImportPathsAsync(paths));
        });
        return root;
    }

    private Control ScriptDetails(ScriptEntry? entry)
    {
        if (entry is null) return Ui.Stack(ReferenceUi.SmallTitle("О скрипте"), Ui.Text("Выберите скрипт", "section", 20), Ui.Text("Здесь появятся описание, сведения и действия.", "muted", 12));
        var root = Ui.Stack(); root.Spacing = 20;
        var caption = new Grid { ColumnDefinitions = new("*,Auto") }; caption.Children.Add(ReferenceUi.SmallTitle("О скрипте"));
        var more = ReferenceUi.IconButton("Действия со скриптом", "more", () => { }); more.Click += (_,_) => ScriptMenu(entry).Open(more); more.Classes.Add("ghost"); caption.Children.Add(more.Place(0,1)); root.Children.Add(caption);
        var badge = Ui.IconBadge(ReferenceUi.CategoryIcon(entry.Category), 23); badge.Width = badge.Height = 54; badge.Classes.Add("featured");
        var star = ReferenceUi.IconButton(entry.Favorite ? "Убрать из избранного" : "В избранное", "star", () => _ = GuardAsync("favorite", () => ToggleFavoriteAsync(entry))); star.Width = star.Height = 41;
        var name = Ui.Text(entry.DisplayName, "section", _compact ? 19 : 23); name.FontWeight = FontWeight.Bold;
        var title = new Grid { ColumnDefinitions = new("54,*,41"), ColumnSpacing = 15 };
        title.Children.Add(badge); var copy = Ui.Stack(name, Ui.Text(ReferenceUi.Category(entry.Category) + " · " + entry.StatusLabel, "muted", 11.5)); copy.Spacing = 4;
        title.Children.Add(copy.Place(0,1)); title.Children.Add(star.Place(0,2)); root.Children.Add(title);
        var description = Ui.Text(string.IsNullOrWhiteSpace(entry.DisplayDescription) ? "Локальный PowerShell-скрипт из вашей библиотеки." : entry.DisplayDescription, "soft", 13); description.LineHeight = 21;
        root.Children.Add(description); root.Children.Add(ReferenceUi.Tags(entry)); root.Children.Add(ReferenceUi.Line());
        long size = 0; try { size = new FileInfo(entry.FilePath).Length; } catch (IOException) { }
        var metadata = Ui.Stack(
            ReferenceUi.Metadata("users", "Источник", "Локальная библиотека"),
            ReferenceUi.Metadata("clock", "Последнее обновление", entry.LastModified.ToString("d MMMM yyyy", Russian)),
            ReferenceUi.Metadata("document", "Файл", entry.FileName),
            ReferenceUi.Metadata("document", "Размер", (size / 1024d).ToString("0.#", Russian) + " КБ"),
            ReferenceUi.Metadata("folder", "Категория", ReferenceUi.Category(entry.Category)),
            ReferenceUi.Metadata("shield", "Права при запуске", "Текущий пользователь"),
            ReferenceUi.Metadata("computer", "Совместимость", "Windows 10 / 11")); metadata.Spacing = 14; if (!_compact) root.Children.Add(metadata);
        var run = Ui.WithIcon(Ui.Action("Запустить скрипт", () => OpenRun(entry), "primary"), "play"); run.Name = "DetailsRun"; run.HorizontalAlignment = HorizontalAlignment.Stretch; run.HorizontalContentAlignment = HorizontalAlignment.Center; run.MinHeight = 41;
        var preview = Ui.WithIcon(Command("Предпросмотр", () => ViewCodeAsync(entry)), "script"); preview.HorizontalAlignment = HorizontalAlignment.Stretch;
        var source = Ui.WithIcon(Command("Исходный код", () => ExportScriptAsync(entry)), "export"); source.HorizontalAlignment = HorizontalAlignment.Stretch;
        var secondary = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 10 }; secondary.Children.Add(preview); secondary.Children.Add(source.Place(0,1));
        root.Children.Add(Ui.Stack(run, secondary));
        if (_compact) root.Children.Add(metadata);
        var explanation = Ui.Text(string.IsNullOrWhiteSpace(entry.DisplayNotes) ? entry.DisplayDescription : entry.DisplayNotes, "muted", 12.5); explanation.LineHeight = 20;
        root.Children.Add(Ui.Stack(Ui.Text("Описание", "section", 13), explanation)); root.Children.Add(ReferenceUi.Line());
        var codeCaption = new Grid { ColumnDefinitions = new("*,Auto") }; codeCaption.Children.Add(Ui.Text("Предпросмотр кода", "section", 13)); codeCaption.Children.Add(Ui.Text("PowerShell", "muted", 10).Place(0,1));
        var code = new Border { Child = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Child = new ScriptCodeView(entry.FilePath) }, Height = 190, CornerRadius = new(9), ClipToBounds = true };
        root.Children.Add(Ui.Stack(codeCaption,code)); return root;
    }
    /// <summary>Кнопка «Создать» с вариантами: пустой скрипт, SC AI, шаблон, категория.</summary>
    private Button CreateMenuButton()
    {
        var button = Ui.Action("Создать", () => { }, "primary"); button.Name = "LibraryCreate";
        button.Content = Ui.Row(Ui.Icon("plus", 15), Ui.Text("Создать"), Ui.Icon("chevrondown", 13));
        MenuItem Item(string label, string icon, string hint, Func<Task> action)
        {
            var text = Ui.Stack(Ui.Text(label, size: 13), Ui.Text(hint, "muted", 10)); text.Spacing = 1;
            var item = new MenuItem { Header = text, Icon = Ui.Icon(icon, 16) }; item.Click += async (_, _) => await GuardAsync(label, action); return item;
        }
        button.Click += (_, _) => new ContextMenu { Items =
        {
            Item("Написать с SC AI", "ai", "Опишите задачу — ИИ напишет код", () => { Navigate("ai"); return Task.CompletedTask; }),
            Item("Пустой скрипт", "document", "Заготовка с param() и справкой", CreateAsync),
            Item("Отчёт из Excel / CSV", "export", "Готовый шаблон экспорта таблиц", CreateExcelReportAsync),
            new Separator(),
            Item("Новая категория", "folder", "Папка для группировки скриптов", CreateCategoryAsync)
        } }.Open(button);
        return button;
    }

    private async Task CreateExcelReportAsync()
    {
        var entry = await Task.Run(() => _repository.Create("Отчёт из Excel или CSV", "Windows", "Экспорт Excel/CSV в CSV или JSON", "", ["excel", "csv"], false, "Draft"));
        await Task.Run(() => _repository.WriteCode(entry, PowerShellHost.ReadRuntimeScript("ExcelCsvReport.ps1")));
        await _library.RefreshAsync(entry.Id); Navigate("library"); OpenRun(entry);
    }
    private async Task CreateCategoryAsync()
    {
        var name = await Dialogs.PromptAsync(this, "Новая категория", "Название категории"); if (name is null) return;
        await Task.Run(() => _repository.CreateCategory(name)); await _library.RefreshAsync(); _library.Category = name; Navigate("library"); Notify($"Категория «{name}» создана.");
    }

    private Control CatalogRow(ScriptEntry entry)
    {
        var row = new Grid { ColumnDefinitions = new("50,*,76,14"), ColumnSpacing = 16, Background = Brushes.Transparent, DataContext = entry };
        var badge = Ui.IconBadge(ReferenceUi.CategoryIcon(entry.Category), 22); badge.Width = badge.Height = 50;
        if (_library.SelectedEntry?.Id == entry.Id) badge.Classes.Add("featured"); row.Children.Add(badge);
        var name = ReferenceUi.Ellipsis(entry.DisplayName, "section", 14); name.FontWeight = FontWeight.Bold;
        var detail = ReferenceUi.Ellipsis(string.IsNullOrWhiteSpace(entry.DisplayDescription) ? entry.FileName : entry.DisplayDescription, "muted", 12);
        var text = Ui.Stack(name, detail, ReferenceUi.Tags(entry)); text.Spacing = 5; text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text.Place(0,1));
        var date = Ui.Text("Обновлен\n" + entry.LastModified.ToString("dd MMM yyyy", Russian), "muted", 10.5); date.TextAlignment = TextAlignment.Right; row.Children.Add(date.Place(0,2));
        row.Children.Add(Ui.Icon("chevron", 15).Place(0,3)); row.ContextMenu = ScriptMenu(entry);
        return row;
    }
    private ContextMenu ScriptMenu(ScriptEntry entry)
    {
        MenuItem Item(string label, string icon, Func<Task> action, string? gesture = null)
        {
            var item = new MenuItem { Header = label, Icon = Ui.Icon(icon, 15), InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
            item.Click += async (_, _) => await GuardAsync(label, action); return item;
        }
        var run = Item("Запустить…", "play", () => { OpenRun(entry); return Task.CompletedTask; }, "Enter"); run.FontWeight = FontWeight.SemiBold;
        return new ContextMenu { Items =
        {
            run,
            Item("Просмотреть код", "eye", () => ViewCodeAsync(entry)),
            Item("Редактировать код", "edit", () => EditCodeAsync(entry), "Ctrl+E"),
            Item("Спросить SC AI", "ai", () => { OpenAi(entry); return Task.CompletedTask; }),
            new Separator(),
            Item(entry.Favorite ? "Убрать из избранного" : "В избранное", "star", () => ToggleFavoriteAsync(entry)),
            Item("Сведения и теги", "tag", () => MetadataAsync(entry)),
            Item("Версии и сравнение", "history", () => VersionsAsync(entry)),
            Item("История запусков", "status", () => RunsAsync(entry)),
            Item("Проверить синтаксис", "check", () => SyntaxAsync(entry)),
            Item("Профиль скрипта", "info", async () => await Dialogs.MessageAsync(this, "Профиль скрипта", await Task.Run(() => _repository.GetInsights(entry)))),
            new Separator(),
            Item("Создать копию", "copy", async () => { var copy = await Task.Run(() => _repository.Duplicate(entry)); await _library.RefreshAsync(copy.Id); }, "Ctrl+D"),
            Item("Экспортировать .ps1", "export", () => ExportScriptAsync(entry)),
            Item("Показать в Проводнике", "folderopen", () => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", entry.FilePath }, UseShellExecute = false }); return Task.CompletedTask; }),
            new Separator(), Item("Переместить в корзину", "trash", () => DeleteAsync(entry), "Delete")
        } };
    }

    private async Task ToggleFavoriteAsync(ScriptEntry entry)
    { entry.Favorite = !entry.Favorite; await Task.Run(() => _repository.SaveMeta(entry)); await _library.RefreshAsync(entry.Id); }

    /// <summary>Код открывается по запросу в отдельном окне, а не постоянно на странице.</summary>
    private async Task ViewCodeAsync(ScriptEntry entry)
    {
        var dialog = Dialogs.Window(entry.DisplayName, 1000, 740);
        var name = Ui.Text(entry.DisplayName, "heading", 22); name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap;
        var path = Ui.Text(entry.FilePath, "muted", 10); path.Classes.Add("mono"); path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap;
        var head = Ui.Stack(name, path); head.Spacing = 4;
        var code = new Border { Child = new ScriptCodeView(entry.FilePath), CornerRadius = new(12), ClipToBounds = true, BorderThickness = new(1) }; Ui.Resource(code, Border.BorderBrushProperty, "ScBorder");
        var edit = Ui.Button("Редактировать", async () => { dialog.Close(); await GuardAsync("edit", () => EditCodeAsync(entry)); }); edit.Content = Ui.Row(Ui.Icon("edit", 14), Ui.Text("Редактировать"));
        var ask = Ui.Action("Спросить SC AI", () => { dialog.Close(); OpenAi(entry); }); ask.Content = Ui.Row(Ui.Icon("ai", 14), Ui.Text("Спросить SC AI"));
        var run = Ui.Action("Запустить…", () => { dialog.Close(); OpenRun(entry); }, "primary"); run.Content = Ui.Row(Ui.Icon("play", 13), Ui.Text("Запустить…"));
        var footer = new Grid { ColumnDefinitions = new("*,Auto") }; footer.Children.Add(Ui.Action("Закрыть", () => dialog.Close(), "ghost")); footer.Children.Add(Ui.Row(ask, edit, run).Place(0, 1));
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 14, Margin = new(22) };
        root.Children.Add(head); root.Children.Add(code.Place(1, 0)); root.Children.Add(footer.Place(2, 0)); dialog.Content = root;
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) dialog.Close(); };
        await dialog.ShowDialog(this);
    }

    private async Task RunsAsync(ScriptEntry entry)
    {
        var records = _history.Recent.Where(r => r.ScriptId == entry.Id).ToArray();
        var dialog = Dialogs.Window("История запусков", 560, 520);
        var list = Ui.Stack(); list.Spacing = 6;
        if (records.Length == 0) list.Children.Add(Ui.Text("Скрипт ещё не запускался. Результаты появятся здесь после выполнения.", "muted", 12));
        foreach (var record in records)
        {
            var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
            var ok = record.Outcome == RunOutcome.Success; var icon = Ui.Icon(ok ? "ok" : "warning", 16); Ui.Resource(icon, Lucide.Avalonia.LucideIcon.ForegroundProperty, ok ? "ScAccentText" : "ScDanger");
            row.Children.Add(icon); row.Children.Add(Ui.Stack(Ui.Text(record.Label, "section", 13), Ui.Text(record.FinishedAt.LocalDateTime.ToString("dd.MM.yyyy · HH:mm"), "muted", 11)).Place(0, 1));
            row.Children.Add(Ui.Text(record.DurationLabel, "mono", 11).Place(0, 2)); list.Children.Add(new Border { Child = row, Padding = new(12), Classes = { "libraryPane" } });
        }
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 14, Margin = new(22) };
        root.Children.Add(Ui.Stack(Ui.Text("История запусков", "heading", 22), Ui.Text(entry.DisplayName, "muted", 12)));
        root.Children.Add(Ui.Scroll(list).Place(1, 0));
        var close = Ui.Action("Закрыть", () => dialog.Close()); close.HorizontalAlignment = HorizontalAlignment.Right; root.Children.Add(close.Place(2, 0));
        dialog.Content = root; await dialog.ShowDialog(this);
    }
}
