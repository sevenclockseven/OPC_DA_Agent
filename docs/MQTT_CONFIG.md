# MQTT 配置说明

## 问题修复

### 原问题
之前 MQTT 客户端是简化实现，只有打印日志，没有真正连接和发送数据到 MQTT 服务器。

### 修复内容
1. ✅ 添加了 MQTT 客户端库：`github.com/eclipse/paho.mqtt.golang`
2. ✅ 实现了真正的 MQTT 连接逻辑
3. ✅ 实现了真正的 MQTT 数据发布
4. ✅ 修复了 collector.ini 中的配置错误

## 配置说明

### MQTT 配置项（collector.ini）

```ini
[mqtt]
enabled   = true          # 是否启用MQTT
broker    = 172.16.32.245 # MQTT服务器地址
port      = 1883           # MQTT端口（默认1883）
topic     = /opc/test      # 发布主题
username  =                 # 用户名（可选）
password  =                 # 密码（可选）
client_id = opc_collector_01 # 客户端ID（必须唯一；留空自动生成）
qos       = 1              # 服务质量等级（0/1/2）
retain    = false          # 是否保留消息
tls_enabled = false          # true=ssl://（通常8883）
tls_insecure_skip_verify = false # 跳过证书校验（自签应急，不安全）
```

### 配置示例

#### 1. 连接到公共测试 MQTT 服务器（推荐用于测试）
```ini
[mqtt]
enabled   = true
broker    = test.mosquitto.org
port      = 1883
topic     = /opc/test
client_id = opc_collector_test_01
```

#### 2. 连接到本地 MQTT 服务器
```ini
[mqtt]
enabled   = true
broker    = localhost
port      = 1883
topic     = /opc/data
client_id = opc_collector_local_01
```

#### 3. 连接到远程 MQTT 服务器（带认证）
```ini
[mqtt]
enabled   = true
broker    = 172.16.32.245
port      = 1883
topic     = /opc/data
username  = opc_user
password  = opc_password
client_id = opc_collector_01
```

#### 4. TLS / 8883（如 HiveMQ 等要求加密的 broker）
```ini
[mqtt]
enabled   = true
broker    = broker.hivemq.com
port      = 8883
topic     = /opc/data
username  = opc_user
password  = opc_password
tls_enabled = true
tls_insecure_skip_verify = false
client_id = opc_collector_01
```

## 验证 MQTT 数据发送

### 方法1: 使用 MQTT 客户端工具

**使用 mosquitto_sub 订阅主题：**
```bash
mosquitto_sub -h test.mosquitto.org -t /opc/test -v
```

**使用 MQTT.fx 客户端：**
- 连接：test.mosquitto.org:1883
- 订阅：/opc/test

### 方法2: 查看程序日志

运行程序后，应该看到以下日志：
```
✅ MQTT已连接到 tcp://test.mosquitto.org:1883
✓ MQTT连接成功
📤 MQTT发布成功: /opc/test
```

## 数据格式

MQTT 发布的 JSON 数据格式：
```json
{
  "timestamp": "2026-01-18T16:38:28+08:00",
  "values": {
    "device1_value": 1.0,
    "device2_value": 0.0,
    "device3_value": 0.0
  },
  "metadata": {
    "device1_value": {
      "quality": 192,
      "timestamp": "2026-01-18T16:38:28+08:00"
    },
    "device2_value": {
      "quality": 192,
      "timestamp": "2026-01-18T16:38:28+08:00"
    }
  }
}
```

## 故障排除

### 1. 连接失败

**错误信息：**
```
MQTT连接失败: network Error : dial tcp 172.16.32.245:1883: connectex: ...
```

**可能原因和解决方法：**
- **IP 地址错误** → 检查 broker 配置是否正确
- **端口错误** → 确认 MQTT 服务器端口是否为 1883
- **防火墙阻止** → 检查服务器防火墙设置
- **MQTT 服务器未运行** → 确认 MQTT 服务器正在运行

### 2. 认证失败

**错误信息：**
```
MQTT连接失败: CONNACK error: not authorized
```

**解决方法：**
- 检查用户名和密码是否正确
- 确认客户端 ID 是否唯一

### 3. 发布失败

**错误信息：**
```
❌ MQTT发布失败: ...
```

**解决方法：**
- 检查网络连接是否稳定
- 确认权限是否允许发布到该主题
- 检查主题名称是否正确

### 4. 无数据发送

**可能原因：**
- MQTT 未启用 → 检查 `[mqtt]` 部分的 `enabled` 是否为 `true`
- 没有配置任务 → 检查 `[task1]` 等任务配置
- 采集周期太长 → 减小 `job_interval_second` 值

**测试方法：**
```bash
# 使用测试配置
./bin/go_collector.exe --config go_collector/collector_test.ini
```

## 常用 MQTT 服务器

### 公共测试服务器

| 服务器 | 地址 | 端口 | 说明 |
|--------|------|------|------|
| Eclipse Mosquitto | test.mosquitto.org | 1883 | 公共测试 |
| HiveMQ | broker.hivemq.com | 1883 | 需要 TLS |
| EMQX | broker.emqx.io | 1883 | 公共测试 |

### 本地安装

**安装 Mosquitto MQTT Broker:**

```bash
# Ubuntu/Debian
sudo apt-get install mosquitto mosquitto-clients

# CentOS/RHEL
sudo yum install mosquitto mosquitto-clients

# Windows
# 从 https://mosquitto.org/download/ 下载安装
```

**启动 Mosquitto 服务器：**
```bash
mosquitto -v
```

## 性能优化

### 1. 批量发布

当前实现是单个发布，可以考虑批量发布以提升性能。

### 2. 连接参数

可以在连接时添加以下参数：
- 自动重连：`opts.SetAutoReconnect(true)`
- 连接超时：`opts.SetConnectTimeout(30 * time.Second)`
- Keep Alive：`opts.SetKeepAlive(60 * time.Second)`

### 3. QoS 等级

- QoS 0：最多一次（最快，不保证送达）
- QoS 1：至少一次（推荐）
- QoS 2：恰好一次（最可靠，最慢）

## 相关链接

- [MQTT 协议](http://mqtt.org/)
- [Eclipse Paho Go Client](https://github.com/eclipse/paho.mqtt.golang)
- [Mosquitto MQTT Broker](https://mosquitto.org/)

---

**最后更新**: 2026-01-18
**测试状态**: ✅ 已验证 MQTT 数据发送成功
