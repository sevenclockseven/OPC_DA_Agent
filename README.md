# OPC DA 数据采集系统

完整的 OPC DA 数据采集解决方案，支持 Windows 和 Linux 平台。

## 📁 项目结构

```
OPC_DA_Agent/
├── go_collector/          # Go 采集器（Linux）
│   ├── collector_main.go
│   ├── ConfigManager.go
│   ├── collector_web.go
│   ├── KeyTransformer.go
│   ├── Types.go
│   ├── go.mod
│   ├── go.sum
│   ├── collector.ini
│   ├── transform.json
│   ├── build_collector.bat
│   ├── build_collector.sh
│   └── README.md
│
├── csharp_agent/          # C# 代理（Windows）
│   ├── Program.cs
│   ├── Config.cs
│   ├── Logger.cs
│   ├── DataModel.cs
│   ├── HttpServer.cs
│   ├── OPCService.cs
│   ├── OPCBrowser.cs
│   ├── OPC_DA_Agent.csproj
│   ├── App.config
│   ├── packages.config
│   ├── build.bat
│   └── README.md
│
├── docs/                  # 文档（放在各子目录中）
│   ├── AGENTS.md          # AI 开发指南
│   ├── BROWSE_API.md
│   ├── COLLECTOR_CONFIG.md
│   ├── FILE_INDEX.md
│   ├── KEY_VALUE_FORMAT.md
│   ├── OPC_DA_REAL_FORMAT.md
│   └── OPC_DA_SETUP_GUIDE.md
│
├── bin/                   # 编译输出（自动生成）
│   ├── go_collector.exe    # Linux 采集器可执行文件
│   └── windows_agent.exe  # Windows 代理可执行文件
│
└── README.md             # 本文件
```

## 🚀 快速开始

### Windows 代理（C#）

```batch
# 编译
cd csharp_agent
build.bat

# 运行
cd bin\Release
OPC_DA_Agent.exe --config config.json
```

**Web API**: http://localhost:8080/api/

### Linux 采集器（Go）

```bash
# 编译
cd go_collector
./build_collector.sh

# 或交叉编译（在 Windows 上）
./build_collector.bat

# 运行
./collector --config collector.ini --web-port 9090
```

**Web 界面**: http://localhost:9090/

## 📖 详细文档

### Go 采集器
参见 [go_collector/README.md](go_collector/README.md)

### C# 代理
参见 [csharp_agent/README.md](csharp_agent/README.md)

### 开发者文档
- [AGENTS.md](AGENTS.md) - AI 开发指南
- [BROWSE_API.md](docs/BROWSE_API.md) - 浏览 API 文档
- [COLLECTOR_CONFIG.md](docs/COLLECTOR_CONFIG.md) - 采集器配置
- [FILE_INDEX.md](docs/FILE_INDEX.md) - 文件索引

## 🔧 编译命令

### C# Windows 代理
```batch
# 使用构建脚本
cd csharp_agent
build.bat

# 或直接使用 MSBuild
msbuild OPC_DA_Agent.csproj /p:Configuration=Release
```

### Go Linux 采集器
```bash
# 使用构建脚本
cd go_collector
./build_collector.sh

# 或直接使用 go build
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go Types.go
```

## 🧪 测试

### 测试 Go 采集器
```bash
cd go_collector
./final-v2.exe --config collector.ini --web-port 9090
```

### 测试 C# 代理
```batch
cd csharp_agent
msbuild OPC_DA_Agent.csproj /p:Configuration=Debug
cd bin\Debug
OPC_DA_Agent.exe
```

## 📊 API 端点

### C# 代理 (端口 8080)
- `GET /api/status` - 系统状态
- `GET /api/data` - 当前数据（键值对）
- `GET /api/data/list` - 当前数据（列表）
- `GET /api/browse` - 浏览根节点
- `GET /api/browse/node?nodeId=xxx` - 浏览指定节点
- `POST /api/save-tags` - 保存标签配置

### Go 采集器 (端口 9090)
- `GET /api/config` - 获取配置
- `POST /api/config` - 更新配置
- `GET /api/config/import` - 导入配置
- `GET /api/config/export` - 导出配置
- `POST /api/mqtt/test` - 测试 MQTT
- `GET /api/status` - 获取状态
- `GET /api/data` - 获取数据

## 🏗️ 架构

```
┌─────────────────┐         HTTP API          ┌──────────────────┐
│  Linux采集程序   │ ◄─────────────────────────► │ Windows代理程序  │
│  (Go语言)       │                            │  (C# .NET)      │
│                 │                            │                 │
│  ┌───────────┐  │                            │  ┌───────────┐  │
│  │ MQTT发布  │  │                            │  │ OPC DA    │  │
│  │ (可选)    │  │                            │  │ 客户端    │  │
│  └───────────┘  │                            │  └───────────┘  │
│                 │                            │                 │
│  ┌───────────┐  │                            │  ┌───────────┐  │
│  │ Web配置   │  │                            │  │ OPC浏览器  │  │
│  │ 界面      │  │                            │  │ (浏览/选择)│  │
│  └───────────┘  │                            │  └───────────┘  │
└─────────────────┘                            └──────────────────┘
                                                    │
                                                    │ OPC DA协议
                                                    ▼
                                           ┌──────────────────┐
                                           │  OPC DA服务器    │
                                           │  (Windows)       │
                                           └──────────────────┘
```

## 📝 配置示例

### collector.ini (Go 采集器)
```ini
[main]
title=采集系统
debug=False
task_count=1
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data

[task1]
task=True
job_interval_second=1
tag_count=100
tag_opc1=channel1.device1.value
tag_dbn1=device1_value
```

### config.json (C# 代理)
```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "batch_size": 500,
  "log_file": "logs\\opc_agent.log",
  "log_level": "Info"
}
```

## 🔍 故障排除

### Windows 代理
| 问题 | 解决方案 |
|------|---------|
| 无法连接 OPC 服务器 | 检查 OPC 服务、DCOM 权限 |
| HTTP 服务器启动失败 | 以管理员权限运行 |
| 点号找不到 | 使用浏览 API 查看可用标签 |

### Linux 采集器
| 问题 | 解决方案 |
|------|---------|
| 配置加载失败 | 检查 INI/JSON 格式 |
| MQTT 连接失败 | 检查服务器地址、端口 |
| Web 界面无法访问 | 检查端口占用、防火墙 |

## 📄 许可证

MIT License

---

**项目完成**: 2026-01-18
