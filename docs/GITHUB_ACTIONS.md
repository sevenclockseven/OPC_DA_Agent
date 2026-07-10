# GitHub Actions 使用说明

## 概述

本项目使用 GitHub Actions 自动编译 C# 代码并生成可执行文件。

## 工作流程说明

### 触发条件

| 触发条件 | 说明 |
|----------|------|
| `push` 到 `main` 或 `develop` | 自动编译并上传 artifacts |
| `pull_request` 到 `main` 或 `develop` | 自动编译并上传 artifacts |
| `workflow_dispatch` | 手动触发编译 |

### 编译产物

每次运行会生成以下产物（Artifacts）：

| 产物名称 | 说明 | 保留天数 |
|----------|------|---------|
| `OPC_DA_Agent_NET40` | Debug + Release 两个版本的可执行文件及其依赖（整个 `bin/` 目录） | 90 天 |

## 如何使用

### 1. 自动触发编译

推送代码到 `main` 或 `develop` 分支会自动触发编译：

```bash
git add .
git commit -m "feat: add new feature"
git push origin main
```

### 2. 手动触发编译

1. 进入 GitHub 仓库页面
2. 点击 **Actions** 标签
3. 选择 **Build C# OPC DA Agent (.NET 4.0 XP Compatible) - Fixed** 工作流
4. 点击 **Run workflow** 按钮
5. 选择分支（main 或 develop）
6. 点击 **Run workflow** 按钮

### 3. 下载编译产物

#### 方法1: 通过 Actions 页面下载

1. 进入 GitHub 仓库页面
2. 点击 **Actions** 标签
3. 选择最近的运行记录
4. 在页面底部找到 **Artifacts** 部分
5. 点击下载所需的产物：
   - `OPC_DA_Agent_Release` - Release 版本（推荐）
   - `OPC_DA_Agent_Debug` - Debug 版本
   - `OPC_DA_Agent_Windows` - ZIP 压缩包

#### 方法2: 通过 gh 命令行工具下载

```bash
# 列出最近的 artifacts
gh run list

# 下载最新的 artifact
gh run download

# 下载特定的 artifact
gh run download -n OPC_DA_Agent_Release
```

## 工作流程配置

### Build C# OPC DA Agent

**文件**: `.github/workflows/build-csharp-net40-fixed.yml`

**主要步骤**:

1. **Checkout code** - 检出代码
2. **Setup MSBuild** - 设置 MSBuild 16.0 路径
3. **Create working copy** - 拷贝 `csharp_agent/` 到 `work/` 作为构建工作目录
4. **Download NuGet CLI** - 下载 `nuget.exe`
5. **Restore NuGet packages** - 恢复 `Newtonsoft.Json` 等到 `work/csharp_agent/packages/`
6. **Build Debug** - 以 .NET 4.0（XP 兼容）编译 Debug 版本（用 4.8 引用集兜底 `FrameworkPathOverride`）
7. **Build Release** - 编译 Release 版本
8. **Verify builds** - 校验 `bin/Debug` 与 `bin/Release` 的 exe 是否生成
9. **Upload artifacts** - 上传 `OPC_DA_Agent_NET40`（含 Debug + Release）

## 输出文件结构

### OPC_DA_Agent_Release

```
OPC_DA_Agent_Release/
├── OPC_DA_Agent.exe          # 主可执行文件
├── OPC_DA_Agent.exe.config    # 配置文件
├── Newtonsoft.Json.dll         # 依赖库
└── Opc.Ua.*.dll            # OPC UA 库文件
```

### OPC_DA_Agent_Windows.zip

```
OPC_DA_Agent_Windows.zip
└── OPC_DA_Agent_Release/      # 包含 Release 版本的所有文件
```

## 本地运行编译产物

### 1. 下载并解压

```bash
# 下载 ZIP
gh run download -n OPC_DA_Agent_Windows

# 解压
unzip OPC_DA_Agent_Windows.zip -d opc-agent-windows
cd opc-agent-windows
```

### 2. 创建配置文件

创建 `config.json`：

```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "batch_size": 500,
  "enable_compression": true,
  "tags_file": "tags.json",
  "log_file": "logs\\opc_agent.log",
  "log_level": "Info"
}
```

### 3. 运行程序

```batch
# 创建日志目录
mkdir logs

# 运行程序
OPC_DA_Agent.exe --config config.json
```

访问 Web API：http://localhost:8080/api/

## 环境要求

- **Runner**: windows-2022
- **.NET Framework**: 4.8（Windows 自带）
- **MSBuild**: Visual Studio 自带
- **NuGet**: Actions 环境自带

## 故障排除

### 1. 编译失败

**可能原因**:
- NuGet 包依赖冲突
- .NET Framework 版本不兼容
- 代码语法错误

**解决方法**:
- 查看 Actions 日志中的错误信息
- 检查 `packages.config` 中的依赖版本
- 在本地使用 MSBuild 手动编译测试

### 2. 产物下载失败

**可能原因**:
- Artifact 保留期已过期
- 网络连接问题
- 权限不足

**解决方法**:
- 确认 Artifact 还在保留期内（Debug 30 天，Release 90 天）
- 检查 GitHub 账号权限
- 尝试重新运行工作流

### 3. 程序无法运行

**可能原因**:
- 缺少依赖的 DLL 文件
- 配置文件格式错误
- 端口被占用

**解决方法**:
- 确认所有 DLL 文件都在同一目录
- 检查 `config.json` 格式
- 使用其他端口或停止占用端口的程序

## 自定义工作流程

### 修改保留天数

编辑 `.github/workflows/build-csharp.yml`：

```yaml
- name: Upload artifacts
  uses: actions/upload-artifact@v4
  with:
    name: OPC_DA_Agent_NET40
    path: work\csharp_agent\bin/
    retention-days: 180  # 修改为 180 天
```

### 添加自动化发布

可以集成到 GitHub Releases：

```yaml
- name: Create GitHub Release
  uses: softprops/action-gh-release@v1
  if: startsWith(github.ref, 'refs/tags/')
  with:
    files: OPC_DA_Agent_Windows.zip
```

### 添加编译通知

可以添加邮件或 Slack 通知：

```yaml
- name: Send notification
  uses: 8398a7/action-slack@v3
  with:
    status: ${{ job.status }}
    text: 'C# 编译完成'
    webhook_url: ${{ secrets.SLACK_WEBHOOK }}
```

## 相关链接

- [GitHub Actions 文档](https://docs.github.com/en/actions)
- [Microsoft Setup MSBuild Action](https://github.com/microsoft/setup-msbuild)
- [GitHub Actions Upload Artifact](https://github.com/actions/upload-artifact)
- [.NET Framework 4.8 下载](https://dotnet.microsoft.com/download/dotnet-framework/net48)

---

**最后更新**: 2026-01-18
