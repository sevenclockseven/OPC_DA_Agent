using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OPCAutomation;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC DA 数据采集服务
    /// 使用 OPC Automation COM 接口
    /// </summary>
    public class OPCService : IDisposable
    {
        private OPCServer _opcServer;
        private OPCGroup _opcGroup;
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

        public bool IsConnected => _opcServer != null && _opcServer.ServerState == (int)OPCServerState.Running;
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
                _opcServer = new OPCServer();

                // 连接到OPC服务器（使用ProgID）
                _opcServer.Connect(_config.OpcServerProgId);

                _logger.Info($"成功连接到OPC DA服务器: {_opcServer.ServerName}");

                // 创建OPC组
                _opcGroup = _opcServer.OPCGroups.Add("OPC_DA_Agent_Group");
                _opcGroup.UpdateRate = _config.UpdateInterval;
                _opcGroup.IsActive = true;
                _opcGroup.IsSubscribed = true;

                // 加载标签配置
                await LoadTagsAsync();

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
        private async Task LoadTagsAsync()
        {
            try
            {
                if (_config.TagsFile != null && System.IO.File.Exists(_config.TagsFile))
                {
                    var json = await System.IO.File.ReadAllTextAsync(_config.TagsFile);
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

                        _opcGroup.OPCItems.AddItems(
                            itemNames.Length,
                            itemNames,
                            clientHandles,
                            out serverHandles,
                            out itemIds
                        );

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
                _opcGroup.OPCItems.AddItems(
                    itemNames.Length,
                    itemNames,
                    clientHandles,
                    out serverHandles,
                    out itemIds
                );

                // 读取值
                object[] values;
                short[] qualities;
                DateTime[] timestamps;
                short[] errors;

                _opcGroup.Read(
                    OPCDataSource.OPC_DS_DEVICE,
                    out values,
                    out qualities,
                    out timestamps,
                    out errors
                );

                lock (_lock)
                {
                    for (int i = 0; i < tags.Count && i < values.Length; i++)
                    {
                        var tag = tags[i];
                        var value = values[i];
                        var error = errors[i];

                        if (error == 0) // Good
                        {
                            _lastValues[tag.NodeId] = value;
                        }
                        else
                        {
                            _logger.Debug($"读取标签 {tag.NodeId} 失败: 错误代码 {error}");
                        }
                    }
                }

                // 清理项
                _opcGroup.OPCItems.RemoveItems(itemIds.Length, itemIds);
            }
            catch (Exception ex)
            {
                _logger.Error($"批量读取失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取当前所有数据（键值对格式）
        /// </summary>
        public BatchKeyValueResponse GetCurrentData()
        {
            var data = new Dictionary<string, object>();
            var metadata = new Dictionary<string, TagMetadata>();
            var timestamp = DateTime.Now;

            lock (_lock)
            {
                foreach (var tag in _tags.Where(t => t.Enabled))
                {
                    if (_lastValues.TryGetValue(tag.NodeId, out var value) && value != null)
                    {
                        // 使用标签名称作为键
                        var key = tag.Name ?? tag.NodeId;
                        data[key] = value;

                        metadata[key] = new TagMetadata
                        {
                            DataType = tag.DataType,
                            Quality = "Good",
                            Timestamp = timestamp,
                            Status = "Good"
                        };
                    }
                }
            }

            return new BatchKeyValueResponse
            {
                BatchId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Count = data.Count,
                Data = data,
                Metadata = metadata,
                ElapsedMs = 0
            };
        }

        /// <summary>
        /// 获取当前所有数据（列表格式，兼容旧版本）
        /// </summary>
        public List<TagValue> GetCurrentDataList()
        {
            var result = new List<TagValue>();
            var timestamp = DateTime.Now;

            lock (_lock)
            {
                foreach (var tag in _tags.Where(t => t.Enabled))
                {
                    if (_lastValues.TryGetValue(tag.NodeId, out var value) && value != null)
                    {
                        result.Add(new TagValue
                        {
                            Key = tag.Name ?? tag.NodeId,
                            Value = value,
                            Quality = "Good",
                            Timestamp = timestamp,
                            Status = "Good",
                            DataType = tag.DataType,
                            NodeId = tag.NodeId,
                            Name = tag.Name ?? tag.NodeId
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 批量读取指定节点
        /// </summary>
        public async Task<List<TagValue>> ReadNodesAsync(List<string> nodeIds, int timeoutMs = 5000)
        {
            var result = new List<TagValue>();
            if (!IsConnected) return result;

            try
            {
                var itemNames = nodeIds.ToArray();
                var itemIds = new int[itemNames.Length];
                var serverHandles = new int[itemNames.Length];
                var clientHandles = new int[itemNames.Length];

                for (int i = 0; i < itemNames.Length; i++)
                {
                    clientHandles[i] = i + 1;
                }

                // 添加项到组
                _opcGroup.OPCItems.AddItems(
                    itemNames.Length,
                    itemNames,
                    clientHandles,
                    out serverHandles,
                    out itemIds
                );

                // 读取值
                object[] values;
                short[] qualities;
                DateTime[] timestamps;
                short[] errors;

                _opcGroup.Read(
                    OPCDataSource.OPC_DS_DEVICE,
                    out values,
                    out qualities,
                    out timestamps,
                    out errors
                );

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
        public async Task<bool> ReloadConfig()
        {
            try
            {
                Stop();

                await LoadTagsAsync();

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
        /// 浏览OPC服务器节点
        /// </summary>
        public async Task<List<OPCNode>> BrowseRootAsync()
        {
            // OPC DA browse is not supported in this implementation
            // Use OPC UA for browsing or implement OPC DA browse manually
            throw new NotImplementedException("OPC DA browsing is not implemented. Use OPC UA server for browsing.");
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
                _opcGroup?.OPCItems?.RemoveAll();
                _opcServer?.OPCGroups?.RemoveAll();
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
