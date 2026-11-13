using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Automation;
using Lucide.Avalonia;

namespace SC.ScriptForge;

public static class Ui
{
    public static bool ReduceMotion { get => Motion.Reduced; set => Motion.Reduced = value; }
    public static TextBlock Text(string text, string? style = null, double? size = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (style is not null) block.Classes.Add(style); if (size.HasValue) block.FontSize = size.Value;
        return block;
    }
    public static TextBlock Bound(string path, string? style = null) { var block = Text("", style); block.Bind(TextBlock.TextProperty, new Binding(path)); return block; }
    public static StackPanel Stack(params Control[] children)
    { var panel = new StackPanel { Spacing = 12 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    public static StackPanel Row(params Control[] children)
    { var panel = Stack(children); panel.Orientation = Orientation.Horizontal; panel.Spacing = 8; return panel; }
    public static Border Card(Control content)
    {
        var card = new Border { Child = content, Classes = { "card" } };
        Motion.Card(card); return card;
    }
    public static Button Button(string label, Func<Task> action, string? style = null)
    {
        var button = new Button { Content = label }; if (style is not null) button.Classes.Add(style);
        ConfigureMotion(button);
        AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await action(); return button;
    }
    public static Button Action(string label, Action action, string? style = null) => Button(label, () => { action(); return Task.CompletedTask; }, style);
    public static void ConfigureMotion(Button button) => Motion.Button(button);
    public static Border IconBadge(string icon, double size = 20)
    {
        var glyph = Icon(icon, size); glyph.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Child = glyph, Width = size + 24, Height = size + 24, CornerRadius = new(12), Classes = { "iconBadge" } };
        return badge;
    }
    public static Border Pill(string text, bool accent = false)
    {
        var pill = new Border { Child = Text(text, "muted", 11), Classes = { "pill" } }; return pill;
    }
    public static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
    public static TextBox Input(string? value = null, string? hint = null, bool multiline = false) => new()
    { Text = value ?? "", PlaceholderText = hint, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 90 : 38 };
    public static Control Field(string label, Control input) { AutomationProperties.SetName(input, label); var field = Stack(Text(label, "soft", 11.5), input); field.Spacing = 5; return field; }
    public static void Resource(AvaloniaObject control, AvaloniaProperty property, string key) => control.Bind(property, new DynamicResourceExtension(key));
    public static Button WithIcon(Button button, string icon, bool trailing = false)
    {
        var label = Text(button.Content?.ToString() ?? "");
        button.Content = trailing ? Row(label, Icon(icon, 15)) : Row(Icon(icon, 15), label);
        return button;
    }
    public static LucideIcon Icon(string name, double size = 20)
    {
        var kind = name switch
        {
            "home" => LucideIconKind.House,
            "computer" => LucideIconKind.Monitor,
            "script" => LucideIconKind.CodeXml,
            "star" => LucideIconKind.Star,
            "folder" => LucideIconKind.Folder,
            "trash" => LucideIconKind.Trash,
            "history" => LucideIconKind.RotateCcwClock,
            "settings" => LucideIconKind.Settings,
            "tools" => LucideIconKind.Wrench,
            "cpu" => LucideIconKind.Cpu,
            "network" => LucideIconKind.Network,
            "memory" => LucideIconKind.MemoryStick,
            "disk" => LucideIconKind.HardDrive,
            "gpu" => LucideIconKind.Gpu,
            "bios" => LucideIconKind.CircuitBoard,
            "play" => LucideIconKind.Play,
            "edit" => LucideIconKind.Pencil,
            "more" => LucideIconKind.Ellipsis,
            "copy" => LucideIconKind.Copy,
            "chevron" => LucideIconKind.ChevronRight,
            "ai" => LucideIconKind.Sparkles,
            "arrowup" => LucideIconKind.ArrowUp,
            "arrowdiag" => LucideIconKind.ArrowUpRight,
            "arrowright" => LucideIconKind.ArrowRight,
            "chevrondown" => LucideIconKind.ChevronDown,
            "close" => LucideIconKind.X,
            "stop" => LucideIconKind.Square,
            "search" => LucideIconKind.Search,
            "newchat" => LucideIconKind.SquarePen,
            "theme" => LucideIconKind.SunMoon,
            "status" => LucideIconKind.Activity,
            "plus" => LucideIconKind.Plus,
            "import" => LucideIconKind.Import,
            "export" => LucideIconKind.Download,
            "check" => LucideIconKind.ShieldCheck,
            "document" => LucideIconKind.FileText,
            "ask" => LucideIconKind.MessageCircleQuestionMark,
            "tag" => LucideIconKind.Tag,
            "bug" => LucideIconKind.Bug,
            "shield" => LucideIconKind.ShieldCheck,
            "speed" => LucideIconKind.Gauge,
            "comment" => LucideIconKind.MessageSquareText,
            "wand" => LucideIconKind.WandSparkles,
            "attach" => LucideIconKind.Paperclip,
            "eye" => LucideIconKind.Eye,
            "save" => LucideIconKind.Save,
            "server" => LucideIconKind.Server,
            "plug" => LucideIconKind.PlugZap,
            "leaf" => LucideIconKind.Leaf,
            "rocket" => LucideIconKind.Rocket,
            "sliders" => LucideIconKind.SlidersHorizontal,
            "refresh" => LucideIconKind.RefreshCw,
            "ok" => LucideIconKind.CircleCheck,
            "warning" => LucideIconKind.TriangleAlert,
            "info" => LucideIconKind.Info,
            "send" => LucideIconKind.SendHorizontal,
            "folderopen" => LucideIconKind.FolderOpen,
            "terminal" => LucideIconKind.SquareTerminal,
            "heart" => LucideIconKind.Heart,
            "list" => LucideIconKind.List,
            "brain" => LucideIconKind.BrainCircuit,
            "users" => LucideIconKind.Users,
            "mail" => LucideIconKind.Mail,
            "window" => LucideIconKind.AppWindow,
            "key" => LucideIconKind.KeyRound,
            "cloud" => LucideIconKind.Cloud,
            "globe" => LucideIconKind.Globe,
            "layers" => LucideIconKind.Layers,
            "library" => LucideIconKind.Library,
            "flame" => LucideIconKind.Flame,
            "clock" => LucideIconKind.Clock,
            "grid" => LucideIconKind.LayoutGrid,
            "chart" => LucideIconKind.ChartColumn,
            "trend" => LucideIconKind.TrendingUp,
            "failed" => LucideIconKind.CircleX,
            "stopped" => LucideIconKind.CircleStop,
            _ => LucideIconKind.FileText
        };
        return new LucideIcon { Kind = kind, Size = size, StrokeWidth = 1.8, Classes = { "glyph" }, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }
}
