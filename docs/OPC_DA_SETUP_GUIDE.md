# OPC DA服务器配置指南

## 一、常见OPC DA服务器配置

### 1. WinCC OPC服务器

#### 1.1 启用OPC服务器

**步骤**:
1. 打开 WinCC Explorer
2. 右键点击 `变量管理器`
3. 选择 `添加新的驱动程序`
4. 选择 `OPC驱动程序`
5. 配置OPC服务器连接

**OPC服务器名称**:
```
OPCServer.WinCC
```

**连接字符串**:
```
opcda://localhost/OPCServer.WinCC
```

#### 1.2 配置DCOM权限（远程访问）

**步骤**:
1. 运行 `dcomcnfg`
2. 展开 `组件服务` → `计算机` → `我的电脑`
3. 右键 `我的电脑` → `属性`
4. 选择 `COM安全` 标签
5. 配置访问权限

**权限设置**:
- 启动和激活权限: 添加Everyone
- 访问权限: 添加Everyone

#### 1.3 查看点号

**方法1: WinCC Explorer**
```
变量管理器 → 变量 → 属性 → 变量名称
```

**方法2: 使用脚本**
```vbscript
' WinCC VBS脚本
Dim oServer
Set oServer = CreateObject("OPCServer.WinCC")
oServer.Connect ""

' 遍历所有标签
For Each item In oServer.OPCItems
    WScript.Echo item.ItemID
Next
```

**点号格式示例**:
```
Plant1.Line1.Temperature
Plant1.Line1.Pressure
Plant1.Tank1.Level
```

### 2. KEPServerEX

#### 2.1 配置OPC服务器

**步骤**:
1. 打开 KEPServerEX 配置工具
2. 创建通道（Channel）
3. 添加设备（Device）
4. 添加标签（Tag）

**OPC服务器名称**:
```
Kepware.KEPServerEx.V6
```

**连接字符串**:
```
opcda://localhost/Kepware.KEPServerEx.V6
```

#### 2.2 查看点号

**方法1: 设备浏览器**
```
配置 → 通道 → 设备 → 标签
```

**方法2: OPC客户端**
使用KEPServerEX自带的OPC客户端工具

**点号格式示例**:
```
Channel1.Device1.Temperature
Channel1.Device1.Pressure
Channel1.Device1.FlowRate
```

### 3. Matrikon OPC Server

#### 3.1 配置

**OPC服务器名称**:
```
Matrikon.OPC.Simulation
```

**连接字符串**:
```
opcda://localhost/Matrikon.OPC.Simulation
```

#### 3.2 查看点号

使用 Matrikon OPC Explorer 浏览标签

**点号格式示例**:
```
Matrikon.Simulation.Temperature
Matrikon.Simulation.Pressure
```

### 4. Iconics OPC Server

#### 4.1 配置

**OPC服务器名称**:
```
ICONICS.OPCServer.1
```

**连接字符串**:
```
opcda://localhost/ICONICS.OPCServer.1
```

#### 4.2 查看点号

使用 Genesis64 或 OPC客户端浏览

**点号格式示例**:
```
Project.Temperature
Project.Pressure
```

## 二、Windows代理配置

### 2.1 基本配置

**config.json**:
```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "batch_size": 500,
  "tags_file": "tags.json"
}
```

### 2.2 标签配置

**tags.json** (WinCC示例):
```json
[
  {
    "node_id": "Plant1.Line1.Temperature",
    "name": "Temperature",
    "description": "生产线温度",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Plant1.Line1.Pressure",
    "name": "Pressure",
    "description": "生产线压力",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Plant1.Tank1.Level",
    "name": "Level",
    "description": "储罐液位",
    "data_type": "Double",
    "enabled": true
  }
]
```

**tags.json** (KEPServerEX示例):
```json
[
  {
    "node_id": "Channel1.Device1.Temperature",
    "name": "Temperature",
    "description": "设备温度",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Channel1.Device1.Pressure",
    "name": "Pressure",
    "description": "设备压力",
    "data_type": "Double",
    "enabled": true
  }
]
```

