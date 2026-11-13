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
    $uptime = (Get-Date) - $os.LastBootUpTime
    [pscustomobject]@{ ComputerName = $ComputerName; LastBootUpTime = $os.LastBootUpTime; UptimeDays = [math]::Round($uptime.TotalDays, 2); Uptime = ('{0}d {1:00}:{2:00}:{3:00}' -f $uptime.Days, $uptime.Hours, $uptime.Minutes, $uptime.Seconds) }
} finally { if ($null -ne $session) { Remove-CimSession -CimSession $session } }
