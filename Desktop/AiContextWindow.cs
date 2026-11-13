using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

public static class AiContextWindow
{
    public static async Task ShowAsync(Window owner, AiAssistantViewModel model)
    {
        if (!model.CanEditContext) return;
        var window = Dialogs.Window("Прикрепить скрипт", 640, 620); window.DataContext = model;
        var entry = new ComboBox { Name = "AiContextScript", HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Выберите скрипт из библиотеки", ItemTemplate = new FuncDataTemplate<ScriptEntry>((x,_) =>
            { var text = Ui.Text(x?.Name ?? "",size:13); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; return text; }) };
        // Фоновое обновление библиотеки заменяет список, и ComboBox может сам выбрать другой элемент по позиции.
        // Поэтому выбор синхронизируется по Id, а контекст меняется только явным выбором пользователя.
        var syncing = false;
        void Sync()
        {
            syncing = true;
            try { entry.ItemsSource = model.Library.Entries; entry.SelectedItem = model.SelectedEntry is { } current ? model.Library.Entries.FirstOrDefault(e => e.Id == current.Id) : null; }
            finally { syncing = false; }
        }
        entry.SelectionChanged += (_,_) => { if (!syncing && entry.SelectedItem is ScriptEntry selected && (entry.IsDropDownOpen || entry.IsKeyboardFocusWithin)) model.SelectedEntry = selected; };
        entry.DropDownClosed += (_,_) => { if (!syncing && entry.SelectedItem is ScriptEntry selected && selected.Id != model.SelectedEntry?.Id) model.SelectedEntry = selected; };
        void LibraryChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(model.Library.Entries)) Avalonia.Threading.Dispatcher.UIThread.Post(Sync); }
        void ModelChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs args) { if (args.PropertyName == nameof(model.SelectedEntry)) Sync(); }
        model.Library.PropertyChanged += LibraryChanged; model.PropertyChanged += ModelChanged; Sync();
        var scope = new ComboBox { Name = "AiContextScope", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<AiContextChoice>((x,_) => Ui.Text(x?.Name ?? "",size:12)) };
        scope.Bind(ItemsControl.ItemsSourceProperty,new Binding(nameof(model.Scopes)));
        scope.Bind(ComboBox.SelectedItemProperty,new Binding(nameof(model.SelectedScope)) { Mode = BindingMode.TwoWay });
        scope.Bind(Control.IsEnabledProperty,new Binding(nameof(model.CanGenerate)));
        var first = new NumericUpDown { Minimum = 0, Maximum = 100000, HorizontalAlignment = HorizontalAlignment.Stretch };
        var last = new NumericUpDown { Minimum = 0, Maximum = 100000, HorizontalAlignment = HorizontalAlignment.Stretch };
        first.Bind(NumericUpDown.ValueProperty,new Binding(nameof(model.FirstLine)) { Mode = BindingMode.TwoWay });
        last.Bind(NumericUpDown.ValueProperty,new Binding(nameof(model.LastLine)) { Mode = BindingMode.TwoWay });
        var rangeGrid = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12 };
        rangeGrid.Children.Add(Ui.Field("От строки",first)); rangeGrid.Children.Add(Ui.Field("До строки",last).Place(0,1));
        var rangeBody = Ui.Stack(rangeGrid,Ui.Text("Оставьте 0 / 0, чтобы передать выбранную функцию или весь скрипт.","muted",11)); rangeBody.Margin = new(14);
        var range = new Expander { Header = "Выбрать диапазон строк", Content = rangeBody, HorizontalAlignment = HorizontalAlignment.Stretch };
        var versions = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<ScriptVersion>((x,_) => Ui.Text(x?.Description ?? "",size:12)) };
        versions.Bind(ItemsControl.ItemsSourceProperty,new Binding(nameof(model.Versions)));
        versions.Bind(ComboBox.SelectedItemProperty,new Binding(nameof(model.SelectedVersion)) { Mode = BindingMode.TwoWay });
        var versionField = Ui.Field("Предыдущая версия для сравнения",versions); versionField.Bind(Control.IsVisibleProperty,new Binding(nameof(model.IsVersionTask)));
        var fields = Ui.Stack(Ui.Field("Скрипт",entry),Ui.Bound(nameof(model.ContextStatus),"muted"),Ui.Field("Фрагмент",scope),range,versionField);
        fields.Margin = new(20); var panel = new Border { Child = fields, Classes = { "libraryPane" } };
        var remove = Ui.Action("Открепить скрипт",() => { model.SelectedEntry = null; window.Close(); },"ghost"); remove.Name = "AiRemoveContext";
        var refresh = Ui.Button("Перечитать файл",model.PrepareAsync,"ghost"); refresh.Bind(Control.IsEnabledProperty,new Binding(nameof(model.CanGenerate)));
        var footer = new Grid { ColumnDefinitions = new("*,Auto") }; footer.Children.Add(remove);
        footer.Children.Add(Ui.Action("Готово",() => window.Close(),"primary").Place(0,1));
        var body = Ui.Stack(Ui.Text("Прикрепить скрипт", "heading",24),Ui.Text("SC AI увидит только выбранный скрипт или его часть.","muted",13),panel,refresh,
            Ui.Text("Читается только выбранный фрагмент. Открытие контекста не запускает скрипт и не загружает модель в память.","muted",11));
        var root = new Grid { RowDefinitions = new("*,Auto"), RowSpacing = 20, Margin = new(24) };
        root.Children.Add(Ui.Scroll(body)); root.Children.Add(footer.Place(1,0)); window.Content = root;
        window.Closed += (_,_) => { model.Library.PropertyChanged -= LibraryChanged; model.PropertyChanged -= ModelChanged; };
        await window.ShowDialog(owner);
    }
}
