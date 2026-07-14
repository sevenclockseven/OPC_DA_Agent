package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"strings"
	"time"

	"github.com/gorilla/mux"
)

type WebServer struct {
	configPath    string
	configManager *ConfigManager
	transformer   *KeyTransformer
	collector     *Collector
}

func NewWebServer(configPath string, collector *Collector) *WebServer {
	return &WebServer{
		configPath:    configPath,
		configManager: NewConfigManager(),
		transformer:   NewKeyTransformer(),
		collector:     collector,
	}
}

// Start 启动Web服务器
func (ws *WebServer) Start(port int) error {
	r := mux.NewRouter()

	// 静态文件服务
	r.PathPrefix("/static/").Handler(http.StripPrefix("/static/", http.FileServer(http.Dir("./web/static"))))

	// Web页面
	r.HandleFunc("/", ws.handleHome).Methods("GET")
	r.HandleFunc("/web/monitor", ws.handleMonitorPage).Methods("GET")
	r.HandleFunc("/web/http", ws.handleHttpPage).Methods("GET")
	r.HandleFunc("/web/mqtt", ws.handleMqttPage).Methods("GET")
	r.HandleFunc("/web/rtdb", ws.handleRtdbPage).Methods("GET")
	r.HandleFunc("/web/transform", ws.handleTransformPage).Methods("GET")
	r.HandleFunc("/web/tasks", ws.handleTasksPage).Methods("GET")

	// API接口
	r.HandleFunc("/api/config", ws.handleGetConfig).Methods("GET")
	r.HandleFunc("/api/config", ws.handleUpdateConfig).Methods("POST")
	r.HandleFunc("/api/config/validate", ws.handleValidateConfig).Methods("POST")
	r.HandleFunc("/api/mqtt/test", ws.handleMqttTest).Methods("POST")
	r.HandleFunc("/api/rtdb/test", ws.handleRtdbTest).Methods("POST")
	r.HandleFunc("/api/http/test", ws.handleHttpTest).Methods("POST")
	r.HandleFunc("/api/transform/preview", ws.handleTransformPreview).Methods("POST")
	r.HandleFunc("/api/transform/rules", ws.handleGetTransformRules).Methods("GET")
	r.HandleFunc("/api/transform/rules", ws.handleUpdateTransformRules).Methods("POST")
	r.HandleFunc("/api/transform/debug", ws.handleTransformDebug).Methods("GET")
	r.HandleFunc("/api/webhook/test", ws.handleWebhookTest).Methods("POST")

	addr := fmt.Sprintf(":%d", port)
	fmt.Printf("Web服务器启动在 http://localhost%s\n", addr)
	return http.ListenAndServe(addr, r)
}

// 页面处理函数

