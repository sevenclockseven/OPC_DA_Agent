using System;
using System.Collections.Generic;
using System.Threading;

using OpcNetApi;
using OpcNetApi.Com;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC DA 数据采集服务
    /// 使用 OPC .NET API 的 Mock 实现
    /// </summary>
    public class OPCService : IDisposable
    {
        private Server _opcServer;
        private Group _opcGroup;
        private List<TagConfig> _tags;
        private Dictionary<string, object> _lastValues;
        private Timer _updateTimer;
        private bool _isRunning;
        private object _lock = new object();

        // 统计信息
        private long _totalReads = 0;
        private long _totalErrors = 0;
        private DateTime _startTime;

        private readonly Logger _logger;
        private readonly Config _config;

        public bool IsConnected
        {
            get { return _opcServer != null && _opcServer.IsConnected; }
        }

        public int TagCount
        {
            get { return _tags != null ? _tags.Count : 0; }
        }

        public long TotalReads
        {
            get { return _totalReads; }
        }

        public long TotalErrors
        {
            get { return _totalErrors; }
        }

        public DateTime StartTime
        {
            get { return _startTime; }
        }

        public OPCService(Config config, Logger logger)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (logger == null) throw new ArgumentNullException("logger");

            _config = config;
            _logger = logger;
            _tags = new List<TagConfig>();
            _lastValues = new Dictionary<string, object>();
            _startTime = DateTime.Now;
        }

        /// <summary>
        /// 连接到OPC DA服务器
        /// </summary>
        public bool Connect()
        {
            try
            {
                _logger.Info("正在连接到OPC服务器...");
                
                // Mock实现 - 使用模拟OPC服务器
                string serverName = ExtractServerName(_config.OpcServerUrl);
                _opcServer = new Server(serverName);
                _opcServer.Connect();

                _logger.Info(string.Format("已连接到OPC服务器: {0}", serverName));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("连接OPC服务器失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 启动数据采集
        /// </summary>
        public bool Start()
        {
            if (_opcServer == null || !_opcServer.IsConnected)
            {
                _logger.Error("OPC服务器未连接，无法启动数据采集");
                return false;
            }

            try
            {
                // 创建OPC组
                _opcGroup = _opcServer.CreateGroup("DataGroup", true, 1000, null, null, null, null);

                // 创建示例OPC标签
                CreateSampleTags();

                // 启动定时更新
                _updateTimer = new Timer(OnUpdateTimer, null, 0, _config.UpdateInterval);
                _isRunning = true;

                _logger.Info("OPC数据采集已启动");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("启动数据采集失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 停止数据采集
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            if (_updateTimer != null)
            {
                _updateTimer.Dispose();
                _updateTimer = null;
            }
            _logger.Info("OPC数据采集已停止");
        }

        /// <summary>
        /// 定时更新数据
        /// </summary>
        private void OnUpdateTimer(object state)
        {
            if (!_isRunning || _opcGroup == null) return;

            try
            {
                UpdateSampleData();
                _totalReads++;
            }
            catch (Exception ex)
            {
                _totalErrors++;
                _logger.Error("更新数据时发生错误", ex);
            }
        }

        /// <summary>
        /// 创建示例OPC标签
        /// </summary>
        private void CreateSampleTags()
        {
            // 创建一些示例标签用于测试
            var sampleTags = new string[] { 
                "Channel1.Device1.Temperature", 
                "Channel1.Device1.Pressure",
                "Channel1.Device1.Flow"
            };

            foreach (string tagName in sampleTags)
            {
                var tagConfig = new TagConfig
                {
                    NodeId = tagName,
                    Name = tagName,
                    Active = true
                };
                _tags.Add(tagConfig);
                _lastValues[tagName] = GetRandomValue();
            }

            _logger.Info(string.Format("已创建 {0} 个OPC标签", _tags.Count));
        }

        /// <summary>
        /// 更新示例数据
        /// </summary>
        private void UpdateSampleData()
        {
            lock (_lock)
            {
                foreach (string key in _lastValues.Keys)
                {
                    _lastValues[key] = GetRandomValue();
                }
            }
        }

        /// <summary>
        /// 生成随机值
        /// </summary>
        private object GetRandomValue()
        {
            var random = new Random();
            return random.NextDouble() * 100;
        }

        /// <summary>
        /// 获取状态信息
        /// </summary>
        public object GetStatus()
        {
            return new
            {
                isConnected = IsConnected,
                tagCount = TagCount,
                totalReads = TotalReads,
                totalErrors = TotalErrors,
                uptimeSeconds = (DateTime.Now - StartTime).TotalSeconds,
                memoryUsageMb = GC.GetTotalMemory(false) / (1024 * 1024)
            };
        }

        /// <summary>
        /// 获取当前数据
        /// </summary>
        public object GetData()
        {
            var result = new Dictionary<string, object>();
            lock (_lock)
            {
                foreach (var kvp in _lastValues)
                {
                    if (kvp.Value != null)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 获取浏览根节点
        /// </summary>
        public object GetBrowseRoot()
        {
            return new[]
            {
                new { nodeId = "Root", name = "根节点", hasChildren = true }
            };
        }

        /// <summary>
        /// 提取服务器名称
        /// </summary>
        private string ExtractServerName(string opcServerUrl)
        {
            // 从 OPC DA URL 中提取服务器名称
            // 例如: opcda://localhost/OPCServer.WinCC -> OPCServer.WinCC
            int lastSlash = opcServerUrl.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < opcServerUrl.Length - 1)
            {
                return opcServerUrl.Substring(lastSlash + 1);
            }
            return "MockServer";
        }

        public void Dispose()
        {
            Stop();
            if (_opcServer != null)
            {
                _opcServer.Disconnect();
                _opcServer = null;
            }
        }
    }
}