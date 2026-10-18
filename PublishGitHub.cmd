@echo off
setlocal
set "PUBLICATION_SCRIPT=%~dp0dispatch-release.ps1"
set "PUBLICATION_NO_PAUSE="
if /i "%~1"=="--no-pause" (
    set "PUBLICATION_NO_PAUSE=1"
    shift
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PUBLICATION_SCRIPT%" %1 %2 %3 %4 %5 %6 %7 %8 %9
set "PUBLICATION_EXIT=%ERRORLEVEL%"
if not defined PUBLICATION_NO_PAUSE pause
exit /b %PUBLICATION_EXIT%