func (ws *WebServer) handleHome(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>OPC DA Collector - Web配置</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }
        h1 { color: #333; border-bottom: 3px solid #4CAF50; padding-bottom: 10px; }
        .menu { display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 15px; margin-top: 30px; }
        .menu-item { background: #4CAF50; color: white; padding: 20px; border-radius: 6px; text-decoration: none; text-align: center; transition: all 0.3s; }
        .menu-item:hover { background: #45a049; transform: translateY(-2px); box-shadow: 0 4px 8px rgba(0,0,0,0.2); }
        .menu-item h3 { margin: 0 0 10px 0; font-size: 18px; }
        .menu-item p { margin: 0; font-size: 14px; opacity: 0.9; }
        .info { background: #e3f2fd; padding: 15px; border-radius: 6px; margin-top: 20px; border-left: 4px solid #2196F3; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔧 OPC DA Collector Web配置界面</h1>
        <p>欢迎使用OPC DA采集程序Web配置界面</p>

        <div class="menu">
            <a href="/web/http" class="menu-item">
                <h3>🌐 数据源</h3>
                <p>配置HTTP数据源</p>
            </a>
            <a href="/web/tasks" class="menu-item">
                <h3>📋 采集任务</h3>
                <p>配置采集任务</p>
            </a>
            <a href="/web/mqtt" class="menu-item">
                <h3>📡 MQTT输出</h3>
                <p>配置MQTT发布</p>
            </a>
            <a href="/web/rtdb" class="menu-item">
                <h3>💾 RTDB输出</h3>
                <p>配置实时库输出</p>
            </a>
            <a href="/web/transform" class="menu-item">
                <h3>🔄 键名转换</h3>
                <p>配置转换规则</p>
            </a>
            <a href="/web/monitor" class="menu-item">
                <h3>🔔 监控配置</h3>
                <p>配置Webhook预警</p>
            </a>
        </div>

        <div class="info">
            <strong>快速开始:</strong>
            <ol>
                <li>配置MQTT或HTTP服务器</li>
                <li>设置键名转换规则</li>
                <li>导入或创建配置文件</li>
                <li>启动采集程序</li>
            </ol>
        </div>
    </div>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) handleHttpPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>数据源配置 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        h1 { color: #333; }
        .http-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(350px, 1fr)); gap: 15px; margin-top: 20px; }
        .http-card { background: #f9f9f9; border: 1px solid #ddd; border-radius: 6px; padding: 15px; }
        .http-card.disabled { opacity: 0.6; }
        .http-name { font-size: 16px; font-weight: bold; color: #333; margin-bottom: 10px; }
        .http-info { color: #666; font-size: 14px; line-height: 1.6; }
        .http-actions { margin-top: 10px; display: flex; gap: 8px; }
        .btn { padding: 6px 12px; border: none; border-radius: 4px; cursor: pointer; font-size: 13px; }
        .btn-primary { background: #4CAF50; color: white; }
        .btn-danger { background: #f44336; color: white; }
        .btn-edit { background: #2196F3; color: white; }
        .btn:hover { opacity: 0.85; }
        .add-http { background: #4CAF50; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; margin-top: 15px; }
        .modal { display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); z-index: 1000; }
        .modal-content { background: white; margin: 5% auto; padding: 20px; border-radius: 8px; width: 90%; max-width: 500px; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; color: #555; }
        input, select { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        .modal-actions { display: flex; gap: 10px; justify-content: flex-end; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
        .badge { display: inline-block; padding: 2px 8px; border-radius: 12px; font-size: 12px; color: white; }
        .badge-on { background: #4CAF50; }
        .badge-off { background: #999; }
    </style>
</head>
<body>
    <div class="container">
        <a href="/" class="back">← 返回首页</a>
        <h1>🌐 数据源配置</h1>
        <p>配置HTTP数据源（C# OPC DA Agent地址）</p>
        <div id="result"></div>

        <div class="http-grid" id="httpGrid"></div>

        <button class="add-http" onclick="openModal()">+ 添加数据源</button>

        <div class="modal" id="httpModal">
            <div class="modal-content">
                <h2 id="modalTitle">添加数据源</h2>
                <div class="form-group">
                    <label>数据源名称</label>
                    <input type="text" id="httpName" placeholder="例：数据源1">
                </div>
                <div class="form-group">
                    <label>启用</label>
                    <select id="httpEnabled">
                        <option value="true">启用</option>
                        <option value="false">禁用</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>URL地址</label>
                    <input type="text" id="httpUrl" placeholder="例：http://192.168.1.100:8080/api/data">
                </div>
                <div class="form-group">
                    <label>请求方法</label>
                    <select id="httpMethod">
                        <option value="GET">GET</option>
                        <option value="POST">POST</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>超时时间(毫秒)</label>
                    <input type="number" id="httpTimeout" value="5000" min="1000">
                </div>
                <div class="modal-actions">
                    <button class="btn" onclick="closeModal()" style="background:#666;color:white;">取消</button>
                    <button class="btn btn-primary" onclick="saveHttp()">保存</button>
                </div>
            </div>
        </div>
    </div>

    <script>
        let httpConfigs = [];
        let editingIndex = -1;

        async function loadData() {
            try {
                const resp = await fetch('/api/config');
                const data = await resp.json();
                if (data.success) {
                    httpConfigs = data.data.http_configs || [];
                    renderHttpConfigs();
                }
            } catch (e) {
                showResult(false, '加载配置失败: ' + e.message);
            }
        }

        function renderHttpConfigs() {
            const grid = document.getElementById('httpGrid');
            if (httpConfigs.length === 0) {
                grid.innerHTML = '<div style="color:#999;padding:20px;">暂无数据源，请点击下方按钮添加</div>';
                return;
            }

            grid.innerHTML = '';
            httpConfigs.forEach((config, index) => {
                const card = document.createElement('div');
                card.className = 'http-card' + (config.enabled ? '' : ' disabled');
                card.innerHTML =
                    '<div class="http-name">' + (config.name || '数据源' + (index+1)) + ' <span class="badge ' + (config.enabled ? 'badge-on' : 'badge-off') + '">' + (config.enabled ? '启用' : '禁用') + '</span></div>' +
                    '<div class="http-info">' +
                    'URL: ' + (config.url || '未配置') + '<br>' +
                    '方法: ' + (config.method || 'GET') + '<br>' +
                    '超时: ' + (config.timeout || 5000) + 'ms' +
                    '</div>' +
                    '<div class="http-actions">' +
                    '<button class="btn btn-edit" onclick="editHttp(' + index + ')">编辑</button>' +
                    '<button class="btn btn-danger" onclick="deleteHttp(' + index + ')">删除</button>' +
                    '</div>';
                grid.appendChild(card);
            });
        }

        function openModal(index) {
            editingIndex = index !== undefined ? index : -1;
            document.getElementById('modalTitle').textContent = index !== undefined ? '编辑数据源' : '添加数据源';

            if (index !== undefined && httpConfigs[index]) {
                const config = httpConfigs[index];
                document.getElementById('httpName').value = config.name || '';
                document.getElementById('httpEnabled').value = config.enabled ? 'true' : 'false';
                document.getElementById('httpUrl').value = config.url || '';
                document.getElementById('httpMethod').value = config.method || 'GET';
                document.getElementById('httpTimeout').value = config.timeout || 5000;
            } else {
                document.getElementById('httpName').value = '';
                document.getElementById('httpEnabled').value = 'true';
                document.getElementById('httpUrl').value = '';
                document.getElementById('httpMethod').value = 'GET';
                document.getElementById('httpTimeout').value = 5000;
            }

            document.getElementById('httpModal').style.display = 'block';
        }

        function closeModal() {
            document.getElementById('httpModal').style.display = 'none';
            editingIndex = -1;
        }

        async function saveHttp() {
            const name = document.getElementById('httpName').value.trim();
            const url = document.getElementById('httpUrl').value.trim();
            if (!name || !url) {
                showResult(false, '名称和URL不能为空');
                return;
            }

            const config = {
                name: name,
                enabled: document.getElementById('httpEnabled').value === 'true',
                url: url,
                method: document.getElementById('httpMethod').value,
                timeout: parseInt(document.getElementById('httpTimeout').value) || 5000
            };

            if (editingIndex >= 0) {
                httpConfigs[editingIndex] = config;
            } else {
                httpConfigs.push(config);
            }

            try {
                const resp = await fetch('/api/config');
                const fullConfig = await resp.json();
                if (fullConfig.success) {
                    fullConfig.data.http_configs = httpConfigs;
                    const saveResp = await fetch('/api/config', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(fullConfig.data)
                    });
                    const result = await saveResp.json();
                    showResult(result.success, result.message);
                    if (result.success) {
                        closeModal();
                        loadData();
                    }
                }
            } catch (e) {
                showResult(false, '保存失败: ' + e.message);
            }
        }

        function editHttp(index) {
            openModal(index);
        }

        async function deleteHttp(index) {
            if (!confirm('确认删除此数据源？')) return;
            httpConfigs.splice(index, 1);

            try {
                const resp = await fetch('/api/config');
                const fullConfig = await resp.json();
                if (fullConfig.success) {
                    fullConfig.data.http_configs = httpConfigs;
                    const saveResp = await fetch('/api/config', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(fullConfig.data)
                    });
                    const result = await saveResp.json();
                    showResult(result.success, result.message);
                    if (result.success) loadData();
                }
            } catch (e) {
                showResult(false, '删除失败: ' + e.message);
            }
        }

        function showResult(success, message) {
            const div = document.getElementById('result');
            div.innerHTML = '<div class="' + (success ? 'success' : 'error') + '">' + (success ? '✓ ' : '✗ ') + message + '</div>';
            setTimeout(() => div.innerHTML = '', 3000);
        }

        loadData();
    </script>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) handleRtdbPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>RTDB配置 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        h1 { color: #333; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; color: #555; }
        input, select, textarea { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        button { background: #4CAF50; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; margin-right: 10px; }
        button:hover { background: #45a049; }
        .test { background: #2196F3; }
        .back { background: #666; border: 2px solid #333; color: white; padding: 8px 16px; text-decoration: none; border-radius: 4px; display: inline-block; }
        .back:hover { background: #555; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
    </style>
</head>
<body>
    <div class="container">
        <h1>💾 RTDB输出配置</h1>
        <a href="/" class="back">← 返回首页</a>

        <form id="rtdbForm">
            <div class="form-group">
                <label>启用RTDB输出</label>
                <select id="enabled" name="enabled">
                    <option value="false">否</option>
                    <option value="true">是</option>
                </select>
            </div>
            <div class="form-group">
                <label>RTDB服务器地址</label>
                <input type="text" id="host" name="host" placeholder="例如: 192.168.1.100">
            </div>
            <div class="form-group">
                <label>RTDB端口</label>
                <input type="number" id="port" name="port" placeholder="例如: 9001">
            </div>
            <div class="form-group">
                <label>输出格式</label>
                <select id="format" name="format">
                    <option value="{key},{value},{quality},{timestamp}">CSV: key,value,quality,timestamp</option>
                    <option value="json">JSON格式</option>
                    <option value="custom">自定义格式</option>
                </select>
            </div>
            <div class="form-group">
                <label>自定义格式模板</label>
                <textarea id="custom_format" name="custom_format" rows="3" placeholder="例如: {key},{value},{quality},{timestamp}"></textarea>
            </div>

            <button type="button" onclick="saveRtdb()">💾 保存配置</button>
            <button type="button" class="test" onclick="testRtdb()">🧪 测试连接</button>
        </form>

        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        async function loadRtdb() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success && data.data.rtdb) {
                const rtdb = data.data.rtdb;
                document.getElementById('enabled').value = rtdb.enabled?.toString() || 'false';
                document.getElementById('host').value = rtdb.host || '';
                document.getElementById('port').value = rtdb.port || '';
                const format = rtdb.format || '{key},{value},{quality},{timestamp}';
                if (format === '{key},{value},{quality},{timestamp}') {
                    document.getElementById('format').value = '{key},{value},{quality},{timestamp}';
                } else if (format === 'json') {
                    document.getElementById('format').value = 'json';
                } else {
                    document.getElementById('format').value = 'custom';
                    document.getElementById('custom_format').value = format;
                }
            }
        }

        async function saveRtdb() {
            let format = document.getElementById('format').value;
            if (format === 'custom') {
                format = document.getElementById('custom_format').value;
            }

            const rtdb = {
                enabled: document.getElementById('enabled').value === 'true',
                host: document.getElementById('host').value,
                port: parseInt(document.getElementById('port').value) || 0,
                format: format
            };

            const response = await fetch('/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ rtdb })
            });

            const result = await response.json();
            showResult(result);
        }

        async function testRtdb() {
            let format = document.getElementById('format').value;
            if (format === 'custom') {
                format = document.getElementById('custom_format').value;
            }

            const rtdb = {
                enabled: true,
                host: document.getElementById('host').value,
                port: parseInt(document.getElementById('port').value) || 0,
                format: format
            };

            const response = await fetch('/api/rtdb/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(rtdb)
            });

            const result = await response.json();
            showResult(result);
        }

        function showResult(result) {
            const div = document.getElementById('result');
            if (result.success) {
                div.innerHTML = '<div class="success">✓ ' + (result.message || '操作成功') + '</div>';
            } else {
                div.innerHTML = '<div class="error">✗ ' + (result.message || '操作失败') + '</div>';
            }
        }

        loadRtdb();
    </script>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) handleMqttPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>MQTT配置 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        h1 { color: #333; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; color: #555; }
        input, select { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        button { background: #4CAF50; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; margin-right: 10px; }
        button:hover { background: #45a049; }
        .test { background: #2196F3; }
        .back { background: #666; border: 2px solid #333; color: white; padding: 8px 16px; text-decoration: none; border-radius: 4px; display: inline-block; }
        .back:hover { background: #555; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
    </style>
</head>
<body>
    <div class="container">
        <h1>📡 MQTT配置</h1>
        <a href="/" class="back">← 返回首页</a>

        <form id="mqttForm">
            <div class="form-group">
                <label>启用MQTT</label>
                <select id="enabled" name="enabled">
                    <option value="false">否</option>
                    <option value="true">是</option>
                </select>
            </div>
            <div class="form-group">
                <label>Broker地址</label>
                <input type="text" id="broker" name="broker" placeholder="例如: 172.16.32.98">
            </div>
            <div class="form-group">
                <label>端口</label>
                <input type="number" id="port" name="port" value="1883">
            </div>
            <div class="form-group">
                <label>主题(Topic)</label>
                <input type="text" id="topic" name="topic" placeholder="例如: opc/data">
            </div>
            <div class="form-group">
                <label>客户端ID</label>
                <input type="text" id="client_id" name="client_id" placeholder="例如: opc_collector_01">
            </div>
            <div class="form-group">
                <label>QoS</label>
                <select id="qos" name="qos">
                    <option value="0">0 - 最多一次</option>
                    <option value="1" selected>1 - 至少一次</option>
                    <option value="2">2 - 恰好一次</option>
                </select>
            </div>
            <div class="form-group">
                <label>Retain</label>
                <select id="retain" name="retain">
                    <option value="false">否</option>
                    <option value="true">是</option>
                </select>
            </div>
            <div class="form-group">
                <label>输出格式</label>
                <select id="format" name="format" onchange="onMqttFormatChange()">
                    <option value="full">完整格式(full)</option>
                    <option value="flat">扁平格式(flat)</option>
                    <option value="custom">自定义模板</option>
                </select>
            </div>
            <div class="form-group" id="mqttCustomFormatGroup" style="display:none;">
                <label>自定义格式模板</label>
                <textarea id="mqtt_custom_format" name="mqtt_custom_format" rows="3" placeholder="例如: {key},{value},{quality},{timestamp}"></textarea>
            </div>
            <div class="form-group">
                <label>逐点拆分(split)</label>
                <select id="split" name="split">
                    <option value="false">否 - 一批拼一行</option>
                    <option value="true">是 - 每点一条报文</option>
                </select>
            </div>
            <div class="form-group">
                <label>JS转换(js_transform, 可选)</label>
                <textarea id="js_transform" name="js_transform" rows="3" placeholder="返回电文的JS表达式, 变量 point={key,value,quality,timestamp}"></textarea>
            </div>

            <button type="button" onclick="saveMqtt()">💾 保存配置</button>
            <button type="button" class="test" onclick="testMqtt()">🧪 测试连接</button>
        </form>

        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        function onMqttFormatChange() {
            const fmt = document.getElementById('format').value;
            document.getElementById('mqttCustomFormatGroup').style.display = (fmt === 'custom') ? 'block' : 'none';
        }

        async function loadMqtt() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success && data.data.mqtt) {
                const mqtt = data.data.mqtt;
                document.getElementById('enabled').value = mqtt.enabled?.toString() || 'false';
                document.getElementById('broker').value = mqtt.broker || '';
                document.getElementById('port').value = mqtt.port || 1883;
                document.getElementById('topic').value = mqtt.topic || '';
                document.getElementById('client_id').value = mqtt.client_id || '';
                document.getElementById('qos').value = mqtt.qos?.toString() || '1';
                document.getElementById('retain').value = mqtt.retain?.toString() || 'false';
                const format = mqtt.format || 'full';
                if (format === 'full' || format === 'flat') {
                    document.getElementById('format').value = format;
                    document.getElementById('mqttCustomFormatGroup').style.display = 'none';
                } else {
                    document.getElementById('format').value = 'custom';
                    document.getElementById('mqtt_custom_format').value = format;
                    document.getElementById('mqttCustomFormatGroup').style.display = 'block';
                }
                document.getElementById('split').value = (mqtt.split === true).toString();
                document.getElementById('js_transform').value = mqtt.js_transform || '';
            }
        }

        async function saveMqtt() {
            let format = document.getElementById('format').value;
            if (format === 'custom') {
                format = document.getElementById('mqtt_custom_format').value;
            }
            const mqtt = {
                enabled: document.getElementById('enabled').value === 'true',
                broker: document.getElementById('broker').value,
                port: parseInt(document.getElementById('port').value),
                topic: document.getElementById('topic').value,
                client_id: document.getElementById('client_id').value,
                qos: parseInt(document.getElementById('qos').value),
                retain: document.getElementById('retain').value === 'true',
                format: format,
                split: document.getElementById('split').value === 'true',
                js_transform: document.getElementById('js_transform').value
            };

            const response = await fetch('/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ mqtt })
            });

            const result = await response.json();
            showResult(result);
        }

        async function testMqtt() {
            let format = document.getElementById('format').value;
            if (format === 'custom') {
                format = document.getElementById('mqtt_custom_format').value;
            }
            const mqtt = {
                broker: document.getElementById('broker').value,
                port: parseInt(document.getElementById('port').value),
                client_id: document.getElementById('client_id').value,
                format: format,
                split: document.getElementById('split').value === 'true',
                js_transform: document.getElementById('js_transform').value
            };

            const response = await fetch('/api/mqtt/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(mqtt)
            });

            const result = await response.json();
            showResult(result);
        }

        function showResult(result) {
            const div = document.getElementById('result');
            if (result.success) {
                div.innerHTML = '<div class="success">✓ ' + (result.message || '操作成功') + '</div>';
            } else {
                div.innerHTML = '<div class="error">✗ ' + (result.message || '操作失败') + '</div>';
            }
        }

        loadMqtt();
    </script>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) handleTransformPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>键名转换规则 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 1000px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        h1 { color: #333; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; color: #555; }
        input, select, textarea { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        button { background: #4CAF50; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; margin-right: 10px; }
        button:hover { background: #45a049; }
        .test { background: #2196F3; }
        .back { background: #666; border: 2px solid #333; color: white; padding: 8px 16px; text-decoration: none; border-radius: 4px; display: inline-block; }
        .back:hover { background: #555; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
        .rule-item { background: #f9f9f9; padding: 10px; margin: 10px 0; border-radius: 4px; border-left: 4px solid #4CAF50; cursor: move; }
        .rule-item.dragging { opacity: 0.5; border-left-color: #2196F3; }
        .rule-item.drag-over { border-top: 2px solid #2196F3; }
        .preview { background: #e3f2fd; padding: 15px; border-radius: 6px; margin-top: 20px; }
        .rule-buttons { display: inline-block; margin-left: 10px; }
        .rule-buttons button { padding: 4px 8px; margin-right: 5px; font-size: 12px; }
        .source-selector { background: #fff3e0; padding: 15px; border-radius: 6px; margin-bottom: 20px; border-left: 4px solid #ff9800; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔄 键名转换规则</h1>
        <a href="/" class="back">← 返回首页</a>

        <div class="source-selector">
            <div class="form-group">
                <label>选择数据源</label>
                <select id="source_selector" onchange="onSourceChange()">
                    <option value="">默认（所有数据源共用）</option>
                </select>
            </div>
        </div>

        <div class="form-group">
            <label>启用转换</label>
            <select id="enabled" name="enabled">
                <option value="true">是</option>
                <option value="false">否</option>
            </select>
        </div>

        <h3>添加新规则</h3>
        <div class="form-group">
            <label>规则类型</label>
            <select id="rule_type" name="rule_type">
                <option value="RemovePrefix">移除前缀</option>
                <option value="RemoveSuffix">移除后缀</option>
                <option value="AddPrefix">添加前缀</option>
                <option value="AddSuffix">添加后缀</option>
                <option value="Replace">简单替换</option>
                <option value="RegexReplace">正则替换</option>
                <option value="ToLower">转小写</option>
                <option value="ToUpper">转大写</option>
                <option value="Trim">去除空格</option>
                <option value="SplitAndSelect">分割选择</option>
            </select>
        </div>
        <div class="form-group">
            <label>匹配模式/分隔符</label>
            <input type="text" id="pattern" placeholder="例如: lt.sc. 或 _">
        </div>
        <div class="form-group">
            <label>替换内容/格式</label>
            <input type="text" id="replacement" placeholder="例如: _ 或 {0}">
        </div>
        <div class="form-group">
            <label>索引（用于分割选择）</label>
            <input type="number" id="index" value="0">
        </div>
        <div class="form-group">
            <label>描述</label>
            <input type="text" id="description" placeholder="规则描述">
        </div>

        <button type="button" onclick="addRule()">➕ 添加规则</button>
        <button type="button" class="test" onclick="previewTransform()">👁️ 预览转换</button>
        <button type="button" onclick="saveRules()">💾 保存规则</button>

        <h3>当前规则列表</h3>
        <div id="rulesList"></div>

        <div id="preview" class="preview" style="display: none;"></div>
        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        let rules = [];
        let currentSource = '';

        async function loadSources() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success && data.data.http_configs) {
                const select = document.getElementById('source_selector');
                data.data.http_configs.forEach(config => {
                    const option = document.createElement('option');
                    option.value = config.name;
                    option.textContent = config.name;
                    select.appendChild(option);
                });
            }
        }

        function onSourceChange() {
            currentSource = document.getElementById('source_selector').value;
            loadRules();
        }

        async function loadRules() {
            let url = '/api/transform/rules';
            if (currentSource) {
                url += '?source=' + encodeURIComponent(currentSource);
            }
            const response = await fetch(url);
            const data = await response.json();
            if (data.success) {
                rules = data.data.rules || [];
                document.getElementById('enabled').value = data.data.enabled?.toString() || 'true';
                renderRules();
            }
        }

        function renderRules() {
            const container = document.getElementById('rulesList');
                if (rules.length === 0) {
                container.innerHTML = '<p>暂无转换规则</p>';
                return;
            }

            let html = '';
            rules.forEach((rule, index) => {
                html += '<div class="rule-item" draggable="true" data-index="' + index + '"' +
                    ' ondragstart="onDragStart(event)" ondragover="onDragOver(event)"' +
                    ' ondragenter="onDragEnter(event)" ondragleave="onDragLeave(event)"' +
                    ' ondrop="onDrop(event)" ondragend="onDragEnd(event)">' +
                    '<strong>#' + (index + 1) + ' ' + rule.rule_type + '</strong>' +
                    (rule.pattern ? ' | 模式: ' + rule.pattern : '') +
                    (rule.replacement ? ' | 替换: ' + rule.replacement : '') +
                    (rule.description ? ' | ' + rule.description : '') +
                    '<span class="rule-buttons">' +
                    (index > 0 ? '<button onclick="moveRule(' + index + ', -1)" style="background: #2196F3;">↑</button>' : '') +
                    (index < rules.length - 1 ? '<button onclick="moveRule(' + index + ', 1)" style="background: #2196F3;">↓</button>' : '') +
                    '<button onclick="removeRule(' + index + ')" style="background: #f44336;">删除</button>' +
                    '</span></div>';
            });
            container.innerHTML = html;
        }

        let draggedIndex = null;

        function onDragStart(e) {
            draggedIndex = parseInt(e.target.dataset.index);
            e.target.classList.add('dragging');
            e.dataTransfer.effectAllowed = 'move';
        }

        function onDragOver(e) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
        }

        function onDragEnter(e) {
            e.preventDefault();
            e.target.closest('.rule-item')?.classList.add('drag-over');
        }

        function onDragLeave(e) {
            e.target.closest('.rule-item')?.classList.remove('drag-over');
        }

        function onDrop(e) {
            e.preventDefault();
            const targetItem = e.target.closest('.rule-item');
            if (!targetItem) return;
            const targetIndex = parseInt(targetItem.dataset.index);
            
            if (draggedIndex !== null && draggedIndex !== targetIndex) {
                const item = rules.splice(draggedIndex, 1)[0];
                rules.splice(targetIndex, 0, item);
                renderRules();
            }
            
            targetItem.classList.remove('drag-over');
        }

        function onDragEnd(e) {
            e.target.classList.remove('dragging');
            document.querySelectorAll('.rule-item').forEach(el => el.classList.remove('drag-over'));
            draggedIndex = null;
        }

        function moveRule(index, direction) {
            const newIndex = index + direction;
            if (newIndex < 0 || newIndex >= rules.length) return;
            
            const item = rules.splice(index, 1)[0];
            rules.splice(newIndex, 0, item);
            renderRules();
        }

        function addRule() {
            const rule = {
                rule_type: document.getElementById('rule_type').value,
                pattern: document.getElementById('pattern').value,
                replacement: document.getElementById('replacement').value,
                index: parseInt(document.getElementById('index').value) || 0,
                enabled: true,
                description: document.getElementById('description').value
            };

            rules.push(rule);
            renderRules();

            document.getElementById('pattern').value = '';
            document.getElementById('replacement').value = '';
            document.getElementById('description').value = '';
        }

        function removeRule(index) {
            rules.splice(index, 1);
            renderRules();
        }

        async function previewTransform() {
            const testKeys = prompt("请输入测试键名（多个用逗号分隔）:\\n例如: lt.sc.20251_M4102_ZZT,lt.sc.20251_M4102_CYBJ");
            if (!testKeys) return;

            const keys = testKeys.split(',').map(k => k.trim());

            const response = await fetch('/api/transform/preview', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    rules: rules,
                    test_keys: keys
                })
            });

            const result = await response.json();
            if (result.success) {
                let html = '<h4>转换预览:</h4><table style="width: 100%; border-collapse: collapse;">';
                html += '<tr style="background: #f0f0f0;"><th style="padding: 8px; text-align: left;">原始键名</th><th style="padding: 8px; text-align: left;">转换后</th></tr>';

                for (const [original, transformed] of Object.entries(result.data)) {
                    html += '<tr><td style="padding: 8px; border-bottom: 1px solid #ddd;">' + original + '</td><td style="padding: 8px; border-bottom: 1px solid #ddd;">' + transformed + '</td></tr>';
                }
                html += '</table>';

                document.getElementById('preview').innerHTML = html;
                document.getElementById('preview').style.display = 'block';
            } else {
                showResult(result);
            }
        }

        async function saveRules() {
            const config = {
                source: currentSource,
                enabled: document.getElementById('enabled').value === 'true',
                rules: rules
            };

            const response = await fetch('/api/transform/rules', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });

            const result = await response.json();
            showResult(result);
        }

        function showResult(result) {
            const div = document.getElementById('result');
            if (result.success) {
                div.innerHTML = '<div class="success">✓ ' + (result.message || '操作成功') + '</div>';
            } else {
                div.innerHTML = '<div class="error">✗ ' + (result.message || '操作失败') + '</div>';
            }
        }

        loadSources();
        loadRules();
    </script>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) handleMonitorPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>监控配置 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 800px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        h1 { color: #333; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; color: #555; }
        input, select, textarea { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        button { background: #4CAF50; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; margin-right: 10px; }
        button:hover { background: #45a049; }
        .test { background: #2196F3; }
        .back { background: #666; border: 2px solid #333; color: white; padding: 8px 16px; text-decoration: none; border-radius: 4px; display: inline-block; }
        .back:hover { background: #555; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔔 监控配置</h1>
        <a href="/" class="back">← 返回首页</a>

        <form id="webhookForm">
            <div class="form-group">
                <label>启用Webhook预警</label>
                <select id="enabled" name="enabled">
                    <option value="false">否</option>
                    <option value="true">是</option>
                </select>
            </div>
            <div class="form-group">
                <label>Webhook URL</label>
                <input type="text" id="url" name="url" placeholder="例如: https://example.com/webhook">
            </div>
            <div class="form-group">
                <label>触发事件（逗号分隔）</label>
                <textarea id="events" name="events" rows="3" placeholder="mqtt_error,http_error,collect_error"></textarea>
            </div>

            <button type="button" onclick="saveWebhook()">💾 保存配置</button>
            <button type="button" class="test" onclick="testWebhook()">🧪 测试发送</button>
        </form>

        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        async function loadWebhook() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success && data.data.webhook) {
                const webhook = data.data.webhook;
                document.getElementById('enabled').value = webhook.enabled?.toString() || 'false';
                document.getElementById('url').value = webhook.url || '';
                document.getElementById('events').value = (webhook.events || []).join(',');
            }
        }

        async function saveWebhook() {
            const eventsStr = document.getElementById('events').value;
            const events = eventsStr ? eventsStr.split(',').map(e => e.trim()) : [];

            const webhook = {
                enabled: document.getElementById('enabled').value === 'true',
                url: document.getElementById('url').value,
                events: events
            };

            const response = await fetch('/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ webhook })
            });

            const result = await response.json();
            showResult(result);
        }

        async function testWebhook() {
            const response = await fetch('/api/webhook/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    url: document.getElementById('url').value,
                    event: 'test',
                    message: '这是一条测试消息'
                })
            });

            const result = await response.json();
            showResult(result);
        }

        function showResult(result) {
            const div = document.getElementById('result');
            if (result.success) {
                div.innerHTML = '<div class="success">✓ ' + (result.message || '操作成功') + '</div>';
            } else {
                div.innerHTML = '<div class="error">✗ ' + (result.message || '操作失败') + '</div>';
            }
        }

        loadWebhook();
    </script>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) renderHTML(w http.ResponseWriter, html string) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	io.WriteString(w, html)
}

func (ws *WebServer) handleGetConfig(w http.ResponseWriter, r *http.Request) {
	config := ws.configManager.Load(ws.configPath)
	if config == nil {
		ws.writeJSON(w, false, "无法加载配置", nil) // 返回错误信息
		return
	}
	ws.writeJSON(w, true, "配置加载成功", config)
}

func (ws *WebServer) handleUpdateConfig(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil) // 返回错误信息
		return
	}

	var updates map[string]interface{}
	if err := json.Unmarshal(body, &updates); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil) // 返回错误信息
		return
	}

	// 加载现有配置
	config := ws.configManager.Load(ws.configPath)
	if config == nil {
		config = &AppConfig{}
	}

	// 更新配置
	if err := ws.updateConfigFromMap(config, updates); err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("更新配置失败: %v", err), nil) // 返回错误信息
		return
	}

	// 保存配置
	if err := ws.configManager.Save(ws.configPath, config); err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("保存配置失败: %v", err), nil)
		return
	}

	if ws.collector != nil {
		ws.collector.Reload(config)
	}

	ws.writeJSON(w, true, "配置已更新", nil)
}

func (ws *WebServer) handleValidateConfig(w http.ResponseWriter, r *http.Request) {
	config := ws.configManager.Load(ws.configPath)
	if config == nil {
		ws.writeJSON(w, false, "无法加载配置", nil)
		return
	}

	errors := []string{}
	warnings := []string{}

	// 验证MQTT配置
	if config.MqttConfig != nil && config.MqttConfig.Enabled {
		if config.MqttConfig.Broker == "" {
			errors = append(errors, "MQTT服务器地址不能为空")
		}
		if config.MqttConfig.Port <= 0 || config.MqttConfig.Port > 65535 {
			errors = append(errors, "MQTT端口无效")
		}
		if config.MqttConfig.Topic == "" {
			errors = append(errors, "MQTT主题不能为空")
		}
	}

	for _, httpConfig := range config.HttpConfigs {
		if httpConfig.Enabled {
			if httpConfig.Url == "" {
				errors = append(errors, fmt.Sprintf("HTTP[%s] URL不能为空", httpConfig.Name))
			}
			if httpConfig.Timeout <= 0 {
				errors = append(errors, fmt.Sprintf("HTTP[%s]超时时间无效", httpConfig.Name))
			}
		}
	}

	if len(config.Tasks) == 0 {
		warnings = append(warnings, "未配置任何任务")
	}

	result := map[string]interface{}{
		"errors":   errors,
		"warnings": warnings,
		"valid":    len(errors) == 0,
	}

	if len(errors) > 0 {
		ws.writeJSON(w, false, "配置验证失败", result)
	} else if len(warnings) > 0 {
		ws.writeJSON(w, true, "配置验证通过（有警告）", result)
	} else {
		ws.writeJSON(w, true, "配置验证通过", result)
	}
}

func (ws *WebServer) handleMqttTest(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil)
		return
	}

	var mqttConfig MqttConfig
	if err := json.Unmarshal(body, &mqttConfig); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	// 测试MQTT连接
	err = testMqttConnection(&mqttConfig)
	if err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("MQTT连接失败: %v", err), nil)
		return
	}

	ws.writeJSON(w, true, "MQTT连接成功", nil)
}

func (ws *WebServer) handleRtdbTest(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil)
		return
	}

	var rtdbConfig RtdbConfig
	if err := json.Unmarshal(body, &rtdbConfig); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	testMessage := map[string]interface{}{
		"timestamp": time.Now().Format(time.RFC3339),
		"values": map[string]interface{}{
			"test_tag": 123.45,
		},
		"metadata": map[string]map[string]interface{}{
			"test_tag": {"quality": 192},
		},
	}

	client := NewRtdbClient(&rtdbConfig)
	if err := client.Connect(); err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("RTDB初始化失败: %v", err), nil)
		return
	}

	if err := client.Send(testMessage, "测试"); err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("RTDB发送失败: %v", err), nil)
		return
	}

	ws.writeJSON(w, true, "RTDB测试数据已发送", nil)
}

