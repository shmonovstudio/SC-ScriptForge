[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$ComputerName = $env:COMPUTERNAME,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

$session = $null
try {
    $cim = @{ ErrorAction = 'Stop' }
    if ($ComputerName -notin @('.', 'localhost', $env:COMPUTERNAME)) { $session = New-CimSession -ComputerName $ComputerName -ErrorAction Stop; $cim.CimSession = $session }
    $os = Get-CimInstance @cim -ClassName Win32_OperatingSystem
    $system = Get-CimInstance @cim -ClassName Win32_ComputerSystem
    $bios = Get-CimInstance @cim -ClassName Win32_BIOS
    $cpu = @(Get-CimInstance @cim -ClassName Win32_Processor)
    [pscustomobject]@{ ComputerName = $system.Name; OperatingSystem = $os.Caption; Version = $os.Version; Manufacturer = $system.Manufacturer; Model = $system.Model; SerialNumber = $bios.SerialNumber; Processor = ($cpu.Name -join '; '); Cores = ($cpu | Measure-Object NumberOfCores -Sum).Sum; LogicalProcessors = ($cpu | Measure-Object NumberOfLogicalProcessors -Sum).Sum; TotalPhysicalMemory = $system.TotalPhysicalMemory; LastBootUpTime = $os.LastBootUpTime }
} finally { if ($null -ne $session) { Remove-CimSession -CimSession $session } }
