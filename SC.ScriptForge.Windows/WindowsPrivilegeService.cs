using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SC.ScriptForge.Core;

namespace SC.ScriptForge.Windows;

public sealed class WindowsPrivilegeService : IPrivilegeService
{
    public bool IsProcessElevated()
    {
        if (!Native.OpenProcessToken(Native.GetCurrentProcess(), 0x0008, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!Native.GetTokenInformation(token, 20, out var elevation, sizeof(int), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return elevation != 0;
        }
    }
    public Task<PermissionCheckResult> CheckAsync(OperationDescriptor operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Descriptors supplied by callers cannot grant arbitrary operations.
        var result = operation.Id switch
        {
            "inventory.read" => new(true, PermissionRequirement.CurrentUser, "Чтение доступных данных; каждый источник проверяет доступ отдельно."),
            "script.run" => new(true, PermissionRequirement.CurrentUser, "Выполнение с текущим токеном. Доступ к ресурсам определяет Windows."),
            "external.connect" => new(false, PermissionRequirement.ExternalAuthorization, "Права проверяет целевой сервер.", "Авторизуйтесь на целевом ресурсе; локальное повышение не предоставляет доменные права."),
            _ => new PermissionCheckResult(false, operation.Requirement, "Операция не зарегистрирована.")
        };
        return Task.FromResult(result);
    }
    private static class Native
    {
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int info, int length, out int returnLength);
    }
}
