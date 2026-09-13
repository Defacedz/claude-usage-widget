@echo off
rem Relaunches the installed Claude Widget - after closing it by hand, for
rem instance. Not installed yet? It offers to run Installer.bat for you.

set "EXE=%ProgramFiles%\ClaudeWidget\ClaudeWidget.exe"
if not exist "%EXE%" (
    echo Claude Widget is not installed on this machine yet.
    choice /C YN /M "Install it now"
    if errorlevel 2 exit /b 1
    call "%~dp0Installer.bat"
    exit /b
)

start "" "%EXE%"
