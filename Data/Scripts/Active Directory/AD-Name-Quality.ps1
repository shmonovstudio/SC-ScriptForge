<#
.PARAMETER SearchBase
RU: OU в формате DN; пустое поле — весь домен.
EN: OU distinguished name; leave blank for the whole domain.
.PARAMETER RequirePatronymic
RU: Проверять наличие третьей части ФИО. Включайте только если этого требует ваша организация.
EN: Require a third name part only if your organization requires it.
#>
[CmdletBinding()]
param(
    [string]$SearchBase = '',
    [string]$Server = '',
    [switch]$RequirePatronymic,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }
if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) { throw (T 'Установите модуль ActiveDirectory (RSAT) и подключитесь к домену.' 'Install the ActiveDirectory module (RSAT) and connect to your domain.') }
Import-Module ActiveDirectory -ErrorAction Stop
$query = @{ Filter = '*'; ErrorAction = 'Stop' }
if ($SearchBase) { $query.SearchBase = $SearchBase }
if ($Server) { $query.Server = $Server }

$count = 0
foreach ($user in (Get-ADUser @query -Properties DisplayName, GivenName, Surname)) {
    foreach ($field in @('DisplayName', 'GivenName', 'Surname')) {
        $value = [string]$user.$field
        $issues = @()
        $suggestion = ($value.Trim() -replace '\s{2,}', ' ')
        if ([string]::IsNullOrWhiteSpace($value)) { $issues += T 'Не заполнено' 'Missing value' }
        else {
            if ($value -ne $value.Trim()) { $issues += T 'Пробелы по краям' 'Leading or trailing spaces' }
            if ($value -match '\s{2,}') { $issues += T 'Повторяющиеся пробелы' 'Repeated spaces' }
            if ($value -cmatch '\p{L}' -and ($value -ceq $value.ToLowerInvariant() -or $value -ceq $value.ToUpperInvariant())) {
                $issues += T 'Проверьте регистр букв' 'Review letter case'
                $suggestion = [Globalization.CultureInfo]::GetCultureInfo('ru-RU').TextInfo.ToTitleCase($suggestion.ToLowerInvariant())
            }
            if ($field -eq 'DisplayName' -and $RequirePatronymic -and ($value.Trim() -split '\s+').Count -lt 3) { $issues += T 'Меньше трёх частей ФИО' 'Fewer than three name parts' }
        }
        foreach ($issue in $issues) {
            $count++
            [pscustomobject]@{ SamAccountName = $user.SamAccountName; DisplayName = $user.DisplayName; Attribute = $field; Issue = $issue; CurrentValue = $value; SuggestedValue = $suggestion }
        }
    }
}
if ($count -eq 0) { Write-Information (T 'Ошибок в проверенных полях ФИО не найдено.' 'No issues found in the checked name fields.') -InformationAction Continue }
