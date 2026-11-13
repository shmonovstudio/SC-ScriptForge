using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using SC.ScriptForge.Models;
using SC.ScriptForge.Core;

namespace SC.ScriptForge.Services;

/// <summary>Explicit local execution. Values travel through a JSON file and a fixed splatting wrapper.</summary>
public sealed class ScriptExecutionService(string dataRoot)
{
    public async Task<ScriptRunResult> RunAsync(ScriptExecutionRequest request, IProgress<ScriptOutput>? output = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Timeout <= TimeSpan.Zero || request.Timeout > TimeSpan.FromHours(24)) throw new ArgumentOutOfRangeException(nameof(request.Timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);
        if (request.BypassOnce && (await new ExecutionPolicyService().ReadAsync(request.Engine, cancellationToken).ConfigureAwait(false)).IsManaged)
            throw new InvalidOperationException("Политика PowerShell управляется организацией. Bypass для процесса не отменяет это ограничение.");
        var actualFingerprint = await Task.Run(() => ScriptParameterService.Fingerprint(request.ScriptPath), cancellationToken).ConfigureAwait(false);
        if (actualFingerprint != request.Fingerprint) throw new InvalidOperationException("Скрипт изменился. Обновите параметры перед запуском.");
        var runDirectory = Path.Combine(dataRoot, "Runs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var payloadPath = Path.Combine(runDirectory, "parameters.json");
        var logPath = Path.Combine(runDirectory, "output.log");
        var policyStatusPath = Path.Combine(runDirectory, "policy-blocked.flag");
        var stopwatch = Stopwatch.StartNew();
        Process? process = null;
        ExecutionJob? job = null;
        var cancelled = false;
        var timedOut = false;
        try
        {
            foreach (var directory in request.OutputDirectories.Distinct(StringComparer.OrdinalIgnoreCase)) Directory.CreateDirectory(directory);
            // Restrict the transient payload before writing any values, including secret strings.
            using (File.Create(payloadPath)) { }
            var security = new FileSecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(payloadPath).SetAccessControl(security);
            await File.WriteAllTextAsync(payloadPath, JsonSerializer.Serialize(new { request.ScriptPath, request.Parameters }), new UTF8Encoding(false), deadline.Token).ConfigureAwait(false);
            var info = PowerShellHost.StartInfo(request.Engine, PowerShellHost.ReadRuntimeScript("InvokeScript.ps1"), request.BypassOnce);
            info.WorkingDirectory = Path.GetDirectoryName(request.ScriptPath)!;
            info.Environment["SC_RUN_PAYLOAD"] = payloadPath;
            info.Environment["SC_POLICY_BLOCKED"] = policyStatusPath;
            info.Environment["SC_SCRIPTFORGE_LANGUAGE"] = LibraryLocalization.Normalize(request.Language);
            var eventPrefix = "SCFORGE:" + Guid.NewGuid().ToString("N") + ":";
            info.Environment["SC_EVENT_PREFIX"] = eventPrefix;
            process = Process.Start(info) ?? throw new IOException("Не удалось запустить PowerShell.");
            job = new ExecutionJob(process);
            process.StandardInput.Close();
            await using var log = new StreamWriter(logPath, false, new UTF8Encoding(false));
            using var outputGate = new SemaphoreSlim(1);
            var loggedCharacters = 0;
            const int maximumLogCharacters = 5_000_000;
            var truncated = false;
            async Task ReadAsync(StreamReader reader, bool isError)
            {
                await foreach (var line in ReadBoundedLinesAsync(reader, deadline.Token))
                {
                    await outputGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (loggedCharacters < maximumLogCharacters)
                        {
                            var item = DecodeOutput(line, isError, eventPrefix);
                            var prefix = isError ? "[ошибка] " : item.Kind == ScriptOutputKind.Warning ? "[предупреждение] " : "";
                            var bounded = item.Text.Length > 16_000 ? item.Text[..16_000] + " …" : item.Text;
                            await log.WriteLineAsync(prefix + bounded).ConfigureAwait(false);
                            loggedCharacters += bounded.Length + prefix.Length;
                            output?.Report(item with { Text = bounded });
                        }
                        else if (!truncated)
                        {
                            truncated = true;
                            const string message = "Журнал достиг ограничения объёма; дальнейший вывод пропускается. Скрипт продолжает работу.";
                            await log.WriteLineAsync(message).ConfigureAwait(false); output?.Report(new ScriptOutput(message) { Kind = ScriptOutputKind.Warning });
                        }
                    }
                    finally { outputGate.Release(); }
                }
            }
            var stdout = ReadAsync(process.StandardOutput, false);
            var stderr = ReadAsync(process.StandardError, true);
            try { await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                cancelled = cancellationToken.IsCancellationRequested;
                timedOut = !cancelled;
                job.Terminate();
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            await log.FlushAsync().ConfigureAwait(false);
            stopwatch.Stop();
            return new ScriptRunResult(process.ExitCode, cancelled, stopwatch.Elapsed, logPath)
            {
                ExecutionPolicyBlocked = File.Exists(policyStatusPath), TimedOut = timedOut,
                Error = timedOut ? OperationError.From(new TimeoutException()) : cancelled ? OperationError.From(new OperationCanceledException())
                    : process.ExitCode != 0 ? new(ErrorKind.PowerShellRuntime, "PowerShell завершил сценарий с ошибкой.", "Проверьте входные данные и диагностику запуска.") : null
            };
        }
        finally
        {
            job?.Dispose();
            if (process is not null) { try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); } } catch (InvalidOperationException) { } }
            process?.Dispose();
            // Parameter values, including transient secret values, are not retained in run history.
            if (File.Exists(payloadPath)) File.Delete(payloadPath);
            if (File.Exists(policyStatusPath)) File.Delete(policyStatusPath);
        }
    }

    // Keep draining even a newline-free hostile stream while bounding each allocation.
    private static async IAsyncEnumerable<string> ReadBoundedLinesAsync(StreamReader reader, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new char[4096]; var line = new StringBuilder(); var truncated = false;
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) is var count && count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                var ch = buffer[i];
                if (ch == '\n') { yield return line.ToString().TrimEnd('\r') + (truncated ? " … [строка сокращена]" : ""); line.Clear(); truncated = false; }
                else if (line.Length < 512_000) line.Append(ch);
                else truncated = true;
            }
        }
        if (line.Length > 0 || truncated) yield return line.ToString() + (truncated ? " … [строка сокращена]" : "");
    }

    private static ScriptOutput DecodeOutput(string line, bool isError, string prefix)
    {
        if (!isError && line.StartsWith(prefix, StringComparison.Ordinal) && line.Length <= 512_000)
        {
            try
            {
                using var json = JsonDocument.Parse(line[prefix.Length..]);
                var root = json.RootElement;
                var kind = root.GetProperty("kind").GetString() switch
                {
                    "data" => ScriptOutputKind.Data, "warning" => ScriptOutputKind.Warning,
                    "information" => ScriptOutputKind.Information, "diagnostic" => ScriptOutputKind.Diagnostic,
                    _ => ScriptOutputKind.Message
                };
                var fields = root.GetProperty("fields").EnumerateArray().Take(24)
                    .Select(f => new ScriptResultField(f.GetProperty("name").GetString() ?? "", f.GetProperty("value").GetString() ?? "")).ToArray();
                return new(root.GetProperty("text").GetString() ?? "") { Kind = kind, Fields = fields, FilePath = root.GetProperty("filePath").GetString(), PreviewLimited = root.TryGetProperty("limited", out var limited) && limited.ValueKind == JsonValueKind.True };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
        }
        return new(line.Length > 16_000 ? line[..16_000] + " …" : line, isError) { PreviewLimited = line.Length > 16_000 };
    }
}
