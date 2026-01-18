# OPC DA数据采集系统 - 项目总结

## 📋 项目概述

这是一个完整的OPC DA数据采集系统，支持：
- Windows端：OPC DA服务器浏览和点号选择
- Linux端：通过HTTP API采集数据，支持MQTT/HTTP输出
- Web界面：可视化配置管理

## 📁 项目结构

```
OPC_DA_Agent/
├── Windows代理程序 (C# .NET)
│   ├── OPC_DA_Agent.csproj          # 项目文件
│   ├── App.config                   # 应用配置
│   ├── packages.config              # NuGet包
│   ├── Program.cs                   # 主程序入口
│   ├── Config.cs                    # 配置类
│   ├── ConfigManager.cs             # 配置管理器
│   ├── DataModel.cs                 # 数据模型
│   ├── Logger.cs                    # 日志类
│   ├── OPCBrowser.cs                # OPC浏览器（浏览/搜索/导出）
│   ├── OPCService.cs                # OPC服务（数据采集）
│   └── HttpServer.cs                # HTTP REST API服务器
│
├── Linux采集程序 (Go)
│   ├── collector_main.go            # 主程序入口
│   ├── collector_web.go             # Web配置服务器
│   ├── ConfigManager.go             # 配置管理器（INI/JSON）
│   ├── KeyTransformer.go            # 键名转换器
│   └── go.mod                       # Go模块依赖
│
├── 配置文件
│   ├── collector.ini                # Linux采集器配置（INI格式）
│   ├── tags.example.json            # 标签示例
│   └── transform.json               # 键名转换规则
│
├── 构建脚本
│   ├── build.bat                    # Windows构建脚本
│   └── build_linux.sh               # Linux构建脚本
│
├── MQTT示例
│   └── mqtt_example.go              # MQTT发布/订阅示例
│
└── 文档
    ├── README.md                    # 主文档
    ├── QUICK_START.md               # 快速开始
    ├── COLLECTOR_CONFIG.md          # 采集器配置详解
    ├── BROWSE_API.md                # 浏览API文档
    ├── KEY_VALUE_FORMAT.md          # 键值对格式说明
    ├── OPC_DA_FORMAT.md             # OPC DA点号格式
    ├── OPC_DA_REAL_FORMAT.md        # 真实数据格式分析
    ├── OPC_DA_SETUP_GUIDE.md        # OPC服务器配置指南
    └── PROJECT_SUMMARY.md           # 项目总结（本文件）
```

## 🎯 核心功能

### Windows代理程序

#### 1. OPC服务器浏览
```bash
# 浏览根节点
curl http://localhost:8080/api/browse

# 浏览指定节点
curl "http://localhost:8080/api/browse/node?nodeId=Plant1&depth=2"

# 搜索标签
curl "http://localhost:8080/api/search?q=Temperature"

# 导出所有变量
curl -X POST http://localhost:8080/api/export?maxDepth=5
```

#### 2. 点号选择和配置
```bash
# 保存选择的标签
curl -X POST http://localhost:8080/api/save-tags \
  -H "Content-Type: application/json" \
  -d @selected_tags.json
```

#### 3. 数据采集
- 支持2000-6000个点
- 1秒采集频率（可配置）
- 键值对数据格式
- 数据压缩传输

### Linux采集程序

#### 1. 配置管理
- **INI格式**：兼容现有配置
- **JSON格式**：现代化配置
- **Web界面**：可视化配置

#### 2. MQTT配置
```ini
[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data
username=
password=
client_id=opc_collector_01
qos=1
retain=False
```

#### 3. HTTP配置
```ini
[http]
enabled=True
url=http://39.99.163.239:8080/api/data
method=POST
timeout=30000
headers=Content-Type:application/json;Authorization:Bearer token123
```

#### 4. 键名转换规则
```json
{
  "enabled": true,
  "rules": [
    {
      "rule_type": "RemovePrefix",
      "pattern": "lt.sc.",
      "description": "移除lt.sc.前缀"
    }
  ]
}
```

## 🔧 技术栈

### Windows端
- **语言**: C# (.NET Framework 4.8)
- **OPC库**: OPC Foundation .NET API
- **HTTP**: HttpListener (内置)
- **JSON**: Newtonsoft.Json

### Linux端
- **语言**: Go 1.20
- **HTTP**: net/http + gorilla/mux
- **MQTT**: Eclipse Paho MQTT Go客户端
- **INI解析**: gopkg.in/ini.v1

## 📊 数据流

```
OPC DA服务器 (Windows)
    ↓ (OPC DA协议)
Windows代理程序
    ↓ (HTTP API)
Linux采集程序
    ↓ (键名转换)
    ↓ (MQTT/HTTP)
目标系统
```

## 🚀 快速开始

### 1. Windows代理部署

```powershell
# 编译
msbuild OPC_DA_Agent.csproj /p:Configuration=Release

# 运行（管理员权限）
cd bin\Release
OPC_DA_Agent.exe
```

