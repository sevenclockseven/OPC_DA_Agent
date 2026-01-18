# OPC DA实际数据格式

## 真实数据示例

根据你提供的数据，OPC DA的实际格式如下：

```json
[
  {
    "errorCode": 0,
    "value": 0.8214290142059326,
    "quality": 192,
    "timestamp": "2026-01-16T07:38:34.921Z",
    "topic": "流量14"
  },
  {
    "errorCode": 0,
    "value": 3.0320301055908203,
    "quality": 192,
    "timestamp": "2026-01-15T09:11:53.046Z",
    "topic": "流量15"
  },
  {
    "errorCode": 0,
    "value": 0,
    "quality": 192,
    "timestamp": "2026-01-04T09:05:56.484Z",
    "topic": "给定4"
  }
]
```

## 数据字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `errorCode` | number | 错误码，0表示正常 |
| `value` | number/string | 数据值 |
| `quality` | number | 质量码（192=Good） |
| `timestamp` | string | 时间戳（ISO 8601格式） |
| `topic` | string | **标签名称（键）** |

## 质量码说明

| 质量码 | 含义 | 说明 |
|--------|------|------|
| 192 | Good | 数据正常 |
| 0 | Bad | 数据异常 |
| 其他 | Uncertain | 不确定状态 |

## 键值对转换

### 原始格式 → 键值对格式

```json
// 原始格式（数组）
[
  {"topic": "流量14", "value": 0.821, "quality": 192},
  {"topic": "流量15", "value": 3.032, "quality": 192}
]

// 转换为键值对
{
  "流量14": 0.821,
  "流量15": 3.032
}
```

## 配置示例

### tags.json

```json
[
  {
    "node_id": "流量14",
    "name": "流量14",
    "description": "流量传感器14",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "流量15",
    "name": "流量15",
    "description": "流量传感器15",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "给定4",
    "name": "给定4",
    "description": "给定值4",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "粉料设定值",
    "name": "粉料设定值",
    "description": "粉料设定值",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "读粉累计1",
    "name": "读粉累计1",
    "description": "粉料累计1",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "读粉累计2",
    "name": "读粉累计2",
    "description": "粉料累计2",
    "data_type": "Double",
    "enabled": true
  },
  {
    "node_id": "运行频率3",
    "name": "运行频率3",
    "description": "运行频率3",
    "data_type": "Double",
    "enabled": true
  }
]
```

## 数据处理示例

### Python处理

```python
import json

# 原始数据
raw_data = [
    {
        "errorCode": 0,
        "value": 0.8214290142059326,
        "quality": 192,
        "timestamp": "2026-01-16T07:38:34.921Z",
        "topic": "流量14"
    },
    {
        "errorCode": 0,
        "value": 3.0320301055908203,
        "quality": 192,
        "timestamp": "2026-01-15T09:11:53.046Z",
        "topic": "流量15"
    }
]

# 转换为键值对
key_value_data = {}
metadata = {}

for item in raw_data:
    if item['errorCode'] == 0:  # 只处理正常数据
        key = item['topic']
        value = item['value']

        key_value_data[key] = value
        metadata[key] = {
            'quality': item['quality'],
            'timestamp': item['timestamp']
        }

print("键值对数据:")
print(json.dumps(key_value_data, indent=2, ensure_ascii=False))

print("\n元数据:")
print(json.dumps(metadata, indent=2, ensure_ascii=False))
```

**输出**:
```json
{
  "流量14": 0.8214290142059326,
  "流量15": 3.0320301055908203
}
```

### Go处理

```go
type OPCData struct {
    ErrorCode int       `json:"errorCode"`
    Value     float64   `json:"value"`
    Quality   int       `json:"quality"`
    Timestamp string    `json:"timestamp"`
    Topic     string    `json:"topic"`
}

func processData(rawData []OPCData) (map[string]interface{}, map[string]interface{}) {
    keyValues := make(map[string]interface{})
    metadata := make(map[string]interface{})

    for _, item := range rawData {
        if item.ErrorCode == 0 {
            keyValues[item.Topic] = item.Value
            metadata[item.Topic] = map[string]interface{}{
                "quality":   item.Quality,
                "timestamp": item.Timestamp,
            }
        }
    }

    return keyValues, metadata
}
```

### JavaScript处理

