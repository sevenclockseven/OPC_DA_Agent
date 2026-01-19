using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC DA 数据采集服务
    /// 使用 OPC Automation COM 接口
    /// </summary>
    public class OPCService : IDisposable
    {
        private object _opcServer;
        private object _opcGroup;
        private List<TagConfig> _tags;
        private Dictionary<string, object> _lastValues;
        private Timer _updateTimer;
        private bool _isRunning;
        private readonly object _lock = new object();

        // 统计信息
        private long _totalReads = 0;
        private long _totalErrors = 0;
        private DateTime _startTime;

        private readonly Logger _logger;
        private readonly Config _config;

        public bool IsConnected => _opcServer != null;
        public int TagCount => _tags?.Count ?? 0;
        public long TotalReads => _totalReads;
        public long TotalErrors => _totalErrors;
        public DateTime StartTime => _startTime;

        public OPCService(Config config, Logger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tags = new List<TagConfig>();
            _lastValues = new Dictionary<string, object>();
            _startTime = DateTime.Now;
        }

        /// <summary>
        /// 连接到OPC DA服务器
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                _logger.Info($"正在连接到OPC DA服务器: {_config.OpcServerProgId}");

                // 创建OPC服务器实例
                _opcServer = Activator.CreateInstance(Type.GetTypeFromProgID("OPCServer"));

                // 连接到OPC服务器（使用ProgID）
                var connectMethod = _opcServer.GetType().GetMethod("Connect");
                connectMethod.Invoke(_opcServer, new object[] { _config.OpcServerProgId });

                _logger.Info($"成功连接到OPC DA服务器");

                // 创建OPC组
                var opcGroups = _opcServer.GetType().GetProperty("OPCGroups").GetValue(_opcServer);
                var addMethod = opcGroups.GetType().GetMethod("Add");
                _opcGroup = addMethod.Invoke(opcGroups, new object[] { "OPC_DA_Agent_Group" });

                // 设置组属性
                var updateRateProperty = _opcGroup.GetType().GetProperty("UpdateRate");
                updateRateProperty.SetValue(_opcGroup, _config.UpdateInterval);

                var isActiveProperty = _opcGroup.GetType().GetProperty("IsActive");
                isActiveProperty.SetValue(_opcGroup, true);

                var isSubscribedProperty = _opcGroup.GetType().GetProperty("IsSubscribed");
                isSubscribedProperty.SetValue(_opcGroup, true);

                // 加载标签配置
                LoadTags();

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"连接OPC DA服务器失败: {ex.Message}", ex);
                _totalErrors++;
                return false;
            }
        }

        /// <summary>
        /// 加载标签配置
        /// </summary>
        private void LoadTags()
        {
            try
            {
                if (_config.TagsFile != null && System.IO.File.Exists(_config.TagsFile))
                {
                    var json = System.IO.File.ReadAllText(_config.TagsFile);
                    _tags = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TagConfig>>(json);
                    _logger.Info($"从文件加载了 {_tags.Count} 个标签");
                }
                else
                {
                    // 从配置中加载标签
                    if (_config.Tags != null && _config.Tags.Count > 0)
                    {
                        _tags = _config.Tags;
                        _logger.Info($"从配置加载了 {_tags.Count} 个标签");
                    }
                    else
                    {
                        _logger.Warn("未配置任何标签，请检查配置文件");
                    }
                }

                // 初始化最后值字典
                lock (_lock)
                {
                    _lastValues.Clear();
                    foreach (var tag in _tags.Where(t => t.Enabled))
                    {
                        _lastValues[tag.NodeId] = null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"加载标签配置失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 启动数据采集
        /// </summary>
        public bool Start()
        {
            if (_isRunning)
            {
                _logger.Warn("数据采集已在运行中");
                return true;
            }

            if (!IsConnected)
            {
                _logger.Error("无法启动数据采集：未连接到OPC DA服务器");
                return false;
            }

            try
            {
                _isRunning = true;
                _startTime = DateTime.Now;

                // 添加标签到组
                if (_tags.Count > 0)
                {
                    var enabledTags = _tags.Where(t => t.Enabled).ToList();
                    if (enabledTags.Count > 0)
                    {
                        var itemNames = enabledTags.Select(t => t.NodeId).ToArray();
                        var itemIds = new int[itemNames.Length];
                        var serverHandles = new int[itemNames.Length];
                        var clientHandles = new int[itemNames.Length];

                        for (int i = 0; i < itemNames.Length; i++)
                        {
                            clientHandles[i] = i + 1;
                        }

                        var opcItems = _opcGroup.GetType().GetProperty("OPCItems").GetValue(_opcGroup);
                        var addItemsMethod = opcItems.GetType().GetMethod("AddItems");
                        var parameters = new object[] 
                        { 
                            itemNames.Length, 
                            itemNames, 
                            clientHandles, 
                            serverHandles, 
                            itemIds 
                        };
                        addItemsMethod.Invoke(opcItems, parameters);

                        _logger.Info($"已添加 {enabledTags.Count} 个标签到OPC组");
                    }
                }

                // 创建定时器
                _updateTimer = new Timer(
                    async _ => await UpdateDataAsync(),
                    null,
                    0,
                    _config.UpdateInterval
                );

                _logger.Info($"数据采集已启动，更新间隔: {_config.UpdateInterval}ms");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"启动数据采集失败: {ex.Message}", ex);
                _isRunning = false;
                return false;
            }
        }

        /// <summary>
        /// 停止数据采集
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _updateTimer?.Dispose();
            _updateTimer = null;

            _logger.Info("数据采集已停止");
        }

        /// <summary>
        /// 更新数据（定时执行）
        /// </summary>
        private async Task UpdateDataAsync()
        {
            if (!_isRunning || !IsConnected) return;

            try
            {
                var enabledTags = _tags.Where(t => t.Enabled).ToList();
                if (enabledTags.Count == 0) return;

                // 批量读取
                var batchSize = _config.BatchSize;
                for (int i = 0; i < enabledTags.Count; i += batchSize)
                {
                    var batch = enabledTags.Skip(i).Take(batchSize).ToList();
                    await ReadBatchAsync(batch);
                }

                _totalReads++;
            }
            catch (Exception ex)
            {
                _logger.Error($"更新数据失败: {ex.Message}", ex);
                _totalErrors++;
            }
        }

        /// <summary>
        /// 批量读取数据
        /// </summary>
        private async Task ReadBatchAsync(List<TagConfig> tags)
        {
            try
            {
                var itemNames = tags.Select(t => t.NodeId).ToArray();
                var itemIds = new int[itemNames.Length];
                var serverHandles = new int[itemNames.Length];
                var clientHandles = new int[itemNames.Length];

                for (int i = 0; i < itemNames.Length; i++)
                {
                    clientHandles[i] = i + 1;
                }

                // 添加项到组
                var opcItems = _opcGroup.GetType().GetProperty("OPCItems").GetValue(_opcGroup);
                var addItemsMethod = opcItems.GetType().GetMethod("AddItems");
                var parameters = new object[] 
                { 
                    itemNames.Length, 
                    itemNames, 
                    clientHandles, 
                    serverHandles, 
                    itemIds 
                };
                addItemsMethod.Invoke(opcItems, parameters);

                // 读取值
                object[] values = null;
                short[] qualities = null;
                DateTime[] timestamps = null;
                short[] errors = null;

                var readMethod = _opcGroup.GetType().GetMethod("Read");
                var readParameters = new object[] 
                { 
                    1, // OPC_DS_DEVICE = 1
                    values,
                    qualities,
                    timestamps,
                    errors
                };
                readMethod.Invoke(_opcGroup, readParameters);

                values = (object[])readParameters[1];
                qualities = (short[])readParameters[2];
                timestamps = (DateTime[])readParameters[3];
                errors = (short[])readParameters[4];

                // 读取值
                object[] values = null;
                short[] qualities = null;
                DateTime[] timestamps = null;
                short[] errors = null;

                var readMethod = _opcGroup.GetType().GetMethod("Read");
                var readParameters = new object[] 
                { 
                    1, // OPC_DS_DEVICE = 1
                    values,
                    qualities,
                    timestamps,
                    errors
                };
                readMethod.Invoke(_opcGroup, readParameters);

                values = (object[])readParameters[1];
                qualities = (short[])readParameters[2];
                timestamps = (DateTime[])readParameters[3];
                errors = (short[])readParameters[4];

                var timestamp = DateTime.Now;

                for (int i = 0; i < nodeIds.Count && i < values.Length; i++)
                {
                    var value = values[i];
                    var error = errors[i];

                    var quality = error == 0 ? "Good" : "Bad";

                    result.Add(new TagValue
                    {
                        NodeId = nodeIds[i],
                        Name = nodeIds[i],
                        Value = value,
                        Quality = quality,
                        Timestamp = timestamp,
                        Status = error == 0 ? "Good" : $"Error: {error}"
                    });
                }

                _totalReads++;

                // 清理项
                _opcGroup.OPCItems.RemoveItems(itemIds.Length, itemIds);
            }
            catch (Exception ex)
            {
                _logger.Error($"批量读取节点失败: {ex.Message}", ex);
                _totalErrors++;
            }

            return result;
        }

        /// <summary>
        /// 获取系统状态
        /// </summary>
        public SystemStatus GetStatus()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();

            return new SystemStatus
            {
                OpcConnected = IsConnected,
                OpcServerUrl = _config.OpcServerProgId,
                TagCount = _tags.Count,
                EnabledTagCount = _tags.Count(t => t.Enabled),
                UptimeSeconds = (DateTime.Now - _startTime).TotalSeconds,
                LastUpdateTime = DateTime.Now,
                TotalRequests = _totalReads,
                TotalDataPoints = _totalReads * _tags.Count(t => t.Enabled),
                ErrorCount = _totalErrors,
                MemoryUsageMb = process.WorkingSet64 / 1024.0 / 1024.0,
                CpuUsagePercent = 0 // 需要PerformanceCounter实现
            };
        }

        /// <summary>
        /// 重新加载配置
        /// </summary>
        public bool ReloadConfig()
        {
            try
            {
                Stop();

                LoadTags();

                if (IsConnected)
                {
                    Start();
                }

                _logger.Info("配置已重新加载");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"重新加载配置失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 导出所有变量节点
        /// </summary>
        public List<TagConfig> ExportAllVariables(int maxDepth = 3)
        {
            if (_browser == null)
            {
                throw new InvalidOperationException("浏览器未初始化");
            }
            return _browser.ExportAllVariables(maxDepth);
        }
            return _browser.GetNodeDetail(nodeId);
        }
            return _browser.SearchNodes(searchTerm, maxResults);
        }
            return _browser.BrowseTree(nodeId, maxDepth);
        }
            return _browser.BrowseNode(nodeId, depth);
        }
            return _browser.BrowseRoot();
        }

        /// <summary>
        /// 浏览指定节点的子节点
        /// </summary>
        public async Task<List<OPCNode>> BrowseNodeAsync(string nodeId, int depth = 1)
        {
            // OPC DA browse is not supported in this implementation
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 浏览节点树
        /// </summary>
        public async Task<OPCNode> BrowseTreeAsync(string nodeId, int maxDepth = 3)
        {
            // OPC DA browse is not supported in this implementation
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 搜索节点
        /// </summary>
        public async Task<List<OPCNode>> SearchNodesAsync(string searchTerm, int maxResults = 1000)
        {
            // OPC DA browse is not supported in this implementation
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 获取节点详细信息
        /// </summary>
        public async Task<OPCNodeDetail> GetNodeDetailAsync(string nodeId)
        {
            // OPC DA browse is not supported in this implementation
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 导出所有变量节点
        /// </summary>
        public async Task<List<TagConfig>> ExportAllVariablesAsync(int maxDepth = 3)
        {
            // OPC DA browse is not supported in this implementation
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public void Dispose()
        {
            Stop();

            try
            {
                if (_opcGroup != null)
                {
                    var opcItems = _opcGroup.GetType().GetProperty("OPCItems").GetValue(_opcGroup);
                    var removeAllMethod = opcItems.GetType().GetMethod("RemoveAll");
                    removeAllMethod.Invoke(opcItems, null);
                }

                if (_opcServer != null)
                {
                    var opcGroups = _opcServer.GetType().GetProperty("OPCGroups").GetValue(_opcServer);
                    var removeAllMethod = opcGroups.GetType().GetMethod("RemoveAll");
                    removeAllMethod.Invoke(opcGroups, null);

                    var disconnectMethod = _opcServer.GetType().GetMethod("Disconnect");
                    disconnectMethod.Invoke(_opcServer, null);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"清理OPC资源失败: {ex.Message}", ex);
            }

            _opcGroup = null;
            _opcServer = null;

            _logger.Info("OPC服务已释放");
        }
    }

    /// <summary>
    /// OPC服务器状态枚举
    /// </summary>
    public enum OPCServerState
    {
        Running = 1,
        Failed = 2,
        NoConfig = 3,
        Suspended = 4,
        Test = 5
    }
}
