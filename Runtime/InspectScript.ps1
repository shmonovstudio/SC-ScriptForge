$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
try {
    $path = $env:SC_INSPECT_PATH
    $tokens = $null; $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        $lines = $ast.Extent.Text -split "\r?\n"
        $details = @($parseErrors | Select-Object -First 10 | ForEach-Object {
            $line = $_.Extent.StartLineNumber
            $source = $lines[$line - 1].Trim()
            if ($source.Length -gt 300) { $source = $source.Substring(0, 300) + '…' }
            "Строка $line, столбец $($_.Extent.StartColumnNumber): $($_.Message)`n    $source"
        })
        throw ("Ошибка синтаксиса PowerShell.`n" + ($details -join "`n`n"))
    }
    $help = $null
    try { $help = $ast.GetHelpContent() } catch { }
    $parameters = [Collections.Generic.List[object]]::new()
    $setNames = [Collections.Generic.List[string]]::new()
    $defaultSet = ''
    if ($ast.ParamBlock) {
        foreach ($attribute in $ast.ParamBlock.Attributes) {
            if ($attribute.TypeName.Name -eq 'CmdletBinding') {
                foreach ($argument in $attribute.NamedArguments) {
                    if ($argument.ArgumentName -eq 'DefaultParameterSetName' -and $argument.Argument -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $defaultSet = $argument.Argument.Value }
                }
            }
        }
        foreach ($parameter in $ast.ParamBlock.Parameters) {
            $name = $parameter.Name.VariablePath.UserPath
            $choices = [Collections.Generic.List[string]]::new()
            $sets = [Collections.Generic.List[string]]::new()
            $required = [Collections.Generic.List[string]]::new()
            foreach ($attribute in $parameter.Attributes) {
                if ($attribute -isnot [System.Management.Automation.Language.AttributeAst]) { continue }
                if ($attribute.TypeName.Name -eq 'ValidateSet') {
                    foreach ($argument in $attribute.PositionalArguments) {
                        if ($argument -is [System.Management.Automation.Language.StringConstantExpressionAst] -or $argument -is [System.Management.Automation.Language.ConstantExpressionAst]) { $choices.Add([string]$argument.Value) }
                    }
                }
                if ($attribute.TypeName.Name -eq 'Parameter') {
                    $set = '*'; $mandatory = $false
                    foreach ($argument in $attribute.NamedArguments) {
                        if ($argument.ArgumentName -eq 'Mandatory') { $mandatory = $argument.ExpressionOmitted -or $argument.Argument.Extent.Text -eq '$true' }
                        if ($argument.ArgumentName -eq 'ParameterSetName' -and $argument.Argument -is [System.Management.Automation.Language.StringConstantExpressionAst]) { $set = [string]$argument.Argument.Value }
                    }
                    $sets.Add($set)
                    if ($set -ne '*' -and -not $setNames.Contains($set)) { $setNames.Add($set) }
                    if ($mandatory) { $required.Add($set) }
                }
            }
            if ($sets.Count -eq 0) { $sets.Add('*') }
            $literal = $null
            if ($parameter.DefaultValue -is [System.Management.Automation.Language.StringConstantExpressionAst] -or $parameter.DefaultValue -is [System.Management.Automation.Language.ConstantExpressionAst]) { $literal = [string]$parameter.DefaultValue.Value }
            elseif ($parameter.DefaultValue -is [System.Management.Automation.Language.VariableExpressionAst] -and $parameter.DefaultValue.VariablePath.UserPath -in @('true','false')) { $literal = $parameter.DefaultValue.VariablePath.UserPath }
            $description = ''
            if ($help -and $help.Parameters) {
                foreach ($key in $help.Parameters.Keys) {
                    if ($key -eq $name) { $description = [string]$help.Parameters[$key]; break }
                }
            }
            $parameters.Add([pscustomobject]@{
                Name = $name; TypeName = $parameter.StaticType.FullName; Description = $description.Trim()
                HasDefault = $null -ne $parameter.DefaultValue; DefaultDisplay = $(if ($parameter.DefaultValue) { $parameter.DefaultValue.Extent.Text } else { '' })
                LiteralDefault = $literal; Choices = @($choices.ToArray()); Sets = @($sets.ToArray()); RequiredIn = @($required.ToArray())
            })
        }
    }
    $result = [pscustomobject]@{ ScriptPath = $path; DefaultSet = $defaultSet; ParameterSets = @($setNames.ToArray()); Parameters = @($parameters.ToArray()) }
    [Console]::Out.WriteLine((ConvertTo-Json -InputObject $result -Depth 8 -Compress))
    exit 0
} catch { [Console]::Error.WriteLine($_.ToString()); exit 1 }
