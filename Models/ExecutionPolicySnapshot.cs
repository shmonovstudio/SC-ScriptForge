namespace SC.ScriptForge.Models;

public sealed class ExecutionPolicySnapshot
{
    public string Effective { get; set; } = "Undefined";
    public string MachinePolicy { get; set; } = "Undefined";
    public string UserPolicy { get; set; } = "Undefined";
    public string Process { get; set; } = "Undefined";
    public string CurrentUser { get; set; } = "Undefined";
    public string LocalMachine { get; set; } = "Undefined";
    public bool IsManaged => Defined(MachinePolicy) || Defined(UserPolicy);
    private static bool Defined(string? value) => !string.IsNullOrEmpty(value) && !value.Equals("Undefined", StringComparison.OrdinalIgnoreCase);
    public string Summary => IsManaged ? $"Управляется организацией · {Effective}" : $"Действующая политика: {Effective}";
    public string Details => IsManaged
        ? "Групповая политика имеет приоритет. Для разрешения запуска обратитесь к администратору организации."
        : "Bypass разрешает один запуск в отдельном процессе. RemoteSigned разрешает локальные скрипты для вашей учётной записи; скачанным файлам может требоваться подпись.";
}
