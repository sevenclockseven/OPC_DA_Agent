# Linux采集程序配置说明

## 配置文件格式

### 1. INI格式（推荐）

**collector.ini**:
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

[monitor]
monitor=True
mode=email
email=2018241195@qq.com
ip=172.16.32.245
logon_website=http://admin.ciicp.com/internetAdmin/factory/api/collect/device/initialize
watch_website=http://admin.ciicp.com/internetAdmin/factory/api/collect/device/realtime/sync
collect_name=ciicp_sc
org_code=WBQY0009
dcs_code=01
dcs_name=shaocheng
app_key=nrt0Tu1x5GsBn9HxStg
app_secret=5nsuiuZpOlRCE3H9q3A

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
username=
password=
timeout=30000
headers=Content-Type:application/json;Authorization:Bearer token123

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
tag_opc4=lt.sc.20251_M4102_RRDY
tag_dbn4=20251_M4102_RRDY
tag_opc5=lt.sc.20251_M4202_RDY
tag_dbn5=20251_M4202_RDY
tag_opc6=lt.sc.20251_M4202_RUN
tag_dbn6=20251_M4202_RUN
tag_opc7=lt.sc.20251_M4202_ERR
tag_dbn7=20251_M4202_ERR
tag_opc8=lt.sc.20251_M4202_TZ
tag_dbn8=20251_M4202_TZ
tag_opc9=lt.sc.20251_M4202_CDBH
tag_dbn9=20251_M4202_CDBH
```

### 2. JSON格式

**collector.json**:
```json
{
  "main": {
    "title": "辽塔172.16.32.245烧成",
    "debug": false,
    "task_count": 1,
    "rtdb_host": ["172.16.32.98"],
    "rtdb_port": [8100],
    "opc_host": "172.16.32.98",
    "opc_server": "KEPware.KEPServerEx.V4",
    "opc_mode": "open",
    "opc_sync": true
  },
  "remote": {
    "remote": true,
    "rtdb_host": ["39.99.163.239", "39.99.164.49"],
    "rtdb_port": [8100, 8100]
  },
  "monitor": {
    "monitor": true,
    "mode": "email",
    "email": "2018241195@qq.com",
    "ip": "172.16.32.245",
    "logon_website": "http://admin.ciicp.com/internetAdmin/factory/api/collect/device/initialize",
    "watch_website": "http://admin.ciicp.com/internetAdmin/factory/api/collect/device/realtime/sync",
    "collect_name": "ciicp_sc",
    "org_code": "WBQY0009",
    "dcs_code": "01",
    "dcs_name": "shaocheng",
    "app_key": "nrt0Tu1x5GsBn9HxStg",
    "app_secret": "5nsuiuZpOlRCE3H9q3A"
  },
  "mqtt": {
    "enabled": true,
    "broker": "172.16.32.98",
    "port": 1883,
    "topic": "opc/data",
    "username": "",
    "password": "",
    "client_id": "opc_collector_01",
    "qos": 1,
    "retain": false
  },
  "http": {
    "enabled": false,
    "url": "http://172.16.32.98:8080/api/data",
    "method": "POST",
    "username": "",
    "password": "",
    "timeout": 30000,
    "headers": {
      "Content-Type": "application/json",
      "Authorization": "Bearer token123"
    }
  },
  "tasks": [
    {
      "enabled": true,
      "job_start_date": "2015-07-05 00:00:00",
      "job_interval_mode": "second",
      "job_interval_second": 1,
      "tag_device": "2025",
      "tag_component": 1,
      "tag_count": 1489,
      "tag_group": "sc",
      "tag_precision": 3,
      "tag_state": "2025_sc_state",
      "tags": [
        {
          "opc_tag": "lt.sc.20251_M4102_ZZT",
          "db_name": "20251_M4102_ZZT"
        },
        {
          "opc_tag": "lt.sc.20251_M4102_CYBJ",
          "db_name": "20251_M4102_CYBJ"
        },
        {
          "opc_tag": "lt.sc.20251_M4102_JYBJ",
          "db_name": "20251_M4102_JYBJ"
        }
      ]
    }
  ]
}
```

## 配置项说明

### [main] 主配置

| 配置项 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| `title` | string | 系统标题 | 辽塔172.16.32.245烧成 |
| `debug` | bool | 调试模式 | False |
| `task_count` | int | 任务数量 | 1 |
| `rtdb_host` | string[] | 实时数据库主机 | 172.16.32.98 |
| `rtdb_port` | int[] | 实时数据库端口 | 8100 |
| `opc_host` | string | OPC服务器主机 | 172.16.32.98 |
| `opc_server` | string | OPC服务器名称 | KEPware.KEPServerEx.V4 |
| `opc_mode` | string | OPC模式 | open |
| `opc_sync` | bool | 同步模式 | True |

### [remote] 远程配置

| 配置项 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| `remote` | bool | 启用远程 | True |
| `rtdb_host` | string[] | 远程实时数据库主机 | 39.99.163.239,39.99.164.49 |
| `rtdb_port` | int[] | 远程实时数据库端口 | 8100,8100 |

### [monitor] 监控配置

| 配置项 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| `monitor` | bool | 启用监控 | True |
| `mode` | string | 监控模式 | email |
| `email` | string | 告警邮箱 | 2018241195@qq.com |
| `ip` | string | 监控IP | 172.16.32.245 |
| `logon_website` | string | 登录URL | http://admin.ciicp.com/... |
| `watch_website` | string | 监控URL | http://admin.ciicp.com/... |
| `collect_name` | string | 采集名称 | ciicp_sc |
| `org_code` | string | 组织代码 | WBQY0009 |
| `dcs_code` | string | DCS代码 | 01 |
| `dcs_name` | string | DCS名称 | shaocheng |
| `app_key` | string | 应用密钥 | nrt0Tu1x5GsBn9HxStg |
| `app_secret` | string | 应用密钥 | 5nsuiuZpOlRCE3H9q3A |

### [mqtt] MQTT配置

| 配置项 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| `enabled` | bool | 启用MQTT | True |
| `broker` | string | MQTT服务器地址 | 172.16.32.98 |
| `port` | int | MQTT端口 | 1883 |
| `topic` | string | MQTT主题 | opc/data |
| `username` | string | 用户名 | (可选) |
| `password` | string | 密码 | (可选) |
| `client_id` | string | 客户端ID | opc_collector_01 |
| `qos` | int | 服务质量 | 0/1/2 |
| `retain` | bool | 保留消息 | False |

### [http] HTTP配置

| 配置项 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| `enabled` | bool | 启用HTTP | False |
| `url` | string | HTTP URL | http://172.16.32.98:8080/api/data |
| `method` | string | HTTP方法 | POST/GET |
| `username` | string | 用户名 | (可选) |
| `password` | string | 密码 | (可选) |
| `timeout` | int | 超时时间(ms) | 30000 |
| `headers` | dict | 请求头 | Content-Type:application/json |

### [taskX] 任务配置

| 配置项 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| `task` | bool | 启用任务 | True |
| `job_start_date` | datetime | 开始时间 | 2015-07-05 00:00:00 |
| `job_interval_mode` | string | 间隔模式 | second/minute/hour |
| `job_interval_second` | int | 间隔(秒) | 1 |
| `tag_device` | string | 设备标识 | 2025 |
| `tag_component` | int | 组件编号 | 1 |
| `tag_count` | int | 标签数量 | 1489 |
| `tag_group` | string | 标签组 | sc |
| `tag_precision` | int | 数据精度 | 3 |
| `tag_state` | string | 状态标签 | 2025_sc_state |
| `tag_opcX` | string | OPC标签 | lt.sc.20251_M4102_ZZT |
| `tag_dbnX` | string | 数据库字段名 | 20251_M4102_ZZT |

## 键名转换规则

### 规则类型

| 规则类型 | 说明 | 示例 |
|----------|------|------|
| `Replace` | 简单替换 | 将"-"替换为"_" |
| `RegexReplace` | 正则替换 | 移除特殊字符 |
| `ToLower` | 转小写 | ABC → abc |
| `ToUpper` | 转大写 | abc → ABC |
| `Trim` | 去除空格 | " abc " → "abc" |
| `RemovePrefix` | 移除前缀 | 移除"lt.sc." |
| `RemoveSuffix` | 移除后缀 | 移除"_TAG" |
| `AddPrefix` | 添加前缀 | 添加"OPC_" |
| `AddSuffix` | 添加后缀 | 添加"_VALUE" |
| `SplitAndSelect` | 分割选择 | "a.b.c" → 选择第2部分 |
| `Format` | 格式化 | 使用模板格式化 |

### 配置示例

**transform.json**:
```json
{
  "enabled": true,
  "default_prefix": "",
  "default_suffix": "",
  "rules": [
    {
      "rule_type": "RemovePrefix",
      "pattern": "lt.sc.",
      "replacement": "",
      "index": 0,
      "enabled": true,
      "description": "移除lt.sc.前缀"
    },
    {
      "rule_type": "RegexReplace",
      "pattern": "[^\\w\\.]+",
      "replacement": "_",
      "index": 0,
      "enabled": true,
      "description": "替换特殊字符为下划线"
    },
    {
      "rule_type": "ToLower",
      "enabled": false,
      "description": "转小写"
    }
  ]
}
```

### 转换示例

| 原始键名 | 转换后 | 规则 |
|----------|--------|------|
| `lt.sc.20251_M4102_ZZT` | `20251_M4102_ZZT` | 移除lt.sc.前缀 |
| `lt.sc.20251_M4102_CYBJ` | `20251_M4102_CYBJ` | 移除lt.sc.前缀 |
| `lt.sc.20251_M4102_JYBJ` | `20251_M4102_JYBJ` | 移除lt.sc.前缀 |

## Web配置界面

### 1. 配置管理页面

**URL**: `http://localhost:9090/web/config`

