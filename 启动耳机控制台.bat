@echo off
chcp 936 >nul
title 原道原点耳机控制台
echo ========================================
echo    原道原点耳机控制台
echo    正在准备，请稍候...
echo ========================================
echo.
cd /d "E:\Project\Bluetooth"
taskkill /f /im YuandaoTws.App.exe >nul 2>&1
echo [1/2] 正在编译最新版本...
dotnet build src/YuandaoTws.App -v q
if errorlevel 1 (
    echo.
    echo 编译失败，请把上方报错信息截图发我。
    pause
    exit /b 1
)
echo [2/2] 编译完成，正在启动程序...
start "" "E:\Project\Bluetooth\src\YuandaoTws.App\bin\Debug\net8.0-windows10.0.19041.0\YuandaoTws.App.exe"
echo.
echo 程序已启动，请查看屏幕。
exit /b 0
