# OPC服务器浏览API文档

## 概述

Windows代理程序提供了完整的OPC服务器浏览功能，可以：
1. 浏览服务器上的所有节点
2. 搜索特定节点
3. 查看节点详细信息
4. 导出所有变量节点
5. 选择需要代理的标签

## API端点

### 1. 浏览根节点

获取OPC服务器的顶级节点（ObjectsFolder）

```
GET /api/browse
```

**响应示例：**
```json
{
  "success": true,
  "message": "OK",
  "data": [
    {
      "nodeId": "ns=0;i=85",
      "displayName": "Objects",
      "nodeClass": "Object",
      "browseName": "Objects",
      "description": "",
      "isForward": true,
      "referenceTypeId": "ns=0;i=35",
      "depth": 0
    },
    {
      "nodeId": "ns=0;i=2253",
      "displayName": "Server",
      "nodeClass": "Object",
      "browseName": "Server",
      "description": "",
      "isForward": true,
      "referenceTypeId": "ns=0;i=35",
      "depth": 0
    }
  ]
}
```

### 2. 浏览指定节点

获取指定节点的子节点

```
GET /api/browse/node?nodeId=<nodeId>&depth=<depth>
```

**参数：**
- `nodeId` (必填): 节点ID，如 `ns=2;s=Channel1.Device1`
- `depth` (可选): 浏览深度，默认为1

**响应示例：**
```json
{
  "success": true,
  "message": "OK",
  "data": [
    {
      "nodeId": "ns=2;s=Channel1.Device1.Temperature",
      "displayName": "Temperature",
      "nodeClass": "Variable",
      "browseName": "Temperature",
      "description": "温度传感器",
      "isForward": true,
      "referenceTypeId": "ns=0;i=47",
      "depth": 1
    },
    {
      "nodeId": "ns=2;s=Channel1.Device1.Pressure",
      "displayName": "Pressure",
      "nodeClass": "Variable",
      "browseName": "Pressure",
      "description": "压力传感器",
      "isForward": true,
      "referenceTypeId": "ns=0;i=47",
      "depth": 1
    }
  ]
}
```

### 3. 浏览节点树

递归浏览节点及其子节点，形成树形结构

```
GET /api/browse/tree?nodeId=<nodeId>&maxDepth=<maxDepth>
```

**参数：**
- `nodeId` (可选): 起始节点ID，默认为 `ObjectsFolder`
- `maxDepth` (可选): 最大深度，默认为3

**响应示例：**
```json
{
  "success": true,
  "message": "OK",
  "data": {
    "nodeId": "ObjectsFolder",
    "displayName": "ObjectsFolder",
    "nodeClass": "Object",
    "children": [
      {
        "nodeId": "ns=2;s=Channel1",
        "displayName": "Channel1",
        "nodeClass": "Object",
        "children": [
          {
            "nodeId": "ns=2;s=Channel1.Device1",
            "displayName": "Device1",
            "nodeClass": "Object",
            "children": [
              {
                "nodeId": "ns=2;s=Channel1.Device1.Temperature",
                "displayName": "Temperature",
                "nodeClass": "Variable",
                "children": []
              }
            ]
          }
        ]
      }
    ]
  }
}
```

### 4. 搜索节点

搜索包含指定关键词的节点

```
GET /api/search?q=<searchTerm>&max=<maxResults>
```

**参数：**
- `searchTerm` (必填): 搜索关键词
- `max` (可选): 最大结果数，默认为1000

**响应示例：**
```json
{
  "success": true,
  "message": "OK",
  "data": [
    {
      "nodeId": "ns=2;s=Channel1.Device1.Temperature",
      "displayName": "Temperature",
      "nodeClass": "Variable",
      "browseName": "Temperature",
      "description": "温度传感器",
      "isForward": true,
      "referenceTypeId": "ns=0;i=47",
      "depth": 0
    }
  ]
}
```

### 5. 获取节点详细信息

获取节点的详细属性和当前值

```
GET /api/node?nodeId=<nodeId>
```

**参数：**
- `nodeId` (必填): 节点ID

**响应示例：**
```json
{
  "success": true,
  "message": "OK",
  "data": {
    "nodeId": "ns=2;s=Channel1.Device1.Temperature",
    "displayName": "Temperature",
    "description": "温度传感器",
    "dataType": "Double",
    "valueRank": -1,
    "accessLevel": "Read",
    "userAccessLevel": "Read",
    "currentValue": 85.6,
    "currentQuality": "Good",
    "currentTimestamp": "2026-01-16T10:30:45Z"
  }
}
```

### 6. 导出所有变量节点

导出OPC服务器上的所有变量节点，用于选择代理

```
POST /api/export?maxDepth=<maxDepth>
```

**参数：**
- `maxDepth` (可选): 浏览深度，默认为3

**响应示例：**
```json
{
  "success": true,
  "message": "OK",
  "data": {
    "count": 2450,
    "tags": [
      {
        "node_id": "ns=2;s=Channel1.Device1.Temperature",
        "name": "Temperature",
        "description": "温度传感器",
        "data_type": "Double",
        "enabled": true
      },
      {
        "node_id": "ns=2;s=Channel1.Device1.Pressure",
        "name": "Pressure",
        "description": "压力传感器",
        "data_type": "Double",
        "enabled": true
      }
    ]
  }
}
```

### 7. 保存标签配置

将选择的标签保存到配置文件并重新加载

```
POST /api/save-tags
Content-Type: application/json
```

**请求体：**
```json
[
  {
    "node_id": "ns=2;s=Channel1.Device1.Temperature",
    "name": "Temperature",
    "description": "温度传感器",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "ns=2;s=Channel1.Device1.Pressure",
    "name": "Pressure",
    "description": "压力传感器",
    "data_type": "Double",
    "enabled": true
  }
]
```

