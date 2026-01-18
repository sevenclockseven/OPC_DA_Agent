# OPC DA键值对数据格式说明

## 概述

OPC DA的数据本质上是键值对（Key-Value）形式：
- **Key**: 标签名称或节点ID
- **Value**: 对应的数值

本系统支持两种数据格式：
1. **键值对格式**（推荐）- 更高效，更适合MQTT传输
2. **列表格式**（兼容）- 传统格式，包含完整元数据

## 数据格式对比

### 1. 键值对格式（推荐）

**API端点**: `GET /api/data`

**响应示例**:
```json
{
  "success": true,
  "message": "OK",
  "data": {
    "batch_id": "abc123",
    "timestamp": "2026-01-16T10:30:45Z",
    "count": 3,
    "data": {
      "Temperature": 85.6,
      "Pressure": 125.3,
      "FlowRate": 45.2
    },
    "metadata": {
      "Temperature": {
        "data_type": "Double",
        "quality": "Good",
        "timestamp": "2026-01-16T10:30:45Z",
        "status": "Good"
      },
      "Pressure": {
        "data_type": "Double",
        "quality": "Good",
        "timestamp": "2026-01-16T10:30:45Z",
        "status": "Good"
      },
      "FlowRate": {
        "data_type": "Double",
        "quality": "Good",
        "timestamp": "2026-01-16T10:30:45Z",
        "status": "Good"
      }
    },
    "elapsed_ms": 0
  }
}
```

**优点**:
- 数据量小，传输效率高
- 适合MQTT消息传输
- 易于解析和处理
- 符合键值对的本质

**缺点**:
- 元数据分离，需要额外解析

### 2. 列表格式（兼容）

**API端点**: `GET /api/data/list`

**响应示例**:
```json
{
  "success": true,
  "message": "OK",
  "data": {
    "batch_id": "abc123",
    "timestamp": "2026-01-16T10:30:45Z",
    "count": 3,
    "data": [
      {
        "key": "Temperature",
        "value": 85.6,
        "quality": "Good",
        "timestamp": "2026-01-16T10:30:45Z",
        "status": "Good",
        "data_type": "Double",
        "node_id": "ns=2;s=Channel1.Device1.Temperature",
        "name": "Temperature"
      },
      {
        "key": "Pressure",
        "value": 125.3,
        "quality": "Good",
        "timestamp": "2026-01-16T10:30:45Z",
        "status": "Good",
        "data_type": "Double",
        "node_id": "ns=2;s=Channel1.Device1.Pressure",
        "name": "Pressure"
      }
    ],
    "elapsed_ms": 0
  }
}
```

**优点**:
- 完整的元数据
- 传统格式，易于理解
- 包含节点ID等额外信息

**缺点**:
- 数据量较大
- 传输效率较低

## Linux采集器适配

### 键值对格式处理

Linux采集器（Go）会自动处理键值对格式：

```go
// 数据结构
type BatchKeyValueResponse struct {
    BatchId    string                 `json:"batch_id"`
    Timestamp  time.Time              `json:"timestamp"`
    Count      int                    `json:"count"`
    Data       map[string]interface{} `json:"data"`        // 键值对
    Metadata   map[string]TagMetadata `json:"metadata"`    // 元数据
    ElapsedMs  float64                `json:"elapsed_ms"`
}

// 处理示例
func processData(response BatchKeyValueResponse) {
    for key, value := range response.Data {
        metadata := response.Metadata[key]
        fmt.Printf("%s = %v (Quality: %s, Type: %s)\n",
            key, value, metadata.Quality, metadata.DataType)
    }
}
```

### MQTT发布格式

使用键值对格式发布到MQTT：

```json
{
  "batch_id": "abc123",
  "timestamp": "2026-01-16T10:30:45Z",
  "count": 3,
  "data": {
    "Temperature": 85.6,
    "Pressure": 125.3,
    "FlowRate": 45.2
  },
  "metadata": {
    "Temperature": {
      "data_type": "Double",
      "quality": "Good",
      "timestamp": "2026-01-16T10:30:45Z"
    }
  }
}
```

## 使用示例

### 1. 获取键值对格式数据

```bash
curl http://localhost:8080/api/data | jq
```

**响应**:
```json
{
  "success": true,
  "data": {
    "data": {
      "Temperature": 85.6,
      "Pressure": 125.3
    },
    "metadata": {
      "Temperature": {
        "data_type": "Double",
        "quality": "Good"
      }
    }
  }
}
```

### 2. 获取列表格式数据

```bash
curl http://localhost:8080/api/data/list | jq
```

**响应**:
```json
{
  "success": true,
  "data": {
    "data": [
      {
        "key": "Temperature",
        "value": 85.6,
        "quality": "Good",
        "data_type": "Double"
      }
    ]
  }
}
```

### 3. Python处理示例

#### 处理键值对格式
```python
import requests
import json

# 获取数据
response = requests.get('http://localhost:8080/api/data')
data = response.json()

# 提取键值对
values = data['data']['data']
metadata = data['data']['metadata']

# 处理每个标签
for key, value in values.items():
    meta = metadata[key]
    print(f"{key}: {value} ({meta['quality']}, {meta['data_type']})")

# 输出:
# Temperature: 85.6 (Good, Double)
# Pressure: 125.3 (Good, Double)
```

