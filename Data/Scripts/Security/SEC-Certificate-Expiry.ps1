<#
.PARAMETER StorePath
RU: Хранилище сертификатов Windows, например Cert:\LocalMachine\My.
EN: Windows certificate store, for example Cert:\LocalMachine\My.
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$StorePath = 'Cert:\LocalMachine\My',
    [ValidateRange(0, 3650)][int]$Days = 30,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

if ($StorePath -notmatch '^Cert:\\') { throw (T 'Укажите путь в хранилище Cert:\.' 'Specify a path in the Cert:\ store.') }
$now = Get-Date; $count = 0
foreach ($cert in (Get-ChildItem -LiteralPath $StorePath -Recurse -ErrorAction Stop)) {
    if ($cert -is [Security.Cryptography.X509Certificates.X509Certificate2] -and $cert.NotAfter -le $now.AddDays($Days)) {
        $count++
        [pscustomobject]@{ Subject = $cert.Subject; Issuer = $cert.Issuer; NotAfter = $cert.NotAfter; DaysRemaining = [int][math]::Floor(($cert.NotAfter - $now).TotalDays); Thumbprint = $cert.Thumbprint; HasPrivateKey = $cert.HasPrivateKey; Status = $(if ($cert.NotAfter -le $now) { T 'Истёк' 'Expired' } else { T 'Скоро истекает' 'Expiring soon' }) }
    }
}
if ($count -eq 0) { Write-Information (T 'Сертификаты с истёкшим или близким сроком действия не найдены.' 'No expired or soon-to-expire certificates found.') -InformationAction Continue }
