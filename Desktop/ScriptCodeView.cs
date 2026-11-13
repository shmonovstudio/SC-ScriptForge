using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;

namespace SC.ScriptForge;

/// <summary>Bounded, asynchronous source preview. Highlighting is visual only; no PowerShell is invoked.</summary>
public sealed class ScriptCodeView : UserControl
{
    private const int MaximumCharacters = 65_536;
    private const int MaximumLines = 400;
    private static readonly Regex Tokens = new(
        "(?s:<#.*?#>)|(?m:#[^\\r\\n]*)|['‘’‚‛](?:['‘’‚‛]{2}|[^'‘’‚‛])*['‘’‚‛]|[\"“”„](?:`.|[^\"“”„`])*[\"“”„]|\\$\\{[^}\\r\\n]*\\}|\\$[\\w:?]+|\\b(?:param|if|else|elseif|foreach|for|while|function|return|switch|try|catch|finally|throw|begin|process|end)\\b|\\b[A-Za-z][\\w]*-[\\w-]+\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private readonly string _path;
    private readonly ContentControl _content = new();
    private readonly TextBlock _summary = Ui.Text("Читаем файл…", "muted", 12);
    private readonly Button _copy;
    private CancellationTokenSource? _reading;
    private string? _code;
    private bool _truncated;

    public ScriptCodeView(string path)
    {
        _path = path; Name = "SourcePreview";
        _copy = Ui.Button("Копировать код", async () =>
        {
            if (_code is not null && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(_code);
        }, "ghost");
        _copy.FontSize = 12; _copy.Padding = new(8,4); _copy.MinHeight = 28; _copy.IsEnabled = false;
        var bar = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new(18,7,10,7) };
        bar.Children.Add(_summary); bar.Children.Add(_copy.Place(0,1));
        var root = new Grid { RowDefinitions = new("Auto,*") };
        root.Children.Add(bar); root.Children.Add(_content.Place(1,0));
        Ui.Resource(root, BackgroundProperty, "ScCodeBackground"); Content = root;
        AttachedToVisualTree += async (_, _) => { if (_code is null) await LoadAsync(); };
        DetachedFromVisualTree += (_, _) => _reading?.Cancel();
    }

    private async Task LoadAsync()
    {
        _reading?.Cancel();
        using var cancellation = new CancellationTokenSource(); _reading = cancellation;
        try
        {
            var bytes = new byte[MaximumCharacters * 4 + 4];
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var read = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var decoded = Decode(bytes.AsSpan(0, read));
            var length = decoded.Length;
            var source = decoded[..Math.Min(length, MaximumCharacters)].Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = source.Split('\n'); _truncated = length > MaximumCharacters || lines.Length > MaximumLines;
            _code = lines.Length > MaximumLines ? string.Join('\n', lines.Take(MaximumLines)) : source;
            ShowCode(_code);
            var lineCount = Math.Min(lines.Length, MaximumLines);
            _summary.Text = _truncated ? $"Фрагмент · {lineCount} строк · полный файл в редакторе" : $"PowerShell · {lineCount} строк";
            _copy.Content = _truncated ? "Копировать фрагмент" : "Копировать код";
            Avalonia.Automation.AutomationProperties.SetName(_copy, _truncated ? "Копировать фрагмент" : "Копировать код");
            _copy.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _summary.Text = "Не удалось прочитать файл";
            _content.Content = Ui.Text("Файл недоступен. Обновите библиотеку или откройте другой скрипт.", "muted", 12).Margin(20);
        }
        finally { if (ReferenceEquals(_reading, cancellation)) _reading = null; }
    }

    private void ShowCode(string code) => _content.Content = Render(code);

    /// <summary>BOM first, then strict UTF-8; legacy scripts without a BOM are usually Windows-1251.</summary>
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) return System.Text.Encoding.UTF8.GetString(bytes[3..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return System.Text.Encoding.Unicode.GetString(bytes[2..]);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return System.Text.Encoding.BigEndianUnicode.GetString(bytes[2..]);
        try
        {
            // flush: false tolerates a multi-byte character cut by the read limit.
            var decoder = new System.Text.UTF8Encoding(false, true).GetDecoder();
            var chars = new char[bytes.Length];
            return new string(chars, 0, decoder.GetChars(bytes, chars, flush: false));
        }
        catch (System.Text.DecoderFallbackException)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    /// <summary>Splits PowerShell source into runs with a theme brush key (null = plain text). Cyrillic names and typographic quotes are recognised as PowerShell does.</summary>
    public static IReadOnlyList<(string Text, string? Resource)> Highlight(string code)
    {
        var parts = new List<(string, string?)>();
        try
        {
            var position = 0;
            foreach (Match match in Tokens.Matches(code))
            {
                if (match.Index > position) parts.Add((code[position..match.Index], null));
                var first = match.Value[0];
                var resource = first == '#' || match.Value.StartsWith("<#") ? "ScCodeComment" : "'‘’‚‛\"“”„".Contains(first) ? "ScCodeString" : first == '$' ? "ScCodeVariable" : match.Value.Contains('-') ? "ScAccentText" : "ScCodeKeyword";
                parts.Add((match.Value, resource)); position = match.Index + match.Length;
            }
            if (position < code.Length) parts.Add((code[position..], null));
        }
        catch (RegexMatchTimeoutException) { parts.Clear(); parts.Add((code, null)); }
        return parts;
    }

    /// <summary>Подсвеченный код с номерами строк. Используется и для просмотра скрипта, и для кода от SC AI.</summary>
    public static Control Render(string code)
    {
        var text = new SelectableTextBlock { Name = "PreviewSourceText", FontFamily = new("avares://SC.ScriptForge/Assets/Fonts/JetBrainsMono#JetBrains Mono, Consolas"),
            FontSize = 11, LineHeight = 21, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Top, Inlines = new InlineCollection() };
        Ui.Resource(text, TextBlock.ForegroundProperty, "ScSoft");
        void Add(string value, string? resource = null)
        {
            if (value.Length == 0) return;
            var run = new Run(value); if (resource is not null) Ui.Resource(run, TextElement.ForegroundProperty, resource); text.Inlines.Add(run);
        }
        foreach (var (value, resource) in Highlight(code)) Add(value, resource);
        var numbers = Ui.Text(string.Join('\n', Enumerable.Range(1, code.Count(c => c == '\n') + 1)), "muted", 10);
        numbers.FontFamily = text.FontFamily; numbers.LineHeight = 21; numbers.TextAlignment = TextAlignment.Right; numbers.VerticalAlignment = VerticalAlignment.Top; numbers.Opacity = .7;
        var grid = new Grid { ColumnDefinitions = new("30,Auto"), ColumnSpacing = 16, Margin = new(12,12,22,18), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        grid.Children.Add(numbers); grid.Children.Add(text.Place(0,1));
        return new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    }
}
