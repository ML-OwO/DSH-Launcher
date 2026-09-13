@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
title DeepSeek Harness
cd /d "%USERPROFILE%"

echo 正在检查 DeepSeek Harness 更新...

set "PKG_NAME=@deepseek-ai/dsh"

:: 获取本地全局安装的版本号
for /f "delims=" %%i in ('dsh --version 2^>nul') do set "LOCAL_VER=%%i"

:: 1. 获取 npm 上最新的稳定版（latest）
for /f "delims=" %%i in ('npm view !PKG_NAME! version 2^>nul') do set "LATEST_VER=%%i"

:: 2. 获取 npm 上最新的全量版本（包含 alpha/beta 等预览版）
for /f "delims=" %%i in ('powershell -NoProfile -Command "(npm view !PKG_NAME! versions --json 2>$null | ConvertFrom-Json)[-1]"') do set "NEWEST_VER=%%i"

:: ----------------------------------------------------
:: 检查点 A：如果存在 Alpha/预览版本，仅输出提示信息
:: ----------------------------------------------------
if defined NEWEST_VER (
    if defined LATEST_VER (
        if not "!NEWEST_VER!"=="!LATEST_VER!" (
            echo [提示] 官方当前已发布预览版: !NEWEST_VER! ^(已自动跳过，优先保障稳定版^)
            echo.
        )
    )
)

:: ----------------------------------------------------
:: 检查点 B：针对 Latest 稳定版的更新逻辑
:: ----------------------------------------------------
if defined LATEST_VER (
    if defined LOCAL_VER (
        if not "!LOCAL_VER!"=="!LATEST_VER!" (
            echo [提示] 发现新的稳定版！
            echo 当前版本: !LOCAL_VER!
            echo 最新稳定版: !LATEST_VER!
            echo.
            set /p "choice=是否立即更新全局包到最新稳定版？(Y/N): "
            if /i "!choice!"=="Y" (
                echo.
                echo 正在更新 !PKG_NAME! 到稳定版 !LATEST_VER!...
                echo ==================================================
                call npm install -g !PKG_NAME!@!LATEST_VER! --force --progress=true --loglevel=info
                echo ==================================================
                echo.
                echo 更新完成！
            )
        ) else (
            echo 已是最新稳定版本 !LOCAL_VER!
        )
    ) else (
        echo 未检测到本地 dsh 命令，尝试直接运行。
    )
) else (
    echo 网络连接失败或未找到远程包信息，跳过更新检查。
)

echo.
echo 正在启动 DeepSeek Harness...
echo.

dsh web

pause