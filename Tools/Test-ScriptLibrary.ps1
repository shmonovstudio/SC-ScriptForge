param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$scriptsRoot = Join-Path $projectRoot 'Data\Scripts'
$script:passed = 0; $script:failed = 0
function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Test([string]$name, [scriptblock]$body) {
    try { & $body; $script:passed++; Write-Output "PASS $name" }
    catch { $script:failed++; Write-Output "FAIL $name : $_" }
}
function Invoke-Tool([string]$relative, [hashtable]$parameters = @{}) {
    if (-not $parameters.ContainsKey('Language')) { $parameters.Language = 'en' }
    & (Join-Path $scriptsRoot $relative) @parameters 6>$null
}
function Mock-AD {
    function Get-Module { param([switch]$ListAvailable, $Name) [pscustomobject]@{ Name = 'ActiveDirectory' } }
    function Import-Module { param($Name, $ErrorAction) }
    function Get-ADUser { param($Filter, $Properties, $SearchBase, $Server, $ErrorAction) $global:ForgeFixtureUsers }
}
Test 'all 18 priority scripts parse and have bilingual metadata' {
    $count = 0
    foreach ($metaFile in (Get-ChildItem -LiteralPath $scriptsRoot -Recurse -Filter '*.ps1.meta.json')) {
        $meta = Get-Content -LiteralPath $metaFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        if (-not $meta.Translations -or -not $meta.Translations.en -or -not $meta.Translations.ru) { continue }
        $count++
        $parseErrors = $null; $tokens = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($metaFile.FullName.Replace('.meta.json', ''), [ref]$tokens, [ref]$parseErrors)
        Assert ($parseErrors.Count -eq 0) ($metaFile.Name + ': ' + ($parseErrors -join '; '))
        Assert ($meta.Translations.en.Name -and $meta.Translations.ru.Name) 'Missing title translation'
        Assert (@($ast.ParamBlock.Parameters.Name.VariablePath.UserPath) -contains 'Language') 'Missing Language parameter'
    }
    Assert ($count -eq 18) 'Expected all 18 tools rated 10/10'
}
Test 'AD name audit emits separate issues and normalized suggestions in RU and EN' {
    . (Get-Item 'Function:\Mock-AD').ScriptBlock
    $global:ForgeFixtureUsers = @([pscustomobject]@{ SamAccountName = 'ivanov'; DisplayName = ' иванов  иван '; GivenName = 'Иван'; Surname = 'Иванов' })
    $rows = @(Invoke-Tool 'Active Directory\AD-Name-Quality.ps1' @{ RequirePatronymic = $true })
    Assert ($rows.Count -eq 4) 'Expected whitespace, duplicate space, case and missing name-part issues'
    Assert ($rows | Where-Object { $_.SuggestedValue -eq 'Иванов Иван' }) 'Expected normalized suggestion'
    $ru = @(Invoke-Tool 'Active Directory\AD-Name-Quality.ps1' @{ Language = 'ru' })
    Assert ($ru[0].Issue -eq 'Пробелы по краям') 'Incorrect Russian translation'
}
Test 'inactive AD accounts exclude new and disabled accounts' {
    . (Get-Item 'Function:\Mock-AD').ScriptBlock
    $global:ForgeFixtureUsers = @(
        [pscustomobject]@{ SamAccountName = 'old'; Enabled = $true; LastLogonDate = (Get-Date).AddDays(-100); WhenCreated = (Get-Date).AddYears(-1) },
        [pscustomobject]@{ SamAccountName = 'never'; Enabled = $true; LastLogonDate = $null; WhenCreated = (Get-Date).AddDays(-100) },
        [pscustomobject]@{ SamAccountName = 'new'; Enabled = $true; LastLogonDate = $null; WhenCreated = (Get-Date).AddDays(-1) },
        [pscustomobject]@{ SamAccountName = 'disabled'; Enabled = $false; LastLogonDate = $null; WhenCreated = (Get-Date).AddYears(-1) })
    $rows = @(Invoke-Tool 'Active Directory\AD-Inactive-Users.ps1' @{ Days = 90 })
    Assert ($rows.Count -eq 2 -and $rows.SamAccountName -contains 'never') 'Incorrect inactive accounts'
}
Test 'password expiry handles expired passwords and FILETIME sentinels' {
    . (Get-Item 'Function:\Mock-AD').ScriptBlock
    $global:ForgeFixtureUsers = @(
        [pscustomobject]@{ SamAccountName = 'soon'; Enabled = $true; PasswordNeverExpires = $false; 'msDS-UserPasswordExpiryTimeComputed' = (Get-Date).AddDays(5).ToFileTime() },
        [pscustomobject]@{ SamAccountName = 'expired'; Enabled = $true; PasswordNeverExpires = $false; 'msDS-UserPasswordExpiryTimeComputed' = (Get-Date).AddDays(-5).ToFileTime() },
        [pscustomobject]@{ SamAccountName = 'sentinel'; Enabled = $true; PasswordNeverExpires = $false; 'msDS-UserPasswordExpiryTimeComputed' = [long]::MaxValue },
        [pscustomobject]@{ SamAccountName = 'zero'; Enabled = $true; PasswordNeverExpires = $false; 'msDS-UserPasswordExpiryTimeComputed' = 0 },
        [pscustomobject]@{ SamAccountName = 'never'; Enabled = $true; PasswordNeverExpires = $true; 'msDS-UserPasswordExpiryTimeComputed' = (Get-Date).AddDays(1).ToFileTime() })
    $rows = @(Invoke-Tool 'Active Directory\AD-Password-Expiry.ps1')
    Assert ($rows.Count -eq 2 -and @($rows | Where-Object Status -eq 'Expired').Count -eq 1) 'Incorrect expiry filtering'
}
Test 'missing AD attributes detect whitespace; empty queries have no fake rows' {
    . (Get-Item 'Function:\Mock-AD').ScriptBlock
    $global:ForgeFixtureUsers = @([pscustomobject]@{ SamAccountName = 'one'; DisplayName = 'Employee'; title = ' '; department = 'IT'; telephoneNumber = ''; manager = 'CN=Manager' })
    $rows = @(Invoke-Tool 'Active Directory\AD-Missing-Attributes.ps1')
    Assert ($rows.Count -eq 1 -and $rows[0].Count -eq 2 -and $rows[0].MissingAttributes -eq 'title, telephoneNumber') 'Incorrect missing fields'
    $global:ForgeFixtureUsers = @()
    Assert (@(Invoke-Tool 'Active Directory\AD-Missing-Attributes.ps1').Count -eq 0) 'Unexpected rows'
}
Test 'missing RSAT produces an actionable error in both languages' {
    function Get-Module { param([switch]$ListAvailable, $Name) }
    foreach ($language in @('ru', 'en')) {
        $caught = $false
        try { Invoke-Tool 'Active Directory\AD-Name-Quality.ps1' @{ Language = $language } } catch { $caught = $_.Exception.Message -match 'RSAT' }
        Assert $caught 'Expected RSAT prerequisite error'
    }
}
Test 'remote CIM sessions close after collection fails' {
    $global:ForgeFixtureRemoved = $false
    function New-CimSession { param($ComputerName, $ErrorAction) [pscustomobject]@{ Name = $ComputerName } }
    function Get-CimInstance { param($CimSession, $ClassName, $ErrorAction) throw 'Simulated CIM failure' }
    function Remove-CimSession { param($CimSession) $global:ForgeFixtureRemoved = $true }
    $caught = $false
    try { Invoke-Tool 'Windows\PC-System-Info.ps1' @{ ComputerName = 'fixture-server' } } catch { $caught = $true }
    Assert ($caught -and $global:ForgeFixtureRemoved) 'CIM failure must propagate and close its session'
}
Test 'zero-size disks return unknown capacity without division by zero' {
    function Get-CimInstance { param($ClassName, $Filter, $ErrorAction) [pscustomobject]@{ DeviceID = 'Z:'; VolumeName = 'fixture'; Size = 0; FreeSpace = 0 } }
    $rows = @(Invoke-Tool 'Windows\PC-Disk-Space.ps1')
    Assert ($rows.Count -eq 1 -and $null -eq $rows[0].FreePercent -and $rows[0].Status -eq 'Size unknown') 'Incorrect zero-capacity disk'
}
Test 'DNS A MX CNAME and TXT values are preserved' {
    function Resolve-DnsName {
        param($Name, $Type, [switch]$DnsOnly, $Server, $ErrorAction)
        switch ($Type) {
            'A' { [pscustomobject]@{ Name = $Name; Type = 'A'; IPAddress = '192.0.2.10'; TTL = 60 } }
            'MX' { [pscustomobject]@{ Name = $Name; Type = 'MX'; NameExchange = 'mail.example.test'; Preference = 10; TTL = 60 } }
            'CNAME' { [pscustomobject]@{ Name = $Name; Type = 'CNAME'; NameHost = 'canonical.example.test'; TTL = 60 } }
            'TXT' { [pscustomobject]@{ Name = $Name; Type = 'TXT'; Strings = @('one', 'two'); TTL = 60 } }
        }
    }
    foreach ($type in @('A', 'MX', 'CNAME', 'TXT')) {
        $rows = @(Invoke-Tool 'Network\NET-Dns-Records.ps1' @{ DnsName = 'example.test'; RecordType = $type })
        Assert ($rows.Count -eq 1 -and $rows[0].RecordType -eq $type -and $rows[0].Value) "Incorrect $type output"
    }
}
Test 'DNS failures propagate' {
    function Resolve-DnsName { param($Name, $Type, [switch]$DnsOnly, $ErrorAction) throw 'DNS name does not exist' }
    $caught = $false
    try { Invoke-Tool 'Network\NET-Dns-Records.ps1' @{ DnsName = 'missing.example.test' } } catch { $caught = $true }
    Assert $caught 'Expected DNS failure'
}
Test 'invalid ports fail before a connection is attempted' {
    foreach ($ports in @('0', '65536', 'abc', ',; ')) {
        $caught = $false
        try { Invoke-Tool 'Network\NET-Tcp-Ports.ps1' @{ ComputerName = '127.0.0.1'; Ports = $ports } } catch { $caught = $true }
        Assert $caught "Invalid ports accepted: $ports"
    }
}
Test 'failed logons use XML field names independent of element order' {
    function Get-WinEvent {
        param($ComputerName, $FilterHashtable, $MaxEvents, $ErrorAction)
        $event = [pscustomobject]@{ MachineName = 'TEST'; TimeCreated = Get-Date }
        $event | Add-Member ScriptMethod ToXml { '<Event><EventData><Data Name="Status">0xc000006d</Data><Data Name="IpAddress">192.0.2.9</Data><Data Name="TargetUserName">alice</Data><Data Name="TargetDomainName">TEST</Data><Data Name="LogonType">3</Data></EventData></Event>' }
        $event
    }
    $rows = @(Invoke-Tool 'Security\SEC-Failed-Logons.ps1')
    Assert ($rows.Count -eq 1 -and $rows[0].UserName -eq 'alice' -and $rows[0].IPAddress -eq '192.0.2.9' -and $rows[0].Status -eq '0xc000006d') 'Wrong XML mapping'
}
Test 'no matching events is clean; access denied is an error' {
    function Get-WinEvent { param($ComputerName, $FilterHashtable, $MaxEvents, $ErrorAction) Write-Error 'No events' -ErrorId NoMatchingEventsFound -ErrorAction Stop }
    foreach ($tool in @('Security\SEC-Failed-Logons.ps1', 'Servers\SRV-Windows-Errors.ps1')) { Assert (@(Invoke-Tool $tool).Count -eq 0) 'Unexpected rows' }
    function Get-WinEvent { param($ComputerName, $FilterHashtable, $MaxEvents, $ErrorAction) throw [UnauthorizedAccessException]::new('Access denied') }
    $caught = $false
    try { Invoke-Tool 'Security\SEC-Failed-Logons.ps1' } catch { $caught = $true }
    Assert $caught 'Access denied was swallowed'
}
Test 'event limit produces a warning and severity is translated' {
    function Get-WinEvent { param($ComputerName, $FilterHashtable, $MaxEvents, $ErrorAction) [pscustomobject]@{ MachineName = 'TEST'; TimeCreated = Get-Date; Id = 41; Level = 1; ProviderName = 'Kernel-Power'; Message = 'fixture event' } }
    $all = @(& (Join-Path $scriptsRoot 'Servers\SRV-Windows-Errors.ps1') -MaxEvents 1 -Language en 3>&1)
    Assert (@($all | Where-Object { $_ -is [Management.Automation.WarningRecord] }).Count -eq 1) 'Missing warning'
    Assert (@($all | Where-Object { $_.Level -eq 'Critical' }).Count -eq 1) 'Wrong severity'
}
Test 'local administrators are resolved by language-independent SID' {
    function Get-LocalGroup { param($SID, $ErrorAction) Assert ($SID -eq 'S-1-5-32-544') 'Wrong group SID'; [pscustomobject]@{ Name = 'Администраторы' } }
    function Get-LocalGroupMember { param($Group, $ErrorAction) [pscustomobject]@{ Name = 'TEST\alice'; ObjectClass = 'User'; PrincipalSource = 'Local'; SID = 'S-1-5-21-1' } }
    $rows = @(Invoke-Tool 'Security\SEC-Local-Administrators.ps1')
    Assert ($rows.Count -eq 1 -and $rows[0].Name -eq 'TEST\alice' -and $rows[0].GroupName -eq 'Администраторы') 'Incorrect members'
}
Test 'replication errors retain numeric codes; clean results have no fake rows' {
    . (Get-Item 'Function:\Mock-AD').ScriptBlock
    function Get-ADReplicationFailure { param($Target, $Scope, $ErrorAction) [pscustomobject]@{ Server = 'DC01'; Partner = 'DC02'; FailureCount = 3; FirstFailureTime = Get-Date; LastError = 1722; FailureType = 'Link' } }
    $rows = @(Invoke-Tool 'Servers\SRV-Ad-Replication.ps1' @{ Target = 'example.test'; Scope = 'Domain' })
    Assert ($rows.Count -eq 1 -and $rows[0].LastError -eq 1722) 'Incorrect replication error'
    function Get-ADReplicationFailure { param($Target, $Scope, $ErrorAction) }
    Assert (@(Invoke-Tool 'Servers\SRV-Ad-Replication.ps1' @{ Target = 'DC01' }).Count -eq 0) 'Unexpected rows'
}
Test 'invalid certificate provider paths fail clearly' {
    $caught = $false
    try { Invoke-Tool 'Security\SEC-Certificate-Expiry.ps1' @{ StorePath = 'C:\Windows' } } catch { $caught = $true }
    Assert $caught 'Expected certificate path validation'
}
Test 'release allowlist contains 22 complete scripts without deletion or dynamic execution' {
    [xml]$allowlist = Get-Content -LiteralPath (Join-Path $projectRoot 'Build\BundledScripts.props') -Raw -Encoding UTF8
    $resources = @($allowlist.Project.ItemGroup.EmbeddedResource)
    $releaseScripts = @($resources | Where-Object { $_.LogicalName.EndsWith('.ps1') })
    Assert ($releaseScripts.Count -eq 22 -and $resources.Count -eq 44) 'Expected 22 scripts and their metadata'
    $forbidden = @('Remove-Item','Remove-ADUser','Remove-ADComputer','Remove-ADGroup','Remove-Mailbox','Clear-RecycleBin','Clear-Disk','Format-Volume','Remove-Partition','del','erase','rd','rmdir','rm','Invoke-Expression','iex')
    foreach ($resource in $releaseScripts) {
        $relative = $resource.LogicalName.Substring('SC.ScriptForge.Library/'.Length)
        $file = Join-Path $scriptsRoot $relative
        $text = Get-Content -LiteralPath $file -Raw -Encoding UTF8
        Assert ($relative -notmatch '(?i)anydesk' -and $text -notmatch '(?i)anydesk') 'AnyDesk must not be included'
        Assert (Test-Path -LiteralPath ($file + '.meta.json')) 'Release card is missing'
        $sample = Join-Path (Join-Path $projectRoot 'Samples') ([IO.Path]::GetFileName($file))
        if (Test-Path -LiteralPath $sample) {
            Assert ((Get-Content -LiteralPath $sample -Raw -Encoding UTF8) -ceq $text) ('Sample differs from approved script: ' + $relative)
        }
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
        Assert ($errors.Count -eq 0) ('Syntax error: ' + $relative)
        $commands = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true) | ForEach-Object GetCommandName)
        Assert (@($commands | Where-Object { $_ -in $forbidden }).Count -eq 0) ('Deletion/dynamic execution command: ' + $relative)
        $deletes = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Value -match '^(Delete|DeleteSubKey|DeleteSubKeyTree)$' }, $true))
        Assert ($deletes.Count -eq 0) ('Deletion API: ' + $relative)
    }
}
Test 'corrected inventory includes monitor data without modifying the computer' {
    function Get-CimInstance {
        param($ClassName, $Filter)
        switch ($ClassName) {
            'Win32_OperatingSystem' { [pscustomobject]@{ CSName='fixture'; Caption='Windows fixture'; Version='10.0'; LastBootUpTime=[datetime]'2026-10-20' } }
            'Win32_ComputerSystem' { [pscustomobject]@{ Manufacturer='Fixture'; Model='Test PC'; TotalPhysicalMemory=16GB } }
            'Win32_LogicalDisk' { [pscustomobject]@{ DeviceID='C:'; Size=100GB; FreeSpace=40GB } }
            'Win32_DesktopMonitor' { [pscustomobject]@{ Name='Fixture display'; MonitorManufacturer='Fixture'; MonitorType='LCD'; Status='OK' } }
        }
    }
    $rows = @(& (Join-Path $scriptsRoot 'Windows\System-Inventory.ps1'))
    Assert ($rows.Count -eq 4 -and @($rows | Where-Object Name -eq 'Fixture display').Count -eq 1) 'Corrected monitor inventory is missing'
}
Test 'legacy system summary returns the correct operating system and memory values' {
    function Get-CimInstance {
        param($ClassName)
        if ($ClassName -eq 'Win32_OperatingSystem') { [pscustomobject]@{ Caption='Windows fixture'; BuildNumber='26100'; LastBootUpTime=[datetime]'2026-10-20' } }
        elseif ($ClassName -eq 'Win32_ComputerSystem') { [pscustomobject]@{ TotalPhysicalMemory=16GB } }
        else { throw 'Unexpected CIM class' }
    }
    $rows = @(& (Join-Path $scriptsRoot 'Windows\System-Info.ps1'))
    Assert ($rows.Count -eq 1 -and $rows[0].Windows -eq 'Windows fixture' -and $rows[0].Build -eq '26100' -and $rows[0].MemoryGB -eq 16) 'Incorrect system summary'
}

Write-Output "Checks: $($script:passed + $script:failed); failures: $script:failed"
if ($script:failed) { exit 1 }
