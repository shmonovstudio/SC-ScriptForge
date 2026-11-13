[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
$ProgressPreference = 'SilentlyContinue'
$ErrorActionPreference = 'Continue'
$global:LASTEXITCODE = 0
$script:HadErrors = $false
$script:FormatRecords = [Collections.Generic.List[object]]::new()
function Register-PolicyBlock($record) {
    if ($record.Exception -is [System.Management.Automation.PSSecurityException] -and $record.FullyQualifiedErrorId -eq 'UnauthorizedAccess') {
        [IO.File]::WriteAllText($env:SC_POLICY_BLOCKED, 'ExecutionPolicy')
    }
}
function Write-ForgeError($record) {
    [Console]::Error.WriteLine($record.ToString())
    $invocation = $record.InvocationInfo
    if ($invocation -and $invocation.ScriptLineNumber -gt 0) {
        [Console]::Error.WriteLine(('At line: {0} in {1}' -f $invocation.ScriptLineNumber, $invocation.ScriptName))
        if ($invocation.Line) { [Console]::Error.WriteLine('+ ' + $invocation.Line.Trim()) }
    }
    if ($record.FullyQualifiedErrorId) { [Console]::Error.WriteLine('FullyQualifiedErrorId: ' + $record.FullyQualifiedErrorId) }
    if ($record.CategoryInfo) { [Console]::Error.WriteLine('CategoryInfo: ' + $record.CategoryInfo) }
}
function Write-ForgeEvent($kind, $text, $fields = @(), $filePath = '') {
    if ($text.Length -gt 16000) { $text = $text.Substring(0,16000) + '…'; $script:ForgePreviewLimited = $true }
    $event = @{ kind = $kind; text = $text; fields = @($fields); filePath = $filePath; limited = [bool]$script:ForgePreviewLimited }
    [Console]::Out.WriteLine($env:SC_EVENT_PREFIX + (ConvertTo-Json -InputObject $event -Depth 5 -Compress))
}
function Format-ForgeValue($value) {
    if ($null -eq $value) { return '—' }
    if ($value -is [bool]) { if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { if ($value) { return 'Yes' } else { return 'No' } }; if ($value) { return 'Да' } else { return 'Нет' } }
    if ($value -is [datetime]) { if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { return $value.ToString('yyyy-MM-dd HH:mm') }; return $value.ToString('dd.MM.yyyy HH:mm') }
    if ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        $text = ((@($value | Select-Object -First 12 | ForEach-Object { [string]$_ })) -join ', ')
        if ($value -isnot [System.Collections.ICollection] -or $value.Count -gt 12) { $script:ForgePreviewLimited = $true }
    } else { $text = [string]$value }
    if ($text.Length -gt 1000) { $text = $text.Substring(0,1000) + '…'; $script:ForgePreviewLimited = $true }
    return $text
}
try {
    $payload = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($env:SC_RUN_PAYLOAD, [Text.Encoding]::UTF8))
    $named = @{}
    foreach ($parameter in $payload.Parameters) {
        if ($parameter.TypeName -eq 'System.Management.Automation.SwitchParameter') { $named[$parameter.Name] = [switch]([bool]$parameter.Value) }
        elseif ($parameter.TypeName -eq 'System.String[]') { $named[$parameter.Name] = [string[]]@($parameter.Value) }
        else { $named[$parameter.Name] = $parameter.Value }
    }
    & $payload.ScriptPath @named *>&1 | ForEach-Object {
        $script:ForgePreviewLimited = $false
        if ($_ -is [System.Management.Automation.ErrorRecord]) {
            Register-PolicyBlock $_
            $script:HadErrors = $true
            Write-ForgeError $_
        } elseif ($_ -is [System.Management.Automation.InformationRecord]) {
            Write-ForgeEvent 'information' ([string]$_.MessageData)
        } elseif ($_ -is [System.Management.Automation.WarningRecord]) {
            Write-ForgeEvent 'warning' $_.Message
        } elseif ($_ -is [System.Management.Automation.VerboseRecord] -or $_ -is [System.Management.Automation.DebugRecord]) {
            Write-ForgeEvent 'diagnostic' $_.Message
        } elseif ($_ -is [string] -or $_ -is [ValueType]) {
            if ($_ -is [string]) { [Console]::Out.WriteLine($_) }
            else { [Console]::Out.WriteLine((Format-ForgeValue $_)) }
        } elseif ($_.PSObject.TypeNames -match '^Microsoft.PowerShell.Commands.Internal.Format.') {
            # Keep the whole formatting sequence together so Out-String can render it correctly.
            $script:FormatRecords.Add($_)
        } else {
            $item = $_
            $fields = @()
            $properties = if ($item -is [System.Collections.IDictionary]) { @($item.Keys | Select-Object -First 24) } else { @($item.PSObject.Properties | Where-Object { $_.MemberType -match 'Property' } | Select-Object -First 24 | ForEach-Object { $_.Name }) }
            foreach ($name in $properties) {
                try { $value = if ($item -is [System.Collections.IDictionary]) { $item[$name] } else { $item.$name }; $fields += @{ name = [string]$name; value = (Format-ForgeValue $value) } }
                catch { $fields += @{ name = [string]$name; value = 'Не удалось прочитать' } }
            }
            if (($item -is [System.Collections.IDictionary] -and $item.Count -gt 24) -or @($item.PSObject.Properties | Where-Object { $_.MemberType -match 'Property' } | Select-Object -First 25).Count -gt 24) { $script:ForgePreviewLimited = $true }
            $filePath = if ($item -is [System.IO.FileInfo]) { $item.FullName } else { '' }
            Write-ForgeEvent 'data' (($_ | Out-String -Width 220).TrimEnd()) $fields $filePath
        }
    }
    if ($script:FormatRecords.Count -gt 0) {
        Write-ForgeEvent 'information' (($script:FormatRecords | Out-String -Width 220).TrimEnd())
    }
    if ($script:HadErrors) { exit 1 }
    if ($global:LASTEXITCODE -is [int] -and $global:LASTEXITCODE -ne 0) { exit $global:LASTEXITCODE }
    exit 0
} catch {
    Register-PolicyBlock $_
    Write-ForgeError $_
    [Console]::Error.WriteLine([string]$_.ScriptStackTrace)
    exit 1
}
