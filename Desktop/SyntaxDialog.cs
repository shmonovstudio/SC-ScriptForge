using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.Services;

namespace SC.ScriptForge;

public enum SyntaxDialogChoice { Close, Edit, AskAi }

/// <summary>Parser report: errors grouped by line, the offending code highlighted in place, and a short hint for common mistakes.</summary>
public static class SyntaxDialog
{
    private static readonly FontFamily Mono = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas");

    public static async Task<SyntaxDialogChoice> ShowAsync(Window owner, string scriptName, SyntaxCheckResult result, bool canEdit = true, bool canAskAi = true)
    {
        var dialog = Dialogs.Window(result.Success ? "Синтаксис проверен" : "Ошибка синтаксиса", result.Success ? 600 : 860, result.Success ? 330 : 720);
        var root = new Grid { RowDefinitions = new("Auto,*,Auto"), RowSpacing = 18, Margin = new(26, 24, 26, 22) };

        var groups = result.Issues.GroupBy(i => i.Line).OrderBy(g => g.Key).ToArray();
        var title = result.Success ? "Синтаксис в порядке"
            : result.Issues.Count == 0 ? "Не удалось разобрать скрипт"
            : "Найдено " + Feedback.Plural(result.Issues.Count, "ошибка", "ошибки", "ошибок") + (groups.Length > 1 ? " в " + Feedback.Plural(groups.Length, "строке", "строках", "строках") : groups.Length == 1 ? " в строке " + groups[0].Key : "");
        var heading = Ui.Text(title, "heading", 22);
        var eyebrow = Ui.Text("ПРОВЕРКА СИНТАКСИСА", "eyebrow");
        var subtitle = Ui.Text(scriptName + "  ·  " + result.Engine, "muted", 12);
        var header = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 16 };
        header.Children.Add(Feedback.Badge(result.Success ? "ok" : "failed", result.Success ? Tone.Success : Tone.Danger));
        header.Children.Add(Ui.Stack(eyebrow, heading, subtitle).Also(s => s.Spacing = 4).Place(0, 1));
        root.Children.Add(header);

        if (result.Success)
        {
            root.Children.Add(Feedback.Callout(Tone.Success, "shield", Ui.Text("Парсер PowerShell не нашёл ошибок. Скрипт при проверке не запускался — проверялась только структура кода.", "soft")).Also(c => c.VerticalAlignment = VerticalAlignment.Top).Place(1, 0));
        }
        else
        {
            var list = Ui.Stack(); list.Spacing = 14;
            list.Children.Add(Ui.Text("Скрипт не запускался — проверялась только структура кода. Исправьте отмеченные места и повторите проверку.", "muted", 12));
            if (groups.Length == 0)
                list.Children.Add(CodeBlock((string.IsNullOrWhiteSpace(result.Raw) ? "PowerShell не вернул подробностей." : result.Raw).Split('\n').Select(t => new SyntaxContextLine(0, t.TrimEnd('\r'))).ToArray(), []));
            foreach (var group in groups) list.Children.Add(IssueCard(group.Key, group.ToArray()));
            root.Children.Add(Ui.Scroll(list).Place(1, 0));
        }

