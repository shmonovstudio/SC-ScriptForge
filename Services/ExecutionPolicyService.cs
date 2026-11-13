using System.Diagnostics;
using System.Text.Json;
using SC.ScriptForge.Models;

namespace SC.ScriptForge.Services;

public sealed class ExecutionPolicyService
{
    private readonly Func<PowerShellEngine, string, CancellationToken, Task<string>> _execute;
    public ExecutionPolicyService(Func<PowerShellEngine, string, CancellationToken, Task<string>>? execute = null) => _execute = execute ?? ExecuteAsync;
    private const string SnapshotCommand = "[pscustomobject]@{Effective=[string](Get-ExecutionPolicy);MachinePolicy=[string](Get-ExecutionPolicy -Scope MachinePolicy);UserPolicy=[string](Get-ExecutionPolicy -Scope UserPolicy);Process=[string](Get-ExecutionPolicy -Scope Process);CurrentUser=[string](Get-ExecutionPolicy -Scope CurrentUser);LocalMachine=[string](Get-ExecutionPolicy -Scope LocalMachine)} | ConvertTo-Json -Compress";
    private const string SetLocalScriptsCommand = "if ((Get-ExecutionPolicy -Scope MachinePolicy) -ne 'Undefined' -or (Get-ExecutionPolicy -Scope UserPolicy) -ne 'Undefined') { throw 'Политика управляется организацией. Обратитесь к администратору.' }; Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force -ErrorAction Stop; " + SnapshotCommand;
    private static ExecutionPolicySnapshot Parse(string json) => JsonSerializer.Deserialize<ExecutionPolicySnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Не удалось прочитать политику PowerShell.");
    public async Task<ExecutionPolicySnapshot> ReadAsync(PowerShellEngine engine, CancellationToken token = default) => Parse(await _execute(engine, SnapshotCommand, token));
    public async Task<ExecutionPolicySnapshot> AllowLocalScriptsAsync(PowerShellEngine engine, CancellationToken token = default)
    {
        var before = await ReadAsync(engine, token);
        if (before.IsManaged) throw new InvalidOperationException("Политика управляется организацией. Изменение CurrentUser не отменяет групповую политику.");
        var after = Parse(await _execute(engine, SetLocalScriptsCommand, token));
        if (!after.CurrentUser.Equals("RemoteSigned", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("PowerShell не подтвердил установку RemoteSigned для текущего пользователя.");
        return after;
    }
    private static async Task<string> ExecuteAsync(PowerShellEngine engine, string command, CancellationToken token)
    {
        // Resolve this engine's own security module; PSModulePath may be inherited from another PowerShell edition.
        const string prelude = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; Import-Module (Join-Path $PSHOME 'Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop; ";
        using var process = Process.Start(PowerShellHost.StartInfo(engine, prelude + command)) ?? throw new IOException("Не удалось открыть PowerShell.");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            if (!token.IsCancellationRequested) throw new TimeoutException("Проверка политики PowerShell заняла больше 15 секунд.");
            throw;
        }
        var output = await stdout; var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Не удалось применить настройку PowerShell." : error.Trim());
        return output.Trim();
    }
}
