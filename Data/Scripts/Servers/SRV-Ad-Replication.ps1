[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Target,
    [ValidateSet('Server', 'Site', 'Domain', 'Forest')][string]$Scope = 'Server',
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) { throw (T 'Установите модуль ActiveDirectory (RSAT) и подключитесь к домену.' 'Install the ActiveDirectory module (RSAT) and connect to your domain.') }
Import-Module ActiveDirectory -ErrorAction Stop
$failures = @(Get-ADReplicationFailure -Target $Target -Scope $Scope -ErrorAction Stop)
foreach ($failure in $failures) {
    [pscustomobject]@{ ComputerName = $failure.Server; Partner = $failure.Partner; FailureCount = $failure.FailureCount; FirstFailureTime = $failure.FirstFailureTime; LastError = $failure.LastError; FailureType = [string]$failure.FailureType }
}
if ($failures.Count -eq 0) { Write-Information (T 'Текущие ошибки репликации AD не найдены.' 'No current AD replication failures found.') -InformationAction Continue }
