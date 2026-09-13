@echo off
chcp 65001 >nul
cd /d "%~dp0"
dotnet publish DSHTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o 发布
if errorlevel 1 (
    echo 构建失败，请确认已安装 .NET 8 SDK。
    pause
    exit /b 1
)
echo 构建完成：%~dp0发布\DSH Launcher.exe
pause
