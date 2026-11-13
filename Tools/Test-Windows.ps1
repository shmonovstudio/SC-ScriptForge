$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
& $sdk build (Join-Path $projectRoot 'Tests\SC.ScriptForge.Checks.csproj') -c Release -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& powershell.exe -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Test-ScriptLibrary.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $sdk (Join-Path $projectRoot 'Tests\bin\Release\net10.0-windows\SC.ScriptForge.Checks.dll')
exit $LASTEXITCODE
