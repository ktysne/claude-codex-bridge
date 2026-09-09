@echo off
setlocal
rem Start the settings console. Builds it first when gui\dist\CodexBridgeConsole.exe
rem does not exist yet. Double-click this file, or run it from a terminal.
rem Messages are ASCII on purpose: cmd.exe misreads UTF-8 Japanese as commands.

cd /d "%~dp0"

if not exist "dist\CodexBridgeConsole.exe" (
    echo CodexBridgeConsole.exe is not built yet. Building it first...
    echo.
    call build.bat /quiet
    if errorlevel 1 (
        echo.
        echo [ERROR] Build failed, so the settings console cannot start.
        pause
        endlocal
        exit /b 1
    )
    echo.
)

echo Starting %~dp0dist\CodexBridgeConsole.exe
start "" "dist\CodexBridgeConsole.exe"
endlocal
exit /b 0
