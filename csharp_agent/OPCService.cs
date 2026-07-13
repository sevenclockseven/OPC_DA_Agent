using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OPCAutomation;

namespace OPC_DA_Agent
{
    public class OPCService : IDisposable
    {
        private IOPCAutoServer _opcServer;
        private IOPCGroups _opcGroups;
        private IOPCGroup _opcGroup;
        private OPCItems _opcItems;

        private List<TagConfig> _tags = new List<TagConfig>();
        private Dictionary<string, object> _lastValues = new Dictionary<string, object>();
        private Timer _updateTimer;
        private bool _isRunning;
        private object _lock = new object();

        private Array _serverHandles;
        private Array _errors;
        private int _cancelId;
        private int _transactionId = 1;

        private long _totalReads = 0;
        private long _totalErrors = 0;
        private DateTime _startTime;

        private readonly Logger _logger;
        private readonly Config _config;
        private readonly string _configPath;

        // 浏览结果按节点缓存，避免翻页时反复 COM 枚举（单次可达 3 秒）
        private readonly Dictionary<string, BrowseCacheEntry> _browseCache =
            new Dictionary<string, BrowseCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private class BrowseCacheEntry
        {
            public List<OPCNode> Nodes;
            public DateTime Time;
        }

        public string ConfigPath { get { return _configPath; } }

        public bool IsConnected
        {
            get
            {
                try { return _opcServer != null && _opcServer.ServerState == 1; }
                catch { return false; }
            }
        }

        public int TagCount
        {
            get { return _tags != null ? _tags.Count : 0; }
        }

        public long TotalReads { get { return _totalReads; } }
        public long TotalErrors { get { return _totalErrors; } }
        public DateTime StartTime { get { return _startTime; } }

        public OPCService(Config config, Logger logger, string configPath = null)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (logger == null) throw new ArgumentNullException("logger");

            _config = config;
            _logger = logger;
            _configPath = configPath ?? "config.json";
            _tags = config.Tags != null ? new List<TagConfig>(config.Tags) : new List<TagConfig>();
            _startTime = DateTime.Now;
        }

        public bool Connect()
        {
            try
            {
                _logger.Info(string.Format("正在连接到OPC服务器: {0}...", _config.OpcServerProgId));

                _opcServer = new OPCServer();

                string host = _config.OpcServerHost;
                if (!string.IsNullOrEmpty(host) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    _opcServer.Connect(_config.OpcServerProgId, host);
                }
                else
                {
                    _opcServer.Connect(_config.OpcServerProgId, null);
                }

                _logger.Info(string.Format("已连接到OPC服务器，State={0}", _opcServer.ServerState));
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("连接OPC服务器失败", ex);
                return false;
            }
        }