```javascript
// 原始数据
const rawData = [
  {
    errorCode: 0,
    value: 0.8214290142059326,
    quality: 192,
    timestamp: "2026-01-16T07:38:34.921Z",
    topic: "流量14"
  },
  {
    errorCode: 0,
    value: 3.0320301055908203,
    quality: 192,
    timestamp: "2026-01-15T09:11:53.046Z",
    topic: "流量15"
  }
];

// 转换为键值对
const keyValues = {};
const metadata = {};

rawData.forEach(item => {
  if (item.errorCode === 0) {
    keyValues[item.topic] = item.value;
    metadata[item.topic] = {
      quality: item.quality,
      timestamp: item.timestamp
    };
  }
});

console.log("键值对数据:", keyValues);
console.log("元数据:", metadata);
```

## MQTT消息格式

### 推荐格式

```json
{
  "timestamp": "2026-01-16T07:38:34.921Z",
  "values": {
    "流量14": 0.8214290142059326,
    "流量15": 3.0320301055908203,
    "给定4": 0,
    "粉料设定值": 0
  },
  "metadata": {
    "流量14": {
      "quality": 192,
      "timestamp": "2026-01-16T07:38:34.921Z"
    },
    "流量15": {
      "quality": 192,
      "timestamp": "2026-01-15T09:11:53.046Z"
    }
  }
}
```

### 简化格式（仅值）

```json
{
  "流量14": 0.8214290142059326,
  "流量15": 3.0320301055908203,
  "给定4": 0,
  "粉料设定值": 0
}
```

## 数据库存储

### InfluxDB

```python
from influxdb_client import InfluxDBClient

client = InfluxDBClient(url="http://localhost:8086", token="token", org="org")

for item in raw_data:
    if item['errorCode'] == 0:
        point = {
            "measurement": "opc_data",
            "tags": {"tag_key": item['topic']},
            "fields": {"value": float(item['value'])},
            "time": item['timestamp']
        }
        client.write_api().write(bucket="opc", record=point)
```

### PostgreSQL

```sql
CREATE TABLE opc_data (
    id SERIAL PRIMARY KEY,
    tag_key VARCHAR(255) NOT NULL,
    value DOUBLE PRECISION,
    quality INTEGER,
    timestamp TIMESTAMP,
    UNIQUE(tag_key, timestamp)
);
```

```python
import psycopg2

conn = psycopg2.connect("dbname=opc user=postgres")
cur = conn.cursor()

for item in raw_data:
    if item['errorCode'] == 0:
        cur.execute(
            "INSERT INTO opc_data (tag_key, value, quality, timestamp) VALUES (%s, %s, %s, %s)",
            (item['topic'], item['value'], item['quality'], item['timestamp'])
        )

conn.commit()
```

## 数据分析

### 统计信息

```python
import pandas as pd

# 转换为DataFrame
df = pd.DataFrame(raw_data)

# 过滤正常数据
df_normal = df[df['errorCode'] == 0]

# 统计信息
print(f"总数据点: {len(df)}")
print(f"正常数据: {len(df_normal)}")
print(f"异常数据: {len(df) - len(df_normal)}")

# 按标签分组统计
grouped = df_normal.groupby('topic').agg({
    'value': ['min', 'max', 'mean', 'std'],
    'quality': 'first'
})
print(grouped)
```

### 异常检测

```python
# 检测异常数据
abnormal = df[df['errorCode'] != 0]
if len(abnormal) > 0:
    print("发现异常数据:")
    print(abnormal[['topic', 'errorCode', 'timestamp']])
```

## 常见问题

### Q: 如何处理质量码不为192的数据？

A: 只处理 `quality == 192` 的数据，其他数据可以记录日志或发送告警。

```python
if item['quality'] == 192 and item['errorCode'] == 0:
    # 处理正常数据
    pass
else:
    # 记录异常
    log.warning(f"异常数据: {item}")
```

### Q: 时间戳格式不一致怎么办？

A: 统一转换为ISO 8601格式：

```python
from datetime import datetime

timestamp = item['timestamp']
if isinstance(timestamp, str):
    dt = datetime.fromisoformat(timestamp.replace('Z', '+00:00'))
else:
    dt = datetime.fromtimestamp(timestamp / 1000)
```

### Q: 如何处理大量数据？

A: 使用批量处理：

```python
# 批量处理
batch_size = 500
for i in range(0, len(raw_data), batch_size):
    batch = raw_data[i:i+batch_size]
    process_batch(batch)
```

## 总结

- OPC DA数据是**数组格式**，每个元素包含 `topic`（键）和 `value`（值）
- 质量码 `192` 表示数据正常
- 错误码 `0` 表示无错误
- 需要转换为键值对格式进行处理
- 适合使用时序数据库存储
