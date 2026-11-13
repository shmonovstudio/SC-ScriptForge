<#
.PARAMETER Days
RU: Минимум дней без входа. lastLogonTimestamp обновляется с задержкой; это приблизительная оценка.
EN: Minimum days without sign-in. Replicated lastLogonTimestamp can lag; this is an approximate audit.
#>
[CmdletBinding()]
param(
    [string]$SearchBase = '',
    [string]$Server = '',
    [ValidateRange(1, 36500)][int]$Days = 90,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }
if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) { throw (T 'Установите модуль ActiveDirectory (RSAT) и подключитесь к домену.' 'Install the ActiveDirectory module (RSAT) and connect to your domain.') }
Import-Module ActiveDirectory -ErrorAction Stop
$query = @{ Filter = '*'; ErrorAction = 'Stop' }
if ($SearchBase) { $query.SearchBase = $SearchBase }
if ($Server) { $query.Server = $Server }

$cutoff = (Get-Date).AddDays(-$Days)
$count = 0
foreach ($user in (Get-ADUser @query -Properties DisplayName, LastLogonDate, WhenCreated, Enabled)) {
    $reference = if ($null -ne $user.LastLogonDate) { $user.LastLogonDate } else { $user.WhenCreated }
    if ($user.Enabled -and $null -ne $reference -and $reference -le $cutoff) {
        $count++
        [pscustomobject]@{ SamAccountName = $user.SamAccountName; DisplayName = $user.DisplayName; LastLogonDate = $user.LastLogonDate; Created = $user.WhenCreated; DaysInactive = [int][math]::Floor(((Get-Date) - $reference).TotalDays); Status = $(if ($user.LastLogonDate) { T 'Нет недавних входов' 'No recent sign-ins' } else { T 'Входы не зарегистрированы' 'No recorded sign-in' }) }
    }
}
Write-Information (T 'Оценка приблизительная: реплицируемое время входа может отставать до 14 дней.' 'Approximate audit: replicated sign-in timestamps can lag by up to 14 days.') -InformationAction Continue
if ($count -eq 0) { Write-Information (T 'Неактивные включённые учётные записи не найдены.' 'No inactive enabled accounts found.') -InformationAction Continue }
