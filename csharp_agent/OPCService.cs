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

        // 获取类型信息并创建实例
        var serverType = Type.GetTypeFromProgID(_config.OpcServerProgId);
        if (serverType == null)
        {
            _logger.Error($"无法获取ProgID '{_config.OpcServerProgId}' 对应的类型。");
            return false;
        }

        _logger.Info($"获取到的服务器类型: {serverType.FullName}");
        _opcServer = Activator.CreateInstance(serverType);
        if (_opcServer == null)
        {
            _logger.Error($"无法创建ProgID '{_config.OpcServerProgId}' 对应的实例。");
            return false;
        }

        _logger.Info($"成功创建OPC服务器实例: {_config.OpcServerProgId}");

        // 尝试获取 OPCGroups 属性
        var opcGroupsProperty = _opcServer.GetType().GetProperty("OPCGroups");
        if (opcGroupsProperty == null)
        {
            _logger.Error("无法获取OPCGroups属性。");
            return false;
        }
        _logger.Info("成功获取OPCGroups属性。");

        var opcGroups = opcGroupsProperty.GetValue(_opcServer);
        if (opcGroups == null)
        {
            _logger.Error("获取到的OPCGroups对象为null。");
            return false;
        }
        _logger.Info("成功获取OPCGroups对象。");

  
                    return false;
                }

                var addMethod = opcGroups.GetType().GetMethod("Add");
                if (addMethod == null)
                {
                    _logger.Error("无法获取OPCGroups.Add方法。");
                    return false;
                }

                _opcGroup = addMethod.Invoke(opcGroups, new object[] { "OPC_DA_Agent_Group" });
                if (_opcGroup == null)
                {
                    _logger.Error("无法创建OPC组。");
                    return false;
                }

                _logger.Info($"成功创建OPC组: OPC_DA_Agent_Group");

                // 设置组属性
                var updateRateProperty = _opcGroup.GetType().GetProperty("UpdateRate");
                if (updateRateProperty != null)
                {
                    updateRateProperty.SetValue(_opcGroup, _config.UpdateInterval);
                }

                var isActiveProperty = _opcGroup.GetType().GetProperty("IsActive");
                if (isActiveProperty != null)
                {
                    isActiveProperty.SetValue(_opcGroup, true);
                }

                var isSubscribedProperty = _opcGroup.GetType().GetProperty("IsSubscribed");
                if (isSubscribedProperty != null)
                {
                    isSubscribedProperty.SetValue(_opcGroup, true);
                }

                // 加载标签配置
                LoadTags();

                _logger.Info($"成功连接到OPC DA服务器: {_config.OpcServerProgId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"连接OPC DA服务器失败: {ex.Message}", ex);
                // 记录更详细的堆栈信息有助于定位问题
                _logger.Error($"异常堆栈: {ex.StackTrace}");
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
                        if (_opcGroup == null)
                        {
                            _logger.Error("无法添加标签：OPC组未初始化。");
                            _isRunning = false;
                            return false;
                        }

                        var itemNames = enabledTags.Select(t => t.NodeId).ToArray();
                        var itemIds = new int[itemNames.Length];
                        var serverHandles = new int[itemNames.Length];
                        var clientHandles = new int[itemNames.Length];

                        for (int i = 0; i < itemNames.Length; i++)
                        {
                            clientHandles[i] = i + 1;
                        }

                        var opcItems = _opcGroup.GetType().GetProperty("OPCItems").GetValue(_opcGroup);
                        if (opcItems == null)
                        {
                            _logger.Error("无法获取OPCItems对象。");
                            _isRunning = false;
                            return false;
                        }

                        var addItemsMethod = opcItems.GetType().GetMethod("AddItems");
                        if (addItemsMethod == null)
                        {
                            _logger.Error("无法获取OPCItems.AddItems方法。");
                            _isRunning = false;
                            return false;
                        }

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

                // 创建定时器 - 修正异步回调问题
                _updateTimer = new Timer(
                    state => { _ = Task.Run(UpdateDataAsync); }, // 在后台任务中运行异步方法
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
                _logger.Error($"异常堆栈: {ex.StackTrace}");
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
            await Task.CompletedTask; // 避免CS1998警告

            try
            {
                var nodeIds = tags.Select(t => t.NodeId).ToList();

                object[] values = null;
                short[] qualities = null;
                DateTime[] timestamps = null;
                short[] errors = null;

                var readMethod = _opcGroup.GetType().GetMethod("SyncRead");
                if (readMethod == null)
                {
                    _logger.Error("无法获取OPCGroup.SyncRead方法。");
                    return;
                }

                var readParameters = new object[]
                {
                    1, // OPC_DS_DEVICE = 1
                    nodeIds.Count,
                    nodeIds.ToArray(),
                    values,
                    qualities,
                    timestamps,
                    errors
                };
                readMethod.Invoke(_opcGroup, readParameters);

                values = (object[])readParameters[3];
                qualities = (short[])readParameters[4];
                timestamps = (DateTime[])readParameters[5];
                errors = (short[])readParameters[6];

                var timestamp = DateTime.Now;

                for (int i = 0; i < nodeIds.Count && i < values.Length; i++)
                {
                    var value = values[i];
                    var error = errors[i];
                    var quality = qualities[i]; // 使用 SyncRead 获取的质量

                    var qualityStr = (quality & 0xC0) == 0xC0 ? "Good" : "Bad"; // 简单判断质量
                    var statusStr = error == 0 ? "Good" : $"Error: {error:X4}"; // 错误码转十六进制

                    // 更新缓存
                    lock (_lock)
                    {
                        _lastValues[nodeIds[i]] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"批量读取节点失败: {ex.Message}", ex);
                _totalErrors++;
            }
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

        #region 新增方法

        /// <summary>
        /// 获取当前数据
        /// </summary>
        public Dictionary<string, object> GetCurrentData()
        {
            lock (_lock)
            {
                return new Dictionary<string, object>(_lastValues);
            }
        }

        /// <summary>
        /// 获取当前数据列表
        /// </summary>
        public List<TagValue> GetCurrentDataList()
        {
            var result = new List<TagValue>();

            lock (_lock)
            {
                foreach (var kvp in _lastValues)
                {
                    if (kvp.Value != null)
                    {
                        result.Add(new TagValue
                        {
                            NodeId = kvp.Key,
                            Name = kvp.Key,
                            Value = kvp.Value,
                            Quality = "Good", // 可以从缓存中也存储质量信息
                            Timestamp = DateTime.Now,
                            Status = "Good", // 可以从缓存中也存储状态信息
                            DataType = kvp.Value.GetType().Name
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 读取指定节点
        /// </summary>
        public async Task<List<TagValue>> ReadNodesAsync(List<string> nodeIds)
        {
            await Task.CompletedTask; // 避免CS1998警告

            var result = new List<TagValue>();

            try
            {
                // 批量读取
                var batchSize = _config.BatchSize;
                for (int i = 0; i < nodeIds.Count; i += batchSize)
                {
                    var batch = nodeIds.Skip(i).Take(batchSize).ToList();
                    var batchResult = await ReadBatchAsyncWithNodeIds(batch);
                    result.AddRange(batchResult);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error($"读取节点失败: {ex.Message}", ex);
                _totalErrors++;
                return result;
            }
        }

        /// <summary>
        /// 批量读取指定节点
        /// </summary>
        private async Task<List<TagValue>> ReadBatchAsyncWithNodeIds(List<string> nodeIds)
        {
            await Task.CompletedTask; // 避免CS1998警告

            var result = new List<TagValue>();

            try
            {
                var opcItems = _opcGroup.GetType().GetProperty("OPCItems").GetValue(_opcGroup);

                // 添加临时项
                var itemNames = nodeIds.ToArray();
                var serverHandles = new int[itemNames.Length];
                var clientHandles = new int[itemNames.Length];

                for (int i = 0; i < itemNames.Length; i++)
                {
                    clientHandles[i] = i + 1;
                }

                var addItemsMethod = opcItems.GetType().GetMethod("AddItems");
                var parameters = new object[]
                {
                    itemNames.Length,
                    itemNames,
                    clientHandles,
                    serverHandles,
                    new int[itemNames.Length]
                };
                addItemsMethod.Invoke(opcItems, parameters);

                // 读取值
                object[] values = null;
                short[] qualities = null;
                DateTime[] timestamps = null;
                short[] errors = null;

                var syncReadMethod = _opcGroup.GetType().GetMethod("SyncRead");
                var readParameters = new object[]
                {
                    1, // OPC_DS_DEVICE = 1
                    nodeIds.Count,
                    itemNames,
                    values,
                    qualities,
                    timestamps,
                    errors
                };

                try
                {
                    syncReadMethod.Invoke(_opcGroup, readParameters);

                    values = (object[])readParameters[3];
                    qualities = (short[])readParameters[4];
                    timestamps = (DateTime[])readParameters[5];
                    errors = (short[])readParameters[6];

                    var timestamp = DateTime.Now;

                    for (int i = 0; i < nodeIds.Count && i < values.Length; i++)
                    {
                        var value = values[i];
                        var error = errors[i];
                        var quality = qualities[i];

                        var qualityStr = (quality & 0xC0) == 0xC0 ? "Good" : "Bad"; // 简单判断质量
                        var statusStr = error == 0 ? "Good" : $"Error: {error:X4}"; // 错误码转十六进制

                        result.Add(new TagValue
                        {
                            NodeId = nodeIds[i],
                            Name = nodeIds[i],
                            Value = value,
                            Quality = qualityStr,
                            Timestamp = timestamps[i],
                            Status = statusStr,
                            DataType = value?.GetType().Name ?? "Unknown"
                        });
                    }
                }
                finally
                {
                    // 移除临时项
                    var removeItemsMethod = opcItems.GetType().GetMethod("RemoveItems");
                    var removeParams = new object[]
                    {
                        serverHandles.Length,
                        serverHandles
                    };
                    removeItemsMethod.Invoke(opcItems, removeParams);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"批量读取节点失败: {ex.Message}", ex);
                _totalErrors++;
            }

            return result;
        }

        // 添加浏览相关的方法（返回空实现，因为OPC DA不支持浏览）
        public async Task<List<OPCNode>> BrowseRootAsync()
        {
            await Task.CompletedTask; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public async Task<List<OPCNode>> BrowseNodeAsync(string nodeId, int depth = 1)
        {
            await Task.CompletedTask; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public async Task<OPCNode> BrowseTreeAsync(string nodeId, int maxDepth = 3)
        {
            await Task.CompletedTask; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public async Task<List<OPCNode>> SearchNodesAsync(string searchTerm, int maxResults = 1000)
        {
            await Task.CompletedTask; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public async Task<OPCNodeDetail> GetNodeDetailAsync(string nodeId)
        {
            await Task.CompletedTask; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public async Task<List<TagConfig>> ExportAllVariablesAsync(int maxDepth = 3)
        {
            await Task.CompletedTask; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public async Task<bool> ReloadConfigAsync()
        {
            await Task.CompletedTask; // 避免CS1998警告
            return ReloadConfig();
        }

        #endregion

        /// <summary>
        /// 浏览OPC服务器节点 - OPC DA不支持浏览，抛出异常
        /// </summary>
        public List<OPCNode> BrowseRoot()
        {
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 浏览指定节点的子节点 - OPC DA不支持浏览，抛出异常
        /// </summary>
        public List<OPCNode> BrowseNode(string nodeId, int depth = 1)
        {
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 浏览节点树 - OPC DA不支持浏览，抛出异常
        /// </summary>
        public OPCNode BrowseTree(string nodeId, int maxDepth = 3)
        {
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 搜索节点 - OPC DA不支持搜索，抛出异常
        /// </summary>
        public List<OPCNode> SearchNodes(string searchTerm, int maxResults = 1000)
        {
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 获取节点详细信息 - OPC DA不支持，抛出异常
        /// </summary>
        public OPCNodeDetail GetNodeDetail(string nodeId)
        {
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        /// <summary>
        /// 导出所有变量节点 - OPC DA不支持，抛出异常
        /// </summary>
        public List<TagConfig> ExportAllVariables(int maxDepth = 3)
        {
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
                    if (opcItems != null)
                    {
                        var removeAllMethod = opcItems.GetType().GetMethod("RemoveAll");
                        if (removeAllMethod != null)
                        {
                            removeAllMethod.Invoke(opcItems, null);
                        }
                    }
                }

                if (_opcServer != null)
                {
                    var opcGroups = _opcServer.GetType().GetProperty("OPCGroups").GetValue(_opcServer);
                    if (opcGroups != null)
                    {
                        var removeAllMethod = opcGroups.GetType().GetMethod("RemoveAll");
                        if (removeAllMethod != null)
                        {
                            removeAllMethod.Invoke(opcGroups, null);
                        }
                    }

                    var disconnectMethod = _opcServer.GetType().GetMethod("Disconnect");
                    if (disconnectMethod != null)
                    {
                        disconnectMethod.Invoke(_opcServer, null);
                    }
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