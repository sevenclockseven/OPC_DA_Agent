using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OPC_DA_Agent
{
    public class OPCService : IDisposable
    {
        private dynamic _opcServer;
        private dynamic _opcGroup;
        private List<TagConfig> _tags = new List<TagConfig>();
        private Dictionary<string, object> _lastValues = new Dictionary<string, object>();
        private Timer _updateTimer;
        private bool _isRunning;
        private object _lock = new object();

        private long _totalReads = 0;
        private long _totalErrors = 0;
        private DateTime _startTime;

        private readonly Logger _logger;
        private readonly Config _config;
        private readonly string _configPath;

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
            _startTime = DateTime.Now;
        }

        /// <summary>
        /// 连接到 OPC DA 服务器
        /// </summary>
        public bool Connect()
        {
            try
            {
                _logger.Info(string.Format("正在连接到OPC服务器: {0}...", _config.OpcServerProgId));

                Type serverType = Type.GetTypeFromProgID("OPCAutomation.OPCServer");
                if (serverType == null)
                {
                    _logger.Error("找不到 OPCAutomation.OPCServer，请确保 OPCDAAuto.dll 已注册");
                    return false;
                }

                _opcServer = Activator.CreateInstance(serverType);

                string host = _config.OpcServerHost;
                if (!string.IsNullOrEmpty(host) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    _opcServer.Connect(_config.OpcServerProgId, host);
                }
                else
                {
                    _opcServer.Connect(_config.OpcServerProgId);
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

        /// <summary>
        /// 启动数据采集（使用配置中的标签列表）
        /// </summary>
        public bool Start()
        {
            if (_opcServer == null || _opcServer.ServerState != 1)
            {
                _logger.Error("OPC服务器未连接，无法启动数据采集");
                return false;
            }

            try
            {
                int updateRate = _config.UpdateInterval;

                dynamic groups = _opcServer.OPCGroups;
                groups.DefaultGroupDeadband = 0;
                groups.DefaultGroupIsActive = true;

                _opcGroup = groups.Add("DataGroup");
                _opcGroup.IsActive = true;
                _opcGroup.IsSubscribed = true;
                _opcGroup.UpdateRate = updateRate;

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

        /// <summary>
        /// 应用标签配置：将 _tags 中的标签添加到 OPC Group
        /// </summary>
        private void ApplyTags()
        {
            try
            {
                if (_opcGroup == null) return;

                var itemIds = new List<string>();
                var clientHandles = new List<int>();
                int handle = 1;

                foreach (var tag in _tags)
                {
                    if (tag.Enabled || tag.Active)
                    {
                        itemIds.Add(tag.NodeId);
                        clientHandles.Add(handle++);
                    }
                }

                if (itemIds.Count > 0)
                {
                    Array opcItems = itemIds.ToArray();
                    Array handlesArray = clientHandles.ToArray();
                    object serverHandles;
                    object errors;

                    _opcGroup.OPCItems.AddItems(itemIds.Count, ref opcItems, ref handlesArray, out serverHandles, out errors);
                    _logger.Info(string.Format("已添加 {0}/{1} 个OPC标签", itemIds.Count, _tags.Count));

                    lock (_lock)
                    {
                        foreach (string id in itemIds)
                        {
                            _lastValues[id] = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("添加OPC标签失败", ex);
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
                int count = _opcGroup.OPCItems.Count;
                if (count == 0) return;

                Array serverHandles = new int[count + 1];
                for (int i = 1; i <= count; i++)
                {
                    serverHandles.SetValue(i, i);
                }

                object values;
                object errors;
                object qualities;
                object timestamps;

                _opcGroup.SyncRead(2, count, ref serverHandles, out values, out errors, out qualities, out timestamps);

                lock (_lock)
                {
                    if (values is Array valArray)
                    {
                        for (int i = 1; i <= count; i++)
                        {
                            if (i - 1 < _tags.Count)
                            {
                                string tagId = _tags[i - 1].NodeId;
                                _lastValues[tagId] = valArray.GetValue(i);
                            }
                        }
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

        /// <summary>
        /// 获取状态信息
        /// </summary>
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

        /// <summary>
        /// 获取当前采集数据
        /// </summary>
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

        /// <summary>
        /// 浏览 OPC 服务器根节点
        /// </summary>
        public List<OPCNode> GetBrowseRoot()
        {
            return BrowsePath(null);
        }

        /// <summary>
        /// 浏览指定路径下的子节点
        /// </summary>
        public List<OPCNode> BrowsePath(string nodeId)
        {
            if (_opcServer == null || _opcServer.ServerState != 1)
                throw new InvalidOperationException("未连接到OPC服务器");

            var result = new List<OPCNode>();
            try
            {
                dynamic browser = _opcServer.OPCBrowser;

                browser.MoveToRoot();

                if (!string.IsNullOrEmpty(nodeId) && nodeId != "Root")
                {
                    string[] parts = nodeId.Split('.');
                    foreach (string part in parts)
                    {
                        browser.MoveDown(part);
                    }
                }

                browser.ShowBranches();
                foreach (string branch in browser)
                {
                    if (!string.IsNullOrEmpty(branch))
                    {
                        result.Add(new OPCNode
                        {
                            NodeId = branch,
                            Name = branch,
                            Description = "分支",
                            IsFolder = true,
                            HasChildren = true,
                            Children = new List<OPCNode>()
                        });
                    }
                }

                browser.ShowLeafs(true);
                foreach (string leaf in browser)
                {
                    if (!string.IsNullOrEmpty(leaf))
                    {
                        string fullId = string.IsNullOrEmpty(nodeId) || nodeId == "Root" ? leaf : nodeId + "." + leaf;
                        result.Add(new OPCNode
                        {
                            NodeId = fullId,
                            Name = leaf,
                            Description = "标签",
                            IsFolder = false,
                            HasChildren = false,
                            Children = null
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(string.Format("[Browse] 浏览节点失败: {0}", nodeId ?? "(root)"), ex);
            }

            return result;
        }

        /// <summary>
        /// 更新标签列表（来自 Web UI 选择）
        /// </summary>
        public void UpdateTags(List<TagConfig> newTags)
        {
            lock (_lock)
            {
                if (_opcGroup != null)
                {
                    try
                    {
                        int count = _opcGroup.OPCItems.Count;
                        if (count > 0)
                        {
                            Array handles = new int[count + 1];
                            for (int i = 1; i <= count; i++)
                            {
                                handles.SetValue(i, i);
                            }
                            object errors;
                            _opcGroup.OPCItems.Remove(count, ref handles, out errors);
                        }
                    }
                    catch { }
                }

                _tags = newTags;
                _lastValues.Clear();

                if (_opcGroup != null && _tags.Count > 0)
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

        /// <summary>
        /// 获取当前标签列表
        /// </summary>
        public List<TagConfig> GetTags()
        {
            return _tags;
        }

        public void Dispose()
        {
            Stop();
            if (_opcGroup != null)
            {
                try { ((dynamic)_opcServer).OPCGroups.Remove("DataGroup"); } catch { }
                _opcGroup = null;
            }
            if (_opcServer != null)
            {
                try { ((dynamic)_opcServer).Disconnect(); } catch { }
                _opcServer = null;
            }
        }
    }
}