[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$ErrorActionPreference = 'Stop'
$source = [Console]::In.ReadToEnd()
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$parseErrors)
$issues = [Collections.Generic.List[string]]::new()
$notes = [Collections.Generic.List[string]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$checks = 0
function Literal($node) {
    if ($node -is [Management.Automation.Language.StringConstantExpressionAst]) { return $node.Value }
    return $null
}
function Arguments($command) {
    $named = @{}; $positional = [Collections.Generic.List[string]]::new()
    for ($index = 1; $index -lt $command.CommandElements.Count; $index++) {
        $element = $command.CommandElements[$index]
        if ($element -is [Management.Automation.Language.CommandParameterAst]) {
            $name = $element.ParameterName
            $value = $null
            if ($element.Argument) { $value = Literal $element.Argument }
            elseif ($index + 1 -lt $command.CommandElements.Count -and $command.CommandElements[$index + 1] -isnot [Management.Automation.Language.CommandParameterAst]) {
                $index++; $value = Literal $command.CommandElements[$index]
            }
            $named[$name] = $value
        } else {
            $value = Literal $element
            if ($null -ne $value) { $positional.Add($value) }
        }
    }
    return @{ Named = $named; Positional = $positional }
}
function SelectedProperties($command) {
    $pipeline = $command.Parent
    if ($pipeline -isnot [Management.Automation.Language.PipelineAst]) { return }
    $position = [array]::IndexOf(@($pipeline.PipelineElements), $command)
    if ($position -lt 0 -or $position + 1 -ge $pipeline.PipelineElements.Count) { return }
    $select = $pipeline.PipelineElements[$position + 1]
    if ($select -isnot [Management.Automation.Language.CommandAst] -or $select.GetCommandName() -notin @('Select-Object', 'select')) { return }
    $reading = $true
    foreach ($element in @($select.CommandElements | Select-Object -Skip 1)) {
        if ($element -is [Management.Automation.Language.CommandParameterAst]) {
            $reading = $element.ParameterName -eq 'Property'
            if ($reading -and $element.Argument) { Literal $element.Argument }
        } elseif ($reading) {
            if ($element -is [Management.Automation.Language.ArrayLiteralAst]) { foreach ($item in $element.Elements) { Literal $item } }
            else { Literal $element }
        }
    }
}
$functions = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
$commands = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true))
foreach ($command in $commands) {
    $name = $command.GetCommandName()
    if (-not $name -or $functions -contains $name) { continue }
    $name = ($name -split '\\')[-1]
    if ($name -notin @('Import-Module', 'Get-CimInstance', 'Get-WmiObject', 'gcim', 'gwmi')) { continue }
    $checks++
    if ($checks -gt 16) { $notes.Add('Достигнут лимит проверки зависимостей. Остальные вызовы не проверялись.'); break }
    $arguments = Arguments $command
    $line = $command.Extent.StartLineNumber
    if ($name -eq 'Import-Module') {
        $module = if ($arguments.Named.ContainsKey('Name')) { $arguments.Named.Name } elseif ($arguments.Positional.Count) { $arguments.Positional[0] } else { $null }
        if (-not $module -or $module -notmatch '^[A-Za-z0-9_.-]+$' -or -not $seen.Add('module:' + $module)) { continue }
        if (-not (Get-Module -ListAvailable -Name $module)) {
            $message = "Строка $line : Import-Module $module — такой модуль не найден в выбранном PowerShell."
            if ($module -in @('System.Management', 'System.Diagnostics')) {
                $message += ' Это пространство имён .NET, а не необходимый модуль для Get-CimInstance. Не заменяй его другим выдуманным импортом; для этих CIM-запросов дополнительный Import-Module не нужен.'
            }
            $issues.Add($message)
        }
        continue
    }
    # Inspect local schema only. Never connect to a target taken from the submitted script.
    if (@($arguments.Named.Keys | Where-Object { $_ -in @('ComputerName', 'CimSession', 'Session', 'Credential', 'Query') }).Count -gt 0) {
        $notes.Add("Строка $line : удалённый или произвольный WMI-запрос не проверялся."); continue
    }
    $class = if ($arguments.Named.ContainsKey('ClassName')) { $arguments.Named.ClassName } elseif ($arguments.Named.ContainsKey('Class')) { $arguments.Named.Class } elseif ($arguments.Positional.Count) { $arguments.Positional[0] } else { $null }
    $namespace = if ($arguments.Named.ContainsKey('Namespace')) { $arguments.Named.Namespace } else { 'root\cimv2' }
    if (-not $class -or $class -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or -not $namespace -or $namespace -notmatch '^root[\\/][A-Za-z0-9_\\/]+$') {
        $notes.Add("Строка $line : динамическое имя класса или пространства имён не проверялось."); continue
    }
    try {
        $schema = Get-CimClass -Namespace $namespace -ClassName $class -ErrorAction Stop
        $properties = @($schema.CimClassProperties.Name)
        $selected = @(SelectedProperties $command | Where-Object { $_ -and $_ -notmatch '[*?]' })
        $filter = $arguments.Named.Filter
        $filterProperties = @()
        if ($filter) {
            $withoutValues = $filter -replace "'(?:[^']|'')*'|""(?:[^""]|"""")*""", "''"
            $filterProperties = @([regex]::Matches($withoutValues, '(?i)\b([a-z_]\w*)\s*(?:=|<>|!=|<=|>=|<|>|\blike\b|\bis\b)') | ForEach-Object { $_.Groups[1].Value })
            foreach ($comparison in [regex]::Matches($withoutValues, '(?i)\b([a-z_]\w*)\s*(?:=|<>|!=|<=|>=|<|>)\s*(-?\d+(?:\.\d+)?)\b')) {
                $property = $schema.CimClassProperties | Where-Object { $_.Name -eq $comparison.Groups[1].Value } | Select-Object -First 1
                if ($property -and [string]$property.CimType -eq 'String') {
                    $issues.Add("Строка $line : поле $class.$($property.Name) имеет тип String, но в фильтре сравнивается с числом $($comparison.Groups[2].Value). Не придумывай значение фильтра. Если у нового класса нет эквивалента исходному условию, убери это условие и объясни ограничение.")
                }
            }
        }
        $missing = @(@($selected) + @($filterProperties) | Where-Object { $_ -notin $properties -and $_ -notin @('PSComputerName', 'CimClass', 'CimInstanceProperties', 'CimSystemProperties') -and $_ -notlike '__*' } | Select-Object -Unique)
        if ($missing.Count) {
            $issues.Add("Строка $line : класс $namespace\$class существует, но у него нет полей: $($missing -join ', '). Доступные поля: $(($properties | Select-Object -First 28) -join ', ').")
        }
    } catch {
        if ($_.Exception -is [Microsoft.Management.Infrastructure.CimException] -and [int]$_.Exception.NativeErrorCode -in @(3, 5, 6)) {
            $message = "Строка $line : класс $namespace\$class не существует на этом компьютере."
            if ($class -match 'Monitor') {
                $message += ' Для сведений о мониторах используй проверенный класс root\cimv2:Win32_DesktopMonitor с полями Name, MonitorManufacturer, MonitorType, Status; у него нет Connected, Model, Manufacturer и VideoBus. Не заменяй Connected=true на Status=1: Status имеет строковый тип, для простой инвентаризации фильтр нужно убрать. Для активных мониторов подходит root\wmi:WmiMonitorID с Active, UserFriendlyName, ManufacturerName (последние два — массивы кодов символов).'
            }
            $issues.Add($message)
        } else { $notes.Add("Строка $line : проверить метаданные $namespace\$class не удалось. Это не подтверждение корректности вызова.") }
    }
}
@{ Errors = @($issues.ToArray()); Notes = @($notes.ToArray()) } | ConvertTo-Json -Depth 4 -Compress
