# 快速开始指南

## 一、完整工作流程

### 阶段1：Windows代理端

#### 1.1 启动Windows代理
```powershell
# 编译项目
msbuild OPC_DA_Agent.csproj /p:Configuration=Release

# 运行（需要管理员权限）
cd bin\Release
OPC_DA_Agent.exe
```

#### 1.2 浏览和选择点号

**步骤1: 浏览OPC服务器**
```bash
# 查看根节点
curl http://localhost:8080/api/browse | jq
```

**步骤2: 深入浏览**
```bash
# 浏览特定节点
curl "http://localhost:8080/api/browse/node?nodeId=Plant1&depth=2" | jq
```

**步骤3: 导出所有变量**
```bash
# 导出所有变量节点
curl -X POST http://localhost:8080/api/export?maxDepth=5 > all_tags.json

# 查看总数
cat all_tags.json | jq '.data.count'
```

**步骤4: 选择需要的标签**
```bash
# 筛选特定模式的标签
cat all_tags.json | jq '.data.tags[] | select(.node_id | test("Plant1|Tank1"))' > selected_tags.json
```

**步骤5: 保存配置**
```bash
# 保存到Windows代理
curl -X POST http://localhost:8080/api/save-tags \
  -H "Content-Type: application/json" \
  -d @selected_tags.json
```

### 阶段2：Linux采集器端

#### 2.1 创建配置文件

**方式1: 使用INI格式（推荐）**
```bash
nano collector.ini
```

**内容示例**:
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
client_id=opc_collector_01
qos=1

[http]
enabled=False
url=http://172.16.32.98:8080/api/data
method=POST

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_count=100
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
tag_opc2=lt.sc.20251_M4102_CYBJ
tag_dbn2=20251_M4102_CYBJ
```

**方式2: 使用Web界面创建**
1. 启动Web服务器
2. 访问 `http://localhost:9090/web/config`
3. 填写配置表单
4. 保存配置

#### 2.2 启动采集器

```bash
# 编译Go程序
go mod tidy
go build -o collector collector_main.go ConfigManager.go collector_web.go

# 启动（带Web界面）
./collector --config collector.ini --web-port 9090
```

#### 2.3 访问Web配置界面

```
http://localhost:9090/
```

**功能菜单**:
- ⚙️ 配置管理 - 编辑主配置
- 📡 MQTT配置 - 配置MQTT服务器
- 🌐 HTTP配置 - 配置HTTP服务器
- 🔄 键名转换 - 配置转换规则
- 📁 导入导出 - 配置文件管理

### 阶段3：验证数据流

#### 3.1 查看系统状态
```bash
# 查看状态
curl http://localhost:9090/api/status | jq

# 查看统计
curl http://localhost:9090/api/stats | jq
```

#### 3.2 查看实时数据
```bash
# 获取数据（键值对格式）
curl http://localhost:9090/api/data | jq
```

#### 3.3 查看日志
```bash
# 实时查看日志
tail -f collector.log
```

## 二、常见场景示例

### 场景1：2000个点的MQTT采集

**Windows端**:
```bash
# 1. 导出所有点
curl -X POST http://localhost:8080/api/export?maxDepth=5 > all_tags.json

# 2. 选择前2000个
cat all_tags.json | jq '.data.tags[0:2000]' > selected_tags.json

# 3. 保存配置
curl -X POST http://localhost:8080/api/save-tags -d @selected_tags.json
```

**Linux端** (collector.ini):
```ini
[main]
title=2000点采集
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[mqtt]
enabled=True
broker=172.16.32.98
port=1883
topic=opc/data
client_id=opc_collector_01
qos=1

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_count=2000
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
# ... 更多标签
```

**启动**:
```bash
./collector --config collector.ini
```

### 场景2：HTTP上传到云平台

**Windows端**:
```bash
# 选择需要上传的标签
curl -X POST http://localhost:8080/api/export?maxDepth=5 > all_tags.json
cat all_tags.json | jq '.data.tags[] | select(.node_id | test("Temperature|Pressure"))' > selected_tags.json
curl -X POST http://localhost:8080/api/save-tags -d @selected_tags.json
```

**Linux端** (collector.ini):
```ini
[main]
title=云平台上传
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

### 场景3：双输出（MQTT + HTTP）

**配置文件**:
```ini
[main]
title=双输出采集
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

## 三、Web界面操作指南

### 1. 配置管理

**访问**: `http://localhost:9090/web/config`

**操作**:
1. 填写系统标题
2. 选择调试模式
3. 配置OPC服务器信息
4. 配置RTDB信息
5. 点击"保存配置"

### 2. MQTT配置

**访问**: `http://localhost:9090/web/mqtt`

**操作**:
1. 启用MQTT
2. 填写MQTT服务器地址
3. 填写端口（默认1883）
4. 填写主题
5. 填写认证信息（可选）
6. 点击"测试连接"
7. 点击"保存配置"

### 3. HTTP配置

**访问**: `http://localhost:9090/web/http`

**操作**:
1. 启用HTTP
2. 填写HTTP URL
3. 选择请求方法
4. 配置超时时间
5. 配置请求头（可选）
6. 点击"测试请求"
7. 点击"保存配置"

