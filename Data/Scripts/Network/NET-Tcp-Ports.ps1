<#
.PARAMETER Ports
RU: TCP-порты через запятую, например 80,443,3389,445.
EN: Comma-separated TCP ports, for example 80,443,3389,445.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ComputerName,
    [ValidateNotNullOrEmpty()][string]$Ports = '80,443,3389,445',
    [ValidateRange(100, 30000)][int]$TimeoutMs = 2000,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

$portNumbers = @($Ports -split '[,;\s]+' | Where-Object { $_ } | ForEach-Object {
    $number = 0
    if (-not [int]::TryParse($_, [ref]$number) -or $number -lt 1 -or $number -gt 65535) { throw (T 'Порты должны быть числами от 1 до 65535.' 'Ports must be numbers between 1 and 65535.') }
    $number
} | Select-Object -Unique)
if ($portNumbers.Count -eq 0 -or $portNumbers.Count -gt 256) { throw (T 'Укажите от 1 до 256 портов.' 'Specify between 1 and 256 ports.') }
foreach ($port in $portNumbers) {
    $client = New-Object System.Net.Sockets.TcpClient
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $status = T 'Недоступен' 'Unavailable'
    $detail = ''
    try {
        $task = $client.ConnectAsync($ComputerName, $port)
        if (-not $task.Wait($TimeoutMs)) { $status = T 'Тайм-аут' 'Timed out' }
        elseif ($client.Connected) { $status = T 'Доступен' 'Open' }
    } catch { $detail = $_.Exception.GetBaseException().Message }
    finally { $timer.Stop(); $client.Dispose() }
    [pscustomobject]@{ ComputerName = $ComputerName; Port = $port; Status = $status; DurationMs = $timer.ElapsedMilliseconds; Detail = $detail }
}
