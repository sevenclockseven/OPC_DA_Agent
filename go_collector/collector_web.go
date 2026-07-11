package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"time"

	"github.com/gorilla/mux"
)

// WebServer Web配置服务器
type WebServer struct {
	configPath    string
	configManager *ConfigManager
	transformer   *KeyTransformer
}

// NewWebServer 创建Web服务器
func NewWebServer(configPath string) *WebServer {
	return &WebServer{
		configPath:    configPath,
		configManager: NewConfigManager(),
		transformer:   NewKeyTransformer(),
	}
}

// Start 启动Web服务器
func (ws *WebServer) Start(port int) error {
	r := mux.NewRouter()

	// 静态文件服务
	r.PathPrefix("/static/").Handler(http.StripPrefix("/static/", http.FileServer(http.Dir("./web/static"))))

	// Web页面
	r.HandleFunc("/", ws.handleHome).Methods("GET")
	r.HandleFunc("/web/config", ws.handleConfigPage).Methods("GET")
	r.HandleFunc("/web/mqtt", ws.handleMqttPage).Methods("GET")
	r.HandleFunc("/web/http", ws.handleHttpPage).Methods("GET")
	r.HandleFunc("/web/transform", ws.handleTransformPage).Methods("GET")

	// API接口
	r.HandleFunc("/api/config", ws.handleGetConfig).Methods("GET")
	r.HandleFunc("/api/config", ws.handleUpdateConfig).Methods("POST")
	r.HandleFunc("/api/config/validate", ws.handleValidateConfig).Methods("POST")
	r.HandleFunc("/api/mqtt/test", ws.handleMqttTest).Methods("POST")
	r.HandleFunc("/api/http/test", ws.handleHttpTest).Methods("POST")
	r.HandleFunc("/api/transform/preview", ws.handleTransformPreview).Methods("POST")
	r.HandleFunc("/api/transform/rules", ws.handleGetTransformRules).Methods("GET")
	r.HandleFunc("/api/transform/rules", ws.handleUpdateTransformRules).Methods("POST")
	r.HandleFunc("/api/transform/debug", ws.handleTransformDebug).Methods("GET")

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
            <a href="/web/config" class="menu-item">
                <h3>⚙️ 配置管理</h3>
                <p>查看和编辑主配置</p>
            </a>
            <a href="/web/mqtt" class="menu-item">
                <h3>📡 MQTT配置</h3>
                <p>配置MQTT服务器</p>
            </a>
            <a href="/web/http" class="menu-item">
                <h3>🌐 HTTP配置</h3>
                <p>配置HTTP服务器</p>
            </a>
            <a href="/web/transform" class="menu-item">
                <h3>🔄 键名转换</h3>
                <p>配置转换规则</p>
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

func (ws *WebServer) handleConfigPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>配置管理 - OPC DA Collector</title>
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
        .section { background: #f9f9f9; padding: 15px; margin: 15px 0; border-radius: 6px; border-left: 4px solid #4CAF50; }
        .section h3 { margin-top: 0; color: #4CAF50; }
        .back { background: #666; }
        .test { background: #2196F3; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
    </style>
</head>
<body>
    <div class="container">
        <h1>⚙️ 配置管理</h1>
        <a href="/" class="back">← 返回首页</a>

        <form id="configForm">
            <div class="section">
                <h3>主配置</h3>
                <div class="form-group">
                    <label>系统标题</label>
                    <input type="text" id="title" name="title" placeholder="例如: 辽塔172.16.32.245烧成">
                </div>
                <div class="form-group">
                    <label>调试模式</label>
                    <select id="debug" name="debug">
                        <option value="false">关闭</option>
                        <option value="true">开启</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>OPC服务器主机</label>
                    <input type="text" id="opc_host" name="opc_host" placeholder="例如: 172.16.32.98">
                </div>
                <div class="form-group">
                    <label>OPC服务器名称</label>
                    <input type="text" id="opc_server" name="opc_server" placeholder="例如: KEPware.KEPServerEx.V4">
                </div>
            </div>

            <div class="section">
                <h3>实时数据库配置</h3>
                <div class="form-group">
                    <label>RTDB主机（多个用逗号分隔）</label>
                    <input type="text" id="rtdb_host" name="rtdb_host" placeholder="例如: 172.16.32.98">
                </div>
                <div class="form-group">
                    <label>RTDB端口（多个用逗号分隔）</label>
                    <input type="text" id="rtdb_port" name="rtdb_port" placeholder="例如: 8100">
                </div>
            </div>

            <div class="section">
                <h3>监控配置</h3>
                <div class="form-group">
                    <label>监控模式</label>
                    <select id="monitor_mode" name="monitor_mode">
                        <option value="email">Email</option>
                        <option value="web">Web</option>
                    </select>
                </div>
                <div class="form-group">
                    <label>告警邮箱</label>
                    <input type="email" id="monitor_email" name="monitor_email" placeholder="例如: 2018241195@qq.com">
                </div>
                <div class="form-group">
                    <label>监控IP</label>
                    <input type="text" id="monitor_ip" name="monitor_ip" placeholder="例如: 172.16.32.245">
                </div>
            </div>

            <button type="button" onclick="saveConfig()">💾 保存配置</button>
            <button type="button" class="test" onclick="validateConfig()">✅ 验证配置</button>
            <button type="button" onclick="exportConfig('ini')">📥 导出INI</button>
            <button type="button" onclick="exportConfig('json')">📥 导出JSON</button>
        </form>

        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        async function loadConfig() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success) {
                const config = data.data;
                document.getElementById('title').value = config.main?.title || '';
                document.getElementById('debug').value = config.main?.debug?.toString() || 'false';
                document.getElementById('opc_host').value = config.main?.opc_host || '';
                document.getElementById('opc_server').value = config.main?.opc_server || '';
                document.getElementById('rtdb_host').value = (config.main?.rtdb_host || []).join(',');
                document.getElementById('rtdb_port').value = (config.main?.rtdb_port || []).join(',');
                document.getElementById('monitor_mode').value = config.monitor?.mode || 'email';
                document.getElementById('monitor_email').value = config.monitor?.email || '';
                document.getElementById('monitor_ip').value = config.monitor?.ip || '';
            }
        }

        async function saveConfig() {
            const config = {
                main: {
                    title: document.getElementById('title').value,
                    debug: document.getElementById('debug').value === 'true',
                    opc_host: document.getElementById('opc_host').value,
                    opc_server: document.getElementById('opc_server').value,
                    rtdb_host: document.getElementById('rtdb_host').value.split(',').map(s => s.trim()).filter(s => s),
                    rtdb_port: document.getElementById('rtdb_port').value.split(',').map(s => parseInt(s.trim())).filter(s => s)
                },
                monitor: {
                    mode: document.getElementById('monitor_mode').value,
                    email: document.getElementById('monitor_email').value,
                    ip: document.getElementById('monitor_ip').value
                }
            };

            const response = await fetch('/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });

            const result = await response.json();
            showResult(result);
        }

        async function validateConfig() {
            const response = await fetch('/api/config/validate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: '{}'
            });

            const result = await response.json();
            showResult(result);
        }

        async function exportConfig(format) {
            window.location.href = '/api/config/export?format=' + format;
        }

        function showResult(result) {
            const div = document.getElementById('result');
            if (result.success) {
                div.innerHTML = '<div class="success">✓ ' + (result.message || '操作成功') + '</div>';
            } else {
                div.innerHTML = '<div class="error">✗ ' + (result.message || '操作失败') + '</div>';
            }
        }

        // 页面加载时加载配置
        loadConfig();
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
        .back { background: #666; }
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
                    <option value="true">是</option>
                    <option value="false">否</option>
                </select>
            </div>
            <div class="form-group">
                <label>MQTT服务器地址</label>
                <input type="text" id="broker" name="broker" placeholder="例如: 172.16.32.98">
            </div>
            <div class="form-group">
                <label>端口</label>
                <input type="number" id="port" name="port" value="1883">
            </div>
            <div class="form-group">
                <label>主题</label>
                <input type="text" id="topic" name="topic" placeholder="例如: opc/data" value="opc/data">
            </div>
            <div class="form-group">
                <label>用户名（可选）</label>
                <input type="text" id="username" name="username" placeholder="用户名">
            </div>
            <div class="form-group">
                <label>密码（可选）</label>
                <input type="password" id="password" name="password" placeholder="密码">
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
                    <option value="2">2 - 确保一次</option>
                </select>
            </div>
            <div class="form-group">
                <label>保留消息</label>
                <select id="retain" name="retain">
                    <option value="false">否</option>
                    <option value="true">是</option>
                </select>
            </div>

            <h3>发送格式配置</h3>
            <div class="form-group">
                <label>MQTT发送格式</label>
                <select id="mqtt_format" name="mqtt_format">
                    <option value="full">完整格式（包含timestamp, values, metadata）</option>
                    <option value="flat">扁平格式（仅values）</option>
                    <option value="custom">自定义格式（JS转换）</option>
                </select>
            </div>
            <div class="form-group">
                <label>JS转换代码（仅自定义格式）</label>
                <textarea id="mqtt_js_transform" name="mqtt_js_transform" rows="4" placeholder="function(data) { return data; }"></textarea>
            </div>

            <button type="button" onclick="saveMqtt()">💾 保存配置</button>
            <button type="button" class="test" onclick="testMqtt()">🧪 测试连接</button>
        </form>

        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        async function loadMqtt() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success && data.data.mqtt) {
                const mqtt = data.data.mqtt;
                document.getElementById('enabled').value = mqtt.enabled?.toString() || 'false';
                document.getElementById('broker').value = mqtt.broker || '';
                document.getElementById('port').value = mqtt.port || 1883;
                document.getElementById('topic').value = mqtt.topic || 'opc/data';
                document.getElementById('username').value = mqtt.username || '';
                document.getElementById('password').value = mqtt.password || '';
                document.getElementById('client_id').value = mqtt.client_id || '';
                document.getElementById('qos').value = mqtt.qos?.toString() || '1';
                document.getElementById('retain').value = mqtt.retain?.toString() || 'false';
            }
            if (data.success && data.data.output) {
                const output = data.data.output;
                document.getElementById('mqtt_format').value = output.mqtt_format || 'full';
                document.getElementById('mqtt_js_transform').value = output.mqtt_js_transform || '';
            }
        }

        async function saveMqtt() {
            const mqtt = {
                enabled: document.getElementById('enabled').value === 'true',
                broker: document.getElementById('broker').value,
                port: parseInt(document.getElementById('port').value),
                topic: document.getElementById('topic').value,
                username: document.getElementById('username').value,
                password: document.getElementById('password').value,
                client_id: document.getElementById('client_id').value,
                qos: parseInt(document.getElementById('qos').value),
                retain: document.getElementById('retain').value === 'true'
            };

            const output = {
                mqtt_format: document.getElementById('mqtt_format').value,
                mqtt_js_transform: document.getElementById('mqtt_js_transform').value
            };

            const response = await fetch('/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ mqtt, output })
            });

            const result = await response.json();
            showResult(result);
        }

        async function testMqtt() {
            const response = await fetch('/api/mqtt/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    broker: document.getElementById('broker').value,
                    port: parseInt(document.getElementById('port').value),
                    username: document.getElementById('username').value,
                    password: document.getElementById('password').value
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

        loadMqtt();
    </script>
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
    <title>HTTP配置 - OPC DA Collector</title>
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
        .back { background: #666; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🌐 HTTP配置</h1>
        <a href="/" class="back">← 返回首页</a>

        <form id="httpForm">
            <div class="form-group">
                <label>启用HTTP</label>
                <select id="enabled" name="enabled">
                    <option value="false">否</option>
                    <option value="true">是</option>
                </select>
            </div>
            <div class="form-group">
                <label>HTTP URL</label>
                <input type="text" id="url" name="url" placeholder="例如: http://172.16.32.98:8080/api/data">
            </div>
            <div class="form-group">
                <label>请求方法</label>
                <select id="method" name="method">
                    <option value="POST">POST</option>
                    <option value="GET">GET</option>
                </select>
            </div>
            <div class="form-group">
                <label>用户名（可选）</label>
                <input type="text" id="username" name="username" placeholder="用户名">
            </div>
            <div class="form-group">
                <label>密码（可选）</label>
                <input type="password" id="password" name="password" placeholder="密码">
            </div>
            <div class="form-group">
                <label>超时时间（毫秒）</label>
                <input type="number" id="timeout" name="timeout" value="30000">
            </div>

            <h3>RTDB发送格式配置</h3>
            <div class="form-group">
                <label>RTDB发送格式</label>
                <select id="rtdb_format_type" name="rtdb_format_type">
                    <option value="csv">CSV格式（key,value,quality,timestamp）</option>
                    <option value="json">JSON格式</option>
                    <option value="custom">自定义格式</option>
                </select>
            </div>
            <div class="form-group">
                <label>自定义格式模板</label>
                <textarea id="rtdb_format" name="rtdb_format" rows="3" placeholder="例如: {key},{value},{quality},{timestamp}"></textarea>
            </div>

            <button type="button" onclick="saveHttp()">💾 保存配置</button>
            <button type="button" class="test" onclick="testHttp()">🧪 测试请求</button>
        </form>

        <div id="result" style="margin-top: 20px;"></div>
    </div>

    <script>
        async function loadHttp() {
            const response = await fetch('/api/config');
            const data = await response.json();
            if (data.success && data.data.http) {
                const http = data.data.http;
                document.getElementById('enabled').value = http.enabled?.toString() || 'false';
                document.getElementById('url').value = http.url || '';
                document.getElementById('method').value = http.method || 'POST';
                document.getElementById('timeout').value = http.timeout || 30000;
            }
            if (data.success && data.data.output) {
                const output = data.data.output;
                const rtdbFormat = output.rtdb_format || '';
                if (rtdbFormat === '{key},{value},{quality},{timestamp}') {
                    document.getElementById('rtdb_format_type').value = 'csv';
                } else if (rtdbFormat.startsWith('{')) {
                    document.getElementById('rtdb_format_type').value = 'json';
                } else {
                    document.getElementById('rtdb_format_type').value = 'custom';
                }
                document.getElementById('rtdb_format').value = rtdbFormat;
            }
        }

        async function saveHttp() {
            const http = {
                enabled: document.getElementById('enabled').value === 'true',
                url: document.getElementById('url').value,
                method: document.getElementById('method').value,
                timeout: parseInt(document.getElementById('timeout').value)
            };

            const output = {
                rtdb_format: document.getElementById('rtdb_format').value
            };

            const response = await fetch('/api/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ http, output })
            });

            const result = await response.json();
            showResult(result);
        }

        async function testHttp() {
            const headersStr = document.getElementById('headers').value;
            const headers = {};
            if (headersStr) {
                headersStr.split(';').forEach(pair => {
                    const [key, value] = pair.split(':');
                    if (key && value) {
                        headers[key.trim()] = value.trim();
                    }
                });
            }

            const response = await fetch('/api/http/test', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    url: document.getElementById('url').value,
                    method: document.getElementById('method').value,
                    username: document.getElementById('username').value,
                    password: document.getElementById('password').value,
                    timeout: parseInt(document.getElementById('timeout').value),
                    headers: headers
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

        loadHttp();
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
        .back { background: #666; }
        .success { color: green; font-weight: bold; }
        .error { color: red; font-weight: bold; }
        .rule-item { background: #f9f9f9; padding: 10px; margin: 10px 0; border-radius: 4px; border-left: 4px solid #4CAF50; cursor: move; }
        .rule-item.dragging { opacity: 0.5; border-left-color: #2196F3; }
        .rule-item.drag-over { border-top: 2px solid #2196F3; }
        .preview { background: #e3f2fd; padding: 15px; border-radius: 6px; margin-top: 20px; }
        .rule-buttons { display: inline-block; margin-left: 10px; }
        .rule-buttons button { padding: 4px 8px; margin-right: 5px; font-size: 12px; }
    </style>
</head>
<body>
    <div class="container">
        <h1>🔄 键名转换规则</h1>
        <a href="/" class="back">← 返回首页</a>

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

        async function loadRules() {
            const response = await fetch('/api/transform/rules');
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

            // 清空表单
            document.getElementById('pattern').value = '';
            document.getElementById('replacement').value = '';
            document.getElementById('description').value = '';
        }

        function removeRule(index) {
            rules.splice(index, 1);
            renderRules();
        }

        async function previewTransform() {
            const testKeys = prompt("请输入测试键名（多个用逗号分隔）:\n例如: lt.sc.20251_M4102_ZZT,lt.sc.20251_M4102_CYBJ");
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

        loadRules();
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
		ws.writeJSON(w, false, fmt.Sprintf("保存配置失败: %v", err), nil) // 返回错误信息
		return
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

	// 验证HTTP配置
	if config.HttpConfig != nil && config.HttpConfig.Enabled {
		if config.HttpConfig.Url == "" {
			errors = append(errors, "HTTP URL不能为空")
		}
		if config.HttpConfig.Timeout <= 0 {
			errors = append(errors, "HTTP超时时间无效")
		}
	}

	// 验证任务配置
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
	data, err := os.ReadFile("transform.json")
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

	var config map[string]interface{}
	if err := json.Unmarshal(body, &config); err != nil {
		ws.writeJSON(w, false, "JSON解析失败", nil)
		return
	}

	data, err := json.MarshalIndent(config, "", "  ")
	if err != nil {
		ws.writeJSON(w, false, "序列化失败", nil)
		return
	}

	if err := os.WriteFile("transform.json", data, 0644); err != nil {
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

	// 处理HTTP配置
	if httpData, ok := updates["http"].(map[string]interface{}); ok {
		if config.HttpConfig == nil {
			config.HttpConfig = &HttpConfig{}
		}
		if enabled, ok := httpData["enabled"].(bool); ok {
			config.HttpConfig.Enabled = enabled
		}
		if url, ok := httpData["url"].(string); ok {
			config.HttpConfig.Url = url
		}
		if method, ok := httpData["method"].(string); ok {
			config.HttpConfig.Method = method
		}
		if timeout, ok := httpData["timeout"].(float64); ok {
			config.HttpConfig.Timeout = int(timeout)
		}
	}

	if outputData, ok := updates["output"].(map[string]interface{}); ok {
		if config.OutputConfig == nil {
			config.OutputConfig = &OutputConfig{}
		}
		if mqttFormat, ok := outputData["mqtt_format"].(string); ok {
			config.OutputConfig.MqttFormat = mqttFormat
		}
		if rtdbFormat, ok := outputData["rtdb_format"].(string); ok {
			config.OutputConfig.RtdbFormat = rtdbFormat
		}
		if mqttJsTransform, ok := outputData["mqtt_js_transform"].(string); ok {
			config.OutputConfig.MqttJsTransform = mqttJsTransform
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