**功能**:
- 查看当前配置
- 编辑配置项
- 导入配置文件
- 导出配置文件
- 保存配置

### 2. MQTT配置页面

**URL**: `http://localhost:9090/web/mqtt`

**功能**:
- 配置MQTT服务器地址
- 配置端口、主题、认证信息
- 测试MQTT连接
- 保存配置

### 3. HTTP配置页面

**URL**: `http://localhost:9090/web/http`

**功能**:
- 配置HTTP服务器地址
- 配置请求方法、超时
- 配置请求头
- 测试HTTP连接
- 保存配置

### 4. 键名转换规则页面

**URL**: `http://localhost:9090/web/transform`

**功能**:
- 添加/编辑/删除转换规则
- 预览转换效果
- 保存规则配置

### 5. 配置导入导出页面

**URL**: `http://localhost:9090/web/import-export`

**功能**:
- 上传INI/JSON配置文件
- 下载配置文件
- 配置格式转换（INI ↔ JSON）
- 配置验证

## API接口

### 配置管理API

#### 1. 获取当前配置
```
GET /api/config
```

**响应**:
```json
{
  "success": true,
  "data": {
    "main": { ... },
    "mqtt": { ... },
    "http": { ... },
    "transform": { ... }
  }
}
```

#### 2. 更新配置
```
POST /api/config
Content-Type: application/json
```