**响应示例：**
```json
{
  "success": true,
  "message": "已保存 2 个标签并重新加载",
  "data": null,
  "timestamp": "2026-01-16T10:30:45Z"
}
```

## 使用流程

### 步骤1：浏览服务器结构

```bash
# 获取根节点
curl http://localhost:8080/api/browse
```

### 步骤2：深入浏览特定分支

```bash
# 浏览Channel1下的所有节点
curl "http://localhost:8080/api/browse/node?nodeId=ns=2;s=Channel1&depth=2"
```

### 步骤3：搜索特定标签

```bash
# 搜索包含"Temperature"的节点
curl "http://localhost:8080/api/search?q=Temperature"
```

### 步骤4：查看节点详情

```bash
# 获取节点详细信息
curl "http://localhost:8080/api/node?nodeId=ns=2;s=Channel1.Device1.Temperature"
```

### 步骤5：导出所有变量

```bash
# 导出所有变量节点（深度3）
curl -X POST http://localhost:8080/api/export?maxDepth=3
```

### 步骤6：选择并保存标签

1. 从导出结果中选择需要代理的标签
2. 保存到文件或通过API提交

```bash
# 保存标签配置
curl -X POST http://localhost:8080/api/save-tags \
  -H "Content-Type: application/json" \
  -d @selected_tags.json
```

## 节点类别说明

| NodeClass | 说明 | 是否可代理 |
|-----------|------|-----------|
| Object | 对象容器 | 否（用于组织结构） |
| Variable | 变量（数据点） | **是** |
| ObjectType | 对象类型定义 | 否 |
| VariableType | 变量类型定义 | 否 |
| ReferenceType | 引用类型定义 | 否 |
| DataType | 数据类型定义 | 否 |
| View | 视图 | 否 |

## 浏览深度建议

| 场景 | 推荐深度 | 说明 |
|------|---------|------|
| 简单浏览 | 1-2 | 快速查看顶级结构 |
| 标准浏览 | 2-3 | 查看设备和变量 |
| 深度浏览 | 3-5 | 完整结构（可能较慢） |

## 性能优化

### 1. 限制深度
```bash
# 只浏览2层深度
curl "http://localhost:8080/api/browse/tree?maxDepth=2"
```

### 2. 使用搜索
```bash
# 直接搜索特定标签，避免遍历
curl "http://localhost:8080/api/search?q=Temperature&max=100"
```

### 3. 分批导出
```bash
# 先导出部分节点测试
curl -X POST http://localhost:8080/api/export?maxDepth=2
```

## 常见问题

### Q: 如何知道节点ID？
A: 通过浏览功能查看，节点ID会显示在响应中。

### Q: 浏览很慢怎么办？
A:
1. 减少maxDepth参数
2. 使用搜索功能代替浏览
3. 指定具体的nodeId进行浏览

### Q: 如何选择需要代理的标签？
A:
1. 使用 `/api/export` 导出所有变量
2. 在导出结果中筛选需要的标签
3. 保存为JSON文件
4. 使用 `/api/save-tags` 保存配置

### Q: 如何查看OPC服务器的完整结构？
A:
```bash
# 从根节点开始，逐步深入
curl http://localhost:8080/api/browse
# 然后浏览每个子节点
curl "http://localhost:8080/api/browse/node?nodeId=<子节点ID>&depth=2"
```

## 示例：完整工作流程

### 1. 连接并浏览
```bash
# 查看根节点
curl http://localhost:8080/api/browse | jq
```

### 2. 查看设备列表
```bash
# 假设发现Channel1节点
curl "http://localhost:8080/api/browse/node?nodeId=ns=2;s=Channel1&depth=2" | jq
```

### 3. 搜索特定标签
```bash
# 搜索温度相关标签
curl "http://localhost:8080/api/search?q=Temperature" | jq
```

### 4. 查看标签详情
```bash
# 获取详细信息
curl "http://localhost:8080/api/node?nodeId=ns=2;s=Channel1.Device1.Temperature" | jq
```

### 5. 导出所有变量
```bash
# 导出到文件
curl -X POST http://localhost:8080/api/export?maxDepth=3 > all_tags.json
```

### 6. 筛选并保存
编辑 `all_tags.json`，保留需要的标签，然后：
```bash
# 保存配置
curl -X POST http://localhost:8080/api/save-tags \
  -H "Content-Type: application/json" \
  -d @selected_tags.json
```

### 7. 验证配置
```bash
# 查看当前标签数量
curl http://localhost:8080/api/status | jq '.data.tag_count'
```

## 注意事项

1. **权限**: 确保OPC服务器允许浏览和读取
2. **性能**: 大量节点（>10000）浏览可能较慢
3. **深度**: 建议不超过5层，避免超时
4. **缓存**: 浏览结果会缓存，提高后续速度
5. **错误处理**: 某些节点可能无法访问，会跳过

## 与Linux采集器集成

Linux采集器可以通过调用这些API来：
1. 自动发现可用标签
2. 选择需要采集的标签
3. 动态更新配置

示例脚本：
```bash
#!/bin/bash
# 自动发现并选择标签

# 1. 导出所有变量
curl -X POST http://192.168.1.100:8080/api/export?maxDepth=3 > all_tags.json

# 2. 筛选特定模式的标签（例如包含"Temperature"）
cat all_tags.json | jq '.data.tags[] | select(.name | contains("Temperature"))' > selected_tags.json

# 3. 保存配置
curl -X POST http://192.168.1.100:8080/api/save-tags \
  -H "Content-Type: application/json" \
  -d @selected_tags.json
```
