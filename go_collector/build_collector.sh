#!/bin/bash

# Linux采集器编译脚本

echo "=== 编译Linux采集器 ==="

# 检查Go是否安装
if ! command -v go &> /dev/null; then
    echo "错误: Go未安装"
    exit 1
fi

echo "Go版本: $(go version)"

# 下载依赖
echo "下载依赖..."
go mod tidy

# 编译（本机 Linux；五文件含 Types.go；CGO 关闭避免依赖 musl/glibc 差异）
echo "编译中..."
export GOOS="${GOOS:-linux}"
export GOARCH="${GOARCH:-amd64}"
export CGO_ENABLED=0
mkdir -p dist
go build -ldflags "-s -w" -o dist/collector_linux_amd64 \
    collector_main.go \
    ConfigManager.go \
    collector_web.go \
    KeyTransformer.go \
    Types.go

if [ $? -eq 0 ]; then
    echo "✅ 编译成功!"
    echo "可执行文件: ./dist/collector_linux_amd64"
    echo "ELF头: $(head -c 4 dist/collector_linux_amd64 | xxd -p)  (期望 7f454c46)"
    echo ""
    echo "使用方法:"
    echo "  ./dist/collector_linux_amd64 --config collector.ini --web-port 9090"
    echo "  ./dist/collector_linux_amd64 --help"
else
    echo "❌ 编译失败"
    exit 1
fi
