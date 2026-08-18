@echo off
chcp 936 >nul
title 原点耳机控制
echo ==========================================
echo   原点耳机控制（生产版 YuandaoTws.Desktop）
echo   正在准备，请稍候...
echo ==========================================
echo.

cd /d "%~dp0"

rem 杀掉旧实例：运行中的 exe 会锁住 DLL，导致构建复制失败
taskkill /f /im YuandaoTws.Desktop.exe >nul 2>&1
taskkill /f /im YuandaoTws.App.exe >nul 2>&1

echo [1/2] 正在编译生产版...
dotnet build src/YuandaoTws.Desktop -v q
if errorlevel 1 (
    echo.
    echo 编译失败。请确认已安装 .NET 8 SDK，并查看上方报错信息。
    pause
    exit /b 1
)

echo [2/2] 编译完成，正在启动...
start "" "%~dp0src\YuandaoTws.Desktop\bin\Debug\net8.0-windows10.0.19041.0\YuandaoTws.Desktop.exe"

echo.
echo 程序已启动。若未自动连接耳机，请先在 Windows 蓝牙设置中与耳机配对。
exit /b 0
