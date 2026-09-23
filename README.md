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
│   ├── MainForm.cs            # 状态窗口 + 系统托盘
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

启动后显示状态窗口（连接状态、标签数、读取/错误计数等），窗口内可一键打开 Web UI 与日志；**点关闭按钮 = 最小化到托盘继续运行**，右键托盘图标可打开主界面或退出。命令行参数见 `--help`（含 `--exit-on-close`：关闭窗口直接退出、不驻留托盘）。

Web UI: `http://<ip>:8080/`（浏览 OPC 节点、选点、手动添加 ItemID、导入/导出标签）

> 必须 x86 编译：OPC Automation 的 CLSID `{28E68F9A-8D75-11D1-8DC3-3C302A000000}` 是 32 位进程内 COM，64 位进程在 64 位系统下看不到 WOW6432Node 中注册的它，会报 `REGDB_E_CLASSNOTREG (0x80040154)`。

#### 以 Windows 服务方式运行（无窗口）

若要在后台无窗口、开机自启、崩溃自重启地运行，推荐用 [NSSM](https://nssm.cc/)（Non-Sucking Service Manager）把程序包装成服务。**无需改动代码**——程序是窗口程序（WinExe），NSSM 停止服务时发送 `WM_CLOSE` 关闭消息；程序检测到自己运行在服务会话（Session 0）时会**直接退出并执行 `Cleanup()` 清理逻辑**（桌面双击运行时点 X 则是最小化到托盘、不退出，两套行为按会话自动区分）。

1. 下载 NSSM，使用 **32 位** 版本 `nssm.exe`（本程序是 x86）。
2. 安装服务（管理员 PowerShell）：

   ```batch
   nssm install OPC_DA_Agent "D:\path\to\csharp_agent\bin\Release\OPC_DA_Agent.exe"
   nssm set OPC_DA_Agent AppParameters "--config config.json"
   nssm set OPC_DA_Agent AppDirectory "D:\path\to\csharp_agent\bin\Release"
   nssm set OPC_DA_Agent DisplayName "OPC DA Agent"
   nssm set OPC_DA_Agent Description "OPC DA 采集代理（SSE 推送）"
   nssm set OPC_DA_Agent AppExit Default Restart
   nssm start OPC_DA_Agent
   ```

   - `AppDirectory` 必须指向 exe 所在目录，否则相对的 `config.json` / `tags.json` / `logs\` 会解析失败。
   - `AppExit Default Restart` 让进程崩溃时由 NSSM 自动重启。

3. 常用命令：

   ```batch
   nssm status OPC_DA_Agent
   nssm restart OPC_DA_Agent
   nssm stop OPC_DA_Agent
   nssm remove OPC_DA_Agent confirm
   ```

4. 运行日志写入 `log_file`（启动、连接失败、托盘化、退出原因均有记录），无需配置 I/O 重定向；如需捕获 `stdout` / `stderr` 可在 NSSM GUI 的 **I/O** 页重定向到文件备用。

> ⚠️ **OPC DA + Session 0 注意**：服务运行在 Session 0 非交互桌面。多数 OPC 服务器（Kepware、ABB Freelance 等）只要 DCOM 身份配好即可；但**依赖“与桌面交互”或“交互式用户”的 OPC 服务器在 Session 0 下连不上**。建议服务用**专用账户**（非 LocalSystem）运行，并在“组件服务(DCOM 配置)”中给该账户授予 OPC 服务器访问权限：
>
> ```batch
> nssm set OPC_DA_Agent ObjectName ".\opcuser" "密码"
> ```

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
  "api_token": "",
  "tags_file": "tags.json",
  "log_file": "logs\\opc_agent.log",
  "log_level": "Info"
}
```

- 服务器地址用 `opc_server_prog_id` + `opc_server_host`，或等价的 `opc_server_url`（`opcda://host/progid`）。
- `api_token`：API 访问令牌，空 = 不启用鉴权（默认）。非空时所有 `/api/*` 请求必须携带，见「安全 / API 认证」。
- `tags_file`：标签持久化文件，默认 `tags.json`（与 config.json 同级，可单独指定路径）。

### collector.ini（Go 采集器）

```ini
[main]
title=采集系统
opc_server=Freelance2000OPCServer.42.1
web_token=

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data
format=full
split=false
js_transform=

[task1]
task=True
job_interval_second=1
tag_opc1=channel1.device1.value
tag_dbn1=device1_value
```

MQTT 输出格式（`[mqtt]` 段）：

- `format`：发布报文格式。
  - `full`（默认）：整包 JSON `{"timestamp":...,"values":{...},"metadata":{...}}`，与旧版一致。
  - `flat`：仅 `values` 映射的 JSON。
  - **自定义模板**：含占位符 `{key}` `{value}` `{quality}` `{timestamp}` 的字符串，按每个数据点渲染一行（如 `format = {key},{value},{quality},{timestamp}`）。占位符含义与 RTDB 的 `format` 完全一致。
- `split`：扇出方式。`false`（默认）= 所有点渲染后用换行拼成一个报文发出；`true` = 每个点单独发一条报文（适合时序库/流处理逐点摄入）。
- `js_transform`（可选）：返回电文的 JS 表达式，可用变量 `point = {key,value,quality,timestamp}`；返回字符串直接作为电文，返回对象则经 JSON 序列化。适用于需要嵌套/条件结构的后端（依赖 `github.com/robertkrimen/otto`，已纳入 go.mod）。

- 数据源 URL 默认 `http://172.16.32.98:8080/api/stream`（SSE）。采集器检测到 URL 含 `/api/stream` 时走 SSE 长轮询 + 指数退避断线重连；否则按原 HTTP 轮询。
- `web_token`（`[main]` 段，可选）：Web UI / API 访问令牌，空 = 不启用（默认）。清空该键并保存即关闭鉴权。可在 Web UI「设置」中通过 `POST /api/config` 热更新（请求需携带旧令牌）。

## 安全 / API 认证

C# 代理（`config.json` 的 `api_token`）与 Go 采集器（`collector.ini` 的 `[main] web_token`）各有一个静态访问令牌，语义一致：

- **空 = 完全关闭鉴权**（默认，现网零破坏）；非空时**仅 `/api/*` 路径**要求令牌，Web 页面本身豁免（否则前端无从弹出引导）。
- 携带方式（三选一）：
  - 请求头 `X-Api-Token: <令牌>`（推荐，浏览器 JS 自动附加）；
  - 查询参数 `?token=<令牌>`（SSE / `EventSource` 无法设请求头时用，如 Go 数据源 URL：`http://192.168.111.21:8080/api/stream?token=<令牌>`）；
  - Go 采集器数据源表单的「访问令牌」字段（Web UI「数据源配置 → 添加/编辑数据源」，或 ini 的 `[httpN] token=`）：采集、SSE 订阅与连接测试会自动以 `X-Api-Token` 请求头发送，无需手拼 URL。
- 校验为固定时间比较（防计时侧信道）；令牌在日志中一律脱敏为 `token=***`。

```bash
# Header 方式
curl -H "X-Api-Token: <令牌>" http://localhost:8080/api/status

# SSE / EventSource 方式
curl "http://localhost:8080/api/stream?token=<令牌>"
```

**Web UI**：启用令牌后首次访问 API 会收到 `401`，页面弹窗提示输入令牌，存入浏览器 `localStorage`（key：C# 为 `opc_agent_token`，Go 为 `opc_collector_token`）后自动附加请求头并刷新；改令牌后重新弹窗即可。

**Host / Origin 限制**（两端一致，防 DNS rebinding 与浏览器跨站）：

- 请求 `Host` 必须是 `localhost` / `127.0.0.1` / `::1` / 本机主机名 / 本机网卡 IP，否则 `403`。**请用 `http://localhost:<port>/`、`http://127.0.0.1:<port>/` 或 `http://<本机IP>:<port>/` 访问**（用域名指向本机会被拒）。
- 浏览器带 `Origin` 头时额外校验：scheme 为 http/https、端口与服务端口一致、host 同上；不满足返回 `403`。curl / 采集器不带 `Origin`，不受影响。
- 非法令牌 `401`、非法 Host/Origin `403`，均为 JSON 响应 `{"success":false,...}`。

令牌生成示例：`openssl rand -hex 32`（或任意 16+ 位随机字符串）。该值等同口令，不要提交到公开仓库。

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

以上 `/api/*` 在配置 `api_token` 后均需携带令牌（见「安全 / API 认证」）；`/` 页面豁免。

SSE 帧格式（`text/event-stream`，15s 心跳 `: ping`）：

```
data: {"ts":"2026-...","values":[{"key":"<nodeId>","value":...,"quality":"Good","timestamp":"..."}]}

```

代理默认按 `sse_snapshot_interval_ms`（默认 1000ms）的固定节拍把当前全部最新值做一次全量快照推送，保证订阅值不变时采集器仍持续收到秒级数据；值发生变化时另由 `OnDataChange` 即时补推。设 `0` 则退回纯变化驱动。

### Go 采集器（端口 9090）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/config` | 获取配置 |
| POST | `/api/config` | 更新配置（热加载，失败会回滚并如实返回错误） |
| POST | `/api/config/validate` | 校验配置 |
| POST | `/api/mqtt/test` | 测试 MQTT 连接 |
| POST | `/api/rtdb/test` | 测试 RTDB 连接 |
| POST | `/api/http/test` | 测试 HTTP 数据源连接 |
| POST | `/api/transform/preview` | 预览键名转换效果 |
| GET  | `/api/transform/rules` | 获取转换规则 |
| POST | `/api/transform/rules` | 保存转换规则（同步调试面板） |
| GET  | `/api/transform/debug` | 转换调试信息（规则/样例测试） |
| POST | `/api/webhook/test` | 测试 Webhook 推送 |

以上 `/api/*` 在配置 `web_token` 后均需携带令牌（见「安全 / API 认证」）；`/` 页面豁免。

## 编译 / CI

- **C#**：GitHub Actions（`windows-2022`）执行 `msbuild /p:Platform=x86`；互操作程序集使用仓库内置的 `Interop.OPCAutomation.dll`（HintPath 引用），不在 CI 上重新生成，以保证 CLSID 与目标机已注册的一致。构建产物（`bin/`，含 Debug 与 Release）上传为 artifact `OPC_DA_Agent_NET40`（保留 90 天），本地无 MSBuild 时可直接下载使用。
- **Go**：`go build`（无需 CGO）。

## 文档索引

- [AGENTS.md](AGENTS.md) — 开发指南
- [docs/BROWSE_API.md](docs/BROWSE_API.md) — 浏览 API（分页 / 向下展开）
- [docs/COLLECTOR_CONFIG.md](docs/COLLECTOR_CONFIG.md) — 采集器配置
- [docs/GITHUB_ACTIONS.md](docs/GITHUB_ACTIONS.md) — CI 构建说明
- [docs/OPC_DA_SETUP_GUIDE.md](docs/OPC_DA_SETUP_GUIDE.md) — OPC DA 部署与排错

## 许可证

MIT