func (ws *WebServer) handleHttpTest(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil)
		return
	}

	var httpConfig HttpConfig
	if err := json.Unmarshal(body, &httpConfig); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	// 测试HTTP请求
	err = testHttpConnection(&httpConfig)
	if err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("HTTP请求失败: %v", err), nil)
		return
	}

	ws.writeJSON(w, true, "HTTP请求成功", nil)
}

func (ws *WebServer) handleTransformPreview(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil)
		return
	}

	var request struct {
		Rules    []TransformRule `json:"rules"`
		TestKeys []string        `json:"test_keys"`
	}

	if err := json.Unmarshal(body, &request); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	// 创建转换器
	transformer := NewKeyTransformer()
	transformer.ImportRules(request.Rules)

	// 预览转换
	result := make(map[string]string)
	for _, key := range request.TestKeys {
		result[key] = transformer.Transform(key)
	}

	ws.writeJSON(w, true, "转换预览", result)
}

func (ws *WebServer) handleGetTransformRules(w http.ResponseWriter, r *http.Request) {
	source := r.URL.Query().Get("source")
	fileName := "transform.json"
	if source != "" {
		fileName = "transform_" + source + ".json"
	}

	data, err := os.ReadFile(fileName)
	if err != nil {
		ws.writeJSON(w, false, "读取规则文件失败: "+err.Error(), nil)
		return
	}

	var config map[string]interface{}
	if err := json.Unmarshal(data, &config); err != nil {
		ws.writeJSON(w, false, "解析规则文件失败: "+err.Error(), nil)
		return
	}

	ws.writeJSON(w, true, "规则加载成功", config)
}