        var choice = SyntaxDialogChoice.Close;
        var buttons = Ui.Row(); buttons.HorizontalAlignment = HorizontalAlignment.Right;
        if (!result.Success)
        {
            var copy = Ui.WithIcon(Ui.Button("Копировать", async () => { if (dialog.Clipboard is { } clipboard) await clipboard.SetTextAsync(scriptName + "\n\n" + result.Message); }, "ghost"), "copy");
            buttons.Children.Add(copy);
            if (canAskAi) buttons.Children.Add(Ui.WithIcon(Ui.Action("Исправить с SC AI", () => { choice = SyntaxDialogChoice.AskAi; dialog.Close(); }), "wand"));
            if (canEdit) buttons.Children.Add(Ui.WithIcon(Ui.Action("Открыть в редакторе", () => { choice = SyntaxDialogChoice.Edit; dialog.Close(); }, "primary"), "edit"));
        }
        buttons.Children.Add(Ui.Action("Закрыть", () => dialog.Close(), result.Success ? "primary" : null));
        root.Children.Add(buttons.Place(2, 0));
        dialog.Content = root;
        await dialog.ShowDialog(owner);
        return choice;
    }

    private static Control IssueCard(int line, IReadOnlyList<SyntaxIssue> issues)
    {
        var top = Ui.Row(Feedback.Chip("Строка " + line, Tone.Danger, "failed"),
            Ui.Text(issues.Count == 1 ? "столбец " + issues[0].Column : "столбцы " + string.Join(", ", issues.Select(i => i.Column).Distinct()), "muted", 11));
        var body = Ui.Stack(top); body.Spacing = 12;

        var messages = Ui.Stack(); messages.Spacing = 6;
        foreach (var message in issues.GroupBy(i => i.Message))
        {
            var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new(3), Margin = new(2, 7, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            Ui.Resource(dot, Border.BackgroundProperty, "ScDanger");
            var text = new SelectableTextBlock { Text = message.Key + (message.Count() > 1 ? "  ×" + message.Count() : ""), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            row.Children.Add(dot); row.Children.Add(text.Place(0, 1)); messages.Children.Add(row);
        }
        body.Children.Add(messages);

        var context = issues.SelectMany(i => i.Context).GroupBy(c => c.Number).Select(g => g.First()).OrderBy(c => c.Number).ToArray();
        if (context.Length > 0) body.Children.Add(CodeBlock(context, issues));

        foreach (var hint in issues.Select(i => i.Hint).Where(h => h.Length > 0).Distinct())
            body.Children.Add(Feedback.Callout(Tone.Warning, "wand", Ui.Text(hint, "soft", 12), 12));
        return Feedback.Panel(body, 18);
    }

    /// <summary>Monospace excerpt: shared indentation removed, error lines tinted, exact error ranges marked over the syntax colours.</summary>
    private static Control CodeBlock(IReadOnlyList<SyntaxContextLine> lines, IReadOnlyList<SyntaxIssue> issues)
    {
        var indent = lines.Where(l => l.Text.Trim().Length > 0).Select(l => l.Text.Length - l.Text.TrimStart().Length).DefaultIfEmpty(0).Min();
        var width = Math.Max(2, lines.Max(l => l.Number).ToString().Length);
        // Errors far to the right of a long line would be hidden behind the scroll; start the excerpt shortly before the first one.
        var firstError = issues.Select(i => i.Column - 1 - indent).DefaultIfEmpty(0).Min();
        var shift = firstError > 70 ? firstError - 36 : 0;
        var stack = new StackPanel { Margin = new(0, 8) };
        foreach (var line in lines)
        {
            var text = line.Text.Length >= indent ? line.Text[indent..] : line.Text.TrimStart();
            // Lines too short to reach the excerpt window stay whole: they are context only.
            var shifted = shift > 0 && text.Length > shift;
            if (shifted) text = "…" + text[shift..];
            var offset = shifted ? indent + shift - 1 : indent;
            var ranges = issues.Where(i => i.Line <= line.Number && Math.Max(i.Line, i.EndLine) >= line.Number).Select(i =>
            {
                var start = i.Line == line.Number ? Math.Max(0, i.Column - 1 - offset) : 0;
                var end = i.EndLine == line.Number ? Math.Max(start, i.EndColumn - 1 - offset) : text.Length;
                return (Start: Math.Min(start, text.Length), End: Math.Min(Math.Max(end, start + 1), text.Length + 1));
            }).ToArray();
            var isError = ranges.Length > 0 && issues.Any(i => i.Line == line.Number);

            var row = new Grid { ColumnDefinitions = new("3,Auto,*") };
            var bar = new Border(); if (isError) Ui.Resource(bar, Border.BackgroundProperty, "ScDanger");
            var number = new TextBlock { Text = line.Number > 0 ? line.Number.ToString().PadLeft(width) : "", FontFamily = Mono, FontSize = 12, Margin = new(12, 3, 14, 3), Opacity = isError ? 1 : .55 };
            Ui.Resource(number, TextBlock.ForegroundProperty, isError ? "ScDanger" : "ScMuted");
            var code = new SelectableTextBlock { FontFamily = Mono, FontSize = 12.5, Margin = new(0, 3, 16, 3), TextWrapping = TextWrapping.NoWrap, Inlines = Highlighted(text, ranges) };
            Ui.Resource(code, TextBlock.ForegroundProperty, "ScSoft");
            row.Children.Add(bar); row.Children.Add(number.Place(0, 1)); row.Children.Add(code.Place(0, 2));
            var holder = new Border { Child = row }; if (isError) Ui.Resource(holder, Border.BackgroundProperty, "ScDangerSoft");
            stack.Children.Add(holder);
        }
        var scroller = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var frame = new Border { Child = scroller, CornerRadius = new(10), BorderThickness = new(1), ClipToBounds = true };
        Ui.Resource(frame, Border.BackgroundProperty, "ScCodeBackground"); Ui.Resource(frame, Border.BorderBrushProperty, "ScBorder");
        return frame;
    }

    private static InlineCollection Highlighted(string text, IReadOnlyList<(int Start, int End)> ranges)
    {
        var inlines = new InlineCollection();
        var marked = new bool[text.Length + 1];
        foreach (var (start, end) in ranges) for (var i = start; i < end && i < marked.Length; i++) marked[i] = true;
        var position = 0;
        foreach (var (value, resource) in ScriptCodeView.Highlight(text))
        {
            // Split each syntax token wherever the error mark starts or stops.
            var offset = 0;
            while (offset < value.Length)
            {
                var flag = marked[position + offset]; var length = 1;
                while (offset + length < value.Length && marked[position + offset + length] == flag) length++;
                inlines.Add(Run(value.Substring(offset, length), resource, flag));
                offset += length;
            }
            position += value.Length;
        }
        // An error at the very end of the line (missing brace, quote…) gets a visible marker cell.
        if (marked[text.Length]) inlines.Add(Run(" ", null, true));
        return inlines;
    }

    private static Run Run(string value, string? resource, bool error)
    {
        var run = new Run(value);
        if (error)
        {
            Ui.Resource(run, TextElement.BackgroundProperty, "ScDangerMark"); Ui.Resource(run, TextElement.ForegroundProperty, "ScText");
            run.FontWeight = FontWeight.SemiBold; run.TextDecorations = TextDecorations.Underline;
        }
        else if (resource is not null) Ui.Resource(run, TextElement.ForegroundProperty, resource);
        return run;
    }
}
