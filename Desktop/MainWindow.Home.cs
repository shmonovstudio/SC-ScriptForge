using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.Models;

namespace SC.ScriptForge;

public sealed partial class MainWindow
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private Control HomeView()
    {
        var root = new Grid { Name = "HomePage", MaxWidth = 1600, ColumnDefinitions = new(_compact ? "*" : "*,320") };
        var page = Ui.Stack(); page.Spacing = 25; page.Margin = new(32,34,28,28);
        page.Children.Add(ReferenceUi.Heading("Ваш центр управления", "Дашборд", "Скрипты, автоматизация и результаты — в одном месте",
            Ui.WithIcon(Command("Новый скрипт", CreateAsync, "ink"), "plus")));
        page.Children.Add(HomeHero());
        var stats = new Grid { Name = "HomeStats", ColumnDefinitions = new(_compact ? "*,*" : "*,*,*,*"), RowDefinitions = new(_compact ? "Auto,Auto" : "Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        var values = new[] { ("Скриптов", _library.Entries.Count, "document"), ("Категорий", _library.Entries.Select(e => e.Category).Distinct().Count(), "layers"),
            ("Успешных запусков", _history.Recent.Count(r => r.Outcome == RunOutcome.Success), "ok"), ("В избранном", _library.Entries.Count(e => e.Favorite), "star") };
        foreach (var (label,value,icon) in values)
        {
            var index = stats.Children.Count;
            var badge = Ui.IconBadge(icon,20); badge.Name = "HomeStatIcon_" + index; badge.Width = badge.Height = 42; badge.Classes.Add("featured");
            var number = Ui.Text(value.ToString("N0",Russian), "section", 26); number.Name = "HomeStatValue_" + index;
            var text = Ui.Stack(number,Ui.Text(label,"muted",11.5)); text.Spacing = 3;
            var content = new Grid { ColumnDefinitions = new("42,*"), ColumnSpacing = 14 }; content.Children.Add(badge); content.Children.Add(text.Place(0,1));
            var card = Ui.Card(content); card.Name = "HomeStat_" + index; card.MinHeight = 100;
            stats.Children.Add(card.Place(_compact ? index / 2 : 0,_compact ? index % 2 : index));
        }
        page.Children.Add(stats);
        if (_library.Entries.Count == 0) page.Children.Add(HomeEmpty());
        else { page.Children.Add(HomeCategories()); page.Children.Add(HomeScripts()); }
        root.Children.Add(Ui.Scroll(page));
        if (!_compact) root.Children.Add(ReferenceUi.Side(Ui.Scroll(HomeSidebar()),24).Place(0,1));
        return root;
    }
    private Control HomeHero()
    {
        var copy = Ui.Stack(Ui.Text("Больше времени для важного", "eyebrow"), Ui.Text("Автоматизируйте рутину.\nУправляйте с уверенностью.", "heading", 27),
            Ui.Text("Готовые PowerShell-скрипты для системы,\nсети и ежедневных задач администратора.", "soft", 12.5)); copy.Spacing = 13;
        var actions = Ui.Row(Ui.WithIcon(Ui.Action("Открыть каталог", () => Navigate("library"), "primary"),"grid"),
            Ui.WithIcon(Ui.Action("ИИ-помощник", () => Navigate("ai")),"ai")); actions.Margin = new(0,5,0,0); copy.Children.Add(actions);
        var grid = new Grid { ColumnDefinitions = new("*,150"), ColumnSpacing = 18 }; grid.Children.Add(copy);
        var graphic = new Grid { Width = 140, Height = 160, VerticalAlignment = VerticalAlignment.Center };
        graphic.Children.Add(new Border { Width = 104, Height = 132, CornerRadius = new(16), Background = Brush.Parse("#D8F3F5"), BorderBrush = Brush.Parse("#B6E9ED"), BorderThickness = new(1), RenderTransform = new RotateTransform(-12), Margin = new(-22,-8,0,0) });
        var terminal = Ui.Icon("terminal",68); terminal.Foreground = Brush.Parse("#5BE4E7");
        graphic.Children.Add(new Border { Child = terminal, Width = 116, Height = 116, CornerRadius = new(20), Background = Brush.Parse("#172C3A"), BorderBrush = Brush.Parse("#285467"), BorderThickness = new(1), Margin = new(16,22,0,0), BoxShadow = BoxShadows.Parse("0 12 30 -12 #403A6874") });
        grid.Children.Add(graphic.Place(0,1));
        return new Border { Name = "HomeHero", Child = grid, Classes = { "homeHero" }, Padding = new(25), MinHeight = 240 };
    }
    private Control HomeCategories()
    {
        var categories = _library.Entries.GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(4).ToArray();
        var tiles = new Grid { Name = "HomeCategoryTiles", ColumnDefinitions = new(_compact ? "*,*" : "*,*,*,*"), RowDefinitions = new(_compact ? "Auto,Auto" : "Auto"), ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var group in categories)
        {
            var category = group.Key;
            var button = Ui.Action(category, () => { _library.Category = category; Navigate("library"); });
            button.Classes.Add("homeCategory"); button.MinHeight = 126; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.VerticalAlignment = VerticalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center; button.Padding = new(14,16);
            var icon = Ui.IconBadge(ReferenceUi.CategoryIcon(category),21); icon.Width = icon.Height = 40; icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Classes.Add("featured");
            var label = ReferenceUi.Ellipsis(ReferenceUi.Category(category),"section",11.5); label.TextAlignment = TextAlignment.Center;
            var count = Ui.Text(Count(group.Count(),"скрипт","скрипта","скриптов"),"muted",10.5); count.TextAlignment = TextAlignment.Center;
            var content = Ui.Stack(icon,label,count); content.Spacing = 8; button.Content = content;
            var index = tiles.Children.Count; tiles.Children.Add(button.Place(_compact ? index / 2 : 0,_compact ? index % 2 : index));
        }
        var caption = new Grid { ColumnDefinitions = new("*,Auto") }; caption.Children.Add(Ui.Text("Категории","section",16));
        caption.Children.Add(Ui.WithIcon(Ui.Action("Все категории",()=>Navigate("library"),"link"),"arrowright",true).Place(0,1));
        return Ui.Stack(caption,tiles);
    }
    private Control HomeScripts()
    {
        var caption = new Grid { ColumnDefinitions = new("*,Auto") }; caption.Children.Add(Ui.Text("Недавние скрипты","section",16));
        caption.Children.Add(Ui.Action("Открыть каталог →",()=>Navigate("library"),"link").Place(0,1));
        var rows = Ui.Stack(); rows.Spacing = 0;
        foreach (var entry in _library.Entries.OrderByDescending(e => e.LastModified).Take(5))
        {
            var button = Ui.Action(entry.DisplayName, () => { Navigate("library"); _library.Select(entry.Id); }); button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Classes.Add("homeRecent"); button.Padding = new(14,12); button.Content = HomeScriptRow(entry); rows.Children.Add(button);
        }
        return Ui.Stack(caption,rows);
    }
    private Control HomeScriptRow(ScriptEntry entry)
    {
        var row = new Grid { ColumnDefinitions = new("42,*,106,16"), ColumnSpacing = 14 };
        var icon = Ui.IconBadge("terminal",20); icon.Width = icon.Height = 42; row.Children.Add(icon);
        var copy = Ui.Stack(ReferenceUi.Ellipsis(entry.DisplayName,"section",13.5),ReferenceUi.Ellipsis(entry.DisplayDescription,"muted",11.5),ReferenceUi.Tags(entry)); copy.Spacing = 4; row.Children.Add(copy.Place(0,1));
        var date = Ui.Text("Обновлен\n" + entry.LastModified.ToString("dd MMM yyyy",Russian),"muted",10.5); date.TextAlignment = TextAlignment.Right; row.Children.Add(date.Place(0,2));
        var chevron = Ui.Icon("chevron",15); chevron.HorizontalAlignment = HorizontalAlignment.Center; row.Children.Add(chevron.Place(0,3)); row.ContextMenu = ScriptMenu(entry); return row;
    }
    private Control HomeSidebar()
    {
        var updates = Ui.Stack(ReferenceUi.SmallTitle("Последние обновления")); updates.Spacing = 22;
        foreach (var entry in _library.Entries.OrderByDescending(e=>e.LastModified).Take(5))
        {
            var row = new Grid { ColumnDefinitions = new("36,*"), ColumnSpacing = 12 };
            var icon = Ui.IconBadge(ReferenceUi.CategoryIcon(entry.Category),16); icon.Width = icon.Height = 36; row.Children.Add(icon);
            var copy = Ui.Stack(ReferenceUi.Ellipsis(entry.DisplayName,"section",12),Ui.Text(entry.LastModified.ToString("dd MMMM yyyy",Russian),"muted",10.5)); copy.Spacing = 4; row.Children.Add(copy.Place(0,1));
            var button = Ui.Action(entry.DisplayName,()=>{Navigate("library");_library.Select(entry.Id);},"ghost"); button.Content=row; button.Padding=new(0); button.HorizontalAlignment=HorizontalAlignment.Stretch; button.HorizontalContentAlignment=HorizontalAlignment.Stretch; updates.Children.Add(button);
        }
        if (_library.Entries.Count == 0) updates.Children.Add(Ui.Text("После добавления скриптов здесь появятся обновления.","muted",12));
        var environment = Ui.Stack(Ui.Row(Ui.IconBadge("computer",18),Ui.Text("Рабочее окружение","section",13)),
            ReferenceUi.Metadata("computer","Компьютер",Environment.MachineName), ReferenceUi.Metadata("terminal","Профиль","PowerShell"),
            ReferenceUi.Metadata("shield","Выполнение","Локально"),Ui.WithIcon(Ui.Action("Сведения о системе",()=>Navigate("computer")),"arrowright",true)); environment.Spacing=17;
        var shortcuts = Ui.Stack(ReferenceUi.SmallTitle("Быстрые действия"),
            Ui.WithIcon(Command("Импортировать скрипты",ImportAsync),"import"), Ui.WithIcon(Ui.Action("История запусков",()=>Navigate("history")),"clock"), Ui.WithIcon(Ui.Action("Настройки приложения",()=>Navigate("settings")),"settings"));
        foreach(var button in shortcuts.Children.OfType<Button>()) {button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;}
        var tip = Ui.Card(Ui.Stack(Ui.Row(Ui.Icon("ai",18),Ui.Text("Совет","accent",12)),Ui.Text("Добавляйте часто используемые скрипты в избранное, чтобы находить их быстрее.","muted",12))); Ui.Resource(tip,Border.BackgroundProperty,"ScSelected");
        var root=Ui.Stack(updates,ReferenceUi.Line(),Ui.Card(environment),shortcuts,tip); root.Spacing=28; return root;
    }
    private Control HomeEmpty()
    {
        return Ui.Card(Ui.Stack(Ui.Text("Добавьте первый скрипт","section",18),Ui.Text("Импортируйте готовый .ps1 или создайте скрипт с ИИ-помощником.","muted",13),
            Ui.Row(Ui.WithIcon(Command("Импортировать .ps1",ImportAsync,"primary"),"import"),Ui.WithIcon(Ui.Action("Написать скрипт",()=>Navigate("ai")),"ai"))));
    }
    private static string Count(int value,string one,string few,string many)
    {
        var tens=Math.Abs(value)%100;var units=tens%10;
        return value.ToString("N0",Russian)+" "+(tens is >=11 and <=14?many:units==1?one:units is >=2 and <=4?few:many);
    }
}