#### 处理列表格式
```python
# 获取数据
response = requests.get('http://localhost:8080/api/data/list')
data = response.json()

# 提取列表
items = data['data']['data']

# 处理每个标签
for item in items:
    print(f"{item['key']}: {item['value']} ({item['quality']})")
```

### 4. Go处理示例

#### 处理键值对格式
```go
type KeyValueResponse struct {
    Data map[string]interface{} `json:"data"`
    Metadata map[string]TagMetadata `json:"metadata"`
}

func processData(resp KeyValueResponse) {
    for key, value := range resp.Data {
        meta := resp.Metadata[key]
        fmt.Printf("%s = %v (Quality: %s)\n", key, value, meta.Quality)
    }
}
```

#### 处理列表格式
```go
type TagValue struct {
    Key     string      `json:"key"`
    Value   interface{} `json:"value"`
    Quality string      `json:"quality"`
}

type ListResponse struct {
    Data []TagValue `json:"data"`
}

func processData(resp ListResponse) {
    for _, tag := range resp.Data {
        fmt.Printf("%s = %v (Quality: %s)\n", tag.Key, tag.Value, tag.Quality)
    }
}
```

## MQTT主题设计

### 推荐：使用键值对格式

**主题**: `opc/data/kv`

**消息**:
```json
{
  "timestamp": "2026-01-16T10:30:45Z",
  "values": {
    "Temperature": 85.6,
    "Pressure": 125.3,
    "FlowRate": 45.2
  }
}
```

### 备选：使用列表格式

**主题**: `opc/data/list`

**消息**:
```json
{
  "timestamp": "2026-01-16T10:30:45Z",
  "values": [
    {"key": "Temperature", "value": 85.6},
    {"key": "Pressure", "value": 125.3}
  ]
}
```

## 数据库存储建议

### 时序数据库（InfluxDB）

**键值对格式**:
```
measurement,tag_key=Temperature value=85.6,quality="Good" 1642338645000000000
measurement,tag_key=Pressure value=125.3,quality="Good" 1642338645000000000
```

**列表格式**:
```
measurement,tag_key=Temperature value=85.6,quality="Good",data_type="Double" 1642338645000000000
```

### 关系数据库（PostgreSQL）

**表结构**:
```sql
CREATE TABLE opc_data (
    id SERIAL PRIMARY KEY,
    tag_key VARCHAR(255) NOT NULL,
    value DOUBLE PRECISION,
    quality VARCHAR(50),
    data_type VARCHAR(50),
    timestamp TIMESTAMP,
    UNIQUE(tag_key, timestamp)
);
```

**插入示例**:
```sql
INSERT INTO opc_data (tag_key, value, quality, data_type, timestamp)
VALUES ('Temperature', 85.6, 'Good', 'Double', '2026-01-16 10:30:45');
```

## 性能对比

### 数据大小（1000个标签）

| 格式 | JSON大小 | 传输时间 | 解析时间 |
|------|---------|---------|---------|
| 键值对 | ~35KB | 快 | 快 |
| 列表 | ~85KB | 慢 | 慢 |

### 压缩效果

**启用压缩后**:
- 键值对: ~8KB (压缩率77%)
- 列表: ~15KB (压缩率82%)

## 最佳实践

### 1. 默认使用键值对格式

```bash
# 推荐
curl http://localhost:8080/api/data

# 备用（需要完整元数据时）
curl http://localhost:8080/api/data/list
```

### 2. MQTT传输使用键值对

```json
{
  "timestamp": "2026-01-16T10:30:45Z",
  "values": {
    "Temperature": 85.6,
    "Pressure": 125.3
  }
}
```

### 3. 数据库存储使用键值对

```python
# 批量插入
for key, value in data['data'].items():
    meta = data['metadata'][key]
    insert_data(key, value, meta['quality'], meta['data_type'])
```

### 4. 前端展示使用键值对

```javascript
// Vue/React示例
Object.entries(data.values).forEach(([key, value]) => {
    const meta = data.metadata[key];
    console.log(`${key}: ${value} (${meta.quality})`);
});
```

## 兼容性说明

### 旧版本兼容

如果旧版本客户端需要列表格式：
1. 使用 `/api/data/list` 端点
2. 或者修改配置启用兼容模式

### 新版本推荐

新开发的客户端：
1. 优先使用 `/api/data`（键值对格式）
2. 使用 `/api/data/list` 作为备用

## 常见问题

### Q: 为什么推荐键值对格式？

A:
1. 更符合OPC DA的本质（键值对）
2. 数据量小，传输效率高
3. 适合MQTT等消息队列
4. 易于解析和处理

### Q: 如何处理大量标签？

A:
1. 使用键值对格式减少数据量
2. 启用数据压缩
3. 分批传输

### Q: 如何保留完整元数据？

A:
1. 使用 `/api/data/list` 端点
2. 或者从 `/api/data` 的 metadata 字段获取

### Q: 如何迁移到键值对格式？

A:
1. 更新客户端代码，使用新的数据结构
2. 测试验证数据完整性
3. 逐步切换到新格式

## 总结

- **键值对格式**（`/api/data`）是推荐格式，更高效
- **列表格式**（`/api/data/list`）用于兼容旧版本
- 两种格式都包含完整信息，只是组织方式不同
- 根据使用场景选择合适的格式
