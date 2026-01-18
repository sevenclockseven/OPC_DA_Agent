# 文件索引

## 核心代码文件

### Windows代理程序 (C#)
| 文件 | 大小 | 说明 |
|------|------|------|
| **OPC_DA_Agent.csproj** | 2.6K | 项目文件 |
| **App.config** | 937B | 应用配置 |
| **packages.config** | 311B | NuGet包配置 |
| **Program.cs** | 7.1K | 主程序入口 |
| **Config.cs** | 5.3K | 配置类 |
| **ConfigManager.cs** | 22.3K | 配置管理器（INI/JSON） |
| **DataModel.cs** | 6.2K | 数据模型 |
| **Logger.cs** | 3.3K | 日志类 |
| **OPCBrowser.cs** | 24.2K | OPC浏览器（浏览/搜索/导出） |
| **OPCService.cs** | 18.1K | OPC服务（数据采集） |
| **HttpServer.cs** | 21.4K | HTTP REST API服务器 |

### Linux采集程序 (Go)
| 文件 | 大小 | 说明 |
|------|------|------|
| **collector_main.go** | 6.2K | 主程序入口 |
| **collector_web.go** | 51K | Web配置服务器 |
| **ConfigManager.go** | 16K | 配置管理器 |
| **KeyTransformer.go** | 2.9K | 键名转换器 |
| **go.mod** | 146B | Go模块依赖 |

### 其他代码
| 文件 | 大小 | 说明 |
|------|------|------|
| **mqtt_example.go** | 5.5K | MQTT发布/订阅示例 |
| **linux_collector.go** | 16.6K | Linux采集器（旧版） |

## 配置文件

| 文件 | 大小 | 说明 |
|------|------|------|
| **collector.ini** | 1.3K | Linux采集器配置（INI格式） |
| **tags.example.json** | 651B | 标签示例 |
| **transform.json** | 470B | 键名转换规则 |

## 构建脚本

| 文件 | 大小 | 说明 |
|------|------|------|
| **build.bat** | 1.6K | Windows构建脚本 |
| **build_linux.sh** | 1.3K | Linux构建脚本 |

## 文档

### 主要文档
| 文件 | 大小 | 说明 |
|------|------|------|
| **README.md** | 18K | 项目主文档 |
| **PROJECT_SUMMARY.md** | 13K | 项目总结 |
| **QUICK_START.md** | 9.9K | 快速开始指南 |

### 配置文档
| 文件 | 大小 | 说明 |
|------|------|------|
| **COLLECTOR_CONFIG.md** | 18K | 采集器详细配置 |
| **OPC_DA_FORMAT.md** | 11K | OPC DA点号格式 |
| **OPC_DA_REAL_FORMAT.md** | 8.3K | 真实数据格式分析 |
| **KEY_VALUE_FORMAT.md** | 9.2K | 键值对格式说明 |

### API文档
| 文件 | 大小 | 说明 |
|------|------|------|
| **BROWSE_API.md** | 10K | 浏览API详细说明 |

### 部署文档
| 文件 | 大小 | 说明 |
|------|------|------|
| **OPC_DA_SETUP_GUIDE.md** | 14K | OPC服务器配置指南 |

## 总计

- **代码文件**: 13个 (约150KB)
- **配置文件**: 3个 (约2.4KB)
- **构建脚本**: 2个 (约2.9KB)
- **文档**: 8个 (约93KB)
- **总计**: 26个文件 (约248KB)

## 文件依赖关系

```
collector_main.go
├── ConfigManager.go
├── collector_web.go
└── KeyTransformer.go

collector_web.go
├── ConfigManager.go
└── KeyTransformer.go

ConfigManager.go
└── (无依赖)

KeyTransformer.go
└── (无依赖)
```

## 快速参考

### 启动Windows代理
```powershell
msbuild OPC_DA_Agent.csproj /p:Configuration=Release
cd bin\Release
OPC_DA_Agent.exe
```

### 启动Linux采集器
```bash
go build -o collector collector_main.go ConfigManager.go collector_web.go KeyTransformer.go
./collector --config collector.ini --web-port 9090
```

### 访问Web界面
```
http://localhost:9090/
```

### 查看文档
- 快速开始: [QUICK_START.md](QUICK_START.md)
- 配置说明: [COLLECTOR_CONFIG.md](COLLECTOR_CONFIG.md)
- API文档: [BROWSE_API.md](BROWSE_API.md)
