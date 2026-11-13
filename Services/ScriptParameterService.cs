using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

public sealed class ScriptParameterService
{
    public static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public async Task<ScriptDefinition> InspectAsync(string path, PowerShellEngine engine, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Файл скрипта не найден.", path);
        if (new FileInfo(path).Length > 10_000_000) throw new InvalidOperationException("Форма запуска поддерживает скрипты размером до 10 МБ.");
        var fingerprint = await Task.Run(() => Fingerprint(path), cancellationToken);
        var info = PowerShellHost.StartInfo(engine, PowerShellHost.ReadRuntimeScript("InspectScript.ps1"));
        info.Environment["SC_INSPECT_PATH"] = path;
        using var process = Process.Start(info) ?? throw new IOException("Не удалось открыть PowerShell для чтения параметров.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (!cancellationToken.IsCancellationRequested) throw new TimeoutException("Чтение параметров заняло больше 15 секунд.");
            throw;
        }
        var json = await output;
        var message = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
            ? $"{engine.Name} не смог прочитать параметры скрипта (код завершения {process.ExitCode})."
            : message.Trim());
        var definition = JsonSerializer.Deserialize<ScriptDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Не удалось прочитать параметры скрипта.");
        if (fingerprint != await Task.Run(() => Fingerprint(path), cancellationToken)) throw new IOException("Скрипт изменился во время чтения. Обновите параметры.");
        definition.Fingerprint = fingerprint;
        return definition;
    }
}
