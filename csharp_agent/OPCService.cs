using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using OpcNetApi;
using OpcNetApi.Com;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC DA 数据采集服务
    /// 使用 OPC .NET API
    /// </summary>
    public class OPCService : IDisposable
    {
        private Server _opcServer; // 使用 OPC .NET API 的 Server 类
        private Group _opcGroup;   // 使用 OPC .NET API 的 Group 类
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
        public bool<bool> ConnectAsync()
        {
            try
            {
                _logger.Info($"正在连接到OPC DA服务器: {_config.OpcServerProgId}");

                // 使用 OPC .NET API 创建服务器对象
                _opcServer = new Server(_config.OpcServerProgId);
                _opcServer.Connect();

                _logger.Info($"成功连接到OPC DA服务器: {_config.OpcServerProgId}");

                // 创建OPC组
                _opcGroup = _opcServer.CreateGroup("OPC_DA_Agent_Group", true, _config.UpdateInterval, null, null, null, null);
                _opcGroup.IsActive = true;
                _opcGroup.IsSubscribed = true;

                _logger.Info($"成功创建并激活OPC组: OPC_DA_Agent_Group");

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
                        var results = _opcGroup.AddItems(itemNames, null, null); // API 会自动分配句柄

                        // 检查添加结果 (results 中包含句柄和可能的错误信息)
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (results[i].ResultID.Succeeded())
                            {
                                _logger.Debug($"成功添加标签: {itemNames[i]} (Handle: {results[i].ServerHandle})");
                            }
                            else
                            {
                                _logger.Error($"添加标签失败: {itemNames[i]}, ResultID: {results[i].ResultID}");
                            }
                        }

                        _logger.Info($"已尝试添加 {enabledTags.Count} 个标签到OPC组");
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
        private bool UpdateDataAsync()
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
                     ReadBatchAsync(batch);
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
        private bool ReadBatchAsync(List<TagConfig> tags)
        {
             void; // 避免CS1998警告

            try
            {
                var nodeIds = tags.Select(t => t.NodeId).ToArray(); // API 通常需要数组

                // 使用 API 读取数据
                var results = _opcGroup.SyncRead((int)OpcDataSource.CacheOrDevice, nodeIds, null); // null for default handles

                for (int i = 0; i < nodeIds.Length && i < results.Length; i++)
                {
                    if (results[i].ResultID.Succeeded())
                    {
                        var value = results[i].Value;
                        var quality = results[i].Quality;
                        var timestamp = results[i].Timestamp;

                        // 更新缓存
                        lock (_lock)
                        {
                            _lastValues[nodeIds[i]] = value;
                        }
                    }
                    else
                    {
                        _logger.Error($"读取标签失败: {nodeIds[i]}, ResultID: {results[i].ResultID}");
                        _totalErrors++; // 计入错误
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

        #region 新增方法 (使用 API)

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
                            Quality = "Good", // API 可以提供更详细的 Quality
                            Timestamp = DateTime.Now,
                            Status = "Good", // API 可以提供更详细的 Status
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
        public bool<List<TagValue>> ReadNodesAsync(List<string> nodeIds)
        {
             void; // 避免CS1998警告

            var result = new List<TagValue>();

            try
            {
                // 批量读取
                var batchSize = _config.BatchSize;
                for (int i = 0; i < nodeIds.Count; i += batchSize)
                {
                    var batch = nodeIds.Skip(i).Take(batchSize).ToList();
                    var batchResult =  ReadBatchAsyncWithNodeIds(batch);
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
        private bool<List<TagValue>> ReadBatchAsyncWithNodeIds(List<string> nodeIds)
        {
             void; // 避免CS1998警告

            var result = new List<TagValue>();

            try
            {
                // 创建临时组和项来读取
                var tempGroupName = $"TempGroup_{Guid.NewGuid()}";
                var tempGroup = _opcServer.CreateGroup(tempGroupName, false, 1000, null, null, null, null); // 短暂的更新周期
                tempGroup.IsActive = true;

                var itemsToAdd = nodeIds.ToArray();
                var addResults = tempGroup.AddItems(itemsToAdd, null, null);

                var readResults = tempGroup.SyncRead((int)OpcDataSource.CacheOrDevice, itemsToAdd, null);

                for (int i = 0; i < itemsToAdd.Length && i < readResults.Length; i++)
                {
                    if (readResults[i].ResultID.Succeeded())
                    {
                        result.Add(new TagValue
                        {
                            NodeId = itemsToAdd[i],
                            Name = itemsToAdd[i],
                            Value = readResults[i].Value,
                            Quality = readResults[i].Quality.ToString(), // API Quality
                            Timestamp = readResults[i].Timestamp,
                            Status = readResults[i].ResultID.ToString(), // API ResultID as Status
                            DataType = readResults[i].Value?.GetType().Name ?? "Unknown"
                        });
                    }
                    else
                    {
                        _logger.Error($"读取临时项失败: {itemsToAdd[i]}, ResultID: {readResults[i].ResultID}");
                    }
                }

                // 清理临时组
                tempGroup.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error($"批量读取节点失败: {ex.Message}", ex);
                _totalErrors++;
            }

            return result;
        }

        // 添加浏览相关的方法（返回空实现，因为OPC DA不支持浏览）
        public bool<List<OPCNode>> BrowseRootAsync()
        {
             void; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public bool<List<OPCNode>> BrowseNodeAsync(string nodeId, int depth = 1)
        {
             void; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public bool<OPCNode> BrowseTreeAsync(string nodeId, int maxDepth = 3)
        {
             void; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public bool<List<OPCNode>> SearchNodesAsync(string searchTerm, int maxResults = 1000)
        {
             void; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public bool<OPCNodeDetail> GetNodeDetailAsync(string nodeId)
        {
             void; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public bool<List<TagConfig>> ExportAllVariablesAsync(int maxDepth = 3)
        {
             void; // 避免CS1998警告
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
        }

        public bool<bool> ReloadConfigAsync()
        {
             void; // 避免CS1998警告
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
                _opcGroup?.Dispose();
                _opcServer?.Disconnect();
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