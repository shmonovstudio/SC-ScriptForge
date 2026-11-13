[CmdletBinding()]
param(
    [string]$SearchBase = '',
    [string]$Server = '',
    [ValidateRange(1, 3650)][int]$Days = 14,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }
if (-not (Get-Module -ListAvailable -Name ActiveDirectory)) { throw (T 'Установите модуль ActiveDirectory (RSAT) и подключитесь к домену.' 'Install the ActiveDirectory module (RSAT) and connect to your domain.') }
Import-Module ActiveDirectory -ErrorAction Stop
$query = @{ Filter = '*'; ErrorAction = 'Stop' }
if ($SearchBase) { $query.SearchBase = $SearchBase }
if ($Server) { $query.Server = $Server }

$now = Get-Date
$count = 0
foreach ($user in (Get-ADUser @query -Properties DisplayName, Enabled, PasswordNeverExpires, 'msDS-UserPasswordExpiryTimeComputed')) {
    $raw = $user.'msDS-UserPasswordExpiryTimeComputed'
    if (-not $user.Enabled -or $user.PasswordNeverExpires -or $null -eq $raw -or [long]$raw -le 0 -or [long]$raw -eq [long]::MaxValue) { continue }
    try { $expiry = [datetime]::FromFileTime([long]$raw) } catch { Write-Warning ((T 'Не удалось прочитать срок пароля: ' 'Could not read password expiry: ') + $user.SamAccountName); continue }
    if ($expiry -le $now.AddDays($Days)) {
        $count++
        [pscustomobject]@{ SamAccountName = $user.SamAccountName; DisplayName = $user.DisplayName; ExpiresAt = $expiry; DaysRemaining = [int][math]::Floor(($expiry - $now).TotalDays); Status = $(if ($expiry -le $now) { T 'Истёк' 'Expired' } else { T 'Скоро истекает' 'Expiring soon' }) }
    }
}
if ($count -eq 0) { Write-Information (T 'Пароли с истёкшим или близким сроком действия не найдены.' 'No expired or soon-to-expire passwords found.') -InformationAction Continue }