**请求体**:
```json
{
  "mqtt": {
    "enabled": true,
    "broker": "172.16.32.98",
    "port": 1883,
    "topic": "opc/data"
  }
}
```

#### 3. 导入配置
```
POST /api/config/import
Content-Type: multipart/form-data
```

**参数**:
- `file`: 配置文件（INI或JSON）

#### 4. 导出配置
```
GET /api/config/export?format=ini
```

**参数**:
- `format`: ini 或 json

#### 5. 测试MQTT连接
```
POST /api/mqtt/test
```

**响应**:
```json
{
  "success": true,
  "message": "MQTT连接成功"
}
```

#### 6. 测试HTTP连接
```
POST /api/http/test
```

**响应**:
```json
{
  "success": true,
  "message": "HTTP请求成功"
}
```

#### 7. 预览键名转换
```
POST /api/transform/preview
Content-Type: application/json
```

**请求体**:
```json
{
  "rules": [
    {
      "rule_type": "RemovePrefix",
      "pattern": "lt.sc."
    }
  ],
  "test_keys": ["lt.sc.20251_M4102_ZZT", "lt.sc.20251_M4102_CYBJ"]
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "lt.sc.20251_M4102_ZZT": "20251_M4102_ZZT",
    "lt.sc.20251_M4102_CYBJ": "20251_M4102_CYBJ"
  }
}
```

## 使用流程

### 步骤1：创建配置文件

**方式1: 手动创建**
```bash
# 创建INI配置
nano collector.ini

# 或创建JSON配置
nano collector.json
```

**方式2: 使用Web界面**
1. 访问 `http://localhost:9090/web/config`
2. 填写配置表单
3. 保存配置

**方式3: 导入现有配置**
```bash
# 上传配置文件
curl -X POST http://localhost:9090/api/config/import \
  -F "file=@collector.ini"
```

### 步骤2：配置MQTT

**方式1: 编辑配置文件**
```ini
[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data
username=
password=
```

**方式2: 使用Web界面**
1. 访问 `http://localhost:9090/web/mqtt`
2. 填写MQTT配置
3. 点击"测试连接"
4. 保存配置

### 步骤3：配置键名转换

**方式1: 创建transform.json**
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

**方式2: 使用Web界面**
1. 访问 `http://localhost:9090/web/transform`
2. 添加转换规则
3. 预览转换效果
4. 保存规则

### 步骤4：启动采集器

```bash
# 使用INI配置
./collector --config collector.ini

# 使用JSON配置
./collector --config collector.json

# 使用Web配置（从Web加载）
./collector --web-config http://localhost:9090/api/config
```

### 步骤5：验证配置

