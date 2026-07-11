using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

using Newtonsoft.Json;

namespace OPC_DA_Agent
{
    public class HttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly OPCService _opcService;
        private readonly Logger _logger;
        private readonly Config _config;
        private readonly CancellationTokenSource _cts;
        private bool _isRunning;

        private long _requestCount = 0;

        private HttpListener _listener;

        public HttpServer(Config config, OPCService opcService, Logger logger)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (opcService == null) throw new ArgumentNullException("opcService");
            if (logger == null) throw new ArgumentNullException("logger");

            _config = config;
            _opcService = opcService;
            _logger = logger;
            _cts = new CancellationTokenSource();
        }

        private HttpListener TryCreateListener(string bind, int port)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add(string.Format("http://{0}:{1}/", bind, port));
            return listener;
        }

        public bool Start()
        {
            if (_isRunning)
            {
                _logger.Warn("HTTP服务器已在运行中");
                return true;
            }

            string bind = string.IsNullOrEmpty(_config.HttpBindIp) ? "localhost" : _config.HttpBindIp;
            if (bind == "0.0.0.0" || bind == "+") bind = "+";

            var tries = new System.Collections.Generic.List<string>();
            if (bind == "+")
            {
                tries.Add("+");
                tries.Add("localhost");
            }
            else
            {
                tries.Add(bind);
                tries.Add("localhost");
            }

            foreach (string host in tries)
            {
                try
                {
                    _listener = TryCreateListener(host, _config.HttpPort);
                    _listener.Start();
                    _isRunning = true;
                    _logger.Info(string.Format("HTTP服务器已启动: http://{0}:{1}/", host, _config.HttpPort));
                    _listener.BeginGetContext(OnGetContext, null);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(string.Format("绑定 {0}:{1} 失败", host, _config.HttpPort), ex);
                    try { if (_listener != null) _listener.Close(); } catch { }
                    _listener = null;
                }
            }

            _logger.Error("HTTP服务器启动失败（所有绑定方式均失败），请尝试以管理员权限运行");
            return false;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts.Cancel();
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
            }
            _logger.Info("HTTP服务器已停止");
        }

        private void OnGetContext(IAsyncResult result)
        {
            if (!_isRunning) return;

            HttpListenerContext context = null;
            try
            {
                context = _listener.EndGetContext(result);
                _requestCount++;
                ProcessRequest(context);
            }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                _logger.Error("处理HTTP请求时发生错误", ex);
            }
            finally
            {
                if (_isRunning && _listener != null && _listener.IsListening)
                {
                    try { _listener.BeginGetContext(OnGetContext, null); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            try
            {
                string path = request.Url.AbsolutePath.ToLower();
                string method = request.HttpMethod;
                string query = request.Url.Query;

                byte[] buffer = null;
                bool isHtml = false;

                // === API 路由 ===
                if (path == "/api/status" && method == "GET")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    buffer = Json(ApiResponse.SuccessResponse(_opcService.GetStatus()));
                }
                else if (path == "/api/data" && method == "GET")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    buffer = Json(ApiResponse.SuccessResponse(_opcService.GetData()));
                }
                else if (path == "/api/tags" && method == "GET")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    buffer = Json(ApiResponse.SuccessResponse(_opcService.GetTags()));
                }
                else if (path == "/api/tags" && method == "POST")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    buffer = HandleSaveTags(request);
                }
                else if (path == "/api/browse" && method == "GET")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    buffer = Json(ApiResponse.SuccessResponse(_opcService.GetBrowseRoot()));
                }
                else if (path == "/api/browse/node" && method == "GET")
                {
                    response.ContentType = "application/json; charset=utf-8";
                    string nodeId = ExtractQuery(query, "nodeId");
                    buffer = Json(ApiResponse.SuccessResponse(_opcService.BrowsePath(nodeId)));
                }
                // === Web UI ===
                else if ((path == "/" || path == "/index.html") && method == "GET")
                {
                    response.ContentType = "text/html; charset=utf-8";
                    buffer = Html(GetWebUI());
                }
                else
                {
                    response.ContentType = "application/json; charset=utf-8";
                    buffer = Json(ApiResponse.ErrorResponse("未找到的接口: " + path));
                    response.StatusCode = 404;
                }

                if (buffer != null)
                {
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("处理HTTP请求时发生错误", ex);
                response.StatusCode = 500;
                byte[] errorBuf = Json(ApiResponse.ErrorResponse("内部服务器错误: " + ex.Message));
                response.ContentLength64 = errorBuf.Length;
                response.OutputStream.Write(errorBuf, 0, errorBuf.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private byte[] HandleSaveTags(HttpListenerRequest request)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    string body = reader.ReadToEnd();
                    var tagReq = JsonConvert.DeserializeObject<SaveTagsRequest>(body);
                    if (tagReq != null && tagReq.Tags != null)
                    {
                        _opcService.UpdateTags(tagReq.Tags);
                        return Json(ApiResponse.SuccessResponse(null, string.Format("已保存{0}个标签", tagReq.Tags.Count)));
                    }
                    return Json(ApiResponse.ErrorResponse("请求数据无效"));
                }
            }
            catch (Exception ex)
            {
                return Json(ApiResponse.ErrorResponse("保存标签失败: " + ex.Message));
            }
        }

        private string ExtractQuery(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return null;
            query = query.TrimStart('?');
            foreach (string pair in query.Split('&'))
            {
                var kv = pair.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && kv[0] == key)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        private byte[] Json(ApiResponse apiResponse)
        {
            string json = JsonConvert.SerializeObject(apiResponse);
            return Encoding.UTF8.GetBytes(json);
        }

        private byte[] Html(string html)
        {
            return Encoding.UTF8.GetBytes(html);
        }

        /// <summary>返回单页 Web UI（浏览+选择标签）</summary>
        private string GetWebUI()
        {
            return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>OPC DA 数据采集代理 - 标签配置</title>
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:""Microsoft YaHei"",""Segoe UI"",sans-serif;background:#1e1e2e;color:#cdd6f4}
.container{display:flex;height:100vh}
.left{width:55%;border-right:1px solid #45475a;overflow:auto;padding:16px}
.right{width:45%;padding:16px;overflow:auto}
h2{margin-bottom:12px;color:#cba6f7;font-size:18px}
.node{cursor:pointer;padding:4px 8px;border-radius:6px;margin:2px 0;font-size:14px}
.node:hover{background:#313244}
.node.branch{color:#89b4fa}
.node.leaf{color:#a6e3a1}
.node.selected{background:#45475a}
.children{margin-left:20px;display:none}
.children.open{display:block}
.btn{padding:8px 20px;border:none;border-radius:8px;background:#cba6f7;color:#1e1e2e;font-weight:bold;cursor:pointer;margin:8px 0}
.btn:hover{background:#b4befe}
.tag-list{list-style:none;margin:8px 0}
.tag-item{padding:6px 12px;background:#313244;border-radius:6px;margin:4px 0;display:flex;justify-content:space-between;align-items:center}
.tag-remove{color:#f38ba8;cursor:pointer;font-weight:bold}
.status-bar{position:fixed;bottom:0;left:0;right:0;background:#181825;padding:8px 16px;font-size:12px;color:#6c7086}
.connected{color:#a6e3a1}.disconnected{color:#f38ba8}
</style>
</head>
<body>
<div class=""container"">
  <div class=""left"">
    <h2>OPC 节点浏览</h2>
    <button class=""btn"" onclick=""loadRoot()"" id=""btnBrowse"">浏览根节点</button>
    <div id=""tree""></div>
  </div>
  <div class=""right"">
    <h2>已选标签</h2>
    <button class=""btn"" onclick=""saveTags()"">保存并应用</button>
    <ul class=""tag-list"" id=""tagList""></ul>
  </div>
</div>
<div class=""status-bar"" id=""statusBar"">连接中...</div>
<script>
var selected = {};
var port = location.port || 8080;
var base = '' + location.protocol + '//' + location.hostname + ':' + port;

function api(path, method, data, cb) {
  var x = new XMLHttpRequest();
  x.open(method || 'GET', base + path);
  x.setRequestHeader('Content-Type', 'application/json');
  x.onload = function() {
    try { cb(JSON.parse(x.responseText)); } catch(e) { cb({success:false,message:e.message}); }
  };
  x.send(data ? JSON.stringify(data) : null);
}

function loadRoot() {
  document.getElementById('tree').innerHTML = '<p>加载中...</p>';
  api('/api/browse', 'GET', null, function(r) {
    if (r.success) renderTree('tree', r.data);
    else document.getElementById('tree').innerHTML = '<p style=color:#f38ba8>错误: ' + r.message + '</p>';
  });
}

function renderTree(parentId, nodes) {
  var parent = document.getElementById(parentId);
  parent.innerHTML = '';
  if (!nodes || nodes.length === 0) {
    parent.innerHTML = '<p style=color:#6c7086>（无子节点）</p>';
    return;
  }
  nodes.forEach(function(n) {
    var div = document.createElement('div');
    var nodeDiv = document.createElement('div');
    nodeDiv.className = 'node ' + (n.isFolder ? 'branch' : 'leaf');
    nodeDiv.textContent = (n.isFolder ? '📁 ' : '🏷 ') + n.name;
    nodeDiv.onclick = function() {
      if (n.isFolder) {
        var childrenDiv = document.getElementById('children-' + parentId + '-' + n.nodeId);
        if (childrenDiv) {
          if (childrenDiv.classList.contains('open')) {
            childrenDiv.classList.remove('open');
          } else {
            if (!childrenDiv.dataset.loaded) {
              loadChildren('children-' + parentId + '-' + n.nodeId, n.nodeId);
            }
            childrenDiv.classList.add('open');
          }
        }
      } else {
        toggleTag(n);
        nodeDiv.classList.toggle('selected');
      }
    };
    div.appendChild(nodeDiv);

    if (n.isFolder) {
      var childrenDiv = document.createElement('div');
      childrenDiv.id = 'children-' + parentId + '-' + n.nodeId;
      childrenDiv.className = 'children';
      div.appendChild(childrenDiv);
    }
    parent.appendChild(div);
  });
}

function loadChildren(containerId, nodeId) {
  var container = document.getElementById(containerId);
  container.innerHTML = '<p style=color:#6c7086>加载中...</p>';
  var url = '/api/browse/node?nodeId=' + encodeURIComponent(nodeId);
  api(url, 'GET', null, function(r) {
    if (r.success) {
      renderTree(containerId, r.data);
      container.dataset.loaded = '1';
    } else {
      container.innerHTML = '<p style=color:#f38ba8>' + r.message + '</p>';
    }
  });
}

function toggleTag(node) {
  if (selected[node.nodeId]) {
    delete selected[node.nodeId];
  } else {
    selected[node.nodeId] = { node_id: node.nodeId, name: node.name, description: '', data_type: '', enabled: true, active: true };
  }
  renderTagList();
}

function renderTagList() {
  var ul = document.getElementById('tagList');
  ul.innerHTML = '';
  var keys = Object.keys(selected);
  if (keys.length === 0) {
    ul.innerHTML = '<p style=color:#6c7086>未选择任何标签</p>';
    return;
  }
  keys.forEach(function(id) {
    var t = selected[id];
    var li = document.createElement('li');
    li.className = 'tag-item';
    li.innerHTML = '<span>' + t.name + '</span><span class=""tag-remove"" onclick=""removeTag(\\''+ id + '\\')"">✕</span>';
    ul.appendChild(li);
  });
}

function removeTag(id) {
  delete selected[id];
  renderTagList();
}

function saveTags() {
  var keys = Object.keys(selected);
  if (keys.length === 0) {
    alert('请先选择标签');
    return;
  }
  var tags = keys.map(function(id) {
    return selected[id];
  });
  api('/api/tags', 'POST', { tags: tags }, function(r) {
    if (r.success) alert(r.message || '保存成功');
    else alert('保存失败: ' + r.message);
  });
}

function loadStatus() {
  api('/api/status', 'GET', null, function(r) {
    if (r.success && r.data) {
      var s = r.data;
      var cls = s.isConnected ? 'connected' : 'disconnected';
      var text = s.isConnected ? '已连接' : '未连接';
      document.getElementById('statusBar').innerHTML = '<span class=""' + cls + '"">● ' + text + '</span> | 标签: ' + s.tagCount + ' | 读取: ' + s.totalReads + ' | 错误: ' + s.totalErrors + ' | 运行: ' + Math.floor(s.uptimeSeconds) + '秒';
    }
  });
}

loadRoot();
loadStatus();
setInterval(loadStatus, 3000);
</script>
</body>
</html>";
        }

        public void Dispose() { Stop(); }
    }

    public class SaveTagsRequest
    {
        [JsonProperty("tags")]
        public List<TagConfig> Tags { get; set; }
    }
}
