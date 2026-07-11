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
        private HttpListener _listener;
        private readonly OPCService _opcService;
        private readonly Logger _logger;
        private readonly Config _config;
        private readonly CancellationTokenSource _cts;
        private bool _isRunning;

        private long _requestCount = 0;

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

                _logger.Info(string.Format("[HTTP] {0} {1}{2}", method, path, query));

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
                    _logger.Info("[HTTP] 调用 GetBrowseRoot");
                    try
                    {
                        var data = _opcService.GetBrowseRoot();
                        _logger.Info(string.Format("[HTTP] GetBrowseRoot 返回 {0} 个节点", data.Count));
                        buffer = Json(ApiResponse.SuccessResponse(data));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("[HTTP] GetBrowseRoot 失败", ex);
                        buffer = Json(ApiResponse.ErrorResponse("浏览失败: " + ex.Message));
                    }
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

        private string GetWebUI()
        {
            string webRoot = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[] {
                Path.Combine(webRoot, "web", "index.html"),
                Path.Combine(webRoot, "index.html"),
            };
            foreach (string p in candidates)
            {
                if (File.Exists(p))
                {
                    return File.ReadAllText(p);
                }
            }
            return "<!DOCTYPE html><html><body><h1>Web UI not found</h1><p>Looked in: " + string.Join("; ", candidates) + "</p></body></html>";
        }

        public void Dispose() { Stop(); }
    }

    public class SaveTagsRequest
    {
        [JsonProperty("tags")]
        public List<TagConfig> Tags { get; set; }
    }
}
