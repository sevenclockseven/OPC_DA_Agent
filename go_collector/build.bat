@echo off
echo ========================================
echo   OPC DA Agent 构建脚本
echo ========================================
echo.

REM 设置构建目录
set BUILD_DIR=bin\Release
set SOLUTION=OPC_DA_Agent.csproj

echo [1/3] 清理旧的构建文件...
if exist %BUILD_DIR% (
    rmdir /S /Q %BUILD_DIR%
    echo 已清理旧的构建文件
) else (
    echo 无需清理
)

echo.
echo [2/3] 编译项目...

REM 检查MSBuild路径
set MSBUILD=
for /f "tokens=*" %%i in ('where msbuild 2^>nul') do (
    set MSBUILD=%%i
)

if "%MSBUILD%"=="" (
    echo 错误: 未找到MSBuild，请确保已安装Visual Studio
    echo.
    echo 可选方案:
    echo 1. 使用Visual Studio打开项目并手动编译
    echo 2. 安装Visual Studio Build Tools
    pause
    exit /b 1
)

echo 使用MSBuild: %MSBUILD%
"%MSBUILD%" %SOLUTION% /p:Configuration=Release /p:Platform="Any CPU" /nologo /v:minimal

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo 编译失败！
    pause
    exit /b 1
)

echo 编译成功！

echo.
echo [3/3] 复制配置文件...
copy /Y config.example.json %BUILD_DIR%\config.json
copy /Y tags.example.json %BUILD_DIR%\tags.json
copy /Y README.md %BUILD_DIR%\
copy /Y DEPLOYMENT.md %BUILD_DIR%\
copy /Y quick_start.md %BUILD_DIR%\

echo.
echo ========================================
echo   构建完成！
echo ========================================
echo.
echo 构建目录: %BUILD_DIR%
echo.
echo 运行程序:
echo   cd %BUILD_DIR%
echo   OPC_DA_Agent.exe
echo.
echo 注意: 首次运行需要以管理员身份执行
echo.
pause
