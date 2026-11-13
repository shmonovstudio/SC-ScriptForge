Get-CimInstance Win32_OperatingSystem | Select-Object CSName, Caption, Version, LastBootUpTime
Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer, Model, TotalPhysicalMemory
Get-CimInstance Win32_LogicalDisk -Filter "DriveType = 3" | Select-Object DeviceID, Size, FreeSpace
Get-CimInstance Win32_DesktopMonitor | Select-Object Name, MonitorManufacturer, MonitorType, Status
