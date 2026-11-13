[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$ComputerName = $env:COMPUTERNAME,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

$readMembers = {
    $ErrorActionPreference = 'Stop'
    $group = Get-LocalGroup -SID 'S-1-5-32-544' -ErrorAction Stop
    Get-LocalGroupMember -Group $group -ErrorAction Stop | ForEach-Object {
        [pscustomobject]@{ ComputerName = $env:COMPUTERNAME; GroupName = $group.Name; Name = $_.Name; ObjectClass = $_.ObjectClass; PrincipalSource = [string]$_.PrincipalSource; SID = [string]$_.SID }
    }
}
$members = if ($ComputerName -in @('.', 'localhost', $env:COMPUTERNAME)) { @(& $readMembers) } else { @(Invoke-Command -ComputerName $ComputerName -ScriptBlock $readMembers -ErrorAction Stop) }
$members | Select-Object ComputerName, GroupName, Name, ObjectClass, PrincipalSource, SID
if (@($members).Count -eq 0) { Write-Information (T 'Группа администраторов пуста.' 'The Administrators group has no members.') -InformationAction Continue }
