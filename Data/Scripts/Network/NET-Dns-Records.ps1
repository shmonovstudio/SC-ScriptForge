[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$DnsName,
    [ValidateSet('A', 'AAAA', 'MX', 'CNAME', 'TXT', 'NS', 'SOA', 'PTR')][string]$RecordType = 'A',
    [string]$DnsServer = '',
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

$query = @{ Name = $DnsName; Type = $RecordType; DnsOnly = $true; ErrorAction = 'Stop' }
if ($DnsServer) { $query.Server = $DnsServer }
$count = 0
foreach ($record in (Resolve-DnsName @query)) {
    $value = switch ([string]$record.Type) {
        { $_ -in @('A', 'AAAA') } { $record.IPAddress }
        'MX' { $record.NameExchange }
        { $_ -in @('CNAME', 'NS', 'PTR') } { $record.NameHost }
        'TXT' { $record.Strings -join ' ' }
        'SOA' { $record.PrimaryServer }
        default { [string]$record }
    }
    $count++
    [pscustomobject]@{ DnsName = $record.Name; RecordType = [string]$record.Type; Value = $value; TtlSeconds = $record.TTL; Preference = $record.Preference }
}
if ($count -eq 0) { Write-Information (T 'DNS-записи не найдены.' 'No DNS records found.') -InformationAction Continue }