### 2. 浏览和选择点号

```bash
# 1. 浏览服务器
curl http://localhost:8080/api/browse

# 2. 导出所有变量
curl -X POST http://localhost:8080/api/export?maxDepth=5 > all_tags.json

# 3. 选择标签（编辑all_tags.json）

# 4. 保存配置
curl -X POST http://localhost:8080/api/save-tags -d @selected_tags.json
```

### 3. Linux采集器部署

```bash
# 1. 编译
go mod tidy
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go

# 2. 配置
nano collector.ini

# 3. 启动（带Web界面）
./collector --config collector.ini --web-port 9090
```

### 4. 访问Web界面

```
http://localhost:9090/
```

## 📖 API参考

### Windows代理API

| 端点 | 方法 | 功能 |
|------|------|------|
| `/api/browse` | GET | 浏览根节点 |
| `/api/browse/node` | GET | 浏览指定节点 |
| `/api/browse/tree` | GET | 浏览节点树 |
| `/api/search` | GET | 搜索节点 |
| `/api/node` | GET | 获取节点详情 |
| `/api/export` | POST | 导出所有变量 |
| `/api/save-tags` | POST | 保存标签配置 |
| `/api/data` | GET | 获取当前数据（键值对） |
| `/api/data/list` | GET | 获取当前数据（列表） |
| `/api/status` | GET | 获取系统状态 |

### Linux采集器API

| 端点 | 方法 | 功能 |
|------|------|------|
| `/api/config` | GET | 获取配置 |
| `/api/config` | POST | 更新配置 |
| `/api/config/import` | POST | 导入配置 |
| `/api/config/export` | GET | 导出配置 |
| `/api/mqtt/test` | POST | 测试MQTT连接 |
| `/api/http/test` | POST | 测试HTTP请求 |
| `/api/transform/preview` | POST | 预览键名转换 |
| `/api/status` | GET | 获取状态 |
| `/api/data` | GET | 获取当前数据 |
| `/api/stats` | GET | 获取统计信息 |

## 🔧 配置示例

### collector.ini

```ini
[main]
title=辽塔172.16.32.245烧成
debug=False
task_count=1
rtdb_host=172.16.32.98
rtdb_port=8100
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4
opc_mode=open
opc_sync=True

[remote]
remote=True
rtdb_host=39.99.163.239,39.99.164.49
rtdb_port=8100,8100

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data
username=
password=
client_id=opc_collector_01
qos=1
retain=False

[http]
enabled=False
url=http://172.16.32.98:8080/api/data
method=POST
timeout=30000
headers=Content-Type:application/json

[task1]
task=True
job_start_date=2015-07-05 00:00:00
job_interval_mode=second
job_interval_second=1
tag_device=2025
tag_component=1
tag_count=1489
tag_group=sc
tag_precision=3
tag_state=2025_sc_state
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
tag_opc2=lt.sc.20251_M4102_CYBJ
tag_dbn2=20251_M4102_CYBJ
tag_opc3=lt.sc.20251_M4102_JYBJ
tag_dbn3=20251_M4102_JYBJ
```

### tags.json (Windows代理)

```json
[
  {
    "node_id": "lt.sc.20251_M4102_ZZT",
    "name": "ZZT",
    "description": "主轴状态",
    "data_type": "Boolean",
    "enabled": true
  },
  {
    "node_id": "lt.sc.20251_M4102_CYBJ",
    "name": "CYBJ",
    "description": "超压报警",
    "data_type": "Boolean",
    "enabled": true
  }
]
```

### transform.json (键名转换)

```json
{
  "enabled": true,
  "rules": [
    {
      "rule_type": "RemovePrefix",
      "pattern": "lt.sc.",
      "description": "移除lt.sc.前缀"
    }
  ]
}
```

## 📈 数据格式

### OPC DA真实数据格式

```json
[
  {
    "errorCode": 0,
    "value": 0.8214290142059326,
    "quality": 192,
    "timestamp": "2026-01-16T07:38:34.921Z",
    "topic": "流量14"
  }
]
```

### 转换为键值对

```json
{
  "timestamp": "2026-01-16T07:38:34.921Z",
  "values": {
    "流量14": 0.8214290142059326,
    "流量15": 3.0320301055908203
  },
  "metadata": {
    "流量14": {
      "quality": 192,
      "timestamp": "2026-01-16T07:38:34.921Z"
    }
  }
}
```

## 🎨 Web界面功能

### 页面列表

| 页面 | URL | 功能 |
|------|-----|------|
| 首页 | `/` | 功能菜单 |
| 配置管理 | `/web/config` | 编辑主配置 |
| MQTT配置 | `/web/mqtt` | 配置MQTT |
| HTTP配置 | `/web/http` | 配置HTTP |
| 键名转换 | `/web/transform` | 配置转换规则 |
| 导入导出 | `/web/import-export` | 配置文件管理 |