### 4. 键名转换规则

**访问**: `http://localhost:9090/web/transform`

**操作**:
1. 选择规则类型
2. 填写匹配模式
3. 填写替换内容
4. 点击"添加规则"
5. 点击"预览转换"
6. 点击"保存规则"

### 5. 导入导出

**访问**: `http://localhost:9090/web/import-export`

**操作**:
- **导入**: 选择配置文件 → 上传
- **导出**: 点击导出按钮 → 下载文件
- **转换**: 点击格式转换按钮

## 四、API使用示例

### 1. 获取配置
```bash
curl http://localhost:9090/api/config | jq
```

### 2. 更新配置
```bash
curl -X POST http://localhost:9090/api/config \
  -H "Content-Type: application/json" \
  -d '{"mqtt": {"enabled": true, "broker": "172.16.32.98", "port": 1883}}'
```

### 3. 导入配置
```bash
curl -X POST http://localhost:9090/api/config/import \
  -F "file=@collector.ini"
```

### 4. 导出配置
```bash
# 导出为INI
curl -o collector.ini http://localhost:9090/api/config/export?format=ini

# 导出为JSON
curl -o collector.json http://localhost:9090/api/config/export?format=json
```

### 5. 测试MQTT
```bash
curl -X POST http://localhost:9090/api/mqtt/test \
  -H "Content-Type: application/json" \
  -d '{"broker": "172.16.32.98", "port": 1883}'
```

### 6. 测试HTTP
```bash
curl -X POST http://localhost:9090/api/http/test \
  -H "Content-Type: application/json" \
  -d '{"url": "http://172.16.32.98:8080/api/data", "method": "POST"}'
```

### 7. 预览键名转换
```bash
curl -X POST http://localhost:9090/api/transform/preview \
  -H "Content-Type: application/json" \
  -d '{
    "rules": [{"rule_type": "RemovePrefix", "pattern": "lt.sc."}],
    "test_keys": ["lt.sc.20251_M4102_ZZT", "lt.sc.20251_M4102_CYBJ"]
  }'
```

## 五、配置模板

### 模板1: MQTT基础配置

**创建方式**:
```bash
# 使用API
curl -X POST http://localhost:9090/api/config/template/mqtt_basic
```

**配置内容**:
```ini
[main]
title=MQTT基础配置
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
tag_count=100
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
```

### 模板2: HTTP基础配置

**创建方式**:
```bash
curl -X POST http://localhost:9090/api/config/template/http_basic
```

**配置内容**:
```ini
[main]
title=HTTP基础配置
opc_host=172.16.32.98
opc_server=KEPware.KEPServerEx.V4

[http]
enabled=True
url=http://172.16.32.98:8080/api/data
method=POST

[task1]
task=True
job_interval_second=1
tag_device=2025
tag_count=100
tag_opc1=lt.sc.20251_M4102_ZZT
tag_dbn1=20251_M4102_ZZT
```

### 模板3: 完整配置

**创建方式**:
```bash
curl -X POST http://localhost:9090/api/config/template/full
```

## 六、故障排除

### 问题1: 无法启动Web服务器

**症状**:
```
错误: 端口被占用
```

**解决**:
```bash
# 检查端口占用
netstat -tuln | grep 9090

# 使用其他端口
./collector --config collector.ini --web-port 8080
```

### 问题2: 配置文件格式错误

**症状**:
```
错误: 无法加载配置文件
```

**解决**:
1. 检查INI格式（章节、键值对）
2. 检查JSON格式（括号、逗号）
3. 使用Web界面验证配置

### 问题3: MQTT连接失败

**症状**:
```
错误: MQTT连接失败
```

**解决**:
1. 检查MQTT服务器地址和端口
2. 确认MQTT服务正在运行
3. 使用Web界面测试连接

### 问题4: 数据采集失败

**症状**:
```
错误: 无法从OPC服务器读取数据
```

**解决**:
1. 检查Windows代理是否运行
2. 确认网络连接正常
3. 查看日志文件

## 七、最佳实践

### 1. 配置管理

- 使用版本控制（Git）管理配置文件
- 定期备份配置
- 使用描述性版本号

### 2. 命名规范

- 使用英文命名标签
- 保持命名一致性
- 避免特殊字符

### 3. 性能优化

- 合理设置批次大小（500-1000）
- 启用数据压缩
- 调整更新间隔

### 4. 安全配置

- 限制Web界面访问
- 使用强密码
- 定期更新配置

## 八、下一步

完成快速开始后，可以：

1. **深入学习**: 阅读 [COLLECTOR_CONFIG.md](COLLECTOR_CONFIG.md) 了解详细配置
2. **高级功能**: 了解键名转换规则
3. **性能调优**: 根据实际场景调整参数
4. **监控维护**: 设置日志监控和告警

## 九、获取帮助

如需进一步帮助，请查看：

- **COLLECTOR_CONFIG.md** - 详细配置说明
- **OPC_DA_FORMAT.md** - OPC DA点号格式
- **OPC_DA_SETUP_GUIDE.md** - OPC服务器配置
- **README.md** - 完整文档

或通过Web界面的"帮助"页面查看更多信息。
