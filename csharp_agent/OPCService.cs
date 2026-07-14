using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OPCAutomation;

namespace OPC_DA_Agent
{
    public class OPCService : IDisposable
    {
        private IOPCAutoServer _opcServer;
        private IOPCGroups _opcGroups;
        private OPCGroup _opcGroup;
        private OPCItems _opcItems;

        private List<TagConfig> _tags = new List<TagConfig>();
        private Dictionary<string, object> _lastValues = new Dictionary<string, object>();
        private Timer _updateTimer;
        private object _lock = new object();
        private readonly SemaphoreSlim _browseSemaphore = new SemaphoreSlim(1, 1);

        private Array _serverHandles;

        // SSE 推送：已连接的流式客户端 + clientHandle→nodeId 映射
        private readonly List<StreamWriter> _sseClients = new List<StreamWriter>();
        private readonly object _sseLock = new object();
        private List<string> _clientHandleNodes = new List<string>();

        // SSE 发布：OnDataChange（运行在 OPC 的 COM STA 线程）只把变化入队后立即返回，
        // 由独立发布线程序列化并推流，避免 JSON 序列化 + 网络写入占用 STA 线程、饿死其它 COM 调用（如浏览）
        private readonly BlockingCollection<List<TagValue>> _sseQueue = new BlockingCollection<List<TagValue>>();
        private volatile bool _sseRunning = false;
        private Thread _ssePublisher;

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

        private string ResolveTagsFilePath()
        {
            var name = string.IsNullOrEmpty(_config.TagsFile) ? "tags.json" : _config.TagsFile;
            if (Path.IsPathRooted(name)) return name;
            var dir = Path.GetDirectoryName(_configPath);
            return Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, name);
        }

        private List<TagConfig> LoadTagsFromFile()
        {
            var path = ResolveTagsFilePath();
            if (File.Exists(path))
            {
                try
                {
                    var tags = JsonConvert.DeserializeObject<List<TagConfig>>(File.ReadAllText(path));
                    if (tags != null) return tags;
                }
                catch (Exception ex)
                {
                    _logger.Error("读取标签文件失败: " + path, ex);
                }
            }
            return new List<TagConfig>();
        }