func (ws *WebServer) handleUpdateTransformRules(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil)
		return
	}

	var requestData map[string]interface{}
	if err := json.Unmarshal(body, &requestData); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	source, _ := requestData["source"].(string)
	fileName := "transform.json"
	if source != "" {
		fileName = "transform_" + source + ".json"
	}

	delete(requestData, "source")

	data, err := json.MarshalIndent(requestData, "", "  ")
	if err != nil {
		ws.writeJSON(w, false, "序列化失败", nil)
		return
	}

	if err := os.WriteFile(fileName, data, 0644); err != nil {
		ws.writeJSON(w, false, "保存规则文件失败: "+err.Error(), nil)
		return
	}

	ws.writeJSON(w, true, "规则已保存到 transform.json", nil)
}

func (ws *WebServer) handleTransformDebug(w http.ResponseWriter, r *http.Request) {
	debugInfo := map[string]interface{}{
		"transformer_enabled": ws.transformer.IsEnabled(),
		"rule_count":          len(ws.transformer.ExportRules()),
		"rules":               ws.transformer.ExportRules(),
	}

	data, err := os.ReadFile("transform.json")
	if err != nil {
		debugInfo["file_error"] = err.Error()
	} else {
		debugInfo["file_content"] = string(data)
	}

	testKeys := []string{
		"Channel2.Device1.Value",
		"lt.sc.20251_M4102_ZZT",
		"10011_Channel2.Device1",
	}

	testResults := make(map[string]string)
	for _, key := range testKeys {
		testResults[key] = ws.transformer.TestTransform(key)
	}
	debugInfo["test_results"] = testResults

	ws.writeJSON(w, true, "调试信息", debugInfo)
}

