using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
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
        private CancellationTokenSource _cts;
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

            _cts = new CancellationTokenSource();

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
                    // 期望通配符绑定却落到 localhost 是静默降级：请求在 HTTP.sys 层被拒（400）且应用层无任何日志，必须显式告警
                    if (bind == "+" && host != "+")
                    {
                        _logger.Warn(string.Format(
                            "HTTP通配绑定(+:{0})失败，已回退为 localhost：远程/IP访问将被 HTTP.sys 拒绝并返回400。请以管理员身份运行，或先执行: netsh http add urlacl url=http://+:{0}/ user=\"NT AUTHORITY\\SYSTEM\"",
                            _config.HttpPort));
                    }
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
                try { _listener.Close(); } catch { }
                _listener = null;
            }
            _logger.Info("HTTP服务器已停止");
        }

        private void OnGetContext(IAsyncResult result)
        {
            if (!_isRunning) return;

            // 必须先重新挂起下一个 Accept，再处理当前请求。
            // 原实现在 ProcessRequest 返回后才挂起；而 /api/stream(SSE) 等长连接会长期占用
            // 唯一的回调线程，导致 BeginGetContext 无法及时重新挂起，浏览器页面与轮询请求被积压，
            // 最终 ERR_CONNECTION_RESET / 超时，必须重启代理才能恢复。
            try
            {
                if (_isRunning && _listener != null && _listener.IsListening)
                    _listener.BeginGetContext(OnGetContext, null);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _logger.Error("重新挂起 HTTP 监听失败", ex);
            }

            HttpListenerContext context = null;
            try
            {
                context = _listener.EndGetContext(result);
                _requestCount++;
                ProcessRequest(context);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                _logger.Error("处理HTTP请求时发生错误", ex);
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            byte[] buffer = null;
            int statusCode = 200;

            try
            {
                string path = request.Url.AbsolutePath.ToLower();
                string method = request.HttpMethod;
                string query = request.Url.Query;

                _logger.Info(string.Format("[HTTP] {0} {1}{2}", method, path, MaskToken(query)));

                if (!AuthorizeRequest(request, response, path))
                {
                    return;
                }
                SetCorsHeaders(request, response);

                if (method == "OPTIONS")
                {
                    response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                    response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Api-Token");
                    SendResponse(response, null, 200);
                    return;
                }

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
                else if (path == "/api/stream" && method == "GET")
                {
                    HandleStream(response, _opcService);
                    return;
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
                    int offset = ParseInt(ExtractQuery(query, "offset"), 0);
                    int limit = ParseInt(ExtractQuery(query, "limit"), 50);
                    _logger.Info("[HTTP] 调用 GetBrowseRoot");
                    try
                    {
                        var data = _opcService.BrowsePaged(null, offset, limit);
                        _logger.Info(string.Format("[HTTP] GetBrowseRoot 返回 {0}/{1} 个节点", data.Nodes.Count, data.Total));
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
                    int offset = ParseInt(ExtractQuery(query, "offset"), 0);
                    int limit = ParseInt(ExtractQuery(query, "limit"), 50);
                    try
                    {
                        var data = _opcService.BrowsePaged(nodeId, offset, limit);
                        buffer = Json(ApiResponse.SuccessResponse(data));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error("[HTTP] BrowsePath 失败", ex);
                        buffer = Json(ApiResponse.ErrorResponse("浏览失败: " + ex.Message));
                    }
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
                    statusCode = 404;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("处理HTTP请求时发生错误", ex);
                statusCode = 500;
                buffer = Json(ApiResponse.ErrorResponse("内部服务器错误: " + ex.Message));
            }

            SendResponse(response, buffer, statusCode);
        }

        /// <summary>
        /// 请求安全检查，顺序固定：Token（仅 /api/*，Web UI 页面壳豁免以便前端弹出令牌引导）→ Host → Origin。
        /// 任一失败时已写出 401/403 响应并返回 false，调用方直接 return。
        /// </summary>
        private bool AuthorizeRequest(HttpListenerRequest request, HttpListenerResponse response, string path)
        {
            string required = _config.ApiToken;
            if (!string.IsNullOrWhiteSpace(required) && path.StartsWith("/api/"))
            {
                string provided = request.Headers["X-Api-Token"];
                if (string.IsNullOrEmpty(provided))
                {
                    provided = ExtractQuery(request.Url.Query, "token");
                }
                if (!FixedTimeEquals(required.Trim(), provided))
                {
                    SendJsonError(response, 401, "未授权：缺少或错误的访问令牌");
                    return false;
                }
            }

            // Host 白名单：防 DNS rebinding（攻击者域名解析到本机后，浏览器会以其域名作 Host 发起请求）
            if (!IsAllowedHost(request.Url.Host))
            {
                SendJsonError(response, 403, "拒绝访问：非法的Host " + request.Url.Host);
                return false;
            }

            // Origin 仅在存在时校验（curl/采集器不带此头），防浏览器跨站页面调用
            string origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && !IsAllowedOrigin(origin))
            {
                SendJsonError(response, 403, "拒绝访问：非法的Origin");
                return false;
            }

            return true;
        }

        // 仅对合法 Origin 精确回显：带 X-Api-Token 的请求不允许 ACAO 为 *
        private void SetCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
        {
            string origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin) && IsAllowedOrigin(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
                response.Headers.Add("Vary", "Origin");
            }
        }

        private bool IsAllowedOrigin(string origin)
        {
            Uri u;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            if (u.Port != _config.HttpPort) return false;
            return IsAllowedHost(u.Host);
        }

        private bool IsAllowedHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host == "127.0.0.1" || host == "::1" || host == "[::1]") return true;
            if (string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork
                            && ua.Address.ToString() == host)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 枚举异常按拒绝处理（fail-closed）：正常路径已由上方 localhost/机器名/特判放行
            }
            return false;
        }

        // 固定时间字符串比较（.NET 4.0 无 CryptographicOperations.FixedTimeEquals，手写等价实现）
        private static bool FixedTimeEquals(string expected, string actual)
        {
            if (actual == null) return false;
            int diff = expected.Length ^ actual.Length;
            int len = Math.Min(expected.Length, actual.Length);
            for (int i = 0; i < len; i++)
            {
                diff |= expected[i] ^ actual[i];
            }
            return diff == 0;
        }

        // 访问日志对 token 参数打码：query 明文落盘等于令牌泄露
        private static string MaskToken(string query)
        {
            if (string.IsNullOrEmpty(query)) return query;
            return Regex.Replace(query, @"(?i)(token=)[^&]*", "$1***");
        }

        private void SendJsonError(HttpListenerResponse response, int statusCode, string message)
        {
            response.ContentType = "application/json; charset=utf-8";
            SendResponse(response, Json(ApiResponse.ErrorResponse(message)), statusCode);
        }

        // 统一写出响应：客户端在传输中途断开（HttpListenerException）或响应已部分提交（InvalidOperationException）
        // 都属于正常边界情况，吞掉二次异常，避免污染日志与后续请求处理
        private void SendResponse(HttpListenerResponse response, byte[] buffer, int statusCode)
        {
            try
            {
                response.StatusCode = statusCode;
                if (buffer != null)
                {
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (HttpListenerException)
            {
            }
            catch (InvalidOperationException)
            {
                // 同时覆盖 ObjectDisposedException（其基类为 InvalidOperationException）
            }
            finally
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        private byte[] HandleSaveTags(HttpListenerRequest request)
        {
            try
            {
                // JSON 默认按 UTF-8 解码：Content-Type 无 charset 时 HttpListenerRequest.ContentEncoding
                // 会落到系统 ANSI（中文 Windows 为 GBK），浏览器 JSON.stringify 的 UTF-8 body 会被解错，
                // 中文 ItemID 在入内存/tags.json 前就已变乱码
                using (var reader = new StreamReader(request.InputStream, new UTF8Encoding(false)))
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

        private int ParseInt(string s, int def)
        {
            int v;
            return int.TryParse(s, out v) ? v : def;
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

        private void HandleStream(HttpListenerResponse response, OPCService opcService)
        {
            response.ContentType = "text/event-stream";
            response.SendChunked = true;
            response.Headers.Add("Cache-Control", "no-cache");

            var sw = new StreamWriter(response.OutputStream, new System.Text.UTF8Encoding(false));
            opcService.AddSseClient(sw);
            try
            {
                sw.Write("retry: 3000\n\n");
                sw.Flush();
                _logger.Info("[SSE] 客户端已连接");
                var heartbeat = DateTime.Now;
                while (_isRunning)
                {
                    Thread.Sleep(1000);
                    if ((DateTime.Now - heartbeat).TotalSeconds > 15)
                    {
                        try
                        {
                            sw.Write(": ping\n\n");
                            sw.Flush();
                            heartbeat = DateTime.Now;
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // 客户端断开，由 finally 清理
            }
            finally
            {
                opcService.RemoveSseClient(sw);
                try { sw.Dispose(); } catch { }
                _logger.Info("[SSE] 客户端已断开");
            }
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
