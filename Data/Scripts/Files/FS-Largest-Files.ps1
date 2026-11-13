[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$FolderPath,
    [ValidateRange(1, 1000)][int]$Top = 50,
    [ValidateRange(0, 2147483647)][int]$MinimumSizeMB = 0,
    [ValidateSet('ru', 'en')][string]$Language = $(if ($env:SC_SCRIPTFORGE_LANGUAGE -eq 'en') { 'en' } else { 'ru' })
)
$ErrorActionPreference = 'Stop'
function T([string]$Ru, [string]$En) { if ($Language -eq 'en') { $En } else { $Ru } }

if (-not (Test-Path -LiteralPath $FolderPath -PathType Container)) { throw (T 'Указанная папка не найдена.' 'The specified folder does not exist.') }
$scanErrors = @()
$files = @(Get-ChildItem -LiteralPath $FolderPath -File -Recurse -Force -ErrorAction SilentlyContinue -ErrorVariable scanErrors |
    Where-Object { $_.Length -ge ([long]$MinimumSizeMB * 1MB) } | Sort-Object Length -Descending | Select-Object -First $Top)
foreach ($file in $files) { [pscustomobject]@{ FullName = $file.FullName; Length = $file.Length; LastWriteTime = $file.LastWriteTime } }
if ($scanErrors.Count -gt 0) { Write-Warning ((T 'Неполный результат. Не удалось прочитать объектов: ' 'Partial result. Could not read objects: ') + $scanErrors.Count) }
elseif ($files.Count -eq 0) { Write-Information (T 'Файлы по заданным условиям не найдены.' 'No files match the selected criteria.') -InformationAction Continue }
