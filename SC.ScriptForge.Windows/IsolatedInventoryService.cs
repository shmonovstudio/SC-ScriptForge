using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SC.ScriptForge.Core;

namespace SC.ScriptForge.Windows;

/// <summary>Same-user, finite allowlist worker; no scripts, shell, elevated token, or persistent service.</summary>
public sealed class IsolatedInventoryService(string categoryId, string executable, IReadOnlyList<string>? prefixArguments = null,
    TimeSpan? timeout = null, DiagnosticLog? log = null) : IInventoryCategoryService
{
    public string CategoryId { get; } = InventoryCategory.Names.ContainsKey(categoryId) ? categoryId : throw new ArgumentException("Unknown inventory category");
    public async Task<InventoryCategory> CollectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in prefixArguments ?? []) info.ArgumentList.Add(arg);
        info.ArgumentList.Add("--inventory-worker"); info.ArgumentList.Add(CategoryId);
        using var process = new Process { StartInfo = info };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(18));
        try
        {
            if (!process.Start()) throw new IOException("Inventory worker could not start");
            var stdout = ReadBoundedAsync(process.StandardOutput, 2_000_000, deadline.Token);
            var stderr = ReadBoundedAsync(process.StandardError, 16_384, deadline.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("Inventory worker failed");
            var result = JsonSerializer.Deserialize<InventoryCategory>(await stdout, InventoryExport.JsonOptions) ?? throw new IOException("Empty inventory response");
            if (result.Id != CategoryId || result.Records.Count > 256 || result.Records.Any(r => r.Fields.Count > 128)) throw new IOException("Invalid inventory response");
            if (result.Records.SelectMany(r => r.Fields).Any(f => f.Error is not null)) log?.Event("inventory.partial." + CategoryId);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log?.Failure("inventory.timeout." + CategoryId, new TimeoutException());
            return InventoryCategory.Failed(CategoryId, OperationError.From(new TimeoutException()));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            log?.Failure("inventory." + CategoryId, error);
            return InventoryCategory.Failed(CategoryId, OperationError.From(error));
        }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); } }
            catch (InvalidOperationException) { }
        }
    }
    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) is var count && count > 0)
        {
            if (result.Length + count > maximum) throw new IOException("Inventory response exceeds limit");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
