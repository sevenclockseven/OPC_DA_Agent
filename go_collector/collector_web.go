package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"strconv"
	"strings"
	"sync/atomic"
	"time"

	"github.com/gorilla/mux"
	mqtt "github.com/eclipse/paho.mqtt.golang"
)

type WebServer struct {
	configPath    string
	configManager *ConfigManager
	transformer   *KeyTransformer
	collector     *Collector
	webToken      atomic.Value // string；middleware 每请求读与配置热更写并发，需原子存取
	webPort       int
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
	ws.webPort = port
	if cfg := ws.configManager.Load(ws.configPath); cfg != nil {
		ws.webToken.Store(cfg.WebToken)
	}

	r := mux.NewRouter()
	r.Use(ws.securityMiddleware)

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
	r.HandleFunc("/web/logs", ws.handleLogsPage).Methods("GET")

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
	r.HandleFunc("/api/tasks/stats", ws.handleTaskStats).Methods("GET")
	r.HandleFunc("/api/logs", ws.handleLogs).Methods("GET")

	addr := fmt.Sprintf(":%d", port)
	fmt.Printf("Web服务器启动在 http://localhost%s\n", addr)
	// 显式超时：ReadHeaderTimeout 防 Slowloris 慢头攻击，Read/Write/Idle 限制僵尸连接占坑
	srv := &http.Server{
		Addr:              addr,
		Handler:           r,
		ReadHeaderTimeout: 10 * time.Second,
		ReadTimeout:       30 * time.Second,
		WriteTimeout:      60 * time.Second,
		IdleTimeout:       120 * time.Second,
	}
	return srv.ListenAndServe()
}

// 页面处理函数

func (ws *WebServer) handleTaskStats(w http.ResponseWriter, r *http.Request) {
	runners := ws.collector.SnapshotRunners()
	stats := make([]TaskStatSnapshot, 0, len(runners))
	for _, tr := range runners {
		stats = append(stats, tr.Stats())
	}
	ws.writeJSON(w, true, "ok", stats)
}

func (ws *WebServer) handleLogs(w http.ResponseWriter, r *http.Request) {
	after, _ := strconv.ParseUint(r.URL.Query().Get("after"), 10, 64)
	limit, _ := strconv.Atoi(r.URL.Query().Get("limit"))
	entries, next := globalLogRing.since(after, limit)
	ws.writeJSON(w, true, "ok", map[string]interface{}{
		"lines": entries,
		"next":  next,
	})
}

