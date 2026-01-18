# Go 采集器

## 说明
这是 Linux 采集器的 Go 源代码。

## 文件说明

### 源代码
- collector_main.go - 主程序入口
- ConfigManager.go - 配置管理（INI/JSON）
- collector_web.go - Web API 服务器
- KeyTransformer.go - 键名转换工具
- Types.go - 类型定义

### 配置文件
- go.mod - Go 模块定义
- go.sum - 依赖校验和
- collector.ini - 配置文件
- transform.json - 转换规则配置

### 构建脚本
- build_collector.bat - Windows 编译脚本
- build_collector.sh - Linux 编译脚本

## 编译命令

bash
# Windows 交叉编译
build_collector.bat

# Linux 原生编译
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go Types.go

# 运行
./collector --config collector.ini --web-port 9090

## Web 界面
启动后访问：http://localhost:9090/
