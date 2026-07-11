using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using OpcNetApi;

namespace OPC_DA_Agent
{
    public class OPCService : IDisposable
    {
        private Server _opcServer;
        private Group _opcGroup;
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

        /// <summary>配置文件路径，用于保存标签配置</summary>
        public string ConfigPath { get { return _configPath; } }

        public bool IsConnected
        {
            get { return _opcServer != null && _opcServer.IsConnected; }
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
                string serverUrl = _config.OpcServerUrl ?? _config.OpcServerProgId;
                _logger.Info(string.Format("正在连接到OPC服务器: {0}...", serverUrl));

                _opcServer = new Server(serverUrl);
                _opcServer.Connect();

                _logger.Info("已连接到OPC服务器");
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
            if (_opcServer == null || !_opcServer.IsConnected)
            {
                _logger.Error("OPC服务器未连接，无法启动数据采集");
                return false;
            }

            try
            {
                int updateRate = _config.UpdateInterval;
                _opcGroup = _opcServer.CreateGroup("DataGroup", true, updateRate, null, null, null, null);

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
                foreach (var tag in _tags)
                {
                    if (tag.Enabled || tag.Active)
                    {
                        itemIds.Add(tag.NodeId);
                    }
                }

                if (itemIds.Count > 0)
                {
                    var added = _opcGroup.AddItems(itemIds.ToArray());
                    _logger.Info(string.Format("已添加 {0}/{1} 个OPC标签", added.Length, itemIds.Count));

                    lock (_lock)
                    {
                        foreach (string id in added)
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
                var values = _opcGroup.SyncReadAll();
                lock (_lock)
                {
                    foreach (var kvp in values)
                    {
                        _lastValues[kvp.Key] = kvp.Value;
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
        /// <param name="nodeId">节点路径（null 或空字符串表示根节点）</param>
        public List<OPCNode> BrowsePath(string nodeId)
        {
            if (_opcServer == null || !_opcServer.IsConnected)
                throw new InvalidOperationException("未连接到OPC服务器");

            var result = new List<OPCNode>();
            try
            {
                var browser = (IOPCBrowseServerAddressSpace)_opcServer.ComObject;

                OPCNAMESPACETYPE nsType;
                browser.QueryOrganization(out nsType);
                _logger.Info(string.Format("[Browse] 命名空间类型: {0}, 目标节点: {1}", nsType, nodeId ?? "(root)"));

                browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_TO, "");

                if (!string.IsNullOrEmpty(nodeId) && nodeId != "Root")
                {
                    string[] parts = nodeId.Split('.');
                    foreach (string part in parts)
                    {
                        _logger.Info(string.Format("[Browse] OPC_BROWSE_DOWN: {0}", part));
                        browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_DOWN, part);
                    }
                }

                OpcNetApi.Com.IEnumString enumBranches;
                int hrBranch = browser.BrowseOPCItemIDs(OPCBROWSETYPE.OPC_BRANCH, "", 0, 0, out enumBranches);
                _logger.Info(string.Format("[Browse] BrowseOPCItemIDs(BRANCH) hr={0}, enum={1}", hrBranch, enumBranches != null ? "ok" : "null"));
                int branchCount = 0;
                if (enumBranches != null)
                {
                    const int batchSize = 100;
                    string[] buffer = new string[batchSize];
                    int fetched;
                    int hr;
                    do
                    {
                        hr = enumBranches.Next(batchSize, buffer, out fetched);
                        branchCount += fetched;
                        for (int i = 0; i < fetched; i++)
                        {
                            string itemId = buffer[i];
                            try
                            {
                                string fullId;
                                browser.GetItemID(buffer[i], out fullId);
                                itemId = fullId ?? buffer[i];
                            }
                            catch { }

                            result.Add(new OPCNode
                            {
                                NodeId = itemId,
                                Name = buffer[i],
                                Description = "分支",
                                IsFolder = true,
                                HasChildren = true,
                                Children = new List<OPCNode>()
                            });
                        }
                    } while (hr == 0 && fetched == batchSize);
                }
                _logger.Info(string.Format("[Browse] 分支节点: {0}", branchCount));

                OpcNetApi.Com.IEnumString enumLeaves;
                int hrLeaf = browser.BrowseOPCItemIDs(OPCBROWSETYPE.OPC_LEAF, "", 0, 0, out enumLeaves);
                _logger.Info(string.Format("[Browse] BrowseOPCItemIDs(LEAF) hr={0}, enum={1}", hrLeaf, enumLeaves != null ? "ok" : "null"));
                int leafCount = 0;
                if (enumLeaves != null)
                {
                    const int batchSize = 100;
                    string[] buffer = new string[batchSize];
                    int fetched;
                    int hr;
                    do
                    {
                        hr = enumLeaves.Next(batchSize, buffer, out fetched);
                        leafCount += fetched;
                        for (int i = 0; i < fetched; i++)
                        {
                            string itemId = buffer[i];
                            try
                            {
                                string fullId;
                                browser.GetItemID(buffer[i], out fullId);
                                itemId = fullId ?? buffer[i];
                            }
                            catch { }

                            result.Add(new OPCNode
                            {
                                NodeId = itemId,
                                Name = buffer[i],
                                Description = "标签",
                                IsFolder = false,
                                HasChildren = false,
                                Children = null
                            });
                        }
                    } while (hr == 0 && fetched == batchSize);
                }
                _logger.Info(string.Format("[Browse] 叶子节点: {0}, 总计: {1}", leafCount, result.Count));
            }
            catch (Exception ex)
            {
                _logger.Error(string.Format("[Browse] 浏览节点失败: {0}", nodeId ?? "(root)"), ex);
            }

            return result;
        }

        /// <summary>
        /// 调用 BrowseOPCItemIDs 并枚举返回的字符串
        /// </summary>
        private List<string> BrowseItems(IOPCBrowseServerAddressSpace browser, OPCBROWSETYPE browseType)
        {
            var result = new List<string>();

            OpcNetApi.Com.IEnumString enumString;
            browser.BrowseOPCItemIDs(
                browseType,
                "",      // 无过滤条件
                0,       // 不限数据类型
                0,       // 不限访问权限
                out enumString);

            if (enumString == null) return result;

            const int batchSize = 100;
            string[] buffer = new string[batchSize];
            int fetched;

            do
            {
                enumString.Next(batchSize, buffer, out fetched);
                for (int i = 0; i < fetched; i++)
                {
                    result.Add(buffer[i]);
                }
            } while (fetched == batchSize);

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
                    _opcGroup.RemoveAllItems();
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
                _opcGroup.Dispose();
                _opcGroup = null;
            }
            if (_opcServer != null)
            {
                _opcServer.Dispose();
                _opcServer = null;
            }
        }
    }
}
