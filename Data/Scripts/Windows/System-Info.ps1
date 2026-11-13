# Пример безопасного сбора информации о локальном Windows-компьютере.
[CmdletBinding()]
param()

$os = Get-CimInstance -ClassName Win32_OperatingSystem
$computer = Get-CimInstance -ClassName Win32_ComputerSystem

[PSCustomObject]@{
    ComputerName = $env:COMPUTERNAME
    Windows      = $os.Caption
    Build        = $os.BuildNumber
    LastBoot     = $os.LastBootUpTime
    MemoryGB     = [math]::Round($computer.TotalPhysicalMemory / 1GB, 2)
}
