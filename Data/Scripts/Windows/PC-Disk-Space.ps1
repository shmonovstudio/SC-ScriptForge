[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$ComputerName = $env:COMPUTERNAME,
    [ValidateRange(0, 100)][int]$MinimumFreePercent = 15,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

$session = $null
try {
    $cim = @{ ErrorAction = 'Stop' }
    if ($ComputerName -notin @('.', 'localhost', $env:COMPUTERNAME)) { $session = New-CimSession -ComputerName $ComputerName -ErrorAction Stop; $cim.CimSession = $session }
    $count = 0
    foreach ($disk in (Get-CimInstance @cim -ClassName Win32_LogicalDisk -Filter 'DriveType=3')) {
        $free = if ($disk.Size -gt 0) { [math]::Round(100 * $disk.FreeSpace / $disk.Size, 2) } else { $null }
        $count++
        [pscustomobject]@{ ComputerName = $ComputerName; DeviceId = $disk.DeviceID; VolumeName = $disk.VolumeName; Size = $disk.Size; FreeSpace = $disk.FreeSpace; FreePercent = $free; Status = $(if ($null -eq $free) { T 'Объём неизвестен' 'Size unknown' } elseif ($free -lt $MinimumFreePercent) { T 'Мало свободного места' 'Low free space' } else { T 'Достаточно места' 'Sufficient space' }) }
    }
    if ($count -eq 0) { Write-Information (T 'Локальные диски не найдены.' 'No fixed disks found.') -InformationAction Continue }
} finally { if ($null -ne $session) { Remove-CimSession -CimSession $session } }
