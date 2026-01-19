using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OPCAutomation;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC浏览器 - 用于浏览OPC服务器上的所有节点
    /// 注意：OPC DA的浏览功能有限，主要通过标签名称访问
    /// </summary>
    public class OPCBrowser : IDisposable
    {
        private OPCServer _opcServer;
        private readonly Logger _logger;
        private readonly Config _config;

        public OPCBrowser(Config config, Logger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 连接到OPC服务器
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                _logger.Info($"正在连接到OPC服务器: {_config.OpcServerProgId}");

                _opcServer = new OPCServer();
                _opcServer.Connect(_config.OpcServerProgId);

                _logger.Info($"成功连接到OPC服务器: {_opcServer.ServerName}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"连接OPC服务器失败: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 浏览根节点下的所有节点
        /// 注意：OPC DA不支持标准浏览，返回空列表
        /// </summary>
        public async Task<List<OPCNode>> BrowseRootAsync()
        {
            if (_opcServer == null)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Info("OPC DA浏览功能有限，建议使用标签名称直接访问");
                return new List<OPCNode>();
            }
            catch (Exception ex)
            {
                _logger.Error($"浏览根节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 浏览指定节点的子节点
        /// 注意：OPC DA不支持标准浏览，返回空列表
        /// </summary>
        public async Task<List<OPCNode>> BrowseNodeAsync(string nodeId, int depth = 1)
        {
            if (_opcServer == null)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Debug($"OPC DA浏览功能有限，建议使用标签名称直接访问: {nodeId}");
                return new List<OPCNode>();
            }
            catch (Exception ex)
            {
                _logger.Error($"浏览节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 递归浏览节点树
        /// 注意：OPC DA不支持标准浏览，返回null
        /// </summary>
        public async Task<OPCNode> BrowseTreeAsync(string nodeId, int maxDepth = 3, int currentDepth = 0)
        {
            if (_opcServer == null)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Debug($"OPC DA浏览功能有限，建议使用标签名称直接访问: {nodeId}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error($"浏览节点树失败: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// 搜索包含指定名称的节点
        /// 注意：OPC DA不支持标准搜索，返回空列表
        /// </summary>
        public async Task<List<OPCNode>> SearchNodesAsync(string searchTerm, int maxResults = 1000)
        {
            if (_opcServer == null)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Info($"OPC DA搜索功能有限，建议使用标签名称直接访问: {searchTerm}");
                return new List<OPCNode>();
            }
            catch (Exception ex)
            {
                _logger.Error($"搜索节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取节点详细信息
        /// 注意：OPC DA不支持标准节点信息，返回基本信息
        /// </summary>
        public async Task<OPCNodeDetail> GetNodeDetailAsync(string nodeId)
        {
            if (_opcServer == null)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Debug($"OPC DA节点详情功能有限，返回基本信息: {nodeId}");

                var detail = new OPCNodeDetail
                {
                    NodeId = nodeId,
                    DisplayName = nodeId,
                    Description = "OPC DA标签",
                    DataType = "Unknown",
                    ValueRank = 0,
                    AccessLevel = "Read",
                    UserAccessLevel = "Read"
                };

                return detail;
            }
            catch (Exception ex)
            {
                _logger.Error($"获取节点详细信息失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 导出所有变量节点到文件
        /// 注意：OPC DA不支持标准浏览，返回空列表
        /// </summary>
        public async Task<List<TagConfig>> ExportAllVariablesAsync(int maxDepth = 3)
        {
            if (_opcServer == null)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Info("OPC DA浏览功能有限，无法导出所有变量节点");
                return new List<TagConfig>();
            }
            catch (Exception ex)
            {
                _logger.Error($"导出变量节点失败: {ex.Message}", ex);
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                _opcServer?.Disconnect();
            }
            catch (Exception ex)
            {
                _logger.Error($"清理OPC浏览器资源失败: {ex.Message}", ex);
            }

            _opcServer = null;
        }
    }

    /// <summary>
    /// OPC节点信息
    /// </summary>
    public class OPCNode
    {
        public string NodeId { get; set; }
        public string DisplayName { get; set; }
        public string NodeClass { get; set; }
        public string BrowseName { get; set; }
        public string Description { get; set; }
        public bool IsForward { get; set; }
        public string ReferenceTypeId { get; set; }
        public int Depth { get; set; }
        public List<OPCNode> Children { get; set; } = new List<OPCNode>();

        public string GetNodeClassName()
        {
            return "变量";
        }
    }

    /// <summary>
    /// OPC节点详细信息
    /// </summary>
    public class OPCNodeDetail
    {
        public string NodeId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string DataType { get; set; }
        public int ValueRank { get; set; }
        public string AccessLevel { get; set; }
        public string UserAccessLevel { get; set; }
        public object CurrentValue { get; set; }
        public string CurrentQuality { get; set; }
        public DateTime CurrentTimestamp { get; set; }
    }
}