## 三、使用浏览器查找点号

### 3.1 启动Windows代理

```powershell
cd bin\Release
OPC_DA_Agent.exe
```

### 3.2 使用浏览器API

#### 步骤1: 浏览根节点
```bash
curl http://localhost:8080/api/browse | jq
```

**响应示例**:
```json
{
  "success": true,
  "data": [
    {
      "nodeId": "Plant1",
      "displayName": "Plant1",
      "nodeClass": "Object"
    },
    {
      "nodeId": "Plant2",
      "displayName": "Plant2",
      "nodeClass": "Object"
    }
  ]
}
```

#### 步骤2: 深入浏览
```bash
curl "http://localhost:8080/api/browse/node?nodeId=Plant1&depth=2" | jq
```

**响应示例**:
```json
{
  "success": true,
  "data": [
    {
      "nodeId": "Plant1.Line1",
      "displayName": "Line1",
      "nodeClass": "Object"
    },
    {
      "nodeId": "Plant1.Line1.Temperature",
      "displayName": "Temperature",
      "nodeClass": "Variable"
    },
    {
      "nodeId": "Plant1.Line1.Pressure",
      "displayName": "Pressure",
      "nodeClass": "Variable"
    }
  ]
}
```

#### 步骤3: 搜索标签
```bash
curl "http://localhost:8080/api/search?q=Temperature" | jq
```

#### 步骤4: 查看节点详情
```bash
curl "http://localhost:8080/api/node?nodeId=Plant1.Line1.Temperature" | jq
```

### 3.3 导出所有变量

```bash
# 导出所有变量（深度5）
curl -X POST http://localhost:8080/api/export?maxDepth=5 > all_tags.json

# 查看总数
cat all_tags.json | jq '.data.count'

# 查看前10个
cat all_tags.json | jq '.data.tags[0:10]'
```

### 3.4 选择并保存标签

```bash
# 筛选特定模式的标签
cat all_tags.json | jq '.data.tags[] | select(.node_id | test("Plant1|Tank1"))' > selected_tags.json

# 保存配置
curl -X POST http://localhost:8080/api/save-tags \
  -H "Content-Type: application/json" \
  -d @selected_tags.json
```

## 四、Linux采集器配置

### 4.1 基本配置

**collector.json**:
```json
{
  "windows_agent_url": "http://192.168.1.100:8080",
  "update_interval": 1000000000,
  "batch_size": 500,
  "enable_compression": true,
  "enable_mqtt": false,
  "http_listen_port": 9090,
  "tags": [
    {
      "node_id": "Plant1.Line1.Temperature",
      "name": "Temperature",
      "description": "生产线温度",
      "enabled": true
    }
  ]
}
```

### 4.2 启动采集器

```bash
./collector --config collector.json
```

### 4.3 验证数据流

```bash
# 查看状态
curl http://localhost:9090/api/status | jq

# 查看数据
curl http://localhost:9090/api/data | jq
```

## 五、不同场景配置示例

### 场景1: WinCC系统（2000个点）

**config.json**:
```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "batch_size": 500,
  "enable_compression": true
}
```

**tags.json** (部分示例):
```json
[
  {
    "node_id": "Plant1.Line1.Section1.Temperature",
    "name": "Temp_Line1_S1",
    "description": "生产线1-工段1温度",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Plant1.Line1.Section1.Pressure",
    "name": "Press_Line1_S1",
    "description": "生产线1-工段1压力",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Plant1.Tank1.Level",
    "name": "Level_Tank1",
    "description": "储罐1液位",
    "data_type": "Double",
    "enabled": true
  }
]
```

### 场景2: KEPServerEX系统（4000个点）

