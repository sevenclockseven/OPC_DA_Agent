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

# 编译
echo "编译中..."
go build -o collector \
    collector_main.go \
    ConfigManager.go \
    collector_web.go \
    KeyTransformer.go

if [ $? -eq 0 ]; then
    echo "✅ 编译成功!"
    echo "可执行文件: ./collector"
    echo ""
    echo "使用方法:"
    echo "  ./collector --config collector.ini --web-port 9090"
    echo "  ./collector --help"
else
    echo "❌ 编译失败"
    exit 1
fi