```bash
# 查看配置
curl http://localhost:9090/api/config | jq

# 测试MQTT
curl -X POST http://localhost:9090/api/mqtt/test

# 查看状态
curl http://localhost:9090/api/status | jq
```

## 配置示例

### 示例1: 基本MQTT配置

**collector.ini**:
```ini
[main]
title=生产线数据采集
debug=False
task_count=1
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_count=1489
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
tag_opc2=lt.sc.20251_M4102_CYBJ
tag_dbn2=20251_M4102_CYBJ
```

**transform.json**:
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

### 示例2: HTTP上传配置

**collector.ini**:
```ini
[main]
title=数据上传到云平台
debug=False
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[http]
enabled=True
url=http://39.99.163.239:8080/api/data
method=POST
timeout=30000
headers=Content-Type:application/json;Authorization:Bearer abc123

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_count=100
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
```

### 示例3: 双输出配置（MQTT + HTTP）

**collector.ini**:
```ini
[main]
title=双输出采集
debug=False
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data

[http]
enabled=True
url=http://39.99.163.239:8080/api/data
method=POST

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_count=100
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
```

## Web界面功能

### 1. 配置编辑器

**功能**:
- 可视化表单编辑
- 实时验证
- 配置模板

**页面**: `/web/config-editor`

### 2. MQTT配置器

**功能**:
- MQTT服务器配置
- 连接测试
- 主题管理

**页面**: `/web/mqtt-config`

### 3. HTTP配置器

**功能**:
- HTTP服务器配置
- 请求头管理
- 测试请求

**页面**: `/web/http-config`

### 4. 键名转换器

**功能**:
- 规则管理
- 预览转换效果
- 规则测试

**页面**: `/web/transform-config`

### 5. 配置导入导出

**功能**:
- 文件上传
- 文件下载
- 格式转换
- 配置验证

**页面**: `/web/import-export`

## 配置验证

### 验证规则

1. **MQTT配置验证**:
   - Broker地址不能为空
   - 端口必须在1-65535之间
   - Topic不能为空

2. **HTTP配置验证**:
   - URL必须是有效的HTTP/HTTPS地址
   - 超时时间必须大于0

3. **任务配置验证**:
   - 标签数量必须匹配
   - OPC标签和数据库字段名必须对应

### 验证API

```
POST /api/config/validate
```

**响应**:
```json
{
  "success": true,
  "errors": [],
  "warnings": []
}
```

## 高级功能

### 1. 配置版本管理

```bash
# 保存配置版本
curl -X POST http://localhost:9090/api/config/save-version \
  -d '{"version": "v1.0", "description": "初始配置"}'

# 恢复配置版本
curl -X POST http://localhost:9090/api/config/restore-version \
  -d '{"version": "v1.0"}'
```

### 2. 配置模板

```bash
# 使用模板创建配置
curl -X POST http://localhost:9090/api/config/template \
  -d '{"template": "mqtt_basic", "name": "生产线配置"}'
```

### 3. 批量操作

```bash
# 批量更新标签
curl -X POST http://localhost:9090/api/tags/batch \
  -H "Content-Type: application/json" \
  -d @tags_batch.json
```

## 故障排除

### 配置加载失败

**问题**: 配置文件格式错误

**解决**:
1. 检查INI格式（章节、键值对）
2. 检查JSON格式（括号、逗号）
3. 使用Web界面验证配置

### MQTT连接失败

**问题**: 无法连接到MQTT服务器

**解决**:
1. 检查MQTT服务器地址和端口
2. 确认MQTT服务正在运行
3. 检查防火墙设置
4. 使用Web界面测试连接

### HTTP请求失败

**问题**: HTTP请求超时或失败

**解决**:
1. 检查URL是否正确
2. 确认HTTP服务器正在运行
3. 检查网络连接
4. 验证认证信息

### 键名转换错误

**问题**: 转换结果不符合预期

**解决**:
1. 检查转换规则顺序
2. 使用预览功能测试规则
3. 调整正则表达式
4. 查看转换日志

## 最佳实践

### 1. 配置文件管理

- 使用版本控制（Git）
- 定期备份配置
- 使用描述性版本号

### 2. MQTT配置

- 使用唯一的ClientID
- 合理设置QoS（1推荐）
- 考虑使用TLS加密

### 3. HTTP配置

- 使用HTTPS（生产环境）
- 设置合理的超时时间
- 添加认证头

### 4. 键名转换

- 先测试再应用
- 保持规则简单
- 记录转换规则

### 5. Web配置

- 限制访问权限
- 启用HTTPS
- 定期更新密码

## 总结

- 支持INI和JSON两种配置格式
- 完整的MQTT和HTTP配置
- 灵活的键名转换规则
- Web界面配置管理
- 配置导入导出功能
- 配置验证和测试
