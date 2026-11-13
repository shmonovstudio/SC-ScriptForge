@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title SC ScriptForge - Avalonia Release
set "SDK=dotnet.exe"
if exist "%~dp0.tools\dotnet\dotnet.exe" set "SDK=%~dp0.tools\dotnet\dotnet.exe"
"%SDK%" publish "%~dp0SC.ScriptForge.csproj" -c Release -r win-x64 --self-contained true -m:1 -p:UseSharedCompilation=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -p:PublishAot=false -p:DebugType=None -p:DebugSymbols=false -o "%~dp0dist"
if errorlevel 1 (
  echo Build failed. Install .NET 10 SDK if it is missing.
  echo https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
if exist "%~dp0Data" if not exist "%~dp0dist\Data" xcopy "%~dp0Data\*" "%~dp0dist\Data\" /E /I /H /Y >nul
start "" "%~dp0dist\SC.ScriptForge.exe"
endlocal