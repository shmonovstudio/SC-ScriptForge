using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SC.ScriptForge;

public enum Tone { Neutral, Info, Success, Warning, Danger }

/// <summary>Tinted status building blocks shared by the syntax report and run results. Colours come from theme resources.</summary>
public static class Feedback
{
    public static (string Fore, string Back, string Line) Keys(Tone tone) => tone switch
    {
        Tone.Success => ("ScSuccess", "ScSuccessSoft", "ScSuccessLine"),
        Tone.Warning => ("ScWarning", "ScWarningSoft", "ScWarningLine"),
        Tone.Danger => ("ScDanger", "ScDangerSoft", "ScDangerLine"),
        Tone.Info => ("ScLink", "ScLinkSoft", "ScLinkLine"),
        _ => ("ScMuted", "ScQuiet", "ScBorder")
    };

    public static Control Icon(string name, Tone tone, double size = 16)
    {
        var icon = Ui.Icon(name, size); icon.Classes.Remove("glyph");
        Ui.Resource(icon, Lucide.Avalonia.LucideIcon.ForegroundProperty, Keys(tone).Fore); return icon;
    }

    /// <summary>Rounded square with a tinted icon, used as the visual anchor of a header.</summary>
    public static Border Badge(string icon, Tone tone, double size = 22)
    {
        var badge = new Border { Child = Icon(icon, tone, size), Width = size + 26, Height = size + 26, CornerRadius = new(14), BorderThickness = new(1), VerticalAlignment = VerticalAlignment.Top };
        var keys = Keys(tone); Ui.Resource(badge, Border.BackgroundProperty, keys.Back); Ui.Resource(badge, Border.BorderBrushProperty, keys.Line); return badge;
    }

    public static Border Chip(string text, Tone tone, string? icon = null)
    {
        var label = Ui.Text(text, null, 11); label.FontWeight = FontWeight.SemiBold; Ui.Resource(label, TextBlock.ForegroundProperty, Keys(tone).Fore);
        Control content = icon is null ? label : Ui.Row(Icon(icon, tone, 12), label).Also(r => r.Spacing = 5);
        var chip = new Border { Child = content, CornerRadius = new(999), Padding = new(9, 3), BorderThickness = new(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var keys = Keys(tone); Ui.Resource(chip, Border.BackgroundProperty, keys.Back); Ui.Resource(chip, Border.BorderBrushProperty, keys.Line); return chip;
    }

    /// <summary>Tinted panel with a leading icon.</summary>
    public static Border Callout(Tone tone, string icon, Control content, double padding = 14)
    {
        var grid = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 12 };
        var glyph = Icon(icon, tone, 17); glyph.VerticalAlignment = VerticalAlignment.Top; glyph.Margin = new(0, 1, 0, 0);
        grid.Children.Add(glyph); grid.Children.Add(content.Place(0, 1));
        var border = new Border { Child = grid, CornerRadius = new(12), Padding = new(padding), BorderThickness = new(1) };
        var keys = Keys(tone); Ui.Resource(border, Border.BackgroundProperty, keys.Back); Ui.Resource(border, Border.BorderBrushProperty, keys.Line); return border;
    }

    /// <summary>Neutral surface without hover motion, for content that is read rather than clicked.</summary>
    public static Border Panel(Control content, double padding = 16)
    {
        var border = new Border { Child = content, CornerRadius = new(14), Padding = new(padding), BorderThickness = new(1) };
        Ui.Resource(border, Border.BackgroundProperty, "ScSurface"); Ui.Resource(border, Border.BorderBrushProperty, "ScBorder"); return border;
    }

    public static TextBlock Toned(string text, Tone tone, double? size = null, FontWeight? weight = null)
    {
        var block = Ui.Text(text, null, size); Ui.Resource(block, TextBlock.ForegroundProperty, Keys(tone).Fore);
        if (weight.HasValue) block.FontWeight = weight.Value; return block;
    }

    internal static T Also<T>(this T value, Action<T> configure) { configure(value); return value; }

    public static string Plural(int count, string one, string few, string many)
    {
        var n = Math.Abs(count) % 100; var last = n % 10;
        return count + " " + (n is > 10 and < 20 ? many : last == 1 ? one : last is >= 2 and <= 4 ? few : many);
    }
}
