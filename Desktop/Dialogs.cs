using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.Core;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;

namespace SC.ScriptForge;

public sealed record MetadataValues(string Name, string Category, string Description, string Notes, string[] Tags, bool Favorite, string Status);
public sealed record ExportOptions(HashSet<string> Categories, bool Sensitive, bool Json);
public static class Dialogs
{
    public static Window Window(string title, double width = 640, double height = 520)
    {
        var dialog = new Window { Title = title + " · SC ScriptForge", Width = width, Height = height, MinWidth = 420, MinHeight = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        Motion.RevealOnOpen(dialog); return dialog;
    }
    public static async Task MessageAsync(Window owner, string title, string message)
    {
        var dialog = Window(title); var copy = Ui.Button("Копировать", async () => { if (dialog.Clipboard is { } clipboard) await clipboard.SetTextAsync(message); });
        dialog.Content = Ui.Stack(Ui.Text(title, "heading"), Ui.Scroll(new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 330 }), Ui.Row(copy, Ui.Action("Закрыть", () => dialog.Close()))).Margin(24);
        await dialog.ShowDialog(owner);
    }
    public static Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        var dialog = Window(title, 550, 310);
        dialog.Content = Ui.Stack(Ui.Text(title, "section"), Ui.Text(message, "muted"), Ui.Row(Ui.Action("Отмена", () => dialog.Close(false)), Ui.Action("Подтвердить", () => dialog.Close(true), "primary"))).Margin(24);
        return dialog.ShowDialog<bool>(owner);
    }
    public static Task<string?> PromptAsync(Window owner, string title, string label)
    {
        var dialog = Window(title, 520, 300); var input = Ui.Input(); var error = Ui.Text("", "muted");
        dialog.Content = Ui.Stack(Ui.Text(title, "section"), Ui.Field(label, input), error, Ui.Row(Ui.Action("Отмена", () => dialog.Close()), Ui.Action("Создать", () => { if (string.IsNullOrWhiteSpace(input.Text)) error.Text = "Введите название."; else dialog.Close(input.Text.Trim()); }, "primary"))).Margin(24);
        dialog.Opened += (_, _) => input.Focus(); return dialog.ShowDialog<string?>(owner);
    }
    private static readonly IReadOnlyList<(string Value, string Label)> Statuses = [("Draft", "Черновик"), ("Ready", "Готов к работе"), ("Archived", "В архиве")];
    public static Task<MetadataValues?> MetadataAsync(Window owner, IReadOnlyList<string> categories, ScriptEntry? entry, MetadataValues? suggestion = null)
    {
        var heading = entry is not null ? "Сведения о скрипте" : suggestion is not null ? "Сохранить скрипт" : "Новый скрипт";
        var dialog = Window(heading, 640, 720);
        var name = Ui.Input(entry?.Name ?? suggestion?.Name); var category = new ComboBox { ItemsSource = categories, SelectedItem = entry?.Category ?? suggestion?.Category ?? categories.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var description = Ui.Input(entry?.Description ?? suggestion?.Description, multiline: true); description.MinHeight = 70; var notes = Ui.Input(entry?.Notes ?? suggestion?.Notes, multiline: true); var tags = Ui.Input(string.Join(", ", entry?.Tags ?? (IEnumerable<string>?)suggestion?.Tags ?? []));
        var favorite = new CheckBox { Content = "Добавить в избранное", IsChecked = entry?.Favorite ?? suggestion?.Favorite ?? false };
        var currentStatus = entry?.Status ?? suggestion?.Status ?? "Draft";
        var status = new ComboBox { ItemsSource = Statuses.Select(s => s.Label).ToArray(), SelectedIndex = Math.Max(0, Statuses.ToList().FindIndex(s => s.Value == currentStatus)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var error = Ui.Text("", "muted");
        var basics = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12 }; basics.Children.Add(Ui.Field("Категория", category)); basics.Children.Add(Ui.Field("Статус", status).Place(0, 1));
        dialog.Content = Ui.Scroll(Ui.Stack(Ui.Text(heading, "heading", 24), Ui.Field("Название", name), basics, Ui.Field("Описание", description),
            Ui.Field("Теги через запятую", tags), new Expander { Header = "Заметки", Content = Ui.Stack(notes).Margin(12), IsExpanded = !string.IsNullOrWhiteSpace(entry?.Notes), HorizontalAlignment = HorizontalAlignment.Stretch }, favorite, error,
            Ui.Row(Ui.Action("Отмена", () => dialog.Close(), "ghost"), Ui.Action(suggestion is not null && entry is null ? "Сохранить в библиотеку" : "Сохранить", () =>
            {
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Укажите название скрипта."; return; }
                dialog.Close(new MetadataValues(name.Text.Trim(), category.SelectedItem as string ?? "Other", description.Text ?? "", notes.Text ?? "",
                    (tags.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), favorite.IsChecked == true, Statuses[Math.Max(0, status.SelectedIndex)].Value));
            }, "primary"))).Margin(24));
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        return dialog.ShowDialog<MetadataValues?>(owner);
    }
    public static Task<string?> CodeAsync(Window owner, string title, string code)
    {
        var dialog = Window(title, 1000, 740); var editor = Ui.Input(code, multiline: true); editor.AcceptsTab = true; editor.TextWrapping = TextWrapping.NoWrap;
        editor.FontFamily = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas"); editor.FontSize = 13;
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);
        var status = Ui.Text("UTF-8 · PowerShell", "muted"); var accepted = false; var closingPrompt = false;
        void Save() { accepted = true; dialog.Close(editor.Text); }
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), Margin = new(20), RowSpacing = 12 };
        root.Children.Add(Ui.Row(Ui.Text(title, "section"), Ui.Text("Ctrl+S · сохранить", "muted"))); root.Children.Add(editor.Place(1, 0));
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 16 };
        footer.Children.Add(status); footer.Children.Add(Ui.Row(Ui.Action("Закрыть", () => dialog.Close(), "ghost"), Ui.Action("Сохранить", Save, "primary")).Place(0,1));
        root.Children.Add(footer.Place(2, 0));
        editor.TextChanged += (_, _) => status.Text = editor.Text == code ? "Сохранено" : "Есть несохранённые изменения";
        dialog.KeyDown += (_, e) => { if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; } };
        dialog.Closing += async (_, e) =>
        {
            if (accepted || editor.Text == code) return;
            e.Cancel = true; if (closingPrompt) return; closingPrompt = true;
            try { if (await ConfirmAsync(dialog, "Закрыть без сохранения?", "Изменения в редакторе будут потеряны.")) { accepted = true; dialog.Close(); } }
            finally { closingPrompt = false; }
        };
        dialog.Content = root; return dialog.ShowDialog<string?>(owner);
    }
    public static async Task VersionsAsync(Window owner, ScriptRepository repository, ScriptEntry entry)
    {
        var dialog = Window("Версии · " + entry.Name, 1050, 700);
        var versions = await Task.Run(() => repository.GetVersions(entry)); var current = await Task.Run(() => ScriptRepository.ReadCode(entry.FilePath));
        var list = new ListBox { ItemsSource = versions, ItemTemplate = new FuncDataTemplate<ScriptVersion>((v, _) => Ui.Text(v?.Description ?? "")) };
        var preview = Ui.Input(multiline: true); preview.IsReadOnly = true; preview.FontFamily = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas");
        var original = new CheckBox { Content = "Показать исходный код версии" }; var error = Ui.Text(versions.Count == 0 ? "История версий пуста" : "− удалено  /  + добавлено", "muted");
        async Task Update()
        {
            try
            {
                if (list.SelectedItem is not ScriptVersion version) return;
                var before = await File.ReadAllTextAsync(version.Path);
                preview.Text = original.IsChecked == true ? before : string.Join("\n", (await Task.Run(() => DiffService.Compare(before, current))).Select(l => (l.Kind == DiffKind.Added ? "+ " : l.Kind == DiffKind.Removed ? "− " : "  ") + l.Text));
            }
            catch (Exception e) { error.Text = OperationError.From(e).Message; }
        }
        list.SelectionChanged += async (_, _) => await Update(); original.IsCheckedChanged += async (_, _) => await Update();
        var restore = Ui.Button("Восстановить выбранную", async () =>
        {
            if (list.SelectedItem is not ScriptVersion version) return;
            if (!await ConfirmAsync(dialog, "Восстановить версию?", "Текущий код сначала будет сохранён новым снимком.")) return;
            try { await Task.Run(() => repository.Restore(entry, version)); dialog.Close(); }
            catch (Exception e) { error.Text = OperationError.From(e).Message; }
        }, "primary");
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), ColumnDefinitions = new("300,*"), ColumnSpacing = 16, RowSpacing = 12, Margin = new(20) };
        var header = Ui.Stack(Ui.Text("История версий", "heading"), error); Grid.SetColumnSpan(header, 2); root.Children.Add(header);
        root.Children.Add(list.Place(1, 0)); root.Children.Add(preview.Place(1, 1)); var footer = Ui.Row(original, restore, Ui.Action("Закрыть", () => dialog.Close())); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer.Place(2, 0));
        dialog.Content = root; list.SelectedIndex = versions.Count > 0 ? 0 : -1; await dialog.ShowDialog(owner);
    }
    public static Task<ExportOptions?> InventoryExportAsync(Window owner)
    {
        var dialog = Window("Экспорт характеристик", 560, 650); var choices = new Dictionary<string, CheckBox>();
        var root = Ui.Stack(Ui.Text("Экспорт характеристик", "heading"), Ui.Text("Выберите разделы отчёта", "muted"));
        foreach (var pair in InventoryCategory.Names) { var check = new CheckBox { Content = pair.Value, IsChecked = true }; choices[pair.Key] = check; root.Children.Add(check); }
        var sensitive = new CheckBox { Content = "Включить имя ПК, пользователя, домен и IP/DNS/шлюзы", IsChecked = false };
        var format = new ComboBox { ItemsSource = new[] { "JSON · типизированные данные", "Текст · удобный отчёт" }, SelectedIndex = 0 };
        var error = Ui.Text("", "muted"); root.Children.Add(sensitive); root.Children.Add(format); root.Children.Add(error);
        root.Children.Add(Ui.Row(Ui.Action("Отмена", () => dialog.Close()), Ui.Action("Выбрать файл", () =>
        {
            var selected = choices.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToHashSet();
            if (selected.Count == 0) { error.Text = "Выберите хотя бы один раздел."; return; }
            dialog.Close(new ExportOptions(selected, sensitive.IsChecked == true, format.SelectedIndex == 0));
        }, "primary"))); dialog.Content = Ui.Scroll(root.Margin(24)); return dialog.ShowDialog<ExportOptions?>(owner);
    }
    public static async Task PaletteAsync(Window owner, IReadOnlyDictionary<string, Action> actions)
    {
        var dialog = Window("Команды", 560, 480); var query = Ui.Input(hint: "Найти команду…"); var list = new ListBox { ItemsSource = actions.Keys.ToArray() };
        void Execute() { if (list.SelectedItem is string key) { dialog.Close(); actions[key](); } }
        query.TextChanged += (_, _) => { list.ItemsSource = actions.Keys.Where(k => k.Contains(query.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray(); list.SelectedIndex = 0; };
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) Execute(); }; list.DoubleTapped += (_, _) => Execute();
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 12, Margin = new(20) }; root.Children.Add(query); root.Children.Add(list.Place(1, 0)); root.Children.Add(Ui.Action("Открыть", Execute, "primary").Place(2, 0));
        dialog.Content = root; dialog.Opened += (_, _) => query.Focus(); list.SelectedIndex = 0; await dialog.ShowDialog(owner);
    }
}
internal static class ControlMargins
{
    public static T Margin<T>(this T control, double value) where T : Control { control.Margin = new(value); return control; }
}
