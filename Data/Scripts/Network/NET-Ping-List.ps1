<#
.PARAMETER InputFile
RU: Текстовый файл UTF-8: один IP-адрес или хост на строку; строки с # пропускаются.
EN: UTF-8 text file: one IP address or host per line; lines starting with # are ignored.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$InputFile,
    [ValidateRange(100, 30000)][int]$TimeoutMs = 2000,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

$targets = @(Get-Content -LiteralPath $InputFile -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | Select-Object -Unique)
if ($targets.Count -eq 0) { throw (T 'Файл не содержит адресов для проверки.' 'The file contains no targets to check.') }
if ($targets.Count -gt 10000) { throw (T 'В одном файле допускается до 10000 адресов.' 'A single file may contain up to 10000 targets.') }
$ping = New-Object System.Net.NetworkInformation.Ping
try {
    foreach ($target in $targets) {
        $address = ''; $ms = $null; $detail = ''
        try {
            $reply = $ping.Send($target, $TimeoutMs)
            $ok = $reply.Status -eq [Net.NetworkInformation.IPStatus]::Success
            if ($null -ne $reply.Address) { $address = [string]$reply.Address }
            if ($ok) { $ms = $reply.RoundtripTime }
            $status = if ($ok) { T 'Доступен' 'Reachable' } else { T 'Не отвечает на ICMP' 'No ICMP reply' }
            $detail = [string]$reply.Status
        } catch { $status = T 'Ошибка проверки' 'Probe error'; $detail = $_.Exception.GetBaseException().Message }
        [pscustomobject]@{ ComputerName = $target; IPAddress = $address; Status = $status; DurationMs = $ms; Detail = $detail }
    }
} finally { $ping.Dispose() }
