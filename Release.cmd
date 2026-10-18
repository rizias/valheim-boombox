@echo off
setlocal
set "RELEASE_SCRIPT=%~dp0release.ps1"
set "RELEASE_NO_PAUSE="
if /i "%~1"=="--no-pause" (
    set "RELEASE_NO_PAUSE=1"
    shift
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%RELEASE_SCRIPT%" %1 %2 %3 %4 %5 %6 %7 %8 %9
set "RELEASE_EXIT=%ERRORLEVEL%"
if not defined RELEASE_NO_PAUSE pause
exit /b %RELEASE_EXIT%