**config.json**:
```json
{
  "opc_server_url": "opcda://localhost/Kepware.KEPServerEx.V6",
  "http_port": 8080,
  "update_interval_ms": 1000,
  "batch_size": 800,
  "enable_compression": true
}
```

**tags.json** (部分示例):
```json
[
  {
    "node_id": "Channel1.Device1.Temperature",
    "name": "Temp_Dev1",
    "description": "设备1温度",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Channel1.Device1.Pressure",
    "name": "Press_Dev1",
    "description": "设备1压力",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "Channel2.Device1.Temperature",
    "name": "Temp_Dev2",
    "description": "设备2温度",
    "data_type": "Double",
    "enabled": true
  }
]
```

### 场景3: 混合系统（多个OPC服务器）

**方案**: 部署多个Windows代理实例

**实例1 - WinCC**:
```json
{
  "opc_server_url": "opcda://localhost/OPCServer.WinCC",
  "http_port": 8081,
  "tags_file": "tags_wincc.json"
}
```

**实例2 - KEPServerEX**:
```json
{
  "opc_server_url": "opcda://localhost/Kepware.KEPServerEx.V6",
  "http_port": 8082,
  "tags_file": "tags_kep.json"
}
```

**Linux采集器配置**:
```json
{
  "windows_agent_url": "http://192.168.1.100:8081",
  "update_interval": 1000000000,
  "batch_size": 500
}
```

## 六、自动化脚本

### 6.1 PowerShell自动发现脚本

```powershell
# discover_tags.ps1

$baseUrl = "http://localhost:8080"

Write-Host "正在导出所有变量节点..." -ForegroundColor Green

# 导出所有变量
$export = Invoke-RestMethod -Uri "$baseUrl/api/export?maxDepth=5" -Method Post
$allTags = $export.data.tags

Write-Host "共找到 $($allTags.Count) 个变量" -ForegroundColor Yellow

# 保存到文件
$allTags | ConvertTo-Json -Depth 10 | Out-File -FilePath "all_tags.json" -Encoding UTF8

Write-Host "已保存到 all_tags.json" -ForegroundColor Green

# 显示前20个
Write-Host "`n前20个标签:" -ForegroundColor Cyan
$allTags[0..19] | ForEach-Object {
    Write-Host "  $($_.node_id)" -ForegroundColor White
}
```

### 6.2 Bash自动发现脚本

```bash
#!/bin/bash
# discover_tags.sh

BASE_URL="http://localhost:8080"

echo "正在导出所有变量节点..."

# 导出所有变量
curl -X POST "$BASE_URL/api/export?maxDepth=5" > all_tags.json

COUNT=$(cat all_tags.json | jq '.data.count')
echo "共找到 $COUNT 个变量"

# 显示前20个
echo ""
echo "前20个标签:"
cat all_tags.json | jq -r '.data.tags[0:20][] | .node_id'
```

### 6.3 Python智能筛选脚本

```python
#!/usr/bin/env python3
# smart_filter.py

import json
import requests

def discover_tags(base_url):
    """发现所有标签"""
    response = requests.post(f"{base_url}/api/export?maxDepth=5")
    data = response.json()
    return data['data']['tags']

def filter_by_pattern(tags, patterns):
    """按模式筛选标签"""
    filtered = []
    for tag in tags:
        node_id = tag['node_id']
        for pattern in patterns:
            if pattern in node_id:
                filtered.append(tag)
                break
    return filtered

def filter_by_device(tags, device_name):
    """筛选特定设备的标签"""
    return [t for t in tags if device_name in t['node_id']]

def save_tags(tags, filename):
    """保存标签配置"""
    with open(filename, 'w', encoding='utf-8') as f:
        json.dump(tags, f, indent=2, ensure_ascii=False)