        private void SaveTagsToFile()
        {
            try
            {
                var path = ResolveTagsFilePath();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(_tags, Formatting.Indented));
                _logger.Info(string.Format("标签已保存到 {0}（{1}个）", path, _tags.Count));
            }
            catch (Exception ex)
            {
                _logger.Error("保存标签文件失败", ex);
            }
        }

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
            _tags = LoadTagsFromFile();
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

                _opcGroup = (OPCGroup)_opcGroups.Add("DataGroup");
                _opcGroup.IsActive = true;
                _opcGroup.IsSubscribed = true;
                _opcGroup.UpdateRate = _config.UpdateInterval;

                _opcItems = _opcGroup.OPCItems;

                if (_tags.Count > 0)
                {
                    ApplyTags();
                }

                _opcGroup.DataChange += OnDataChange;

                _sseRunning = true;
                _ssePublisher = new Thread(SsePublishLoop) { IsBackground = true, Name = "SsePublisher" };
                _ssePublisher.Start();

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
                var nodeByHandle = new List<string>();

                opcItemIDs.Add("");
                clientHandles.Add(0);
                nodeByHandle.Add("");   // 句柄 0 为占位

                foreach (var tag in _tags)
                {
                    if (tag.Enabled || tag.Active)
                    {
                        opcItemIDs.Add(tag.NodeId);
                        clientHandles.Add(opcItemIDs.Count - 1);  // 句柄 = 1-based 项索引
                        nodeByHandle.Add(tag.NodeId);
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

                    _clientHandleNodes = nodeByHandle;

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

                    // 首读一次填充当前值，避免订阅首包到达前 /api/data 为空
                    try
                    {
                        int count = _opcItems.Count;
                        for (int i = 1; i <= count; i++)
                        {
                            try
                            {
                                OPCItem item = _opcItems.Item(i);
                                object v, q, t;
                                item.Read(2, out v, out q, out t);
                                if (i - 1 < _clientHandleNodes.Count && !string.IsNullOrEmpty(_clientHandleNodes[i]))
                                    _lastValues[_clientHandleNodes[i]] = v;
                            }
                            catch { }
                        }
                        _logger.Info("OPC 标签初始值读取完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("OPC 标签初始值读取失败（将由订阅推送补齐）: " + ex.Message);
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
            _sseRunning = false;
            if (_opcGroup != null)
            {
                try { _opcGroup.DataChange -= OnDataChange; } catch { }
            }
            if (_updateTimer != null)
            {
                _updateTimer.Dispose();
                _updateTimer = null;
            }
            lock (_sseLock)
            {
                foreach (var w in _sseClients)
                {
                    try { w.Dispose(); } catch { }
                }
                _sseClients.Clear();
            }
            _logger.Info("OPC数据采集已停止");
        }

        private void OnDataChange(int transactionId, int numItems, ref Array clientHandles,
            ref Array itemValues, ref Array qualities, ref Array timeStamps)
        {
            try
            {
                var changed = new List<TagValue>();
                for (int i = 0; i < numItems; i++)
                {
                    int handle = 0;
                    try { handle = Convert.ToInt32(clientHandles.GetValue(i)); } catch { }
                    if (handle < 0 || handle >= _clientHandleNodes.Count) continue;
                    string nodeId = _clientHandleNodes[handle];
                    if (string.IsNullOrEmpty(nodeId)) continue;

                    object value = itemValues.GetValue(i);
                    int q = 0;
                    try { q = Convert.ToInt32(qualities.GetValue(i)); } catch { }
                    string qualityStr = (q & 0xC0) == 0xC0 ? "Good" : "Bad";
                    DateTime timestamp = timeStamps.GetValue(i) is DateTime ? (DateTime)timeStamps.GetValue(i) : DateTime.Now;

                    lock (_lock)
                    {
                        _lastValues[nodeId] = value;
                    }
                    changed.Add(new TagValue
                    {
                        Key = nodeId,
                        Value = value,
                        Quality = qualityStr,
                        Timestamp = timestamp,
                        Status = qualityStr,
                        DataType = value == null ? null : value.GetType().Name,
                        NodeId = nodeId,
                        Name = nodeId
                    });
                }

                if (changed.Count > 0)
                {
                    System.Threading.Interlocked.Increment(ref _totalReads);
                    // 仅入队：序列化与网络写入交给 SsePublishLoop，避免占用 COM STA 线程饿死浏览等调用
                    bool hasClients;
                    lock (_sseLock) hasClients = _sseClients.Count > 0;
                    if (hasClients) _sseQueue.Add(changed);
                }
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.Increment(ref _totalErrors);
                _logger.Error("处理 OPC 数据变更失败", ex);
            }
        }

        public void AddSseClient(StreamWriter writer)
        {
            lock (_sseLock) _sseClients.Add(writer);
        }

        public void RemoveSseClient(StreamWriter writer)
        {
            lock (_sseLock) _sseClients.Remove(writer);
        }

        private void BroadcastSse(List<TagValue> values)
        {
            var payload = JsonConvert.SerializeObject(new { ts = DateTime.Now, values = values });
            var line = "data: " + payload + "\n\n";
            List<StreamWriter> dead = null;
            lock (_sseLock)
            {
                foreach (var w in _sseClients)
                {
                    try
                    {
                        w.Write(line);
                        w.Flush();
                    }
                    catch
                    {
                        if (dead == null) dead = new List<StreamWriter>();
                        dead.Add(w);
                    }
                }
                if (dead != null)
                {
                    foreach (var d in dead) _sseClients.Remove(d);
                }
            }
        }

        // 独立发布线程：从队列取批次并推送给所有 SSE 客户端，与 OnDataChange(COM STA 线程) 解耦
        private void SsePublishLoop()
        {
            while (_sseRunning)
            {
                List<TagValue> batch;
                if (_sseQueue.TryTake(out batch, 200))
                {
                    try { BroadcastSse(batch); }
                    catch (Exception ex) { _logger.Error("SSE 发布失败", ex); }
                }
            }
            List<TagValue> remaining;
            while (_sseQueue.TryTake(out remaining))
            {
                try { BroadcastSse(remaining); } catch { }
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

            // 快速路径：先在不加锁的情况下查缓存
            lock (_lock)
            {
                BrowseCacheEntry entry;
                if (_browseCache.TryGetValue(key, out entry) &&
                    (DateTime.Now - entry.Time).TotalSeconds < 30)
                {
                    all = entry.Nodes;
                }
                else
                {
                    all = null;
                }
            }

            if (all == null)
            {
                // OPC COM 枚举（CreateBrowser/ShowBranches/ShowLeafs）可能很慢甚至挂起，
                // 故放进独立任务并限时，且用 SemaphoreSlim 串行化，避免多个浏览把线程池耗尽；
                // 同时把 COM 工作移出 _lock，避免阻塞 OnDataChange / GetData / SSE 推送。
                try
                {
                    _browseSemaphore.Wait(7000);
                    try
                    {
                        var task = Task.Factory.StartNew(() => BrowsePath(nodeId));
                        if (!task.Wait(7000))
                        {
                            _logger.Warn(string.Format("[Browse] 枚举超时 (nodeId={0})，返回空结果", key));
                            all = new List<OPCNode>();
                        }
                        else
                        {
                            all = task.Result;
                        }
                    }
                    finally
                    {
                        _browseSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(string.Format("[Browse] 分页枚举失败 (nodeId={0}): {1}", key, ex.Message));
                    all = new List<OPCNode>();
                }

                lock (_lock)
                {
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

            SaveTagsToFile();
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