// 辅助函数

func (ws *WebServer) updateConfigFromMap(config *AppConfig, updates map[string]interface{}) error {
	if mainData, ok := updates["main"].(map[string]interface{}); ok {
		if title, ok := mainData["title"].(string); ok {
			config.Title = title
		}
		if opcServer, ok := mainData["opc_server"].(string); ok {
			config.OpcServer = opcServer
		}
	}

	if mqttData, ok := updates["mqtt"].(map[string]interface{}); ok {
		if config.MqttConfig == nil {
			config.MqttConfig = &MqttConfig{}
		}
		if enabled, ok := mqttData["enabled"].(bool); ok {
			config.MqttConfig.Enabled = enabled
		}
		if broker, ok := mqttData["broker"].(string); ok {
			config.MqttConfig.Broker = broker
		}
		if port, ok := mqttData["port"].(float64); ok {
			config.MqttConfig.Port = int(port)
		}
		if topic, ok := mqttData["topic"].(string); ok {
			config.MqttConfig.Topic = topic
		}
		if username, ok := mqttData["username"].(string); ok {
			config.MqttConfig.Username = username
		}
		if password, ok := mqttData["password"].(string); ok {
			config.MqttConfig.Password = password
		}
		if clientId, ok := mqttData["client_id"].(string); ok {
			config.MqttConfig.ClientId = clientId
		}
		if qos, ok := mqttData["qos"].(float64); ok {
			config.MqttConfig.Qos = int(qos)
		}
		if retain, ok := mqttData["retain"].(bool); ok {
			config.MqttConfig.Retain = retain
		}
	}

	if httpConfigsData, ok := updates["http_configs"].([]interface{}); ok {
		config.HttpConfigs = make([]*HttpConfig, 0)
		for _, item := range httpConfigsData {
			if httpData, ok := item.(map[string]interface{}); ok {
				httpConfig := &HttpConfig{}
				if name, ok := httpData["name"].(string); ok {
					httpConfig.Name = name
				}
				if enabled, ok := httpData["enabled"].(bool); ok {
					httpConfig.Enabled = enabled
				}
				if url, ok := httpData["url"].(string); ok {
					httpConfig.Url = url
				}
				if method, ok := httpData["method"].(string); ok {
					httpConfig.Method = method
				}
				if timeout, ok := httpData["timeout"].(float64); ok {
					httpConfig.Timeout = int(timeout)
				}
				config.HttpConfigs = append(config.HttpConfigs, httpConfig)
			}
		}
	}

	if mqttData, ok := updates["mqtt"].(map[string]interface{}); ok {
		if config.MqttConfig == nil {
			config.MqttConfig = &MqttConfig{}
		}
		if enabled, ok := mqttData["enabled"].(bool); ok {
			config.MqttConfig.Enabled = enabled
		}
		if broker, ok := mqttData["broker"].(string); ok {
			config.MqttConfig.Broker = broker
		}
		if port, ok := mqttData["port"].(float64); ok {
			config.MqttConfig.Port = int(port)
		}
		if topic, ok := mqttData["topic"].(string); ok {
			config.MqttConfig.Topic = topic
		}
		if clientId, ok := mqttData["client_id"].(string); ok {
			config.MqttConfig.ClientId = clientId
		}
		if qos, ok := mqttData["qos"].(float64); ok {
			config.MqttConfig.Qos = int(qos)
		}
		if retain, ok := mqttData["retain"].(bool); ok {
			config.MqttConfig.Retain = retain
		}
		if format, ok := mqttData["format"].(string); ok {
			config.MqttConfig.Format = format
		}
		if jsTransform, ok := mqttData["js_transform"].(string); ok {
			config.MqttConfig.JsTransform = jsTransform
		}
		if split, ok := mqttData["split"].(bool); ok {
			config.MqttConfig.Split = split
		}
	}

	if rtdbData, ok := updates["rtdb"].(map[string]interface{}); ok {
		if config.RtdbConfig == nil {
			config.RtdbConfig = &RtdbConfig{}
		}
		if enabled, ok := rtdbData["enabled"].(bool); ok {
			config.RtdbConfig.Enabled = enabled
		}
		if host, ok := rtdbData["host"].(string); ok {
			config.RtdbConfig.Host = host
		}
		if port, ok := rtdbData["port"].(float64); ok {
			config.RtdbConfig.Port = int(port)
		}
		if format, ok := rtdbData["format"].(string); ok {
			config.RtdbConfig.Format = format
		}
	}

	if webhookData, ok := updates["webhook"].(map[string]interface{}); ok {
		if config.WebhookConfig == nil {
			config.WebhookConfig = &WebhookConfig{}
		}
		if enabled, ok := webhookData["enabled"].(bool); ok {
			config.WebhookConfig.Enabled = enabled
		}
		if url, ok := webhookData["url"].(string); ok {
			config.WebhookConfig.Url = url
		}
		if events, ok := webhookData["events"].([]interface{}); ok {
			config.WebhookConfig.Events = make([]string, len(events))
			for i, e := range events {
				config.WebhookConfig.Events[i] = e.(string)
			}
		}
	}

	if tasksData, ok := updates["tasks"].([]interface{}); ok {
		config.Tasks = make([]*TaskConfig, 0)
		for _, item := range tasksData {
			if taskData, ok := item.(map[string]interface{}); ok {
				task := &TaskConfig{}
				if enabled, ok := taskData["enabled"].(bool); ok {
					task.Enabled = enabled
				}
				if httpSource, ok := taskData["http_source"].(string); ok {
					task.HttpSource = httpSource
				}
				if interval, ok := taskData["job_interval_second"].(float64); ok {
					task.JobIntervalSecond = int(interval)
				}
				if tagsData, ok := taskData["tags"].([]interface{}); ok {
					for _, tagItem := range tagsData {
						if tagData, ok := tagItem.(map[string]interface{}); ok {
							tag := &TagMapping{}
							if opcTag, ok := tagData["opc_tag"].(string); ok {
								tag.OpcTag = opcTag
							}
							if dbName, ok := tagData["db_name"].(string); ok {
								tag.DbName = dbName
							}
							task.Tags = append(task.Tags, tag)
						}
					}
				}
				config.Tasks = append(config.Tasks, task)
			}
		}
	}

	return nil
}

