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
        private readonly object _reconnectLock = new object();
        // 保存/导入标签的串行化：UpdateTags 内 Remove/Add 为多次 COM 调用，并发保存会交叉增删 items 导致句柄映射错乱
        private readonly object _updateTagsLock = new object();
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
        private System.Threading.Timer _snapshotTimer;

        // OPC 自动重连看门狗：仅 !IsConnected 时调用已有 Reconnect()；Stop/Dispose 置位后不再动作
        private System.Threading.Timer _reconnectTimer;
        private volatile bool _autoReconnectStopped = false;
        private int _reconnectFailCount = 0;
        private const int ReconnectBackoffCapMs = 60000;

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

        // 数据面 key 统一为服务器权威 ItemID：浏览路径 node_id 带根节点前缀（可能含非 ASCII
        // 乱码），不是 OPC 实际标签名，不应作为下游 key（SSE/_lastValues/GET /api/tags）
        private static void NormalizeTags(List<TagConfig> tags)
        {
            if (tags == null) return;
            foreach (var tag in tags)
            {
                if (tag != null && !string.IsNullOrWhiteSpace(tag.ItemId))
                    tag.NodeId = tag.ItemId;
            }
        }

        // SAFEARRAY 元素转 int：越界/类型不符返回 false 由调用方决定跳过或计数，
        // 避免在回调/批量循环里写空 catch 吞错
        private static bool TryGetInt(Array arr, int index, out int value)
        {
            value = 0;
            if (arr == null || index < 0 || index >= arr.Length) return false;
            try { value = Convert.ToInt32(arr.GetValue(index)); return true; }
            catch (Exception) { return false; }
        }

        private List<TagConfig> LoadTagsFromFile()
        {
            var path = ResolveTagsFilePath();
            if (File.Exists(path))
            {
                try
                {
                    var tags = JsonConvert.DeserializeObject<List<TagConfig>>(File.ReadAllText(path));
                    if (tags != null)
                    {
                        NormalizeTags(tags);
                        return tags;
                    }
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
                EnsureAutoReconnect();
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
                // OPC 规范：inactive 项不参与 DataChange 回调；AddItems 新项的默认激活态由此属性决定，
                // 不显式置 true 时新订阅项可能永远收不到推送（"订阅成功但无数据"嫌疑之一）
                _opcItems.DefaultIsActive = true;

                if (_tags.Count > 0)
                {
                    ApplyTags();
                }

                _opcGroup.DataChange += OnDataChange;

                // 重连会再次进入本方法：SSE 发布线程若已运行则复用，避免双线程同时消费队列
                if (!_sseRunning)
                {
                    _sseRunning = true;
                    _ssePublisher = new Thread(SsePublishLoop) { IsBackground = true, Name = "SsePublisher" };
                    _ssePublisher.Start();
                }

                // 确定性秒级采样时钟：按固定节拍把当前最新值全量推一次 SSE，保证值不变时也持续采集。
                // 用缓存的 _lastValues 直接做快照，不发起任何 COM 调用，避免占用/阻塞 OPC 的 STA 线程。
                if (_config.SseSnapshotIntervalMs > 0)
                {
                    _snapshotTimer = new System.Threading.Timer(SnapshotTick, null,
                        _config.SseSnapshotIntervalMs, _config.SseSnapshotIntervalMs);
                    _logger.Info(string.Format("已启动 SSE 秒级快照推送，间隔={0}ms", _config.SseSnapshotIntervalMs));
                }

                _logger.Info("OPC数据采集已启动");
                EnsureAutoReconnect();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error("启动数据采集失败", ex);
                EnsureAutoReconnect();
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
                        // 订阅用服务器权威 ItemID；旧数据无 item_id 时回退浏览路径（兼容存量 tags.json）
                        string subscribeId = string.IsNullOrEmpty(tag.ItemId) ? tag.NodeId : tag.ItemId;
                        opcItemIDs.Add(subscribeId);
                        clientHandles.Add(opcItemIDs.Count - 1);  // 句柄 = 1-based 项索引
                        // 数据面 key（SSE/_lastValues/_clientHandleNodes）用 NodeId；
                        // NormalizeTags 已把有 ItemId 的标签 NodeId 规范为 ItemID，key 即 OPC 真实标签名
                        nodeByHandle.Add(tag.NodeId);
                    }
                }

                // 先更新句柄映射与缓存，再发起 AddItems：OnDataChange 可能在 AddItems 返回后、
                // 下一个 UpdateRate 节拍即到达，映射必须先于服务器回调就绪，否则新句柄会误映射到旧节点。
                // 此处不发起任何 COM 调用，持 _lock 不会与 STA 线程上的 OnDataChange 死锁。
                var activeSet = new HashSet<string>();
                foreach (var tag in _tags)
                    if (tag.Enabled || tag.Active) activeSet.Add(tag.NodeId);
                lock (_lock)
                {
                    _clientHandleNodes = nodeByHandle;
                    // 合并而非清空：删除不再订阅的节点，保留仍订阅节点的最近值，新节点 null 占位，
                    // 保证保存/重订阅期间下游快照数据连续
                    var stale = _lastValues.Keys.Where(k => !activeSet.Contains(k)).ToList();
                    foreach (var k in stale) _lastValues.Remove(k);
                    foreach (var tag in _tags)
                        if ((tag.Enabled || tag.Active) && !_lastValues.ContainsKey(tag.NodeId))
                            _lastValues[tag.NodeId] = null;
                }

                // AddItems 是 COM 调用，必须在锁外执行：该调用被封送到 OPC 的宿主 STA 线程，
                // 若持 _lock 期间发起，会与同样需要 _lock 的 OnDataChange（也在该 STA 线程）形成死锁。
                // 不做逐项 Read：逐项的 O(N) 串行 COM 往返是大标签集保存超时的根因（6f53daa 已删）；
                // 初值改用下方组级批量 SyncRead（一次 COM 往返）兜底。
                Array serverHandles = null;
                if (opcItemIDs.Count > 1)
                {
                    Array itemsArray = opcItemIDs.ToArray();
                    Array handlesArray = clientHandles.ToArray();
                    Array errors;
                    _opcItems.AddItems(opcItemIDs.Count - 1, ref itemsArray, ref handlesArray,
                        out serverHandles, out errors, null, null);
                    _serverHandles = serverHandles;

                    // 逐项检查 AddItems 错误码（与句柄数组同为 1-based 对齐，索引 0 为占位）：
                    // ItemID 不被服务器接受时此前是静默失败，用户无法从日志判断标签是否真正订阅成功
                    int failCount = 0;
                    int handleParseFail = 0;
                    var syncIdx = new List<int>();       // 成功项原索引（映射 nodeByHandle）
                    var syncHandles = new List<int>();   // 对应真实 server handle（SyncRead 子集）
                    if (errors != null)
                    {
                        for (int i = 1; i < errors.Length && i < opcItemIDs.Count; i++)
                        {
                            int err;
                            try { err = Convert.ToInt32(errors.GetValue(i)); }
                            catch (Exception ex)
                            {
                                _logger.Warn(string.Format("解析标签添加错误码失败 [{0}]: {1}", opcItemIDs[i], ex.Message));
                                continue;
                            }
                            if (err != 0)
                            {
                                failCount++;
                                _logger.Warn(string.Format("OPC标签添加失败 key=[{0}] ItemID={1} 错误=0x{2:X8}",
                                    i < nodeByHandle.Count ? nodeByHandle[i] : "?", opcItemIDs[i], err));
                            }
                            else if (serverHandles != null)
                            {
                                // 成功项收集真实句柄供批量 SyncRead；失败项句柄为 0 需过滤
                                int sh;
                                if (TryGetInt(serverHandles, i, out sh) && sh > 0)
                                {
                                    syncIdx.Add(i);
                                    syncHandles.Add(sh);
                                }
                                else handleParseFail++;
                            }
                        }
                    }
                    _logger.Info(string.Format("已添加 {0}/{1} 个OPC标签（失败 {2}）",
                        opcItemIDs.Count - 1 - failCount, _tags.Count, failCount));
                    if (handleParseFail > 0)
                        _logger.Warn(string.Format("{0} 个成功项句柄解析失败，跳过其初值兜底（DataChange 仍可补齐）", handleParseFail));

                    // 批量 SyncRead 初值兜底（Source=1 读订阅组缓存，一次 COM 往返）：
                    // Freelance 等服务器不给新订阅项推初值时 _lastValues 恒为 null、快照跳过 → 下游无数据；
                    // 仅 Good 质量回填，Bad 不写伪数据；失败不致命（DefaultIsActive+DataChange 兜底）
                    if (syncHandles.Count > 0)
                    {
                        try
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            int subCount = syncHandles.Count;
                            Array subArray = new object[subCount + 1];
                            for (int j = 0; j < subCount; j++) subArray.SetValue(syncHandles[j], j + 1);
                            Array readValues, readErrors;
                            object readQualities, readTimestamps;
                            _opcGroup.SyncRead(1, subCount, ref subArray,
                                out readValues, out readErrors, out readQualities, out readTimestamps);

                            int okCount = 0;
                            var pending = new List<KeyValuePair<string, object>>();
                            Array qArr = readQualities as Array;
                            for (int j = 1; j <= subCount; j++)
                            {
                                int err;
                                if (!TryGetInt(readErrors, j, out err) || err != 0) continue;
                                int q = 0;
                                if (qArr != null) TryGetInt(qArr, j, out q);
                                if ((q & 0xC0) != 0xC0) continue;
                                object v = readValues.GetValue(j);
                                if (v == null || v is DBNull) continue;
                                int origIdx = syncIdx[j - 1];
                                if (origIdx < nodeByHandle.Count && !string.IsNullOrEmpty(nodeByHandle[origIdx]))
                                {
                                    pending.Add(new KeyValuePair<string, object>(nodeByHandle[origIdx], v));
                                    okCount++;
                                }
                            }
                            if (pending.Count > 0)
                            {
                                lock (_lock)
                                {
                                    foreach (var kv in pending) _lastValues[kv.Key] = kv.Value;
                                }
                            }
                            sw.Stop();
                            _logger.Info(string.Format("批量初值读取完成: 成功 {0}/{1}, 耗时 {2}ms",
                                okCount, subCount, sw.ElapsedMilliseconds));
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn("批量初值读取失败: " + ex.Message);
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
            _autoReconnectStopped = true;
            if (_reconnectTimer != null)
            {
                try { _reconnectTimer.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            }
            _sseRunning = false;
            if (_snapshotTimer != null)
            {
                try { _snapshotTimer.Dispose(); } catch { }
                _snapshotTimer = null;
            }
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

        /// <summary>
        /// 按当前配置重连：拆除旧连接（保留 SSE 通道与发布线程）后重建连接与订阅。
        /// 用于设置窗口修改 OPC 服务器地址后的热切换。
        /// </summary>
        public bool Reconnect()
        {
            lock (_reconnectLock)
            {
                _logger.Info(string.Format("按当前配置重连OPC服务器: {0}", _config.OpcServerProgId));
                TeardownConnection();
                if (!Connect()) return false;
                return Start();
            }
        }

        /// <summary>
        /// 启动/复位自动重连看门狗（配置 opc_reconnect_interval_ms&gt;0 时生效）。
        /// 由 Start() 成败路径调用；与 SettingsForm 手动 Reconnect 共用 _reconnectLock。
        /// </summary>
        private void EnsureAutoReconnect()
        {
            int interval = _config.OpcReconnectIntervalMs;
            if (interval <= 0) return;
            _autoReconnectStopped = false;
            if (_reconnectTimer == null)
            {
                _reconnectTimer = new System.Threading.Timer(
                    ReconnectWatchdogTick, null, interval, Timeout.Infinite);
                _logger.Info(string.Format("已启用OPC自动重连看门狗，探测间隔={0}ms", interval));
            }
            else
            {
                try { _reconnectTimer.Change(interval, Timeout.Infinite); } catch { }
            }
        }

        private void ReconnectWatchdogTick(object state)
        {
            if (_autoReconnectStopped) return;

            try
            {
                if (IsConnected)
                {
                    if (_reconnectFailCount != 0)
                    {
                        _reconnectFailCount = 0;
                        _logger.Info("OPC连接正常，自动重连看门狗待命");
                    }
                    ScheduleReconnectWatchdog(_config.OpcReconnectIntervalMs);
                    return;
                }

                // 首次进入未连接：记一条；其后失败按次数退避，避免服务器长时间不可用时刷屏
                if (_reconnectFailCount == 0)
                {
                    _logger.Warn("OPC未连接，开始自动重连");
                }

                bool ok = Reconnect();
                if (ok)
                {
                    _reconnectFailCount = 0;
                    _logger.Info("OPC自动重连成功");
                    ScheduleReconnectWatchdog(_config.OpcReconnectIntervalMs);
                    return;
                }

                _reconnectFailCount++;
                int delay = NextReconnectDelayMs();
                // 失败日志节流：第 1 次必打，之后约每 6 次（随退避约 30s+）打一条
                if (_reconnectFailCount == 1 || _reconnectFailCount % 6 == 0)
                {
                    _logger.Error(string.Format(
                        "OPC自动重连失败（第{0}次），{1}ms后重试", _reconnectFailCount, delay));
                }
                ScheduleReconnectWatchdog(delay);
            }
            catch (Exception ex)
            {
                _logger.Error("OPC自动重连看门狗异常", ex);
                if (!_autoReconnectStopped)
                {
                    ScheduleReconnectWatchdog(Math.Max(_config.OpcReconnectIntervalMs, 1000));
                }
            }
        }

        // 失败退避：基间隔 × 2^n，封顶 ReconnectBackoffCapMs；基间隔取配置值（至少 1s）
        private int NextReconnectDelayMs()
        {
            int baseMs = Math.Max(_config.OpcReconnectIntervalMs, 1000);
            int shift = Math.Min(_reconnectFailCount - 1, 6);
            if (shift < 0) shift = 0;
            long delay = (long)baseMs << shift;
            if (delay > ReconnectBackoffCapMs) delay = ReconnectBackoffCapMs;
            return (int)delay;
        }

        private void ScheduleReconnectWatchdog(int delayMs)
        {
            if (_autoReconnectStopped || _reconnectTimer == null) return;
            if (delayMs < 250) delayMs = 250;
            try { _reconnectTimer.Change(delayMs, Timeout.Infinite); } catch { }
        }

        private void TeardownConnection()
        {
            if (_snapshotTimer != null)
            {
                try { _snapshotTimer.Dispose(); } catch { }
                _snapshotTimer = null;
            }
            if (_opcGroup != null)
            {
                try { _opcGroup.DataChange -= OnDataChange; } catch { }
            }
            if (_opcGroups != null && _opcGroup != null)
            {
                try { _opcGroups.Remove("DataGroup"); } catch { }
            }
            _opcGroup = null;
            _opcItems = null;
            _opcGroups = null;
            _serverHandles = null;
            lock (_lock)
            {
                _lastValues.Clear();
                _clientHandleNodes = new List<string>();
            }
            if (_opcServer != null)
            {
                try { _opcServer.Disconnect(); } catch { }
                _opcServer = null;
            }
            _logger.Info("OPC旧连接已拆除（SSE通道保持，采集器不断流）");
        }

        public void ApplyUpdateRate()
        {
            if (_opcGroup == null) return;
            try
            {
                _opcGroup.UpdateRate = _config.UpdateInterval;
                _logger.Info(string.Format("OPC组更新频率已应用: {0}ms", _config.UpdateInterval));
            }
            catch (Exception ex)
            {
                _logger.Error("应用OPC组更新频率失败", ex);
            }
        }

        public void ApplySseInterval()
        {
            var interval = _config.SseSnapshotIntervalMs;
            lock (_lock)
            {
                if (interval > 0)
                {
                    if (_snapshotTimer != null)
                    {
                        _snapshotTimer.Change(interval, interval);
                    }
                    else
                    {
                        _snapshotTimer = new System.Threading.Timer(SnapshotTick, null, interval, interval);
                    }
                    _logger.Info(string.Format("SSE快照间隔已应用: {0}ms", interval));
                }
                else if (_snapshotTimer != null)
                {
                    _snapshotTimer.Dispose();
                    _snapshotTimer = null;
                    _logger.Info("SSE秒级快照已关闭");
                }
            }
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

                    // 单项隔离：回调数组中某项转换/访问抛异常只丢该项；
                    // 否则会跳到方法级 catch，把本批排在其后的所有项一并丢弃
                    //（现场表现："前面有读不出的标签，后边整段不推 stream"）
                    try
                    {
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
                    catch
                    {
                        // 计数不刷屏：坏项高频出现时从 /api/status 的 totalErrors 可见
                        System.Threading.Interlocked.Increment(ref _totalErrors);
                    }
                }

                if (changed.Count > 0)
                {
                    System.Threading.Interlocked.Increment(ref _totalReads);
                    // 仅入队：序列化与网络写入交给 SsePublishLoop，避免占用 COM STA 线程饿死浏览等调用。
                    // 注意：这里绝不碰 _sseLock —— 若 BroadcastSse 因某客户端写阻塞而持锁，
                    // 在 STA 线程上取 _sseLock 会反被憋死，进而卡住所有被封送到该 STA 的 COM 调用（浏览/选点）。
                    _sseQueue.Add(changed);
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
            // opc_connected：随帧透传 OPC 会话状态；空 values + false = 源掉线心跳，非“无数据可推”
            var payload = JsonConvert.SerializeObject(new { ts = DateTime.Now, opc_connected = IsConnected, values = values });
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

        // 秒级快照：把当前缓存的最新值拷成快照推一次 SSE。定时器线程（线程池 MTA）执行，
        // 仅短暂加锁拷贝字典、检查客户端数与 ServerState，不与 STA 上的 OnDataChange 共享 _sseLock 长持有，避免死锁。
        private void SnapshotTick(object state)
        {
            if (!_sseRunning) return;

            // 无 SSE 客户端时不浪费工作
            lock (_sseLock)
            {
                if (_sseClients.Count == 0) return;
            }

            // OPC 断开：_lastValues 是断线前陈旧值，不能以 Good + 当前时间伪装成实时数据。
            // 仍入队空帧，由 BroadcastSse 标 opc_connected=false，让 Go 能区分“无变化”和“源已掉线”。
            if (!IsConnected)
            {
                _sseQueue.Add(new List<TagValue>());
                return;
            }

            List<TagValue> snapshot;
            lock (_lock)
            {
                snapshot = new List<TagValue>(_lastValues.Count);
                foreach (var kvp in _lastValues)
                {
                    object val = kvp.Value;
                    // 跳过尚无初值的节点（已订阅、等待服务器首次 DataChange 推送）：
                    // 宁可暂缓上报也不向下游推 null/Bad 伪数据
                    if (val == null) continue;
                    snapshot.Add(new TagValue
                    {
                        Key = kvp.Key,
                        Value = val,
                        Quality = "Good",
                        Timestamp = DateTime.Now,
                        Status = "Good",
                        DataType = val.GetType().Name,
                        NodeId = kvp.Key,
                        Name = kvp.Key
                    });
                }
            }
            if (snapshot.Count == 0) return;

            _sseQueue.Add(snapshot);
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
            var browseStart = DateTime.Now;
            _logger.Info(string.Format("[Browse] 开始 nodeId={0} (线程={1})", key, System.Threading.Thread.CurrentThread.ManagedThreadId));

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
                bool acquired = false;
                try
                {
                    acquired = _browseSemaphore.Wait(7000);
                    if (!acquired)
                    {
                        _logger.Warn(string.Format("[Browse] 获取浏览信号量超时 (nodeId={0})，返回空结果", key));
                        all = new List<OPCNode>();
                    }
                    else
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
                        _logger.Info(string.Format("[Browse] 枚举完成 nodeId={0} 节点数={1} 耗时={2}ms",
                            key, all != null ? all.Count : 0,
                            (DateTime.Now - browseStart).TotalMilliseconds));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(string.Format("[Browse] 分页枚举失败 (nodeId={0}): {1}", key, ex.Message));
                    all = new List<OPCNode>();
                }
                finally
                {
                    if (acquired) _browseSemaphore.Release();
                }

                lock (_lock)
                {
                    _browseCache[key] = new BrowseCacheEntry { Nodes = all, Time = DateTime.Now };
                }
            }
            else
            {
                _logger.Info(string.Format("[Browse] 命中缓存 nodeId={0} 节点数={1}", key, all.Count));
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
            var ts = DateTime.Now;
            _logger.Info(string.Format("[Tags] 开始保存 {0} 个标签", newTags != null ? newTags.Count : 0));

            lock (_updateTagsLock)
            {
                // RemoveItems 是 COM 调用，必须在锁外：同样不能持 _lock 发起封送到 STA 的调用，
                // 否则会与需 _lock 的 OnDataChange（同处 STA 线程）死锁。
                if (_opcItems != null)
                {
                    try
                    {
                        if (_serverHandles != null)
                        {
                            // 用 AddItems 返回的真实 server handle 删除（1-based，过滤 0/无效句柄）：
                            // 硬编码 handles[i]=i 与真实句柄不匹配时 Remove 静默失败，
                            // 组内项残留会让后续保存的组持续膨胀
                            var validHandles = new List<int>();
                            for (int i = 1; i < _serverHandles.Length; i++)
                            {
                                int h;
                                if (TryGetInt(_serverHandles, i, out h) && h > 0) validHandles.Add(h);
                            }
                            if (validHandles.Count > 0)
                            {
                                Array handles = new object[validHandles.Count + 1];
                                for (int i = 0; i < validHandles.Count; i++)
                                    handles.SetValue(validHandles[i], i + 1);
                                Array errors;
                                _opcItems.Remove(validHandles.Count, ref handles, out errors);
                            }
                            _serverHandles = null;
                        }
                        else if (_opcItems.Count > 0)
                        {
                            // 句柄未知时不猜测删除：错删其它项的代价高于残留（下次 ApplyTags 重建）
                            _logger.Warn(string.Format("跳过 RemoveItems：无有效句柄记录（当前 {0} 项）", _opcItems.Count));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn("移除旧OPC标签失败: " + ex.Message);
                    }
                }

                NormalizeTags(newTags);
                // 引用替换（读侧 GetTags 直接返回，无需锁）
                _tags = newTags;

                // ApplyTags 内的 AddItems 均为 COM 调用，已在锁外执行；
                // 其对 _lastValues / _clientHandleNodes 的更新在 ApplyTags 内部加锁完成
                if (_opcItems != null && _tags.Count > 0)
                {
                    ApplyTags();
                }
                else
                {
                    lock (_lock)
                    {
                        _lastValues.Clear();
                        _clientHandleNodes = new List<string>();
                    }
                }

                SaveTagsToFile();
            }
            _logger.Info(string.Format("[Tags] 保存完成 耗时={0}ms", (DateTime.Now - ts).TotalMilliseconds));
        }

        public List<TagConfig> GetTags()
        {
            return _tags;
        }

        public void Dispose()
        {
            _autoReconnectStopped = true;
            if (_reconnectTimer != null)
            {
                try { _reconnectTimer.Dispose(); } catch { }
                _reconnectTimer = null;
            }
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
