@echo off
setlocal
rem Build the settings console and publish CodexBridgeConsole.exe into gui\dist.
rem Double-click this file, or run it from a terminal.
rem   build.bat          build, then open gui\dist in Explorer and wait for a key
rem   build.bat /quiet   build only (used by start.bat)
rem Messages are ASCII on purpose: cmd.exe misreads UTF-8 Japanese as commands.

set "QUIET="
if /i "%~1"=="/quiet" set "QUIET=1"

cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] The .NET SDK ^(dotnet^) was not found in PATH.
    echo         Install the .NET SDK from https://dotnet.microsoft.com/download
    echo         and run this script again. Building needs the SDK; running the
    echo         built exe only needs .NET Framework 4.8, which Windows 10/11 already has.
    goto :fail
)

echo Publishing CodexBridgeConsole ^(Release^) to "%~dp0dist" ...
echo.
dotnet publish "CodexBridgeConsole\CodexBridgeConsole.csproj" -c Release -o "dist" --nologo
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed. See the messages above.
    echo         If CodexBridgeConsole.exe is currently running, close it and try again.
    goto :fail
)

echo.
echo [OK] Built: %~dp0dist\CodexBridgeConsole.exe
echo      Double-click that file to start the settings console.
echo      To change the dropdown choices, put choices.json next to the exe.

if defined QUIET (
    endlocal
    exit /b 0
)

explorer /select,"%~dp0dist\CodexBridgeConsole.exe"
endlocal
pause
exit /b 0

:fail
if defined QUIET (
    endlocal
    exit /b 1
)
echo.
endlocal
pause
exit /b 1
