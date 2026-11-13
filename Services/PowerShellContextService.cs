using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SC.ScriptForge.Services;

public sealed record PowerShellFunction(string Name, int Start, int End, int StartLine, int EndLine);
public sealed record PowerShellInspection(PowerShellFunction[] Functions, string[] Errors);

public sealed class PowerShellContextService
{
    public async Task<PowerShellInspection> InspectAsync(string source, CancellationToken cancellationToken = default)
    {
        const string command = "[Console]::InputEncoding=[Text.Encoding]::UTF8; [Console]::OutputEncoding=[Text.Encoding]::UTF8; $source=[Console]::In.ReadToEnd(); $t=$null; $e=$null; $ast=[System.Management.Automation.Language.Parser]::ParseInput($source,[ref]$t,[ref]$e); $functions=@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]},$true) | ForEach-Object { @{Name=$_.Name;Start=$_.Extent.StartOffset;End=$_.Extent.EndOffset;StartLine=$_.Extent.StartLineNumber;EndLine=$_.Extent.EndLineNumber} }); @{Functions=$functions;Errors=@($e | ForEach-Object { 'Строка '+$_.Extent.StartLineNumber+': '+$_.Message })} | ConvertTo-Json -Depth 5 -Compress";
        var engine = PowerShellHost.InstalledEngines().FirstOrDefault() ?? throw new InvalidOperationException("PowerShell Parser недоступен: установите Windows PowerShell 5.1 или PowerShell 7.");
        var info = PowerShellHost.StartInfo(engine, command); info.StandardInputEncoding = new UTF8Encoding(false);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось запустить Parser.");
        using var job = new ExecutionJob(process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(source.AsMemory(), timeout.Token); process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token);
            var result = await output; _ = await errors;
            if (process.ExitCode != 0 || result.Length > 262144) throw new InvalidOperationException("Parser не завершил проверку. Код не выполнялся.");
            return JsonSerializer.Deserialize<PowerShellInspection>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Parser вернул некорректный результат.");
        }
        catch (OperationCanceledException) { job.Terminate(); if (!cancellationToken.IsCancellationRequested) throw new TimeoutException("Parser превысил 15 секунд."); throw; }
    }
}
