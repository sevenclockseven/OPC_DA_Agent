using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

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
            if (config == null) throw new ArgumentNullException("config");
            if (opcService == null) throw new ArgumentNullException("opcService");
            if (logger == null) throw new ArgumentNullException("logger");

            _config = config;
            _opcService = opcService;
            _logger = logger;

            _listener = new HttpListener();
            _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _config.HttpPort));
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
                _listener.Start();
                _isRunning = true;
                _logger.Info(string.Format("HTTP服务器已启动，监听端口: {0}", _config.HttpPort));

                // 启动异步监听
                _listener.BeginGetContext(OnGetContext, null);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("HTTP服务器启动失败", ex);
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
            _listener.Stop();
            _logger.Info("HTTP服务器已停止");
        }

        /// <summary>
        /// 处理HTTP请求
        /// </summary>
        private void OnGetContext(IAsyncResult result)
        {
            if (!_isRunning) return;

            HttpListenerContext context = null;
            try
            {
                context = _listener.EndGetContext(result);
                _requestCount++;

                // 同步处理请求
                ProcessRequest(context);
            }
            catch (ObjectDisposedException)
            {
                // 监听器已停止
                return;
            }
            catch (Exception ex)
            {
                _logger.Error("处理HTTP请求时发生错误", ex);
            }
            finally
            {
                // 继续监听下一个请求
                if (_isRunning && _listener.IsListening)
                {
                    try
                    {
                        _listener.BeginGetContext(OnGetContext, null);
                    }
                    catch (ObjectDisposedException)
                    {
                        // 监听器已停止
                    }
                }
            }
        }

        /// <summary>
        /// 处理具体的HTTP请求
        /// </summary>
        private void ProcessRequest(HttpListenerContext context)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            response.ContentType = "application/json; charset=utf-8";
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            ApiResponse apiResponse = null;

            try
            {
                string path = request.Url.AbsolutePath.ToLower();
                string method = request.HttpMethod;

                // 路由处理
                if (path == "/api/status" && method == "GET")
                {
                    apiResponse = GetStatus();
                }
                else if (path == "/api/data" && method == "GET")
                {
                    apiResponse = GetData();
                }
                else if (path == "/api/browse" && method == "GET")
                {
                    apiResponse = GetBrowseRoot();
                }
                else
                {
                    apiResponse = ApiResponse.ErrorResponse("未找到的接口");
                    response.StatusCode = 404;
                }

                if (apiResponse != null)
                {
                    string json = JsonConvert.SerializeObject(apiResponse);
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("处理HTTP请求时发生错误", ex);
                response.StatusCode = 500;
                string errorJson = JsonConvert.SerializeObject(ApiResponse.ErrorResponse("内部服务器错误"));
                byte[] errorBuffer = Encoding.UTF8.GetBytes(errorJson);
                response.ContentLength64 = errorBuffer.Length;
                response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// 获取系统状态
        /// </summary>
        private ApiResponse GetStatus()
        {
            var status = _opcService.GetStatus();
            return ApiResponse.SuccessResponse(status);
        }

        /// <summary>
        /// 获取数据
        /// </summary>
        private ApiResponse GetData()
        {
            var data = _opcService.GetData();
            return ApiResponse.SuccessResponse(data);
        }

        /// <summary>
        /// 浏览根节点
        /// </summary>
        private ApiResponse GetBrowseRoot()
        {
            var rootNodes = _opcService.GetBrowseRoot();
            return ApiResponse.SuccessResponse(rootNodes);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}