func (ws *WebServer) handleLogsPage(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>运行日志 - OPC DA Collector</title>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style>
        .log-toolbar { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; margin-bottom: 14px; }
        .log-toolbar input[type="text"] { flex: 1; min-width: 180px; max-width: 320px; }
        .log-view { background: #0b1220; color: #cbd5e1; font-family: ui-monospace, Consolas, "Courier New", monospace; font-size: 12.5px; line-height: 1.55; border-radius: var(--radius-sm); padding: 14px 16px; height: 62vh; overflow-y: auto; white-space: pre-wrap; word-break: break-all; border: 1px solid #1e293b; }
        .log-line { padding: 1px 0; }
        .log-line.warn { color: #fbbf24; }
        .log-line.err { color: #f87171; }
        .log-line.ok { color: #4ade80; }
        .log-line.dim { color: #64748b; }
        .log-meta { color: var(--text-2); font-size: 13px; }
        .btn-plain { border: 1px solid var(--border); background: var(--surface); color: var(--text); border-radius: var(--radius-sm); padding: 7px 14px; font-size: 13px; cursor: pointer; }
        .btn-plain:hover { border-color: var(--primary); color: var(--primary); }
        .btn-plain.active { background: var(--primary); border-color: var(--primary); color: #fff; }
    </style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link active" href="/web/logs">日志</a>
    </div></div>
    <div class="container">
        <h1>运行日志</h1>
        <p class="page-desc">采集器进程日志（内存环形缓冲最近 1000 条，重启后清空）</p>
        <div class="log-toolbar">
            <input type="text" id="logFilter" placeholder="过滤关键字…">
            <button class="btn-plain" id="btnPause" onclick="togglePause()">暂停滚动</button>
            <button class="btn-plain" id="btnClear" onclick="clearView()">清空视图</button>
            <button class="btn-plain active" id="btnFollow" onclick="toggleFollow()">自动跟随</button>
            <span class="log-meta" id="logCount"></span>
        </div>
        <div class="log-view" id="logView"></div>
    </div>
    <script>
        let lastSeq = 0;
        let paused = false;
        let follow = true;
        let allLines = [];
        let timer = null;

        function classify(t) {
            if (/error|失败|错误|fatal|panic|⚠️|未读到/i.test(t)) return 'err';
            if (/warn|警告/i.test(t)) return 'warn';
            if (/✅|成功|已连接/i.test(t)) return 'ok';
            return '';
        }

        function render() {
            const view = document.getElementById('logView');
            const q = document.getElementById('logFilter').value.trim().toLowerCase();
            const frag = document.createDocumentFragment();
            let shown = 0;
            for (let i = allLines.length - 1; i >= 0; i--) {
                const line = allLines[i];
                if (q && line.text.toLowerCase().indexOf(q) === -1) continue;
                const div = document.createElement('div');
                div.className = 'log-line ' + classify(line.text);
                div.textContent = line.text;
                frag.appendChild(div);
                shown++;
                if (shown >= 500) break;
            }
            view.innerHTML = '';
            view.appendChild(frag);
            document.getElementById('logCount').textContent = '缓冲 ' + allLines.length + ' 条 · 显示 ' + shown + ' 条';
            if (follow && !paused) view.scrollTop = 0;
        }

        async function poll() {
            if (paused) return;
            try {
                const resp = await fetch('/api/logs?after=' + lastSeq + '&limit=200');
                const data = await resp.json();
                if (!data.success) return;
                const lines = data.data.lines || [];
                if (lines.length) {
                    allLines = allLines.concat(lines);
                    if (allLines.length > 1000) allLines = allLines.slice(-1000);
                    lastSeq = data.data.next || lastSeq;
                    render();
                }
            } catch (e) { /* 网络抖动下轮询继续 */ }
        }

        function togglePause() {
            paused = !paused;
            const b = document.getElementById('btnPause');
            b.textContent = paused ? '继续滚动' : '暂停滚动';
            b.classList.toggle('active', paused);
        }

        function toggleFollow() {
            follow = !follow;
            document.getElementById('btnFollow').classList.toggle('active', follow);
        }

        function clearView() {
            allLines = [];
            render();
        }

        document.getElementById('logFilter').addEventListener('input', render);
        poll();
        timer = setInterval(poll, 2000);
    </script>
</body>
</html>
`
	ws.renderHTML(w, tmpl)
}

func (ws *WebServer) handleHome(w http.ResponseWriter, r *http.Request) {
	tmpl := `
<!DOCTYPE html>
<html>
<head>
    <title>OPC DA Collector - Web配置</title>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style>
        .menu { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; margin-top: 24px; }
        .menu-item { background: var(--surface); color: var(--text); padding: 22px 20px; border: 1px solid var(--border); border-radius: var(--radius); text-decoration: none; transition: border-color .15s, box-shadow .15s, transform .15s; }
        .menu-item:hover { border-color: var(--primary); box-shadow: var(--shadow); transform: translateY(-2px); }
        .menu-item h3 { margin: 0 0 8px; font-size: 16px; border: none; padding: 0; color: var(--text); }
        .menu-item p { margin: 0; font-size: 13px; color: var(--text-2); }
        .info { background: #eff6ff; padding: 16px 18px; border-radius: var(--radius); margin-top: 26px; border-left: 4px solid var(--primary); font-size: 14px; }
        .info ol { margin: 8px 0 0; padding-left: 20px; color: var(--text-2); }
        .welcome { color: var(--text-2); font-size: 14px; margin: 0 0 4px; }
    </style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link active" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container">
        <h1>Web 配置界面</h1>
        <p class="welcome">OPC DA 采集程序 · 配置与输出管理</p>

        <div class="menu">
            <a href="/web/http" class="menu-item">
                <h3>数据源</h3>
                <p>配置 HTTP 数据源（C# 代理地址）</p>
            </a>
            <a href="/web/tasks" class="menu-item">
                <h3>采集任务</h3>
                <p>绑定数据源与采集间隔</p>
            </a>
            <a href="/web/mqtt" class="menu-item">
                <h3>MQTT 输出</h3>
                <p>配置 MQTT 发布与格式</p>
            </a>
            <a href="/web/rtdb" class="menu-item">
                <h3>RTDB 输出</h3>
                <p>配置实时库输出与调试日志</p>
            </a>
            <a href="/web/transform" class="menu-item">
                <h3>键名转换</h3>
                <p>配置键名转换规则</p>
            </a>
            <a href="/web/monitor" class="menu-item">
                <h3>监控配置</h3>
                <p>配置 Webhook 预警</p>
            </a>
            <a href="/web/logs" class="menu-item">
                <h3>运行日志</h3>
                <p>查看采集器实时运行日志</p>
            </a>
        </div>

        <div class="info">
            <strong>快速开始</strong>
            <ol>
                <li>配置 MQTT 或 RTDB 输出</li>
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
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style>
        .toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    </style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link active" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container">
        <h1>数据源配置</h1>
        <p class="page-desc">配置 HTTP 数据源（C# OPC DA Agent 地址）</p>
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
                    <label>访问令牌(选填)</label>
                    <input type="text" id="httpToken" placeholder="对应目标代理 api_token，留空=不带令牌">
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
                    <button class="btn btn-secondary" onclick="closeModal()">取消</button>
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
                    (config.token ? '<br>令牌: 已配置' : '') +
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
                document.getElementById('httpToken').value = config.token || '';
                document.getElementById('httpMethod').value = config.method || 'GET';
                document.getElementById('httpTimeout').value = config.timeout || 5000;
            } else {
                document.getElementById('httpName').value = '';
                document.getElementById('httpEnabled').value = 'true';
                document.getElementById('httpUrl').value = '';
                document.getElementById('httpToken').value = '';
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
                token: document.getElementById('httpToken').value.trim(),
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
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style></style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link active" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container narrow">
        <h1>RTDB 输出配置</h1>
        <p class="page-desc">配置实时库（KairosDB telnet put）输出</p>

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
            <div class="form-group">
                <label>调试日志（每批打印发送内容）</label>
                <select id="debug" name="debug">
                    <option value="false">关闭</option>
                    <option value="true">开启</option>
                </select>
            </div>

            <button type="button" onclick="saveRtdb()">保存配置</button>
            <button type="button" class="test" onclick="testRtdb()">测试连接</button>
        </form>

        <div id="result"></div>
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
                document.getElementById('debug').value = rtdb.debug?.toString() || 'false';
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
                format: format,
                debug: document.getElementById('debug').value === 'true'
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
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style></style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link active" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container narrow">
        <h1>MQTT 配置</h1>
        <p class="page-desc">配置 MQTT 发布、报文格式与 JS 转换</p>

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

            <button type="button" onclick="saveMqtt()">保存配置</button>
            <button type="button" class="test" onclick="testMqtt()">测试连接</button>
        </form>

        <div id="result"></div>
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
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style></style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link active" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container">
        <h1>键名转换规则</h1>
        <p class="page-desc">按数据源配置键名转换，支持拖拽排序</p>

        <div class="source-selector">
            <div class="form-group" style="margin-bottom: 0;">
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

        <button type="button" onclick="addRule()">添加规则</button>
        <button type="button" class="test" onclick="previewTransform()">预览转换</button>
        <button type="button" onclick="saveRules()">保存规则</button>

        <h3>当前规则列表</h3>
        <div id="rulesList"></div>

        <div id="preview" class="preview" style="display: none;"></div>
        <div id="result"></div>
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
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style></style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link active" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container narrow">
        <h1>监控配置</h1>
        <p class="page-desc">配置 Webhook 预警通知</p>

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

            <button type="button" onclick="saveWebhook()">保存配置</button>
            <button type="button" class="test" onclick="testWebhook()">测试发送</button>
        </form>

        <div id="result"></div>
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

// securityMiddleware 请求安全检查，顺序固定：Host（防 DNS rebinding）→ Origin（防浏览器跨站，仅在存在时校验，curl/采集器不带此头不受影响）→ Token（仅 /api/*，web_token 空=不启用；Web 页面壳豁免以便前端弹出令牌引导）。
// 任一失败时写出 403/401 JSON 并终止请求。
func (ws *WebServer) securityMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		host := r.Host
		if h, _, err := net.SplitHostPort(host); err == nil {
			host = h
		}
		if !isAllowedHost(host) {
			ws.writeJSONStatus(w, http.StatusForbidden, false, "拒绝访问：非法的Host "+host, nil)
			return
		}

		origin := r.Header.Get("Origin")
		if origin != "" && !ws.isAllowedOrigin(origin) {
			ws.writeJSONStatus(w, http.StatusForbidden, false, "拒绝访问：非法的Origin", nil)
			return
		}

		if tok, _ := ws.webToken.Load().(string); tok != "" && strings.HasPrefix(r.URL.Path, "/api/") {
			provided := r.Header.Get("X-Api-Token")
			if provided == "" {
				provided = r.URL.Query().Get("token")
			}
			if !secureCompare(tok, provided) {
				ws.writeJSONStatus(w, http.StatusUnauthorized, false, "未授权：缺少或错误的访问令牌", nil)
				return
			}
		}

		next.ServeHTTP(w, r)
	})
}

func (ws *WebServer) isAllowedOrigin(origin string) bool {
	u, err := url.Parse(origin)
	if err != nil {
		return false
	}
	if u.Scheme != "http" && u.Scheme != "https" {
		return false
	}
	if u.Port() != strconv.Itoa(ws.webPort) {
		return false
	}
	return isAllowedHost(u.Hostname())
}

func isAllowedHost(host string) bool {
	if host == "" || host == "localhost" || host == "127.0.0.1" || host == "::1" {
		return true
	}
	if hn, err := os.Hostname(); err == nil && strings.EqualFold(host, hn) {
		return true
	}
	if addrs, err := net.InterfaceAddrs(); err == nil {
		for _, a := range addrs {
			if ipnet, ok := a.(*net.IPNet); ok && ipnet.IP.String() == host {
				return true
			}
		}
	}
	return false
}

// secureCompare 固定时间字符串比较，防计时侧信道（长度不等直接失败）
func secureCompare(expected, actual string) bool {
	if len(expected) != len(actual) {
		return false
	}
	var diff byte
	for i := 0; i < len(expected); i++ {
		diff |= expected[i] ^ actual[i]
	}
	return diff == 0
}

func (ws *WebServer) writeJSONStatus(w http.ResponseWriter, status int, success bool, message string, data interface{}) {
	response := map[string]interface{}{
		"success":   success,
		"message":   message,
		"data":      data,
		"timestamp": time.Now().Format(time.RFC3339),
	}
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	json.NewEncoder(w).Encode(response)
}

const baseStyle = `<style>
:root {
    --bg: #f3f5f7;
    --surface: #ffffff;
    --text: #1a2332;
    --text-2: #5a6577;
    --border: #e2e8f0;
    --primary: #2563eb;
    --primary-hover: #1d4ed8;
    --ok: #16a34a;
    --danger: #dc2626;
    --info: #0284c7;
    --radius: 10px;
    --radius-sm: 6px;
    --shadow: 0 1px 3px rgba(15,23,42,.08), 0 4px 16px rgba(15,23,42,.06);
    --font: system-ui, -apple-system, "Segoe UI", "Microsoft YaHei", sans-serif;
}
* { box-sizing: border-box; }
body { font-family: var(--font); margin: 0; background: var(--bg); color: var(--text); line-height: 1.55; -webkit-font-smoothing: antialiased; }
.topbar { background: #0f172a; }
.topbar-inner { max-width: 1100px; margin: 0 auto; display: flex; align-items: center; gap: 2px; padding: 0 20px; min-height: 52px; flex-wrap: wrap; }
.brand { font-weight: 650; font-size: 15px; margin-right: 14px; }
.brand a { color: #fff; text-decoration: none; }
.nav-link { color: #94a3b8; text-decoration: none; font-size: 14px; padding: 6px 12px; border-radius: 6px; transition: color .15s, background .15s; }
.nav-link:hover { color: #fff; background: rgba(255,255,255,.08); }
.nav-link.active { color: #fff; background: var(--primary); }
.container { max-width: 1100px; margin: 28px auto; background: var(--surface); padding: 28px 32px 32px; border-radius: var(--radius); box-shadow: var(--shadow); border: 1px solid var(--border); }
.container.narrow { max-width: 760px; }
h1 { font-size: 22px; font-weight: 650; margin: 0 0 6px; color: var(--text); }
.page-desc { color: var(--text-2); font-size: 14px; margin: 0 0 22px; }
h3 { font-size: 15px; font-weight: 650; margin: 26px 0 14px; padding-bottom: 8px; border-bottom: 1px solid var(--border); color: var(--text); }
a.back { display: inline-block; color: var(--text-2); font-size: 13px; text-decoration: none; margin-bottom: 14px; padding: 4px 10px; border: 1px solid var(--border); border-radius: 999px; background: var(--surface); transition: color .15s, border-color .15s; }
a.back:hover { color: var(--primary); border-color: var(--primary); }
.form-group { margin-bottom: 16px; }
label { display: block; margin-bottom: 6px; font-size: 13px; font-weight: 600; color: var(--text-2); }
input, select, textarea { width: 100%; padding: 9px 12px; border: 1px solid var(--border); border-radius: var(--radius-sm); font: inherit; font-size: 14px; background: #fff; color: var(--text); transition: border-color .15s, box-shadow .15s; }
input:focus, select:focus, textarea:focus { outline: none; border-color: var(--primary); box-shadow: 0 0 0 3px rgba(37,99,235,.15); }
textarea { resize: vertical; min-height: 72px; }
button { font: inherit; font-size: 14px; font-weight: 600; color: #fff; background: var(--primary); padding: 9px 18px; border: none; border-radius: var(--radius-sm); cursor: pointer; margin-right: 10px; transition: background .15s, transform .1s; }
button:hover { background: var(--primary-hover); }
button:active { transform: translateY(1px); }
button.test { background: var(--info); }
button.test:hover { background: #0369a1; }
.btn { padding: 7px 14px; border: none; border-radius: var(--radius-sm); cursor: pointer; font-size: 13px; font-weight: 600; font-family: inherit; transition: filter .15s; }
.btn:hover { filter: brightness(1.08); }
.btn-primary { background: var(--primary); color: #fff; }
.btn-danger { background: var(--danger); color: #fff; }
.btn-edit { background: var(--info); color: #fff; }
.btn-secondary { background: #64748b; color: #fff; }
.success, .error { padding: 10px 14px; border-radius: var(--radius-sm); font-size: 14px; font-weight: 600; margin-top: 14px; }
.success { color: #14532d; background: #dcfce7; border: 1px solid #86efac; }
.error { color: #7f1d1d; background: #fee2e2; border: 1px solid #fca5a5; }
.badge { display: inline-block; padding: 2px 9px; border-radius: 999px; font-size: 12px; font-weight: 600; color: #fff; vertical-align: middle; }
.badge-on { background: var(--ok); }
.badge-off { background: #94a3b8; }
.modal { display: none; position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(15,23,42,.55); z-index: 1000; }
.modal-content { background: var(--surface); margin: 6% auto; padding: 26px 28px; border-radius: var(--radius); width: 90%; max-width: 500px; box-shadow: 0 20px 50px rgba(15,23,42,.3); }
.modal-content h2 { margin: 0 0 20px; font-size: 18px; }
.modal-actions { display: flex; gap: 10px; justify-content: flex-end; margin-top: 22px; }
.modal-actions button { margin-right: 0; }
.card-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 16px; margin-top: 18px; }
.card { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; transition: box-shadow .15s, border-color .15s; }
.card:hover { box-shadow: var(--shadow); border-color: #cbd5e1; }
.card.disabled { opacity: .55; }
.card-name { font-size: 15px; font-weight: 650; margin-bottom: 10px; display: flex; align-items: center; gap: 8px; justify-content: space-between; }
.card-info { color: var(--text-2); font-size: 13px; line-height: 1.7; word-break: break-all; }
.card-actions { margin-top: 14px; display: flex; gap: 8px; }
.empty { color: #94a3b8; padding: 28px; text-align: center; border: 1px dashed var(--border); border-radius: var(--radius); background: #fafbfc; }
.add-btn { display: inline-block; margin-top: 18px; }
.preview, .source-selector { padding: 16px 18px; border-radius: var(--radius); margin-top: 18px; font-size: 14px; }
.preview { background: #eff6ff; border: 1px solid #bfdbfe; }
.source-selector { background: #fffbeb; border: 1px solid #fde68a; margin-bottom: 20px; }
.rule-item { background: #f8fafc; padding: 12px 14px; margin: 10px 0; border-radius: var(--radius-sm); border-left: 4px solid var(--primary); cursor: move; font-size: 14px; border-top: 2px solid transparent; }
.rule-item.dragging { opacity: .45; border-left-color: var(--info); }
.rule-item.drag-over { border-top-color: var(--info); }
.rule-buttons { display: inline-block; margin-left: 10px; float: right; }
.rule-buttons button { padding: 3px 9px; margin-left: 5px; margin-right: 0; font-size: 12px; }
.http-grid, .task-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 16px; margin-top: 18px; }
.http-card, .task-card { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 18px 20px; transition: box-shadow .15s, border-color .15s; }
.http-card:hover, .task-card:hover { box-shadow: var(--shadow); border-color: #cbd5e1; }
.http-card.disabled, .task-card.disabled { opacity: .55; }
.http-name, .task-name { font-size: 15px; font-weight: 650; margin-bottom: 10px; }
.http-info, .task-info { color: var(--text-2); font-size: 13px; line-height: 1.7; word-break: break-all; }
.http-actions, .task-actions { margin-top: 14px; display: flex; gap: 8px; }
.add-http, .add-task { display: inline-block; margin-top: 18px; }
</style>
`

// authSnippet 统一注入所有 Web 页面（renderHTML 是唯一出口，避免逐页修改内嵌脚本）：
// 给 fetch 自动附加 X-Api-Token，401 时引导输入令牌并落 localStorage 后刷新。
const authSnippet = `<script>
(function(){
  var prompting = false;
  function tok(){ try { return localStorage.getItem('opc_collector_token') || ''; } catch(e){ return ''; } }
  if (window.fetch) {
    var rawFetch = window.fetch;
    window.fetch = function(input, init){
      init = init || {};
      var headers = {};
      var h = init.headers;
      if (h) {
        if (typeof h.forEach === 'function') { h.forEach(function(v, k){ headers[k] = v; }); }
        else { for (var k in h) { headers[k] = h[k]; } }
      }
      var t = tok();
      if (t) headers['X-Api-Token'] = t;
      init.headers = headers;
      return rawFetch(input, init).then(function(resp){
        if (resp.status === 401 && !prompting) {
          prompting = true;
          var v = prompt('请输入 API 访问令牌 (web_token):');
          prompting = false;
          if (v !== null && v.trim() !== '') {
            try { localStorage.setItem('opc_collector_token', v.trim()); } catch(e){}
            location.reload();
          }
        }
        return resp;
      });
    };
  }
})();
</script>
`

func (ws *WebServer) renderHTML(w http.ResponseWriter, html string) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	html = strings.Replace(html, "</head>", baseStyle+authSnippet+"</head>", 1)
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

	// 文件已保存即以新令牌对外服务，再热加载数据面；热加载失败如实返回并回滚运行时
	ws.webToken.Store(config.WebToken)
	if ws.collector != nil {
		if err := ws.collector.Reload(config); err != nil {
			ws.writeJSON(w, false, fmt.Sprintf("配置已保存到文件，但热加载失败: %v（重启后将应用新配置，请先修正错误）", err), nil)
			return
		}
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
	defer client.Disconnect()

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

	if err := ws.transformer.LoadFromFile(fileName); err != nil {
		ws.writeJSON(w, false, "规则已保存但调试面板同步加载失败: "+err.Error(), nil)
		return
	}

	ws.writeJSON(w, true, "规则已保存到 "+fileName, nil)
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
		if webToken, ok := mainData["web_token"].(string); ok {
			config.WebToken = webToken
		}
	}

	if webToken, ok := updates["web_token"].(string); ok {
		config.WebToken = webToken
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
				if token, ok := httpData["token"].(string); ok {
					httpConfig.Token = token
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
		if debug, ok := rtdbData["debug"].(bool); ok {
			config.RtdbConfig.Debug = debug
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

// testMqttConnection 真实建立一次 MQTT 连接再断开，而不是只校验配置格式（否则地址错/网络不通也会报成功）。
// 测试用一次性唯一 ClientID：与运行中的采集器用相同 ID 连同一 broker 会互相踢下线；测完立即 Disconnect 防止残留会话。
func testMqttConnection(config *MqttConfig) error {
	if config.Broker == "" {
		return fmt.Errorf("MQTT服务器地址不能为空")
	}
	if config.Port <= 0 || config.Port > 65535 {
		return fmt.Errorf("MQTT端口无效")
	}

	opts := mqtt.NewClientOptions()
	opts.AddBroker(fmt.Sprintf("tcp://%s:%d", config.Broker, config.Port))
	opts.SetClientID(fmt.Sprintf("opc_collector_test_%d", time.Now().UnixNano()))
	opts.SetCleanSession(true)
	opts.SetAutoReconnect(false)
	opts.SetConnectRetry(false)
	opts.SetConnectTimeout(5 * time.Second)
	if config.Username != "" {
		opts.SetUsername(config.Username)
	}
	if config.Password != "" {
		opts.SetPassword(config.Password)
	}

	client := mqtt.NewClient(opts)
	token := client.Connect()
	if !token.WaitTimeout(6 * time.Second) {
		return fmt.Errorf("MQTT连接超时（6秒）")
	}
	if token.Error() != nil {
		return token.Error()
	}
	defer client.Disconnect(250)
	if !client.IsConnected() {
		return fmt.Errorf("MQTT连接未建立")
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
	applyApiToken(req, config.Token)

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
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <style></style>
</head>
<body>
    <div class="topbar"><div class="topbar-inner">
        <span class="brand"><a href="/">OPC DA Collector</a></span>
        <a class="nav-link" href="/">首页</a>
        <a class="nav-link" href="/web/http">数据源</a>
        <a class="nav-link active" href="/web/tasks">任务</a>
        <a class="nav-link" href="/web/mqtt">MQTT</a>
        <a class="nav-link" href="/web/rtdb">RTDB</a>
        <a class="nav-link" href="/web/transform">转换</a>
        <a class="nav-link" href="/web/monitor">监控</a>
        <a class="nav-link" href="/web/logs">日志</a>
    </div></div>
    <div class="container">
        <h1>采集任务配置</h1>
        <p class="page-desc">配置采集任务，绑定数据源</p>
        <div id="result"></div>

        <div class="task-grid" id="taskGrid"></div>

        <button class="add-task" onclick="openModal()">+ 添加任务</button>

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
                    <button class="btn btn-secondary" onclick="closeModal()">取消</button>
                    <button class="btn btn-primary" onclick="saveTask()">保存</button>
                </div>
            </div>
        </div>
    </div>

    <script>
        let tasks = [];
        let httpConfigs = [];
        let editingTask = '';
        let taskStats = {};

        async function loadData() {
            try {
                const resp = await fetch('/api/config');
                const data = await resp.json();
                if (data.success) {
                    tasks = data.data.tasks || [];
                    httpConfigs = data.data.http_configs || [];
                    renderTasks();
                    loadStats();
                }
            } catch (e) {
                showResult(false, '加载配置失败: ' + e.message);
            }
        }

        async function loadStats() {
            try {
                const resp = await fetch('/api/tasks/stats');
                const data = await resp.json();
                if (data.success) {
                    taskStats = {};
                    (data.data || []).forEach(s => { taskStats[s.label] = s; });
                    renderTasks();
                }
            } catch (e) { /* 统计接口失败不打断配置页 */ }
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
                const st = taskStats['task' + (index + 1)];
                let tagLine;
                if (!enabled) {
                    tagLine = '标签: —（任务停用）';
                } else if (!st || !st.has_window) {
                    tagLine = '标签: 等待首个10s统计窗口…';
                } else if (st.expected === 0) {
                    tagLine = '标签: 实采 ' + st.hit + '（期望集未获取）';
                } else if (st.warming) {
                    tagLine = '标签: 实采 ' + st.hit + '/' + st.expected + ' · 预热中';
                } else if (st.missing > 0) {
                    tagLine = '标签: 实采 ' + st.hit + '/' + st.expected + ' · 缺失 ' + st.missing;
                } else {
                    tagLine = '标签: 实采 ' + st.hit + '/' + st.expected;
                }
                if (st && st.expected_err) {
                    tagLine += ' · 期望集拉取失败';
                }
                const card = document.createElement('div');
                card.className = 'task-card' + (enabled ? '' : ' disabled');
                card.innerHTML =
                    '<div class="task-name">任务' + (index + 1) + ' <span class="badge ' + (enabled ? 'badge-on' : 'badge-off') + '">' + (enabled ? '启用' : '禁用') + '</span></div>' +
                    '<div class="task-info">' +
                    '数据源: ' + source + '<br>' +
                    '采集间隔: ' + interval + '秒<br>' +
                    tagLine +
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
        setInterval(loadStats, 5000);
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
