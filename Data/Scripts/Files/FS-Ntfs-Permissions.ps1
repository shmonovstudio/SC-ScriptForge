[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$FolderPath,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

if (-not (Test-Path -LiteralPath $FolderPath -PathType Container)) { throw (T 'Указанная папка не найдена.' 'The specified folder does not exist.') }
$acl = Get-Acl -LiteralPath $FolderPath -ErrorAction Stop
foreach ($rule in $acl.Access) {
    [pscustomobject]@{ FullName = $FolderPath; Owner = $acl.Owner; Identity = [string]$rule.IdentityReference; Rights = [string]$rule.FileSystemRights; AccessType = $(if ([string]$rule.AccessControlType -eq 'Allow') { T 'Разрешено' 'Allow' } else { T 'Запрещено' 'Deny' }); IsInherited = $rule.IsInherited; InheritanceFlags = [string]$rule.InheritanceFlags; PropagationFlags = [string]$rule.PropagationFlags }
}
Write-Information (T 'Показаны записи ACL. Эффективный доступ с учётом всех групп и SMB-разрешений не рассчитывается.' 'ACL entries are shown. Effective access across group memberships and SMB permissions is not calculated.') -InformationAction Continue
