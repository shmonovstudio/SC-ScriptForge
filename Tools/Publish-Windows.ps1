param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$Output = 'dist'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
$publishPath = [IO.Path]::GetFullPath($Output, $projectRoot)
if ($publishPath.StartsWith((Join-Path $projectRoot 'Data') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $publishPath -eq (Join-Path $projectRoot 'Data')) {
    throw 'Выберите папку публикации вне пользовательской Data.'
}
& $sdk publish (Join-Path $projectRoot 'SC.ScriptForge.csproj') -c Release -r $Runtime --self-contained true -m:1 -p:UseSharedCompilation=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:PublishAot=false -p:DebugType=None -p:DebugSymbols=false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Сборка не выполнена.' }
# Initialize a library only when no Data folder exists. Updates never overwrite user data.
$sourceData = Join-Path $projectRoot 'Data'
$targetData = Join-Path $publishPath 'Data'
if ((Test-Path -LiteralPath $sourceData) -and -not (Test-Path -LiteralPath $targetData)) { Copy-Item -LiteralPath $sourceData -Destination $targetData -Recurse }
Write-Output "Готово: $(Join-Path $publishPath 'SC.ScriptForge.exe')"
