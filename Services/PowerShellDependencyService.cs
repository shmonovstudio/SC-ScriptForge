using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SC.ScriptForge.Services;

public sealed record PowerShellDependencies(string[] Errors, string[] Notes);

/// <summary>Reads literal module names and local CIM schemas without executing submitted code.</summary>
public sealed class PowerShellDependencyService
{
    public async Task<PowerShellDependencies> InspectAsync(string source, CancellationToken cancellationToken = default)
    {
        var engine = PowerShellHost.InstalledEngines().FirstOrDefault() ?? throw new InvalidOperationException("PowerShell недоступен для проверки зависимостей.");
        var info = PowerShellHost.StartInfo(engine, PowerShellHost.ReadRuntimeScript("InspectDependencies.ps1"));
        info.StandardInputEncoding = new UTF8Encoding(false);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось проверить зависимости PowerShell.");
        using var job = new ExecutionJob(process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.StandardInput.WriteAsync(source.AsMemory(), timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var json = await output; _ = await errors;
            if (process.ExitCode != 0 || json.Length > 262144) throw new InvalidOperationException("Проверка зависимостей не завершилась. Код скрипта не выполнялся.");
            return JsonSerializer.Deserialize<PowerShellDependencies>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Не удалось прочитать результат проверки зависимостей.");
        }
        catch (OperationCanceledException)
        {
            job.Terminate();
            if (!cancellationToken.IsCancellationRequested) throw new TimeoutException("Проверка зависимостей превысила 20 секунд. Код скрипта не выполнялся.");
            throw;
        }
    }
}
