@echo off
cd /d "%~dp0"
if exist "%~dp0dist\SC.ScriptForge.exe" (
  start "" "%~dp0dist\SC.ScriptForge.exe"
) else (
  call "%~dp0BUILD-AND-RUN.cmd"
)
