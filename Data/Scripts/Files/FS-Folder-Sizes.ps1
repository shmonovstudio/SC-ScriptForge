<#
.PARAMETER FolderPath
RU: Размер каждой непосредственной подпапки с учётом её содержимого. Точки повторной обработки пропускаются.
EN: Size of each immediate subfolder including its contents. Reparse points are skipped.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$FolderPath,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

if (-not (Test-Path -LiteralPath $FolderPath -PathType Container)) { throw (T 'Указанная папка не найдена.' 'The specified folder does not exist.') }
$rootErrors = @()
$folders = @(Get-ChildItem -LiteralPath $FolderPath -Directory -Force -ErrorAction SilentlyContinue -ErrorVariable rootErrors | Where-Object { -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) })
$rows = foreach ($folder in $folders) {
    [long]$bytes = 0; [long]$fileCount = 0
    $errors = [Collections.Generic.List[object]]::new()
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($folder.FullName)
    while ($pending.Count -gt 0) {
        $scanErrors = @()
        $children = @(Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction SilentlyContinue -ErrorVariable scanErrors)
        foreach ($failure in $scanErrors) { $errors.Add($failure) }
        foreach ($child in $children) {
            if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($child.PSIsContainer) { $pending.Push($child.FullName) } else { $bytes += $child.Length; $fileCount++ }
        }
    }
    if ($errors.Count -gt 0) { Write-Warning ((T 'Неполный размер папки: ' 'Partial folder size: ') + $folder.FullName) }
    [pscustomobject]@{ FullName = $folder.FullName; Size = $bytes; FileCount = $fileCount; Status = $(if ($errors.Count) { T 'Неполные данные' 'Partial data' } else { T 'Подсчитано' 'Calculated' }) }
}
$rows | Sort-Object Size -Descending
if ($rootErrors.Count -gt 0) { Write-Warning (T 'Не все подпапки доступны для чтения.' 'Some subfolders could not be read.') }
elseif ($folders.Count -eq 0) { Write-Information (T 'В выбранной папке нет подпапок для расчёта.' 'The selected folder has no subfolders to measure.') -InformationAction Continue }
