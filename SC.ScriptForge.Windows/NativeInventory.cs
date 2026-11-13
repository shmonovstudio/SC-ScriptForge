using System.Globalization;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SC.ScriptForge.Core;

namespace SC.ScriptForge.Windows;

public static class NativeInventory
{
    public static async Task<InventoryCategory> CollectAsync(string id)
    {
        if (!InventoryCategory.Names.ContainsKey(id)) throw new ArgumentException("Unknown inventory category");
        var records = id switch
        {
            "os" => Os(), "cpu" => await CpuAsync(), "memory" => Memory(), "disks" => Disks(),
            "gpu" => Gpu(), "network" => Network(), "bios" => Bios(), _ => throw new ArgumentException("Unknown category")
        };
        return new(id, InventoryCategory.Names[id], records);
    }
    private static InventoryField Text(string key, string label, object? value, string source, bool sensitive = false) =>
        new(key, label, source) { Text = value is bool ? null : value?.ToString()?.Trim(), Flag = value is bool flag ? flag : null, Status = value is null || string.IsNullOrWhiteSpace(value.ToString()) ? DataStatus.Unavailable : DataStatus.Success, Sensitive = sensitive };
    private static InventoryField Number(string key, string label, object? value, string? unit, string source)
    {
        var valid = value is not null && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0;
        return new(key, label, source) { Number = valid ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : null, Status = valid ? DataStatus.Success : DataStatus.Unavailable, Unit = unit };
    }
    private static InventoryField Missing(string key, string label, string source, Exception? error = null, string? detail = null) => new(key, label, source)
    {
        Status = error is UnauthorizedAccessException || error is ManagementException m && m.ErrorCode == ManagementStatus.AccessDenied ? DataStatus.AccessDenied
            : error is ManagementException n && n.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.InvalidNamespace ? DataStatus.Unsupported
            : error is null ? DataStatus.Unavailable : DataStatus.Error,
        Detail = detail ?? (error is null ? "Источник не предоставляет достоверное значение." : Classify(error).Message),
        Error = error is null ? null : Classify(error)
    };
    private static OperationError Classify(Exception error) => error is ManagementException wmi ? wmi.ErrorCode switch
    {
        ManagementStatus.AccessDenied => new(ErrorKind.AccessDenied, "Недостаточно прав для чтения этого источника.", "Проверьте разрешения провайдера WMI."),
        ManagementStatus.Timedout => OperationError.From(new TimeoutException()),
        ManagementStatus.InvalidClass or ManagementStatus.InvalidNamespace => new(ErrorKind.Unsupported, "Провайдер не поддерживает эти данные.", "Используйте доступные источники."),
        _ => new(ErrorKind.SourceUnavailable, "Источник WMI недоступен.", "Повторите обновление позднее.")
    } : OperationError.From(error);
    private static IReadOnlyList<InventoryRecord> Query(string ns, string cls, string properties, Func<ManagementBaseObject, InventoryRecord> project)
    {
        try
        {
            using var search = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery("SELECT " + properties + " FROM " + cls),
                new System.Management.EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(8) });
            using var values = search.Get();
            var records = new List<InventoryRecord>();
            foreach (ManagementBaseObject value in values)
            {
                using (value) { records.Add(project(value)); }
                if (records.Count >= 128) break;
            }
            if (records.Count == 0) records.Add(new(cls, [Missing("source", "Данные", "WMI/" + cls)]));
            return records;
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or COMException or System.Runtime.InteropServices.ExternalException or TimeoutException)
        { return [new(cls, [Missing("source", "Данные", "WMI/" + cls, e)])]; }
    }
    private static IReadOnlyList<InventoryRecord> Wmi(string cls, string properties, Func<ManagementBaseObject, InventoryRecord> project) => Query(@"root\cimv2", cls, properties, project);
    private static object? Value(ManagementBaseObject row, string property) => row.Properties[property]?.Value;
    private static InventoryField Date(string key, string label, object? value, string source)
    {
        try { return value is string text && text.Length >= 14 ? new(key, label, source) { Timestamp = new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(text)), Status = DataStatus.Success } : Missing(key, label, source); }
        catch (ArgumentException) { return Missing(key, label, source); }
    }
    private static IReadOnlyList<InventoryRecord> Os()
    {
        var result = new List<InventoryRecord>();
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var baseFields = new List<InventoryField>
        {
            Text("computer", "Имя компьютера", Environment.MachineName, ".NET/Environment", true),
            Text("user", "Текущий пользователь", Environment.UserDomainName + "\\" + Environment.UserName, ".NET/Environment", true),
            Text("architecture", "Архитектура", RuntimeInformation.OSArchitecture, ".NET/RuntimeInformation"),
            Text("displayVersion", "Выпуск", key?.GetValue("DisplayVersion"), "Registry/CurrentVersion"),
            Text("build", "Build", key?.GetValue("CurrentBuildNumber") + "." + key?.GetValue("UBR"), "Registry/CurrentVersion"),
            Number("uptime", "Время работы", Environment.TickCount64 / 1000d, "seconds", "Win32/GetTickCount64"),
            new("elevated", "Повышенный токен", "Win32/TokenElevation") { Flag = new WindowsPrivilegeService().IsProcessElevated(), Status = DataStatus.Success }
        };
        result.Add(new("Текущий сеанс", baseFields));
        result.AddRange(Wmi("Win32_OperatingSystem", "Caption,Version,LastBootUpTime,OSLanguage", row => new("Windows", [
            Text("name", "Название Windows", Value(row, "Caption"), "WMI/Win32_OperatingSystem"),
            Text("version", "Версия", Value(row, "Version"), "WMI/Win32_OperatingSystem"),
            Date("boot", "Последняя загрузка", Value(row, "LastBootUpTime"), "WMI/Win32_OperatingSystem"),
            Text("language", "Язык системы (LCID)", Value(row, "OSLanguage"), "WMI/Win32_OperatingSystem")])));
        result.AddRange(Wmi("Win32_ComputerSystem", "Domain,PartOfDomain", row => new("Рабочая среда", [
            Text("domain", "Домен / рабочая группа", Value(row, "Domain"), "WMI/Win32_ComputerSystem", true),
            Text("domainJoined", "В домене", Value(row, "PartOfDomain"), "WMI/Win32_ComputerSystem")])));
        return result;
    }
    private static async Task<IReadOnlyList<InventoryRecord>> CpuAsync()
    {
        var result = Wmi("Win32_Processor", "Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,CurrentClockSpeed", row => new("Процессор", [
            Text("model", "Модель", Value(row, "Name"), "WMI/Win32_Processor"), Text("vendor", "Производитель", Value(row, "Manufacturer"), "WMI/Win32_Processor"),
            Number("cores", "Физические ядра", Value(row, "NumberOfCores"), null, "WMI/Win32_Processor"),
            Number("threads", "Логические процессоры", Value(row, "NumberOfLogicalProcessors"), null, "WMI/Win32_Processor"),
            Number("maxClock", "Максимальная частота (WMI)", Value(row, "MaxClockSpeed"), "МГц", "WMI/Win32_Processor"),
            Number("clock", "Текущая частота (WMI)", Value(row, "CurrentClockSpeed"), "МГц", "WMI/Win32_Processor")])).ToList();
        InventoryField load;
        if (Native.GetSystemTimes(out var idle1, out var kernel1, out var user1))
        {
            await Task.Delay(400);
            if (Native.GetSystemTimes(out var idle2, out var kernel2, out var user2))
            {
                var total = kernel2 - kernel1 + user2 - user1; var idle = idle2 - idle1;
                load = total > 0 && idle <= total ? Number("load", "Текущая загрузка", 100d * (total - idle) / total, "%", "Win32/GetSystemTimes (400 ms)")
                    : Missing("load", "Текущая загрузка", "Win32/GetSystemTimes");
            }
            else load = Missing("load", "Текущая загрузка", "Win32/GetSystemTimes");
        }
        else load = Missing("load", "Текущая загрузка", "Win32/GetSystemTimes");
        result.Add(new("Текущая активность", [load])); return result;
    }
    private static IReadOnlyList<InventoryRecord> Memory()
    {
        var result = new List<InventoryRecord>(); var state = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        var ok = Native.GlobalMemoryStatusEx(ref state);
        var installedOk = Native.GetPhysicallyInstalledSystemMemory(out var installed);
        result.Add(new("Использование памяти", [
            installedOk ? Number("installed", "Установлено", installed * 1024d, "bytes", "Win32/GetPhysicallyInstalledSystemMemory") : Missing("installed", "Установлено", "Win32/GetPhysicallyInstalledSystemMemory"),
            ok ? Number("total", "Доступно системе всего", state.TotalPhysical, "bytes", "Win32/GlobalMemoryStatusEx") : Missing("total", "Доступно системе всего", "Win32/GlobalMemoryStatusEx"),
            ok ? Number("available", "Свободно", state.AvailablePhysical, "bytes", "Win32/GlobalMemoryStatusEx") : Missing("available", "Свободно", "Win32/GlobalMemoryStatusEx"),
            ok ? Number("used", "Используется", state.TotalPhysical - state.AvailablePhysical, "bytes", "Win32/GlobalMemoryStatusEx") : Missing("used", "Используется", "Win32/GlobalMemoryStatusEx"),
            ok ? Number("load", "Использование", state.Load, "%", "Win32/GlobalMemoryStatusEx") : Missing("load", "Использование", "Win32/GlobalMemoryStatusEx") ]));
        result.AddRange(Wmi("Win32_PhysicalMemory", "DeviceLocator,Capacity,ConfiguredClockSpeed,Manufacturer,SMBIOSMemoryType", row => new(Convert.ToString(Value(row, "DeviceLocator")) ?? "Модуль памяти", [
            Number("capacity", "Объём модуля", Value(row, "Capacity"), "bytes", "WMI/Win32_PhysicalMemory"),
            Number("speed", "Настроенная скорость", Value(row, "ConfiguredClockSpeed"), "МГц", "WMI/Win32_PhysicalMemory"),
            Text("vendor", "Производитель", Value(row, "Manufacturer"), "WMI/Win32_PhysicalMemory"),
            Text("type", "Тип (SMBIOS)", Value(row, "SMBIOSMemoryType"), "WMI/Win32_PhysicalMemory")])));
        return result;
    }
    private static IReadOnlyList<InventoryRecord> Disks()
    {
        var result = new List<InventoryRecord>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) { result.Add(new(drive.Name, [Missing("volume", "Том", ".NET/DriveInfo")])); continue; }
                result.Add(new(drive.Name, [Text("filesystem", "Файловая система", drive.DriveFormat, ".NET/DriveInfo"),
                    Number("size", "Объём", drive.TotalSize, "bytes", ".NET/DriveInfo"), Number("free", "Свободно", drive.AvailableFreeSpace, "bytes", ".NET/DriveInfo"),
                    Number("usedPercent", "Занято", drive.TotalSize > 0 ? 100d * (drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize : 0, "%", ".NET/DriveInfo")]));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { result.Add(new(drive.Name, [Missing("volume", "Том", ".NET/DriveInfo", e)])); }
        }
        result.AddRange(Query(@"root\Microsoft\Windows\Storage", "MSFT_PhysicalDisk", "FriendlyName,Size,MediaType,HealthStatus", row => new("Физический диск", [
            Text("model", "Модель", Value(row, "FriendlyName"), "WMI/MSFT_PhysicalDisk"), Number("size", "Объём", Value(row, "Size"), "bytes", "WMI/MSFT_PhysicalDisk"),
            Text("media", "Тип", Convert.ToInt32(Value(row, "MediaType") ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => null }, "WMI/MSFT_PhysicalDisk"),
            Text("health", "Состояние провайдера", Value(row, "HealthStatus") is { } h ? Convert.ToInt32(h) switch { 0 => "Исправен", 1 => "Предупреждение", 2 => "Неисправен", _ => null } : null, "WMI/MSFT_PhysicalDisk")])));
        if (result.Last().Fields.Any(f => f.Key == "source"))
            result.AddRange(Wmi("Win32_DiskDrive", "Model,Size,InterfaceType", row => new("Физический диск (совместимый источник)", [
                Text("model", "Модель", Value(row, "Model"), "WMI/Win32_DiskDrive"), Number("size", "Объём", Value(row, "Size"), "bytes", "WMI/Win32_DiskDrive"),
                Missing("media", "SSD / HDD", "WMI/Win32_DiskDrive"), Text("bus", "Интерфейс", Value(row, "InterfaceType"), "WMI/Win32_DiskDrive")])));
        return result;
    }
    private static IReadOnlyList<InventoryRecord> Gpu() => DxgiInventory.Read().Concat(Wmi("Win32_VideoController", "Name,AdapterCompatibility,DriverVersion,DriverDate", row => new("Драйвер графического адаптера", [
        Text("model", "Модель", Value(row, "Name"), "WMI/Win32_VideoController"), Text("vendor", "Производитель", Value(row, "AdapterCompatibility"), "WMI/Win32_VideoController"),
        Text("driver", "Версия драйвера", Value(row, "DriverVersion"), "WMI/Win32_VideoController"), Date("driverDate", "Дата драйвера", Value(row, "DriverDate"), "WMI/Win32_VideoController") ]))).ToArray();
    private static IReadOnlyList<InventoryRecord> Bios()
    {
        var result = Wmi("Win32_BaseBoard", "Manufacturer,Product", row => new("Материнская плата", [
            Text("vendor", "Производитель", Value(row, "Manufacturer"), "WMI/Win32_BaseBoard"), Text("model", "Модель", Value(row, "Product"), "WMI/Win32_BaseBoard")])).ToList();
        result.AddRange(Wmi("Win32_BIOS", "Manufacturer,SMBIOSBIOSVersion,ReleaseDate", row => new("BIOS / UEFI", [
            Text("vendor", "Производитель", Value(row, "Manufacturer"), "WMI/Win32_BIOS"), Text("version", "Версия", Value(row, "SMBIOSBIOSVersion"), "WMI/Win32_BIOS"),
            Date("release", "Дата выпуска", Value(row, "ReleaseDate"), "WMI/Win32_BIOS")])));
        return result;
    }
    private static IReadOnlyList<InventoryRecord> Network()
    {
        var result = new List<InventoryRecord>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var ip = adapter.GetIPProperties(); const string source = ".NET/NetworkInterface";
                result.Add(new(adapter.Name, [Text("description", "Адаптер", adapter.Description, source), Text("state", "Состояние", adapter.OperationalStatus, source),
                    adapter.Speed > 0 ? Number("speed", "Скорость канала", adapter.Speed, "bps", source) : Missing("speed", "Скорость канала", source),
                    Text("ipv4", "IPv4", Join(ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString())), source, true),
                    Text("ipv6", "IPv6", Join(ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.Address.ToString())), source, true),
                    Text("dns", "DNS-серверы", Join(ip.DnsAddresses.Select(a => a.ToString())), source, true),
                    Text("gateway", "Шлюз", Join(ip.GatewayAddresses.Select(a => a.Address.ToString())), source, true)]));
            }
            catch (Exception e) when (e is NetworkInformationException or UnauthorizedAccessException) { result.Add(new("Сетевой адаптер", [Missing("adapter", "Данные адаптера", ".NET/NetworkInterface", e)])); }
        }
        return result;
    }
    private static string? Join(IEnumerable<string> values) => string.Join(", ", values) is { Length: > 0 } text ? text : null;
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct MemoryStatus
        { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetPhysicallyInstalledSystemMemory(out ulong memoryKb);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    }
}
