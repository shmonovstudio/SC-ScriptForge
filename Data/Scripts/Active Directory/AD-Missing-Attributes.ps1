<#
.PARAMETER Attributes
RU: Имена полей AD, по одному на строку: title, telephoneNumber, department, manager.
EN: AD attribute names, one per line: title, telephoneNumber, department, manager.
#>
[CmdletBinding()]
param(
    [string]$SearchBase = '',
    [string]$Server = '',
    [ValidateNotNullOrEmpty()][string[]]$Attributes = @('title', 'telephoneNumber', 'department', 'manager'),
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }
if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) { throw (T 'Установите модуль ActiveDirectory (RSAT) и подключитесь к домену.' 'Install the ActiveDirectory module (RSAT) and connect to your domain.') }
Import-Module ActiveDirectory -ErrorAction Stop
$query = @{ Filter = '*'; ErrorAction = 'Stop' }
if ($SearchBase) { $query.SearchBase = $SearchBase }
if ($Server) { $query.Server = $Server }

$Attributes = @($Attributes | ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -Unique)
if ($Attributes.Count -eq 0) { throw (T 'Укажите хотя бы одно поле AD.' 'Specify at least one AD attribute.') }
$count = 0
foreach ($user in (Get-ADUser @query -Properties (@('DisplayName') + $Attributes))) {
    $missing = @($Attributes | Where-Object { [string]::IsNullOrWhiteSpace([string]$user.$_) })
    if ($missing.Count -gt 0) {
        $count++
        [pscustomobject]@{ SamAccountName = $user.SamAccountName; DisplayName = $user.DisplayName; MissingAttributes = $missing -join ', '; Count = $missing.Count }
    }
}
if ($count -eq 0) { Write-Information (T 'Все проверенные поля заполнены.' 'All checked attributes are populated.') -InformationAction Continue }
