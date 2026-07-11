using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Interop.OPCAutomation;

namespace OPC_DA_Agent
{
    public class OPCService : IDisposable
    {
        private OPCServer _opcServer;
        private OPCGroup _opcGroup;
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

        public bool Start()
        {
            if (_opcServer == null || _opcServer.ServerState != 1)
            {
                _logger.Error("OPC服务器未连接，无法启动数据采集");
                return false;
            }

            try
            {
                OPCGroups groups = _opcServer.OPCGroups;
                groups.DefaultGroupDeadband = 0;
                groups.DefaultGroupIsActive = true;

                _opcGroup = groups.Add("DataGroup");
                _opcGroup.IsActive = true;
                _opcGroup.IsSubscribed = true;
                _opcGroup.UpdateRate = _config.UpdateInterval;

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

                    _opcGroup.OPCItems.AddItems(itemIds.Count, opcItems, handlesArray, out serverHandles, out errors);
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
            if (!_isRunning || _opcGroup == null) return;

            try
            {
                int count = _opcGroup.OPCItems.Count;
                if (count == 0) return;

                object serverHandles = new object[count + 1];
                object errors;

                for (int i = 1; i <= count; i++)
                {
                    ((Array)serverHandles).SetValue(i, i);
                }

                object values;
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
                dynamic browser = _opcServer.OPCBrowser;

                browser.ShowBranches();
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
                IEnumerator branchEnum = ((IEnumerable)browser).GetEnumerator();
                while (branchEnum.MoveNext())
                {
                    string branch = branchEnum.Current as string;
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
                IEnumerator leafEnum = ((IEnumerable)browser).GetEnumerator();
                while (leafEnum.MoveNext())
                {
                    string leaf = leafEnum.Current as string;
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
                            object handles = new object[count + 1];
                            for (int i = 1; i <= count; i++)
                            {
                                ((Array)handles).SetValue(i, i);
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

        public List<TagConfig> GetTags()
        {
            return _tags;
        }

        public void Dispose()
        {
            Stop();
            if (_opcGroup != null)
            {
                try { _opcServer.OPCGroups.Remove("DataGroup"); } catch { }
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