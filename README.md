# OPC DA 数据采集系统

OPC DA（OPC DA 2.0）转 HTTP/SSE 的数据采集方案：

- **Windows 代理（C# .NET Framework 4.0，x86）** 连接 OPC DA 服务器，通过订阅（DataChange）实时获取数据，并以 SSE 把变化推送给采集器。
- **Linux 采集器（Go）** 消费 SSE 流（或兼容的 HTTP 轮询），经 `transform.json` 做键名映射后转发到 MQTT / HTTP。

## 架构

```
OPC DA 服务器 (如 ABB Freelance2000)
      │ OPC DA 协议（订阅 / DataChange 推送）
      ▼
Windows 代理 (C# .NET 4.0, x86)
      │ HTTP 控制/配置 + SSE (/api/stream) 实时推送
      ▼
Linux 采集器 (Go)
      │ MQTT / HTTP 转发（transform.json 键名映射）
      ▼
MQTT Broker / 下游系统
```

设计要点（第一性原则）：

- OPC DA 原生就是“订阅-推送”模型（DataChange 回调），不应在 C# 侧用定时器轮询、再让 Go 侧定时拉取。双轮询会丢失变化瞬间并增加延迟。
- C# 代理只做“OPC DA → HTTP/SSE”的适配桥，不内置 MQTT；数据面用 SSE 单向推流，Go 采集器负责真正的转发与协议适配。
- 标签选择持久化在独立的 `tags.json`，与 `config.json` 解耦，便于导入/导出与迁移。

## 目录结构

```
OPC_DA_Agent/
├── csharp_agent/              # Windows 代理 (C# .NET 4.0，必须 x86 编译)
│   ├── Program.cs
│   ├── Config.cs
│   ├── Logger.cs
│   ├── DataModel.cs
│   ├── HttpServer.cs          # HTTP 路由 + SSE 端点
│   ├── OPCService.cs          # OPC 连接 / 浏览 / 订阅推送
│   ├── OPC_DA_Agent.csproj
│   ├── App.config / packages.config
│   ├── Interop.OPCAutomation.dll   # 32 位互操作程序集 (CLSID 28E68F9A...)
│   ├── OPCDAAuto.dll               # OPC Automation COM 服务器二进制
│   └── web/index.html              # 浏览 / 选点 / 手动添加 / 导入导出 Web UI
├── go_collector/              # Linux 采集器 (Go)
│   ├── collector_main.go      # 入口；自动识别 SSE 流 vs HTTP 轮询
│   ├── ConfigManager.go
│   ├── collector_web.go
│   ├── KeyTransformer.go
│   ├── Types.go
│   ├── go.mod
│   └── build_collector.sh
├── docs/                      # 详细文档
└── README.md
```

## 快速开始

### Windows 代理（C#）

```batch
cd csharp_agent
msbuild OPC_DA_Agent.csproj /p:Configuration=Release /p:Platform=x86

cd bin\Release
OPC_DA_Agent.exe --config config.json
```

Web UI: `http://<ip>:8080/`（浏览 OPC 节点、选点、手动添加 ItemID、导入/导出标签）

> 必须 x86 编译：OPC Automation 的 CLSID `{28E68F9A-8D75-11D1-8DC3-3C302A000000}` 是 32 位进程内 COM，64 位进程在 64 位系统下看不到 WOW6432Node 中注册的它，会报 `REGDB_E_CLASSNOTREG (0x80040154)`。

### Linux 采集器（Go）

```bash
cd go_collector
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go Types.go

./collector --config collector.ini --web-port 9090
```

Web UI: `http://<ip>:9090/`

## 配置

### config.json（C# 代理）

```json
{
  "opc_server_prog_id": "Freelance2000OPCServer.42.1",
  "opc_server_host": "192.168.111.21",
  "opc_server_url": "opcda://192.168.111.21/Freelance2000OPCServer.42.1",
  "http_port": 8080,
  "tags_file": "tags.json",
  "log_file": "logs\\opc_agent.log",
  "log_level": "Info"
}
```

- 服务器地址用 `opc_server_prog_id` + `opc_server_host`，或等价的 `opc_server_url`（`opcda://host/progid`）。
- `tags_file`：标签持久化文件，默认 `tags.json`（与 config.json 同级，可单独指定路径）。

### collector.ini（Go 采集器）

```ini
[main]
opc_host=172.16.32.98
opc_server=Freelance2000OPCServer.42.1
title=采集系统
debug=False

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data

[task1]
task=True
job_interval_second=1
tag_opc1=channel1.device1.value
tag_dbn1=device1_value
```

- 数据源 URL 默认 `http://172.16.32.98:8080/api/stream`（SSE）。采集器检测到 URL 含 `/api/stream` 时走 SSE 长轮询 + 指数退避断线重连；否则按原 HTTP 轮询。

## API 端点

### C# 代理（端口 8080）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/status` | 连接状态、读计数、错误计数 |
| GET  | `/api/data` | 当前数据快照（键值对） |
| GET  | `/api/browse?offset=&limit=` | 浏览根节点（分页） |
| GET  | `/api/browse/node?nodeId=&offset=&limit=` | 浏览子节点（分页，向下展开） |
| GET  | `/api/tags` | 获取已选标签 |
| POST | `/api/tags` | 保存 / 导入标签（写入 tags.json） |
| GET  | `/api/stream` | SSE 实时推送 |

SSE 帧格式（`text/event-stream`，15s 心跳 `: ping`）：

```
data: {"ts":"2026-...","values":[{"key":"<nodeId>","value":...,"quality":"Good","timestamp":"..."}]}

```

### Go 采集器（端口 9090）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/config` | 获取配置 |
| POST | `/api/config` | 更新配置 |
| GET  | `/api/config/import` | 导入配置 |
| GET  | `/api/config/export` | 导出配置 |
| POST | `/api/mqtt/test` | 测试 MQTT 连接 |
| GET  | `/api/status` | 运行状态 |
| GET  | `/api/data` | 最近一次采集数据 |

## 编译 / CI

- **C#**：GitHub Actions（`windows-2022`）执行 `msbuild /p:Platform=x86`；互操作程序集使用仓库内置的 `Interop.OPCAutomation.dll`（HintPath 引用），不在 CI 上重新生成，以保证 CLSID 与目标机已注册的一致。
- **Go**：`go build`（无需 CGO）。

## 文档索引

- [AGENTS.md](AGENTS.md) — 开发指南
- [docs/BROWSE_API.md](docs/BROWSE_API.md) — 浏览 API（分页 / 向下展开）
- [docs/COLLECTOR_CONFIG.md](docs/COLLECTOR_CONFIG.md) — 采集器配置
- [docs/GITHUB_ACTIONS.md](docs/GITHUB_ACTIONS.md) — CI 构建说明
- [docs/OPC_DA_SETUP_GUIDE.md](docs/OPC_DA_SETUP_GUIDE.md) — OPC DA 部署与排错

## 许可证

MIT
