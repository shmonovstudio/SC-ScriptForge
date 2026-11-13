[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$ComputerName = $env:COMPUTERNAME,
    [ValidateSet('System', 'Application')][string]$LogName = 'System',
    [ValidateRange(1, 8760)][int]$Hours = 24,
    [ValidateRange(1, 10000)][int]$MaxEvents = 200,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

try {
    $events = @(Get-WinEvent -ComputerName $ComputerName -FilterHashtable @{ LogName = $LogName; Level = @(1, 2); StartTime = (Get-Date).AddHours(-$Hours) } -MaxEvents $MaxEvents -ErrorAction Stop)
} catch {
    if ($_.FullyQualifiedErrorId -like 'NoMatchingEventsFound*') { Write-Information (T 'За выбранный период критические события и ошибки не найдены.' 'No critical events or errors found in this period.') -InformationAction Continue; return }
    throw
}
foreach ($event in $events) {
    [pscustomobject]@{ ComputerName = $event.MachineName; TimeCreated = $event.TimeCreated; EventId = $event.Id; Level = $(if ($event.Level -eq 1) { T 'Критический' 'Critical' } else { T 'Ошибка' 'Error' }); Provider = $event.ProviderName; Message = $event.Message }
}
if ($events.Count -eq $MaxEvents) { Write-Warning (T 'Достигнут лимит событий. В журнале могут быть более ранние записи.' 'The event limit was reached. Earlier matching events may exist.') }
