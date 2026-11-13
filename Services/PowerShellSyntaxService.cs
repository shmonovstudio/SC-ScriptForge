using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SC.ScriptForge.Services;

/// <summary>One parser error. Columns are 1-based; Context holds the source lines around the error as PowerShell decoded them.</summary>
public sealed record SyntaxIssue(int Line, int Column, int EndLine, int EndColumn, string Message, string ErrorId, IReadOnlyList<SyntaxContextLine> Context)
{
    public string Hint => SyntaxHints.For(this);
}
public sealed record SyntaxContextLine(int Number, string Text);

public sealed record SyntaxCheckResult(bool Success, IReadOnlyList<SyntaxIssue> Issues, string Engine, string Raw = "")
{
    /// <summary>Plain-text report for the clipboard.</summary>
    public string Message => Success ? "Синтаксис в порядке. Ошибок не найдено."
        : Issues.Count == 0 ? Raw
        : string.Join("\n\n", Issues.Select(i => $"Строка {i.Line}, столбец {i.Column}: {i.Message}" + (i.Context.FirstOrDefault(c => c.Number == i.Line) is { } line ? "\n    " + line.Text.Trim() : "")));
}

public sealed class PowerShellSyntaxService
{
    public async Task<SyntaxCheckResult> CheckAsync(string path, CancellationToken cancellationToken = default)
    {
        // Run only the built-in AST parser. Never invoke the user's script or load its modules.
        // Windows PowerShell writes with the OEM code page by default, so switch the console to UTF-8 before anything is printed.
        const string command = """
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            $tok = $null; $errs = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($env:SC_SCRIPT_PATH, [ref]$tok, [ref]$errs)
            if ($errs.Count -eq 0) { [Console]::Out.WriteLine('[]'); exit 0 }
            $lines = $ast.Extent.Text -split "\r?\n"
            $items = foreach ($e in ($errs | Select-Object -First 50)) {
                $x = $e.Extent; $from = [Math]::Max(1, $x.StartLineNumber - 1); $to = [Math]::Min($lines.Count, [Math]::Max($x.StartLineNumber, $x.EndLineNumber) + 1)
                $context = @(for ($n = $from; $n -le $to; $n++) { @{ number = $n; text = [string]$lines[$n - 1] } })
                @{ line = $x.StartLineNumber; column = $x.StartColumnNumber; endLine = $x.EndLineNumber; endColumn = $x.EndColumnNumber; message = [string]$e.Message; id = [string]$e.ErrorId; context = $context }
            }
            [Console]::Out.WriteLine((ConvertTo-Json -InputObject @($items) -Depth 5 -Compress))
            exit 2
            """;
        var engine = PowerShellHost.InstalledEngines().FirstOrDefault() ?? throw new InvalidOperationException("PowerShell не найден.");
        var info = PowerShellHost.StartInfo(engine, command);
        info.Environment["SC_SCRIPT_PATH"] = path;
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить синтаксический анализатор.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Синтаксическая проверка превысила 15 секунд.");
        }
        var output = (await standardOutput.ConfigureAwait(false)).Trim();
        var error = (await standardError.ConfigureAwait(false)).Trim();
        var issues = Parse(output);
        if (process.ExitCode == 0 && issues is { Count: 0 }) return new(true, [], engine.Name);
        return new(false, issues ?? [], engine.Name, (output + "\n" + error).Trim());
    }

    private static List<SyntaxIssue>? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            static int Number(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
            static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return document.RootElement.EnumerateArray().Select(e => new SyntaxIssue(Number(e, "line"), Number(e, "column"), Number(e, "endLine"), Number(e, "endColumn"),
                Text(e, "message"), Text(e, "id"),
                e.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Array
                    ? context.EnumerateArray().Select(c => new SyntaxContextLine(Number(c, "number"), Text(c, "text"))).ToArray() : [])).ToList();
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Short human advice for the most common parser errors.</summary>
internal static class SyntaxHints
{
    public static string For(SyntaxIssue issue)
    {
        var line = issue.Context.FirstOrDefault(c => c.Number == issue.Line)?.Text ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\$(if|foreach|for|while|switch)\s*\(", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "Ключевые слова пишутся без «$». Внутри строки используйте подвыражение: $(if ($условие) { 'да' } else { 'нет' }).";
        return issue.ErrorId switch
        {
            "MissingEndCurlyBrace" => "Не хватает закрывающей скобки «}». Проверьте парность фигурных скобок в блоке выше.",
            "MissingEndParenthesisInExpression" or "MissingEndParenthesisInMethodCall" or "MissingEndParenthesisAfterStatement" => "Не хватает закрывающей скобки «)».",
            "MissingEndSquareBracket" or "EndSquareBracketExpectedAtEndOfAttribute" => "Не хватает закрывающей скобки «]».",
            "TerminatorExpectedAtEndOfString" => "Строка не закрыта кавычкой. Проверьте парность кавычек; внутри двойных кавычек экранируйте их символом `.",
            "UnexpectedToken" => "Лишний или неожиданный символ. Часто это пропущенный оператор, запятая или скобка перед этим местом.",
            "MissingExpressionAfterOperator" => "После оператора не хватает значения.",
            "MissingArgument" or "MissingExpression" => "Пропущено значение или аргумент.",
            "ExpectedValueExpression" => "Ожидалось значение — возможно, лишняя запятая или оператор.",
            "MissingStatementBlock" or "MissingStatementBlockAfterElse" or "MissingStatementBlockAfterElseIf" => "После условия или цикла нужен блок в фигурных скобках { … }.",
            "MissingCatchOrFinally" => "После try нужен блок catch или finally.",
            "MissingFunctionBody" => "У функции нет тела в фигурных скобках.",
            "UnexpectedAttribute" => "Атрибут стоит не на своём месте — проверьте блок param().",
            _ => ""
        };
    }
}