def main():
    base_url = "http://localhost:8080"

    # 发现所有标签
    print("正在发现标签...")
    all_tags = discover_tags(base_url)
    print(f"共找到 {len(all_tags)} 个标签")

    # 筛选Plant1的标签
    plant1_tags = filter_by_device(all_tags, "Plant1")
    print(f"Plant1标签: {len(plant1_tags)} 个")
    save_tags(plant1_tags, "plant1_tags.json")

    # 筛选包含Temperature或Pressure的标签
    pattern_tags = filter_by_pattern(all_tags, ["Temperature", "Pressure"])
    print(f"温度/压力标签: {len(pattern_tags)} 个")
    save_tags(pattern_tags, "pattern_tags.json")

    print("\n配置文件已生成:")
    print("  - plant1_tags.json")
    print("  - pattern_tags.json")

if __name__ == '__main__':
    main()
```

## 七、性能优化

### 7.1 批次大小调整

根据点数调整批次大小：

| 点数 | 推荐批次大小 | 说明 |
|------|-------------|------|
| < 1000 | 200-300 | 小规模系统 |
| 1000-3000 | 500 | 中等规模 |
| 3000-6000 | 800-1000 | 大规模系统 |

### 7.2 更新间隔

根据实时性要求调整：

| 场景 | 更新间隔 | 说明 |
|------|---------|------|
| 高速过程 | 500ms | 需要快速响应 |
| 标准过程 | 1000ms | 通用场景 |
| 慢速过程 | 2000-5000ms | 缓慢变化的过程变量 |

### 7.3 数据压缩

启用压缩减少网络流量：

```json
{
  "enable_compression": true
}
```

## 八、故障排除

### 8.1 OPC服务器连接失败

**症状**:
```
错误: 连接OPC服务器失败
```

**解决**:
1. 检查OPC服务器是否运行
2. 确认OPC服务器名称正确
3. 检查DCOM权限（远程访问）
4. 尝试本地连接测试

### 8.2 点号找不到

**症状**:
```
错误: 无法读取标签 Plant1.Temperature
```

**解决**:
1. 使用浏览API查看可用标签
2. 检查点号格式是否正确
3. 确认标签在OPC服务器中存在
4. 检查标签权限

### 8.3 数据质量异常

**症状**:
```
质量: Bad
```

**解决**:
1. 检查传感器连接
2. 确认OPC服务器状态
3. 检查标签配置
4. 查看OPC服务器日志

## 九、监控和维护

### 9.1 日志监控

**Windows**:
```powershell
# 查看日志
Get-Content C:\OPC_Agent\logs\opc_agent.log -Tail 50

# 实时监控
Get-Content C:\OPC_Agent\logs\opc_agent.log -Wait
```

**Linux**:
```bash
# 查看日志
tail -f /opt/opc-collector/logs/collector.log

# 查看错误
grep ERROR /opt/opc-collector/logs/collector.log
```

### 9.2 性能监控

```bash
# 查看系统状态
curl http://localhost:8080/api/status | jq

# 查看统计信息
curl http://localhost:8080/api/stats | jq
```

### 9.3 定期维护

1. **日志轮转**: 每周清理旧日志
2. **配置备份**: 每月备份配置文件
3. **性能检查**: 每月检查采集性能
4. **标签审核**: 每季度审核标签配置

## 十、安全建议

### 10.1 网络安全

- 使用防火墙限制访问
- 启用HTTPS（生产环境）
- 使用VPN或专线连接

### 10.2 权限管理

- 为OPC服务器创建专用账户
- 限制只读权限
- 定期更换密码

### 10.3 数据安全

- 定期备份配置
- 使用加密传输
- 启用访问日志

## 十一、技术支持

如需进一步帮助，请提供：

1. **OPC服务器类型和版本**
2. **点号示例**（5-10个）
3. **错误日志**
4. **网络拓扑图**
5. **系统配置文件**

## 总结

- OPC DA使用点号路径格式（如 `Plant1.Line1.Temperature`）
- 使用浏览器API可以自动发现所有标签
- 不同OPC服务器有不同的命名约定
- 合理配置批次大小和更新间隔
- 定期监控和维护系统