### Web界面特点

- ✅ 响应式设计
- ✅ 实时验证
- ✅ 配置模板
- ✅ 连接测试
- ✅ 转换预览
- ✅ 文件上传/下载

## 🔐 安全配置

### 1. 网络安全
- 限制访问IP
- 使用防火墙
- 启用HTTPS（生产环境）

### 2. 认证授权
- MQTT用户名/密码
- HTTP Basic Auth
- API密钥

### 3. 权限管理
- OPC服务器只读权限
- 文件系统权限限制
- Web界面访问控制

## 📊 性能优化

### 批次大小建议

| 点数 | 批次大小 | 说明 |
|------|---------|------|
| < 1000 | 200-300 | 小规模 |
| 1000-3000 | 500 | 中等规模 |
| 3000-6000 | 800-1000 | 大规模 |

### 更新间隔建议

| 场景 | 间隔 | 说明 |
|------|------|------|
| 高速过程 | 500ms | 快速响应 |
| 标准过程 | 1000ms | 通用场景 |
| 慢速过程 | 2000-5000ms | 缓慢变化 |

### 数据压缩

启用压缩可减少70-80%网络流量：
```json
{
  "enable_compression": true
}
```

## 🐛 故障排除

### Windows代理问题

| 问题 | 解决方案 |
|------|---------|
| 无法连接OPC服务器 | 检查OPC服务、DCOM权限 |
| 点号找不到 | 使用浏览API查看可用标签 |
| HTTP服务器启动失败 | 以管理员权限运行 |

### Linux采集器问题

| 问题 | 解决方案 |
|------|---------|
| 配置加载失败 | 检查INI/JSON格式 |
| MQTT连接失败 | 检查服务器地址、端口 |
| Web界面无法访问 | 检查端口占用、防火墙 |

## 📚 文档索引

### 快速开始
- **QUICK_START.md** - 完整工作流程和示例

### 配置说明
- **COLLECTOR_CONFIG.md** - 采集器详细配置
- **OPC_DA_FORMAT.md** - OPC DA点号格式
- **KEY_VALUE_FORMAT.md** - 键值对格式说明

### API文档
- **BROWSE_API.md** - 浏览API详细说明

### 部署指南
- **OPC_DA_SETUP_GUIDE.md** - OPC服务器配置

### 参考资料
- **README.md** - 项目主文档
- **OPC_DA_REAL_FORMAT.md** - 真实数据格式分析

## 🛠️ 开发指南

### 添加新功能

1. **Windows代理**
   - 修改 `OPCService.cs` 添加新API
   - 修改 `DataModel.cs` 添加新数据结构

2. **Linux采集器**
   - 修改 `collector_main.go` 添加新功能
   - 修改 `collector_web.go` 添加新API端点

3. **配置管理**
   - 修改 `ConfigManager.cs` / `ConfigManager.go`
   - 更新配置结构

### 测试建议

1. **单元测试**
   - 测试键名转换规则
   - 测试配置解析
   - 测试数据格式转换

2. **集成测试**
   - 测试Windows代理连接
   - 测试MQTT/HTTP输出
   - 测试Web界面

3. **性能测试**
   - 测试大量点数（2000-6000）
   - 测试高频率采集（1秒）
   - 测试网络带宽使用

## 📝 配置检查清单

### Windows代理配置
- [ ] OPC服务器URL正确
- [ ] 标签文件已配置
- [ ] HTTP端口可用
- [ ] 以管理员权限运行

### Linux采集器配置
- [ ] 配置文件路径正确
- [ ] MQTT/HTTP配置完整
- [ ] 键名转换规则已配置
- [ ] Web端口可用

### 网络配置
- [ ] Windows和Linux网络连通
- [ ] 防火墙规则已配置
- [ ] OPC服务器可访问
- [ ] MQTT/HTTP服务可访问

## 🎓 学习资源

### OPC DA相关
- OPC Foundation官方文档
- OPC DA规范
- Kepware/WinCC配置手册

### Go语言
- Go官方文档
- gorilla/mux文档
- Paho MQTT文档

### C#/.NET
- .NET Framework文档
- OPC Foundation .NET API
- HttpListener文档

## 🔄 版本历史

### v1.0.0 (2026-01-16)
- ✅ OPC DA服务器浏览功能
- ✅ 点号选择和配置
- ✅ INI/JSON配置支持
- ✅ MQTT/HTTP输出配置
- ✅ 键名转换规则
- ✅ Web配置界面
- ✅ 配置导入导出
- ✅ 键值对数据格式
- ✅ 完整文档

## 📞 技术支持

如需帮助，请提供：
1. 错误日志
2. 配置文件
3. 网络拓扑
4. OPC服务器类型和版本

## 📄 许可证

MIT License

---

**项目完成日期**: 2026-01-16
**最后更新**: 2026-01-16
