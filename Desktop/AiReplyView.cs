using System.ComponentModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using SC.ScriptForge.ViewModels;

namespace SC.ScriptForge;

/// <summary>Selectable, local formatting only. Model output cannot create links, HTML or executable controls.</summary>
public sealed class AiReplyView : UserControl
{
    private static readonly FontFamily Mono = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas");
    private static readonly Regex Inline = new(@"(\*\*[^*\r\n]+\*\*|`[^`\r\n]+`)",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(80));
    public AiReplyView(AiChatTurn turn)
    {
        var userText = new SelectableTextBlock { Text = turn.Prompt, FontSize = 13, LineHeight = 22, TextWrapping = TextWrapping.Wrap };
        var user = new Border { Child = userText, Padding = new(17,12), CornerRadius = new(16,16,4,16), MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(54,0,0,10), Classes = { "aiUserBubble" } };
        var context = Ui.Text(turn.Context,"muted",10); context.TextTrimming = TextTrimming.CharacterEllipsis; context.TextWrapping = TextWrapping.NoWrap;
        var userGroup = Ui.Stack(user,context); userGroup.Spacing = 0; context.HorizontalAlignment = HorizontalAlignment.Right;
        var copy = Ui.Button("Копировать ответ",async () => { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) { await clipboard.SetTextAsync(turn.Answer); ToolTip.SetTip(copyButton!,"Скопировано"); } },"ghost");
        copyButton = copy; copy.Content = Ui.Icon("copy",15); copy.Classes.Add("iconAction"); ToolTip.SetTip(copy,"Копировать ответ");
        var heading = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 10 };
        var badge = Ui.IconBadge("ai",16); badge.Width = badge.Height = 34; badge.CornerRadius = new(11);
        heading.Children.Add(badge);
        var task = Ui.Text(turn.Task,"muted",11); task.TextWrapping = TextWrapping.NoWrap; task.TextTrimming = TextTrimming.CharacterEllipsis;
        var label = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 }; label.Children.Add(Ui.Text("SC AI","section",13)); label.Children.Add(task.Place(0,1));
        heading.Children.Add(label.Place(0,1)); heading.Children.Add(copy.Place(0,2));
        var response = new ContentControl { Margin = new(44,8,4,0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var streaming = new SelectableTextBlock { Name = "AiAnswer", FontSize = 14, LineHeight = 25, TextWrapping = TextWrapping.Wrap };
        var completion = Ui.Text("","muted",10); completion.Margin = new(44,8,0,0);
        var root = Ui.Stack(userGroup,heading,response,completion); root.Spacing = 6; root.Margin = new(0,0,0,28); Content = root;
        void Update(object? sender,PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(turn.Answer) or nameof(turn.Finished))
            {
                if (turn.Finished) response.Content = Format(turn.Answer);
                else { streaming.Text = string.IsNullOrEmpty(turn.Answer) ? "Собираю ответ…" : turn.Answer; response.Content = streaming; }
                copy.IsEnabled = turn.Finished && turn.Answer.Length > 0;
            }
            completion.Text = turn.Completion;
        }
        AttachedToVisualTree += (_,_) => { turn.PropertyChanged -= Update; turn.PropertyChanged += Update; Update(null,new(nameof(turn.Finished))); _ = Motion.RevealAsync(this,220,6); };
        DetachedFromVisualTree += (_,_) => turn.PropertyChanged -= Update;
    }
    private Button? copyButton;
    private static Control Format(string source)
    {
        var stack = Ui.Stack(); stack.Spacing = 10;
        var lines = source.Replace("\r\n","\n").Split('\n'); var paragraph = new List<string>(); var code = new List<string>(); var fenced = false;
        void Flush()
        {
            if (paragraph.Count == 0) return;
            stack.Children.Add(Prose(string.Join('\n',paragraph))); paragraph.Clear();
        }
        void Code()
        {
            var value = string.Join('\n',code); code.Clear();
            var text = new SelectableTextBlock { Text = value, FontFamily = Mono, FontSize = 11, LineHeight = 21, Margin = new(14), TextWrapping = TextWrapping.NoWrap };
            var scroll = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, MaxHeight = 300 };
            var block = new Border { Child = scroll, CornerRadius = new(10), BorderThickness = new(1) };
            Ui.Resource(block,Border.BackgroundProperty,"ScCodeBackground"); Ui.Resource(block,Border.BorderBrushProperty,"ScBorder"); stack.Children.Add(block);
        }
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```",StringComparison.Ordinal)) { Flush(); if (fenced) Code(); fenced = !fenced; continue; }
            if (fenced) { code.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
            if (line.StartsWith("#",StringComparison.Ordinal) && line.TrimStart('#').StartsWith(' ')) { Flush(); var heading = Prose(line.TrimStart('#',' ')); heading.FontWeight = FontWeight.SemiBold; heading.FontSize = 16; stack.Children.Add(heading); }
            else paragraph.Add(line);
        }
        Flush(); if (fenced) Code(); return stack;
    }
    private static SelectableTextBlock Prose(string value)
    {
        var text = new SelectableTextBlock { FontSize = 14, LineHeight = 25, TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection() };
        try
        {
            var offset = 0;
            foreach (Match match in Inline.Matches(value))
            {
                text.Inlines.Add(new Run(value[offset..match.Index])); var token = match.Value;
                var run = new Run(token.StartsWith('`') ? token[1..^1] : token[2..^2]);
                if (token.StartsWith('`')) { run.FontFamily = Mono; run.FontSize = 12; Ui.Resource(run,TextElement.ForegroundProperty,"ScAccentText"); }
                else run.FontWeight = FontWeight.SemiBold;
                text.Inlines.Add(run); offset = match.Index + match.Length;
            }
            text.Inlines.Add(new Run(value[offset..]));
        }
        catch (RegexMatchTimeoutException) { text.Inlines.Clear(); text.Text = value; }
        return text;
    }
}
