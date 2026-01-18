@echo off
echo === 编译Linux采集器（Windows交叉编译）===

REM 检查Go是否安装
where go >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo 错误: Go未安装或未在PATH中
    echo 请从 https://go.dev/dl/ 下载并安装Go
    pause
    exit /b 1
)

echo Go版本:
go version
echo.

REM 设置交叉编译环境
echo 设置交叉编译环境...
set GOOS=linux
set GOARCH=amd64

REM 下载依赖
echo 下载依赖...
go mod tidy

REM 编译
echo 编译中...
go build -o collector_linux ^
    collector_main.go ^
    ConfigManager.go ^
    collector_web.go ^
    KeyTransformer.go

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ✅ 编译成功!
    echo 可执行文件: collector_linux
    echo.
    echo 上传到Linux后执行:
    echo   chmod +x collector_linux
    echo   ./collector_linux --config collector.ini --web-port 9090
    echo.
    echo 按任意键退出...
) else (
    echo.
    echo ❌ 编译失败
    echo 请检查错误信息
    echo.
    echo 按任意键退出...
)

pause
