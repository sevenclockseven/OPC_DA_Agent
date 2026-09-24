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

REM 设置交叉编译环境（必须 GOOS=linux，否则在 Windows 上会编出 PE 并当 Linux 包分发）
echo 设置交叉编译环境...
set GOOS=linux
set GOARCH=amd64
set CGO_ENABLED=0

REM 下载依赖
echo 下载依赖...
go mod tidy

REM 编译（与 AGENTS.md 五文件列表一致，缺 Types.go 会链接失败）
echo 编译中...
if not exist dist mkdir dist
go build -ldflags "-s -w" -o dist\collector_linux_amd64 ^
    collector_main.go ^
    ConfigManager.go ^
    collector_web.go ^
    KeyTransformer.go ^
    Types.go

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ✅ 编译成功!
    echo 可执行文件: dist\collector_linux_amd64
    echo.
    echo 上传到Linux后执行:
    echo   chmod +x collector_linux_amd64
    echo   ./collector_linux_amd64 --config collector.ini --web-port 9090
    echo.
    echo 校验是否为 ELF: head -c 4 dist\collector_linux_amd64 ^| xxd  （应为 7f454c46）
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
