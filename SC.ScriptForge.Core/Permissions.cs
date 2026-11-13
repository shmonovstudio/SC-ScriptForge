namespace SC.ScriptForge.Core;

public enum PermissionRequirement { CurrentUser, Elevation, ExternalAuthorization }
public sealed record OperationDescriptor(string Id, PermissionRequirement Requirement, string Purpose);
public sealed record PermissionCheckResult(bool Allowed, PermissionRequirement Requirement, string Message, string? RequiredAction = null);
public interface IPrivilegeService
{
    bool IsProcessElevated();
    Task<PermissionCheckResult> CheckAsync(OperationDescriptor operation, CancellationToken cancellationToken);
}
public static class Operations
{
    public static readonly OperationDescriptor Inventory = new("inventory.read", PermissionRequirement.CurrentUser, "Получить доступные характеристики компьютера");
    public static readonly OperationDescriptor Scripts = new("script.run", PermissionRequirement.CurrentUser, "Запустить выбранный скрипт с правами пользователя");
    public static readonly OperationDescriptor External = new("external.connect", PermissionRequirement.ExternalAuthorization, "Обратиться к корпоративному ресурсу из пользовательского скрипта");
}
// No confirmed administrative action exists in 0.9. An empty allowlist is deliberate.
// A future helper must share a finite operation enum, validate typed parameters independently,
// show purpose before ShellExecute(runas), authenticate IPC, and exit after one request.
public enum PrivilegedOperation { }
public sealed record PrivilegedRequest(PrivilegedOperation Operation);
public interface IPrivilegedOperationBroker
{
    Task<PermissionCheckResult> ExecuteAsync(PrivilegedRequest request, CancellationToken cancellationToken);
}
public sealed class PrivilegedOperationBroker : IPrivilegedOperationBroker
{
    public Task<PermissionCheckResult> ExecuteAsync(PrivilegedRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PermissionCheckResult(false, PermissionRequirement.Elevation, "Нет разрешённой административной операции.", "Helper добавляется вместе с конкретным проверенным действием."));
    }
}