        public bool Start()
        {
            if (_opcServer == null || _opcServer.ServerState != 1)
            {
                _logger.Error("OPC服务器未连接，无法启动数据采集");
                return false;
            }

            try
            {
                _opcGroups = (IOPCGroups)_opcServer.OPCGroups;
                _opcGroups.DefaultGroupDeadband = 0;
                _opcGroups.DefaultGroupIsActive = true;

                _opcGroup = (IOPCGroup)_opcGroups.Add("DataGroup");
                _opcGroup.IsActive = true;
                _opcGroup.IsSubscribed = true;
                _opcGroup.UpdateRate = _config.UpdateInterval;

                _opcItems = _opcGroup.OPCItems;

                if (_tags.Count > 0)
                {
                    ApplyTags();
                }

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

        private void ApplyTags()
        {
            try
            {
                if (_opcItems == null) return;

                var opcItemIDs = new List<string>();
                var clientHandles = new List<int>();

                opcItemIDs.Add("");
                clientHandles.Add(0);

                foreach (var tag in _tags)
                {
                    if (tag.Enabled || tag.Active)
                    {
                        opcItemIDs.Add(tag.NodeId);
                        clientHandles.Add(opcItemIDs.Count);
                    }
                }

                if (opcItemIDs.Count > 1)
                {
                    Array itemsArray = opcItemIDs.ToArray();
                    Array handlesArray = clientHandles.ToArray();
                    Array serverHandles, errors;

                    _opcItems.AddItems(opcItemIDs.Count - 1, ref itemsArray, ref handlesArray,
                        out serverHandles, out errors, null, null);
                    _serverHandles = serverHandles;

                    _logger.Info(string.Format("已添加 {0}/{1} 个OPC标签", opcItemIDs.Count - 1, _tags.Count));

                    lock (_lock)
                    {
                        foreach (var tag in _tags)
                        {
                            if (tag.Enabled || tag.Active)
                            {
                                _lastValues[tag.NodeId] = null;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("添加OPC标签失败", ex);
            }
        }

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

        private void OnUpdateTimer(object state)
        {
            if (!_isRunning || _opcItems == null) return;

            try
            {
                int count = _opcItems.Count;
                if (count == 0) return;

                _opcGroup.AsyncRead(count, ref _serverHandles, out _errors, _transactionId++, out _cancelId);

                lock (_lock)
                {
                    var enabledTags = _tags.FindAll(t => t.Enabled || t.Active);
                    for (int i = 0; i < count && i < enabledTags.Count; i++)
                    {
                        try
                        {
                            OPCItem item = _opcItems.Item(i + 1);
                            object value, quality, timestamp;
                            item.Read(2, out value, out quality, out timestamp);
                            _lastValues[enabledTags[i].NodeId] = value;
                        }
                        catch { }
                    }
                }
                _totalReads++;
            }
            catch (Exception ex)
            {
                _totalErrors++;
                _logger.Error("更新数据时发生错误", ex);
            }
        }

        public StatusInfo GetStatus()
        {
            return new StatusInfo
            {
                IsConnected = IsConnected,
                TagCount = TagCount,
                TotalRequests = TotalReads,
                ErrorCount = TotalErrors,
                UptimeSeconds = (DateTime.Now - StartTime).TotalSeconds,
                MemoryUsageMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0)
            };
        }

        public object GetData()
        {
            var result = new Dictionary<string, object>();
            lock (_lock)
            {
                foreach (var kvp in _lastValues)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        public List<OPCNode> GetBrowseRoot()
        {
            return BrowsePath(null);
        }

        public List<OPCNode> BrowsePath(string nodeId)
        {
            if (_opcServer == null || _opcServer.ServerState != 1)
                throw new InvalidOperationException("未连接到OPC服务器");

            var result = new List<OPCNode>();
            try
            {
                // OPC Automation 没有 OPCBrowser 属性；用 CreateBrowser() 按 DispId 早期绑定获取浏览器对象
                OPCBrowser browser = _opcServer.CreateBrowser();
                if (browser == null) return result;

                browser.MoveToRoot();

                if (!string.IsNullOrEmpty(nodeId) && nodeId != "Root")
                {
                    string[] parts = nodeId.Split('.');
                    foreach (string part in parts)
                    {
                        // 标签（点号）可能出现在任意层级，并非所有节点都是可下钻的文件夹；
                        // MoveDown 失败时停留在已到达的最深位置，避免 E_FAIL 导致整体失败
                        try
                        {
                            browser.MoveDown(part);
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(string.Format("[Browse] 无法下钻到分支 {0}（可能是该层级的标签，非文件夹），停在当前位置: {1}", part, ex.Message));
                            break;
                        }
                    }
                }

                // 根命名空间可能是超大的扁平结构，逐个 COM 枚举很慢；加时间预算保证响应及时返回，
                // 同时分支（文件夹）优先，保证导航可用
                const int maxBrowseNodes = 5000;
                const int browseTimeBudgetMs = 3000;
                DateTime browseStart = DateTime.Now;
                bool truncated = false;

                browser.ShowBranches();
                foreach (string branch in browser)
                {
                    if (string.IsNullOrEmpty(branch)) continue;
                    if (result.Count >= maxBrowseNodes) { truncated = true; break; }
                    if ((DateTime.Now - browseStart).TotalMilliseconds > browseTimeBudgetMs) { truncated = true; break; }
                    string fullId = string.IsNullOrEmpty(nodeId) || nodeId == "Root" ? branch : nodeId + "." + branch;
                    result.Add(new OPCNode
                    {
                        NodeId = fullId,
                        Name = branch,
                        Description = "分支",
                        IsFolder = true,
                        HasChildren = true,
                        Children = new List<OPCNode>()
                    });
                }

                // Flat=true 会把整棵树的全部叶子摊平返回（数量可达数万），树形浏览只取当前节点的直接叶子
                if (!truncated)
                {
                    browser.ShowLeafs(false);
                    foreach (string leaf in browser)
                    {
                        if (string.IsNullOrEmpty(leaf)) continue;
                        if (result.Count >= maxBrowseNodes) { truncated = true; break; }
                        if ((DateTime.Now - browseStart).TotalMilliseconds > browseTimeBudgetMs) { truncated = true; break; }
                        string fullId = string.IsNullOrEmpty(nodeId) || nodeId == "Root" ? leaf : nodeId + "." + leaf;
                        string itemId = null;
                        try { itemId = browser.GetItemID(leaf); } catch { }
                        result.Add(new OPCNode
                        {
                            NodeId = fullId,
                            Name = leaf,
                            ItemId = itemId,
                            Description = "标签",
                            IsFolder = false,
                            HasChildren = false,
                            Children = null
                        });
                    }
                }

                if (truncated)
                    _logger.Warn(string.Format("[Browse] 节点数超过 {0}，已截断（nodeId={1}）", maxBrowseNodes, nodeId ?? "(root)"));
            }
            catch (Exception ex)
            {
                _logger.Error(string.Format("[Browse] 浏览节点失败: {0}", nodeId ?? "(root)"), ex);
            }

            return result;
        }

        public BrowseResult BrowsePaged(string nodeId, int offset, int limit)
        {
            string key = nodeId ?? "Root";
            List<OPCNode> all;
            lock (_lock)
            {
                if (_browseCache.TryGetValue(key, out var entry) &&
                    (DateTime.Now - entry.Time).TotalSeconds < 30)
                {
                    all = entry.Nodes;
                }
                else
                {
                    try
                    {
                        all = BrowsePath(nodeId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(string.Format("[Browse] 分页枚举失败 (nodeId={0}): {1}", key, ex.Message));
                        all = new List<OPCNode>();
                    }
                    _browseCache[key] = new BrowseCacheEntry { Nodes = all, Time = DateTime.Now };
                }
            }

            if (limit <= 0) limit = 50;
            if (offset < 0) offset = 0;
            var nodes = all.Skip(offset).Take(limit).ToList();
            return new BrowseResult
            {
                Nodes = nodes,
                Total = all.Count,
                Offset = offset,
                Limit = limit,
                HasMore = offset + limit < all.Count
            };
        }

        public void UpdateTags(List<TagConfig> newTags)
        {
            lock (_lock)
            {
                if (_opcItems != null)
                {
                    try
                    {
                        int count = _opcItems.Count;
                        if (count > 0)
                        {
                            Array handles = new object[count + 1];
                            for (int i = 1; i <= count; i++)
                            {
                                handles.SetValue(i, i);
                            }
                            Array errors;
                            _opcItems.Remove(count, ref handles, out errors);
                        }
                    }
                    catch { }
                }

                _tags = newTags;
                _lastValues.Clear();

                if (_opcItems != null && _tags.Count > 0)
                {
                    ApplyTags();
                }
            }

            _config.Tags = newTags;
            try
            {
                _config.SaveToFile(_configPath);
                _logger.Info(string.Format("标签配置已保存到 {0}（{1}个标签）", _configPath, newTags.Count));
            }
            catch (Exception ex)
            {
                _logger.Error("保存标签配置失败", ex);
            }
        }

        public List<TagConfig> GetTags()
        {
            return _tags;
        }

        public void Dispose()
        {
            Stop();
            _browseCache.Clear();
            if (_opcGroup != null)
            {
                try { _opcGroups.Remove("DataGroup"); } catch { }
                _opcGroup = null;
            }
            if (_opcServer != null)
            {
                try { _opcServer.Disconnect(); } catch { }
                _opcServer = null;
            }
        }
    }
}
