using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OPC_DA_Agent
{
    /// <summary>
    /// HTTP REST API服务器
    /// </summary>
    public class HttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly OPCService _opcService;
        private readonly Logger _logger;
        private readonly Config _config;
        private readonly CancellationTokenSource _cts;
        private bool _isRunning;

        private long _requestCount = 0;

        public HttpServer(Config config, OPCService opcService, Logger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _opcService = opcService ?? throw new ArgumentNullException(nameof(opcService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_config.HttpPort}/");
            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 启动HTTP服务器
        /// </summary>
        public bool Start()
        {
            if (_isRunning)
            {
                _logger.Warn("HTTP服务器已在运行中");
                return true;
            }

            try
            {
                // 需要管理员权限才能监听所有接口
                // 如果没有权限，可以改为监听 localhost
                if (!HttpListener.IsSupported)
                {
                    _logger.Error("当前系统不支持HttpListener");
                    return false;
                }

                _listener.Start();
                _isRunning = true;

                // 启动请求处理任务
                Task.Run(() => HandleRequestsAsync(_cts.Token));

                _logger.Info($"HTTP服务器已启动，监听端口: {_config.HttpPort}");
                _logger.Info($"访问地址: http://localhost:{_config.HttpPort}/");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"启动HTTP服务器失败: {ex.Message}", ex);
                if (ex is HttpListenerException hlex && hlex.ErrorCode == 5)
                {
                    _logger.Error("需要管理员权限，请以管理员身份运行此程序");
                    _logger.Error("或者修改配置文件，使用特定IP地址监听");
                }
                return false;
            }
        }

        /// <summary>
        /// 停止HTTP服务器
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts.Cancel();

            try
            {
                _listener.Stop();
            }
            catch (Exception ex)
            {
                _logger.Error($"停止HTTP服务器失败: {ex.Message}", ex);
            }

            _logger.Info("HTTP服务器已停止");
        }

        /// <summary>
        /// 处理HTTP请求
        /// </summary>
        private async Task HandleRequestsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => ProcessRequestAsync(context, token), token);
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException)
                {
                    if (_isRunning)
                    {
                        _logger.Error($"获取请求上下文失败: {ex.Message}", ex);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 处理单个请求
        /// </summary>
        private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken token)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                Interlocked.Increment(ref _requestCount);

                _logger.Debug($"收到请求: {request.HttpMethod} {request.Url.PathAndQuery}");

                // 设置响应头
                response.ContentType = "application/json; charset=utf-8";
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

                // 处理OPTIONS请求（CORS预检）
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                // 路由处理
                var path = request.Url.AbsolutePath.TrimEnd('/');
                var method = request.HttpMethod;
                var queryString = request.Url.Query;

                ApiResponse apiResponse = null;

                if (method == "GET" && path == "/api/status")
                {
                    apiResponse = await HandleGetStatus();
                }
                else if (method == "GET" && path == "/api/data")
                {
                    apiResponse = await HandleGetData();
                }
                else if (method == "GET" && path == "/api/data/list")
                {
                    apiResponse = await HandleGetDataList();
                }
                else if (method == "POST" && path == "/api/data/batch")
                {
                    apiResponse = await HandleBatchRead(request);
                }
                else if (method == "GET" && path == "/api/tags")
                {
                    apiResponse = await HandleGetTags();
                }
                else if (method == "POST" && path == "/api/reload")
                {
                    apiResponse = await HandleReload();
                }
                else if (method == "POST" && path == "/api/config")
                {
                    apiResponse = await HandleUpdateConfig(request);
                }
                else if (method == "GET" && path == "/api/browse")
                {
                    apiResponse = await HandleBrowseRoot();
                }
                else if (method == "GET" && path.StartsWith("/api/browse/node"))
                {
                    apiResponse = await HandleBrowseNode(request);
                }
                else if (method == "GET" && path.StartsWith("/api/browse/tree"))
                {
                    apiResponse = await HandleBrowseTree(request);
                }
                else if (method == "GET" && path.StartsWith("/api/search"))
                {
                    apiResponse = await HandleSearch(request);
                }
                else if (method == "GET" && path.StartsWith("/api/node"))
                {
                    apiResponse = await HandleGetNodeDetail(request);
                }
                else if (method == "POST" && path == "/api/export")
                {
                    apiResponse = await HandleExportVariables(request);
                }
                else if (method == "POST" && path == "/api/save-tags")
                {
                    apiResponse = await HandleSaveTags(request);
                }
                else if (path == "/" || path == "/api")
                {
                    apiResponse = ApiResponse.SuccessResponse(new
                    {
                        name = "OPC DA Agent",
                        version = "1.0.0",
                        endpoints = new[]
                        {
                            "GET  /api/status",
                            "GET  /api/data",
                            "GET  /api/data/list",
                            "POST /api/data/batch",
                            "GET  /api/tags",
                            "POST /api/reload",
                            "POST /api/config",
                            "GET  /api/browse",
                            "GET  /api/browse/node",
                            "GET  /api/browse/tree",
                            "GET  /api/search",
                            "GET  /api/node",
                            "POST /api/export",
                            "POST /api/save-tags"
                        }
                    });
                }
                else
                {
                    apiResponse = ApiResponse.ErrorResponse("未找到的接口");
                    response.StatusCode = 404;
                }

                if (apiResponse != null)
                {
                    var json = JsonConvert.SerializeObject(apiResponse);
                    var buffer = Encoding.UTF8.GetBytes(json);

                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, token);
                }

                response.Close();
            }
            catch (Exception ex)
            {
                _logger.Error($"处理请求失败: {ex.Message}", ex);

                try
                {
                    response.StatusCode = 500;
                    var errorResponse = ApiResponse.ErrorResponse($"服务器错误: {ex.Message}");
                    var json = JsonConvert.SerializeObject(errorResponse);
                    var buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, token);
                    response.Close();
                }
                catch { }
            }
        }

        /// <summary>
        /// 处理：获取状态
        /// </summary>
        private async Task<ApiResponse> HandleGetStatus()
        {
            var status = _opcService.GetStatus();
            return ApiResponse.SuccessResponse(status);
        }

        /// <summary>
        /// 处理：获取当前数据（键值对格式）
        /// </summary>
        private async Task<ApiResponse> HandleGetData()
        {
            var data = _opcService.GetCurrentData();
            return ApiResponse.SuccessResponse(data);
        }

        /// <summary>
        /// 处理：获取当前数据（列表格式）
        /// </summary>
        private async Task<ApiResponse> HandleGetDataList()
        {
            var data = _opcService.GetCurrentDataList();
            var response = new BatchDataResponse
            {
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Count = data.Count,
                Data = data,
                ElapsedMs = 0
            };
            return ApiResponse.SuccessResponse(response);
        }

        /// <summary>
        /// 处理：批量读取
        /// </summary>
        private async Task<ApiResponse> HandleBatchRead(HttpListenerRequest request)
        {
            try
            {
                var body = await ReadRequestBody(request);
                var batchRequest = JsonConvert.DeserializeObject<BatchReadRequest>(body);

                if (batchRequest.NodeIds == null || batchRequest.NodeIds.Count == 0)
                {
                    return ApiResponse.ErrorResponse("node_ids不能为空");
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var data = await _opcService.ReadNodesAsync(batchRequest.NodeIds, batchRequest.TimeoutMs);
                sw.Stop();

                var response = new BatchDataResponse
                {
                    BatchId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.Now,
                    Count = data.Count,
                    Data = data,
                    ElapsedMs = sw.ElapsedMilliseconds
                };

                return ApiResponse.SuccessResponse(response);
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"批量读取失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：获取标签列表
        /// </summary>
        private async Task<ApiResponse> HandleGetTags()
        {
            // 这里可以从OPCService获取标签配置
            // 暂时返回空列表
            return ApiResponse.SuccessResponse(new List<object>());
        }

        /// <summary>
        /// 处理：重新加载配置
        /// </summary>
        private async Task<ApiResponse> HandleReload()
        {
            var success = await _opcService.ReloadConfig();
            if (success)
            {
                return ApiResponse.SuccessResponse(null, "配置已重新加载");
            }
            else
            {
                return ApiResponse.ErrorResponse("配置重新加载失败");
            }
        }

        /// <summary>
        /// 处理：更新配置
        /// </summary>
        private async Task<ApiResponse> HandleUpdateConfig(HttpListenerRequest request)
        {
            try
            {
                var body = await ReadRequestBody(request);
                var updateRequest = JsonConvert.DeserializeObject<ConfigUpdateRequest>(body);

                if (updateRequest.UpdateIntervalMs.HasValue)
                {
                    _config.UpdateInterval = updateRequest.UpdateIntervalMs.Value;
                }

                if (updateRequest.BatchSize.HasValue)
                {
                    _config.BatchSize = updateRequest.BatchSize.Value;
                }

                if (updateRequest.EnableCompression.HasValue)
                {
                    _config.EnableCompression = updateRequest.EnableCompression.Value;
                }

                return ApiResponse.SuccessResponse(null, "配置已更新");
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"更新配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：浏览根节点
        /// </summary>
        private async Task<ApiResponse> HandleBrowseRoot()
        {
            try
            {
                var nodes = await _opcService.BrowseRootAsync();
                return ApiResponse.SuccessResponse(nodes);
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"浏览根节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：浏览指定节点
        /// </summary>
        private async Task<ApiResponse> HandleBrowseNode(HttpListenerRequest request)
        {
            try
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                var nodeId = query["nodeId"];
                var depthStr = query["depth"];

                if (string.IsNullOrEmpty(nodeId))
                {
                    return ApiResponse.ErrorResponse("nodeId参数不能为空");
                }

                int depth = 1;
                if (!string.IsNullOrEmpty(depthStr))
                {
                    int.TryParse(depthStr, out depth);
                }

                var nodes = await _opcService.BrowseNodeAsync(nodeId, depth);
                return ApiResponse.SuccessResponse(nodes);
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"浏览节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：浏览节点树
        /// </summary>
        private async Task<ApiResponse> HandleBrowseTree(HttpListenerRequest request)
        {
            try
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                var nodeId = query["nodeId"];
                var maxDepthStr = query["maxDepth"];

                if (string.IsNullOrEmpty(nodeId))
                {
                    nodeId = "ObjectsFolder";
                }

                int maxDepth = 3;
                if (!string.IsNullOrEmpty(maxDepthStr))
                {
                    int.TryParse(maxDepthStr, out maxDepth);
                }

                var tree = await _opcService.BrowseTreeAsync(nodeId, maxDepth);
                return ApiResponse.SuccessResponse(tree);
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"浏览节点树失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：搜索节点
        /// </summary>
        private async Task<ApiResponse> HandleSearch(HttpListenerRequest request)
        {
            try
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                var searchTerm = query["q"];
                var maxResultsStr = query["max"];

                if (string.IsNullOrEmpty(searchTerm))
                {
                    return ApiResponse.ErrorResponse("q参数不能为空");
                }

                int maxResults = 1000;
                if (!string.IsNullOrEmpty(maxResultsStr))
                {
                    int.TryParse(maxResultsStr, out maxResults);
                }

                var nodes = await _opcService.SearchNodesAsync(searchTerm, maxResults);
                return ApiResponse.SuccessResponse(nodes);
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"搜索节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：获取节点详细信息
        /// </summary>
        private async Task<ApiResponse> HandleGetNodeDetail(HttpListenerRequest request)
        {
            try
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                var nodeId = query["nodeId"];

                if (string.IsNullOrEmpty(nodeId))
                {
                    return ApiResponse.ErrorResponse("nodeId参数不能为空");
                }

                var detail = await _opcService.GetNodeDetailAsync(nodeId);
                return ApiResponse.SuccessResponse(detail);
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"获取节点详细信息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：导出所有变量节点
        /// </summary>
        private async Task<ApiResponse> HandleExportVariables(HttpListenerRequest request)
        {
            try
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                var maxDepthStr = query["maxDepth"];

                int maxDepth = 3;
                if (!string.IsNullOrEmpty(maxDepthStr))
                {
                    int.TryParse(maxDepthStr, out maxDepth);
                }

                var tags = await _opcService.ExportAllVariablesAsync(maxDepth);
                return ApiResponse.SuccessResponse(new
                {
                    count = tags.Count,
                    tags = tags
                });
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"导出变量节点失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理：保存标签配置
        /// </summary>
        private async Task<ApiResponse> HandleSaveTags(HttpListenerRequest request)
        {
            try
            {
                var body = await ReadRequestBody(request);
                var tags = JsonConvert.DeserializeObject<List<TagConfig>>(body);

                if (tags == null || tags.Count == 0)
                {
                    return ApiResponse.ErrorResponse("标签列表不能为空");
                }

                // 保存到文件
                var tagsFile = _config.TagsFile ?? "tags.json";
                var json = JsonConvert.SerializeObject(tags, Formatting.Indented);
                System.IO.File.WriteAllText(tagsFile, json);

                // 重新加载配置
                await _opcService.ReloadConfig();

                return ApiResponse.SuccessResponse(null, $"已保存 {tags.Count} 个标签并重新加载");
            }
            catch (Exception ex)
            {
                return ApiResponse.ErrorResponse($"保存标签失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取请求体
        /// </summary>
        private async Task<string> ReadRequestBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public void Dispose()
        {
            Stop();
            _listener.Close();
            _cts.Dispose();
        }
    }
}
