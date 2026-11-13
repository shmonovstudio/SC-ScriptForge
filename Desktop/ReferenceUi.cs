using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.Models;
using SC.ScriptForge.Services;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

internal static class ReferenceUi
{
    public static Control Heading(string eyebrow, string title, string description, Control? action = null)
    {
        var copy = Ui.Stack(Ui.Text(eyebrow.ToUpperInvariant(), "eyebrow"), Ui.Text(title, "heading"), Ui.Text(description, "muted", 12.5));
        copy.Spacing = 8;
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12 };
        grid.Children.Add(copy);
        if (action is not null) grid.Children.Add(action.Place(0, 1));
        return grid;
    }
    public static Border Line() { var line = new Border { Height = 1 }; Ui.Resource(line, Border.BackgroundProperty, "ScLine"); return line; }
    public static Border Side(Control content, double padding = 28)
    {
        var border = new Border { Child = content, Padding = new(padding, 28), BorderThickness = new(1,0,0,0) };
        Ui.Resource(border, Border.BackgroundProperty, "ScSidebar"); Ui.Resource(border, Border.BorderBrushProperty, "ScLine"); return border;
    }
    public static TextBlock SmallTitle(string text) { var block = Ui.Text(text.ToUpperInvariant(), "navLabel"); return block; }
    public static TextBlock Ellipsis(string text, string? style = null, double size = 12)
    { var label = Ui.Text(text, style, size); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(label, text); return label; }
    public static Button IconButton(string label, string icon, Action action)
    { var button = Ui.Action(label, action, "iconAction"); button.Content = Ui.Icon(icon, 16); ToolTip.SetTip(button, label); return button; }
    public static Control Metadata(string icon, string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new("18,*,1.25*"), ColumnSpacing = 10 };
        row.Children.Add(Ui.Icon(icon, 15)); row.Children.Add(Ui.Text(label, "muted", 12).Place(0, 1)); row.Children.Add(Ui.Text(value, "soft", 12).Place(0, 2)); return row;
    }
    public static string Category(string category) => category switch
    {
        "Windows" => LibraryLocalization.Text("Windows и компьютеры", "Windows and computers"),
        "Network" => LibraryLocalization.Text("Сеть", "Network"), "Files" => LibraryLocalization.Text("Файлы и папки", "Files and folders"),
        "Security" => LibraryLocalization.Text("Безопасность", "Security"), "Servers" => LibraryLocalization.Text("Серверы", "Servers"),
        "Certificates" => LibraryLocalization.Text("Сертификаты", "Certificates"), "Disks" => LibraryLocalization.Text("Диски", "Disks"),
        "Services" => LibraryLocalization.Text("Службы", "Services"), "Other" => LibraryLocalization.Text("Другое", "Other"), _ => category
    };
    public static string CategoryIcon(string category) => category.ToLowerInvariant() switch
    { "windows" or "система" => "computer", "active directory" => "users", "certificates" or "сертификаты" => "key", "network" or "сеть" => "globe", "files" => "folder", "servers" => "computer", "security" or "безопасность" => "shield", "disks" or "диски" => "disk", "services" or "службы" => "settings", _ => "document" };
    public static Control Tags(ScriptEntry entry)
    {
        var tags = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var tag in new[] { Category(entry.Category) }.Concat(entry.Tags).Distinct().Take(3)) { var pill = Ui.Pill(tag); pill.Margin = new(0,0,6,3); tags.Children.Add(pill); }
        return tags;
    }
}

public sealed partial class MainWindow
{
    private static Control PaddedPage(Control content) => new Border { Child = content, Padding = new(30,28) };
    private Control PageSearch(string hint)
    {
        var input = Ui.Input(hint: hint); input.Name = "PageSearch"; input.DataContext = _library; input.MinHeight = 43; input.Padding = new(40,11,54,11);
        input.Bind(TextBox.TextProperty, new Binding(nameof(LibraryViewModel.Query)) { Mode = BindingMode.TwoWay });
        var grid = new Grid(); grid.Children.Add(input);
        var icon = Ui.Icon("search", 15); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new(15,0); grid.Children.Add(icon);
        var keys = Ui.Text("Ctrl K", "muted", 10); keys.HorizontalAlignment = HorizontalAlignment.Right; keys.Margin = new(0,0,13,0); keys.IsHitTestVisible = false; grid.Children.Add(keys); return grid;
    }
}
