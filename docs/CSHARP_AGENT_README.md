# Windows 代理程序

## 说明
这是 Windows OPC DA 代理的 C# 源代码。

## 文件说明

### 源代码
- `Program.cs` - 主程序入口
- `Config.cs` - 配置管理
- `Logger.cs` - 日志工具
- `DataModel.cs` - 数据模型
- `HttpServer.cs` - HTTP REST API 服务器
- `OPCService.cs` - OPC 服务客户端
- `OPCBrowser.cs` - OPC 浏览器

### 配置文件
- `OPC_DA_Agent.csproj` - 项目文件
- `App.config` - 应用配置
- `packages.config` - NuGet 包配置

### 构建脚本
- `build.bat` - 编译脚本

## 编译命令

```batch
# 使用 MSBuild 编译
build.bat

# 或手动编译
msbuild OPC_DA_Agent.csproj /p:Configuration=Release

# 运行
cd bin\Release
OPC_DA_Agent.exe --config config.json
```

## API 端点

- `GET /api/status` - 获取系统状态
- `GET /api/data` - 获取当前数据（键值对）
- `GET /api/data/list` - 获取当前数据（列表）
- `GET /api/browse` - 浏览根节点
- `GET /api/browse/node` - 浏览指定节点
- `POST /api/save-tags` - 保存标签配置

访问地址：http://localhost:8080/