func testMqttConnection(config *MqttConfig) error {
	if config.Broker == "" {
		return fmt.Errorf("MQTT服务器地址不能为空")
	}
	if config.Port <= 0 || config.Port > 65535 {
		return fmt.Errorf("MQTT端口无效")
	}
	return nil
}

func testHttpConnection(config *HttpConfig) error {
	if config.Url == "" {
		return fmt.Errorf("HTTP URL不能为空")
	}

	client := &http.Client{Timeout: time.Duration(config.Timeout) * time.Millisecond}
	req, err := http.NewRequest(config.Method, config.Url, nil)
	if err != nil {
		return err
	}

	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		return nil
	}

	return fmt.Errorf("HTTP状态码: %d", resp.StatusCode)
}

func (ws *WebServer) handleWebhookTest(w http.ResponseWriter, r *http.Request) {
	body, err := io.ReadAll(r.Body)
	if err != nil {
		ws.writeJSON(w, false, "读取请求失败", nil)
		return
	}

	var request struct {
		Url     string `json:"url"`
		Event   string `json:"event"`
		Message string `json:"message"`
	}

	if err := json.Unmarshal(body, &request); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	if request.Url == "" {
		ws.writeJSON(w, false, "Webhook URL不能为空", nil)
		return
	}

	payload := map[string]interface{}{
		"event":   request.Event,
		"message": request.Message,
		"source":  "opc_collector",
	}

	jsonData, _ := json.Marshal(payload)
	resp, err := http.Post(request.Url, "application/json", strings.NewReader(string(jsonData)))
	if err != nil {
		ws.writeJSON(w, false, fmt.Sprintf("发送失败: %v", err), nil)
		return
	}
	defer resp.Body.Close()

	if resp.StatusCode >= 200 && resp.StatusCode < 300 {
		ws.writeJSON(w, true, "Webhook测试成功", nil)
	} else {
		ws.writeJSON(w, false, fmt.Sprintf("Webhook返回状态码: %d", resp.StatusCode), nil)
	}
}

