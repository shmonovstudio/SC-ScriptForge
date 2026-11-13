<#
.SYNOPSIS
Создаёт CSV- или JSON-отчёт из Excel .xlsx или CSV без установки модулей.
.PARAMETER InputFile
Исходный файл .xlsx или .csv. Формулы Excel не вычисляются; читаются сохранённые значения.
.PARAMETER OutputFolder
Папка, в которой будет сохранён отчёт с отметкой времени.
.PARAMETER SheetName
Название листа Excel. Пустое значение выбирает первый лист; для CSV не используется.
.PARAMETER Format
Формат результата: CSV или JSON.
.PARAMETER Delimiter
Разделитель CSV, например точка с запятой или запятая.
.PARAMETER OnlyNonEmptyRows
Пропустить полностью пустые строки.
.PARAMETER MaxRows
Максимальное количество строк данных, которое попадёт в отчёт.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$InputFile,
    [Parameter(Mandatory)][string]$OutputFolder,
    [string]$SheetName = '',
    [ValidateSet('CSV','JSON')][string]$Format = 'CSV',
    [ValidateLength(1,1)][string]$Delimiter = ';',
    [switch]$OnlyNonEmptyRows,
    [ValidateRange(1,100000)][int]$MaxRows = 50000
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) { throw 'Входной файл не найден.' }
$extension = [IO.Path]::GetExtension($InputFile).ToLowerInvariant()
if ($extension -notin @('.xlsx','.csv')) { throw 'Поддерживаются файлы .xlsx и .csv.' }
[void][IO.Directory]::CreateDirectory($OutputFolder)
$records = [Collections.Generic.List[object]]::new()
if ($extension -eq '.csv') {
    Import-Csv -LiteralPath $InputFile -Delimiter ([char]$Delimiter) -Encoding UTF8 | Select-Object -First $MaxRows | ForEach-Object { $records.Add($_) }
} else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($InputFile))
    try {
        function Read-WorkbookXml([string]$Name) {
            $entry = $archive.GetEntry($Name)
            if (-not $entry) { return $null }
            if ($entry.Length -gt 67108864) { throw ('Слишком большая часть Excel: ' + $Name) }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $document = [xml]$reader.ReadToEnd(); return ,$document } finally { $reader.Dispose() }
        }
        $workbook = Read-WorkbookXml 'xl/workbook.xml'
        if (-not $workbook) { throw 'Файл не содержит книгу Excel.' }
        $namespace = [Xml.XmlNamespaceManager]::new($workbook.NameTable)
        $namespace.AddNamespace('x','http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        $sheets = @($workbook.SelectNodes('//x:sheets/x:sheet',$namespace))
        $sheet = if ([string]::IsNullOrWhiteSpace($SheetName)) { $sheets | Select-Object -First 1 } else { $sheets | Where-Object { $_.name -eq $SheetName } | Select-Object -First 1 }
        if (-not $sheet) { throw ('Лист не найден: ' + $SheetName) }
        $relationshipId = $sheet.GetAttribute('id','http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $relationships = Read-WorkbookXml 'xl/_rels/workbook.xml.rels'
        $relationship = @($relationships.Relationships.Relationship) | Where-Object { $_.Id -eq $relationshipId } | Select-Object -First 1
        if (-not $relationship -or $relationship.TargetMode -eq 'External') { throw 'Не удалось определить внутренний лист Excel.' }
        $sheetUri = [Uri]::new([Uri]'http://scriptforge.local/xl/', [string]$relationship.Target)
        if ($sheetUri.Host -ne 'scriptforge.local') { throw 'Некорректная ссылка на лист.' }
        $sheetXml = Read-WorkbookXml $sheetUri.AbsolutePath.TrimStart('/')
        $shared = Read-WorkbookXml 'xl/sharedStrings.xml'
        $strings = [Collections.Generic.List[string]]::new()
        if ($shared) {
            $sharedNamespace = [Xml.XmlNamespaceManager]::new($shared.NameTable)
            $sharedNamespace.AddNamespace('x',$namespace.LookupNamespace('x'))
            foreach ($item in $shared.SelectNodes('//x:si',$sharedNamespace)) { $strings.Add(($item.SelectNodes('.//x:t',$sharedNamespace) | ForEach-Object { $_.InnerText }) -join '') }
        }
        $sheetNamespace = [Xml.XmlNamespaceManager]::new($sheetXml.NameTable)
        $sheetNamespace.AddNamespace('x',$namespace.LookupNamespace('x'))
        $headers = $null
        foreach ($row in $sheetXml.SelectNodes('//x:sheetData/x:row',$sheetNamespace)) {
            $cells = @{}
            foreach ($cell in $row.SelectNodes('x:c',$sheetNamespace)) {
                $letters = ([string]$cell.r) -replace '\d',''
                $index = 0
                foreach ($letter in $letters.ToUpperInvariant().ToCharArray()) { $index = $index * 26 + ([int]$letter - [int][char]'A' + 1) }
                $valueNode = $cell.SelectSingleNode('x:v',$sheetNamespace)
                $value = if ($valueNode) { $valueNode.InnerText } else { '' }
                if ($cell.t -eq 's' -and $value -ne '') { $value = $strings[[int]$value] }
                elseif ($cell.t -eq 'inlineStr') { $value = ($cell.SelectNodes('.//x:t',$sheetNamespace) | ForEach-Object { $_.InnerText }) -join '' }
                elseif ($cell.t -eq 'b') { $value = if ($value -eq '1') { 'true' } else { 'false' } }
                $cells[$index - 1] = [string]$value
            }
            if ($null -eq $headers) {
                if ($cells.Count -eq 0) { continue }
                $lastColumn = ($cells.Keys | Measure-Object -Maximum).Maximum
                $headers = [Collections.Generic.List[string]]::new()
                for ($i=0; $i -le $lastColumn; $i++) {
                    $header = if ($cells.ContainsKey($i) -and -not [string]::IsNullOrWhiteSpace($cells[$i])) { $cells[$i] } else { 'Column' + ($i+1) }
                    $unique = $header; $suffix = 2
                    while ($headers.Contains($unique)) { $unique = $header + '_' + $suffix; $suffix++ }
                    $headers.Add($unique)
                }
                continue
            }
            $record = [ordered]@{}
            for ($i=0; $i -lt $headers.Count; $i++) { $record[$headers[$i]] = if ($cells.ContainsKey($i)) { $cells[$i] } else { '' } }
            $records.Add([pscustomobject]$record)
            if ($records.Count -ge $MaxRows) { break }
        }
        Write-Output ('Лист Excel: ' + $sheet.name)
    } finally { $archive.Dispose() }
}
if ($OnlyNonEmptyRows) {
    $nonempty = @($records | Where-Object { (($_.PSObject.Properties.Value | ForEach-Object { [string]$_ }) -join '').Trim().Length -gt 0 })
    $records.Clear(); foreach ($record in $nonempty) { $records.Add($record) }
}
if ($records.Count -eq 0) { throw 'В файле нет строк данных для отчёта.' }
$outputName = [IO.Path]::GetFileNameWithoutExtension($InputFile) + '-report-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.' + $Format.ToLowerInvariant()
$outputPath = Join-Path $OutputFolder $outputName
if ($Format -eq 'CSV') { $records | Export-Csv -LiteralPath $outputPath -Delimiter ([char]$Delimiter) -NoTypeInformation -Encoding UTF8 }
else { ConvertTo-Json -InputObject @($records.ToArray()) -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding UTF8 }
Write-Output ('Обработано строк: ' + $records.Count)
Write-Output ('Сохранено: ' + $outputPath)
