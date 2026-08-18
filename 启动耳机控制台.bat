@echo off
chcp 65001 >nul
title YuandaoTws Desktop

cd /d "%~dp0"

rem 某些精简启动器不会继承 WINDIR，WPF 字体缓存需要它来定位 Windows\Fonts。
if not defined WINDIR if defined SystemRoot set "WINDIR=%SystemRoot%"
if not defined WINDIR set "WINDIR=%SystemDrive%\Windows"

rem Stop the previous instance so native DLLs can be rebuilt safely.
taskkill /f /im YuandaoTws.Desktop.exe >nul 2>&1
taskkill /f /im YuandaoTws.App.exe >nul 2>&1

set "DOTNET=dotnet"
where dotnet >nul 2>&1
if errorlevel 1 (
    set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
    if not exist "%ProgramFiles%\dotnet\dotnet.exe" (
        echo .NET 8 SDK was not found. Please install the .NET 8 SDK first.
        pause
        exit /b 1
    )
)

echo [1/2] Building YuandaoTws.Desktop for x64...
set "RESTORE_MODE="
if exist "src\YuandaoTws.Desktop\obj\project.assets.json" set "RESTORE_MODE=--no-restore"
"%DOTNET%" build src/YuandaoTws.Desktop -c Debug -p:Platform=x64 -p:PlatformTarget=x64 %RESTORE_MODE% -v q
if errorlevel 1 (
    echo.
    echo Build failed. Check the error output above.
    pause
    exit /b 1
)

echo [2/2] Build complete. Starting the desktop app...
start "" "%~dp0src\YuandaoTws.Desktop\bin\x64\Debug\net8.0-windows10.0.19041.0\YuandaoTws.Desktop.exe"

exit /b 0