func (ws *WebServer) handleTasksPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>采集任务配置 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; background: #f5f5f5; }
        .container { max-width: 1200px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; }
        h1 { color: #333; }
        .task-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 15px; margin-top: 20px; }
        .task-card { background: #f9f9f9; border: 1px solid #ddd; border-radius: 6px; padding: 15px; position: relative; }
        .task-card.disabled { opacity: 0.6; }
        .task-name { font-size: 16px; font-weight: bold; color: #333; margin-bottom: 10px; }
        .task-info { color: #666; font-size: 14px; line-height: 1.6; }
        .task-actions { margin-top: 10px; display: flex; gap: 8px; }
        .btn { padding: 6px 12px; border: none; border-radius: 4px; cursor: pointer; font-size: 13px; }
        .btn-primary { background: #4CAF50; color: white; }
        .btn-danger { background: #f44336; color: white; }
        .btn-edit { background: #2196F3; color: white; }
        .btn:hover { opacity: 0.85; }
        .add-task { background: #4CAF50; color: white; padding: 10px 20px; border: none; border-radius: 4px; cursor: pointer; margin-top: 15px; }
        .modal { display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.5); z-index: 1000; }
        .modal-content { background: white; margin: 5% auto; padding: 20px; border-radius: 8px; width: 90%; max-width: 500px; }
        .form-group { margin-bottom: 15px; }
        label { display: block; margin-bottom: 5px; font-weight: bold; color: #555; }
        input, select { width: 100%; padding: 8px; border: 1px solid #ddd; border-radius: 4px; box-sizing: border-box; }
        .modal-actions { display: flex; gap: 10px; justify-content: flex-end; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
        .badge { display: inline-block; padding: 2px 8px; border-radius: 12px; font-size: 12px; color: white; }
        .badge-on { background: #4CAF50; }
        .badge-off { background: #999; }
    </style>
</head>
<body>
    <div class="container">
        <a href="/" class="back">← 返回首页</a>
        <h1>📋 采集任务配置</h1>
        <p>配置采集任务，绑定数据源</p>
        <div id="result"></div>

        <div class="task-grid" id="taskGrid"></div>

        <button class="add-task" onclick="openModal()">+ 添加任务</button>

        <!-- 添加/编辑弹窗 -->
        <div class="modal" id="taskModal">
            <div class="modal-content">
                <h2 id="modalTitle">添加任务</h2>
                <div class="form-group">
                    <label>启用</label>
                    <select id="taskEnabled">
                        <option value="true">启用</option>
                        <option value="false">禁用</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>采集间隔(秒)</label>
                    <input type="number" id="taskInterval" value="1" min="1">
                </div>
                <div class="form-group">
                    <label>绑定数据源</label>
                    <select id="taskSource"></select>
                </div>
                <div class="modal-actions">
                    <button class="btn" onclick="closeModal()" style="background:#666;color:white;">取消</button>
                    <button class="btn btn-primary" onclick="saveTask()">保存</button>
                </div>
            </div>
        </div>
    </div>

    <script>
        let tasks = [];
        let httpConfigs = [];
        let editingTask = '';

        async function loadData() {
            try {
                const resp = await fetch('/api/config');
                const data = await resp.json();
                if (data.success) {
                    tasks = data.data.tasks || [];
                    httpConfigs = data.data.http_configs || [];
                    renderTasks();
                }
            } catch (e) {
                showResult(false, '加载配置失败: ' + e.message);
            }
        }

        function renderTasks() {
            const grid = document.getElementById('taskGrid');
            if (tasks.length === 0) {
                grid.innerHTML = '<div style="color:#999;padding:20px;">暂无任务，请点击下方按钮添加</div>';
                return;
            }

            grid.innerHTML = '';
            tasks.forEach((task, index) => {
                const enabled = task.enabled;
                const source = task.http_source || '数据源1';
                const interval = task.job_interval_second || 1;
                const card = document.createElement('div');
                card.className = 'task-card' + (enabled ? '' : ' disabled');
                card.innerHTML =
                    '<div class="task-name">任务' + (index + 1) + ' <span class="badge ' + (enabled ? 'badge-on' : 'badge-off') + '">' + (enabled ? '启用' : '禁用') + '</span></div>' +
                    '<div class="task-info">' +
                    '数据源: ' + source + '<br>' +
                    '采集间隔: ' + interval + '秒<br>' +
                    '标签数: ' + (task.tags ? task.tags.length : 0) + '<br>' +
                    '</div>' +
                    '<div class="task-actions">' +
                    '<button class="btn btn-edit" onclick="editTask(' + index + ')">编辑</button>' +
                    '<button class="btn btn-danger" onclick="deleteTask(' + index + ')">删除</button>' +
                    '</div>';
                grid.appendChild(card);
            });
        }

        function openModal(taskIndex) {
            editingTask = taskIndex !== undefined ? taskIndex : -1;
            document.getElementById('modalTitle').textContent = taskIndex !== undefined ? '编辑任务' : '添加任务';

            // 填充数据源下拉
            const select = document.getElementById('taskSource');
            select.innerHTML = '';
            httpConfigs.forEach(config => {
                const opt = document.createElement('option');
                opt.value = config.name || config.url;
                opt.textContent = config.name || config.url;
                select.appendChild(opt);
            });

            if (taskIndex !== undefined && tasks[taskIndex]) {
                const task = tasks[taskIndex];
                document.getElementById('taskEnabled').value = task.enabled ? 'true' : 'false';
                document.getElementById('taskInterval').value = task.job_interval_second || 1;
                select.value = task.http_source || (httpConfigs[0] ? (httpConfigs[0].name || httpConfigs[0].url) : '');
            } else {
                document.getElementById('taskEnabled').value = 'true';
                document.getElementById('taskInterval').value = 1;
            }

            document.getElementById('taskModal').style.display = 'block';
        }

        function closeModal() {
            document.getElementById('taskModal').style.display = 'none';
            editingTask = -1;
        }

        async function saveTask() {
            const enabled = document.getElementById('taskEnabled').value === 'true';
            const interval = parseInt(document.getElementById('taskInterval').value) || 1;
            const source = document.getElementById('taskSource').value;

            const task = {
                enabled: enabled,
                http_source: source,
                job_interval_second: interval,
                tags: []
            };

            if (editingTask >= 0) {
                task.tags = tasks[editingTask].tags || [];
                tasks[editingTask] = task;
            } else {
                tasks.push(task);
            }

            try {
                const resp = await fetch('/api/config');
                const fullConfig = await resp.json();
                if (fullConfig.success) {
                    fullConfig.data.tasks = tasks;
                    const saveResp = await fetch('/api/config', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(fullConfig.data)
                    });
                    const result = await saveResp.json();
                    showResult(result.success, result.message);
                    if (result.success) {
                        closeModal();
                        loadData();
                    }
                }
            } catch (e) {
                showResult(false, '保存失败: ' + e.message);
            }
        }

        function editTask(index) {
            openModal(index);
        }

        async function deleteTask(index) {
            if (!confirm('确认删除此任务？')) return;
            tasks.splice(index, 1);

            try {
                const resp = await fetch('/api/config');
                const fullConfig = await resp.json();
                if (fullConfig.success) {
                    fullConfig.data.tasks = tasks;
                    const saveResp = await fetch('/api/config', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(fullConfig.data)
                    });
                    const result = await saveResp.json();
                    showResult(result.success, result.message);
                    if (result.success) loadData();
                }
            } catch (e) {
                showResult(false, '删除失败: ' + e.message);
            }
        }

        function showResult(success, message) {
            const div = document.getElementById('result');
            div.innerHTML = '<div class="' + (success ? 'success' : 'error') + '">' + (success ? '✓ ' : '✗ ') + message + '</div>';
            setTimeout(() => div.innerHTML = '', 3000);
        }

        loadData();
    </script>
</body>
</html>
	`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) writeJSON(w http.ResponseWriter, success bool, message string, data interface{}) {
	response := map[string]interface{}{
		"success":   success,
		"message":   message,
		"data":      data,
		"timestamp": time.Now().Format(time.RFC3339),
	}

	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	json.NewEncoder(w).Encode(response)
}
