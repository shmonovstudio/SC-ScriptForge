[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$ComputerName = $env:COMPUTERNAME,
    [ValidateRange(1, 8760)][int]$Hours = 24,
    [ValidateRange(1, 10000)][int]$MaxEvents = 200,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

try {
    $events = @(Get-WinEvent -ComputerName $ComputerName -FilterHashtable @{ LogName = 'Security'; Id = 4625; StartTime = (Get-Date).AddHours(-$Hours) } -MaxEvents $MaxEvents -ErrorAction Stop)
} catch {
    if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { Write-Information (T 'За выбранный период неудачные входы не найдены. Проверьте, включён ли аудит входа.' 'No failed logons found in this period. Verify that logon auditing is enabled.') -InformationAction Continue; return }
    throw
}
foreach ($event in $events) {
    $data = @{}
    foreach ($node in ([xml]$event.ToXml()).Event.EventData.Data) { $data[[string]$node.Name] = [string]$node.'#text' }
    [pscustomobject]@{ ComputerName = $event.MachineName; TimeCreated = $event.TimeCreated; UserName = $data.TargetUserName; Domain = $data.TargetDomainName; IPAddress = $data.IpAddress; Workstation = $data.WorkstationName; LogonType = $data.LogonType; Status = $data.Status; SubStatus = $data.SubStatus }
}
if ($events.Count -eq $MaxEvents) { Write-Warning (T 'Достигнут лимит событий. В журнале могут быть более ранние записи.' 'The event limit was reached. Earlier matching events may exist.') }
