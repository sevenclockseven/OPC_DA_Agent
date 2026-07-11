using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using OpcNetApi;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC 节点（已在 DataModel.cs 中定义，此处不再重复定义）
    /// </summary>
    public class OPCBrowser : IDisposable
    {
        private readonly Logger _logger;
        private readonly Config _config;
        private object _opcServer;

        public OPCBrowser(Config config, Logger logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>连接到 OPC 服务器（通过 COM Interop）</summary>
        public bool Connect()
        {
            try
            {
                _logger.Info(string.Format("正在连接到OPC服务器: {0}", _config.OpcServerProgId));

                Type serverType = Type.GetTypeFromProgID(_config.OpcServerProgId);
                if (serverType == null)
                {
                    _logger.Error(string.Format("找不到OPC服务器ProgID: {0}", _config.OpcServerProgId));
                    return false;
                }

                _opcServer = Activator.CreateInstance(serverType);
                _logger.Info("已连接到OPC服务器");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(string.Format("连接OPC服务器失败: {0}", ex.Message), ex);
                return false;
            }
        }

        /// <summary>浏览根节点</summary>
        public List<OPCNode> BrowseRoot()
        {
            return BrowseNode(null);
        }

        /// <summary>浏览指定节点的子节点</summary>
        public List<OPCNode> BrowseNode(string nodeId)
        {
            if (_opcServer == null)
                throw new InvalidOperationException("未连接到OPC服务器");

            var result = new List<OPCNode>();

            try
            {
                var browser = (IOPCBrowseServerAddressSpace)_opcServer;

                OPCNAMESPACETYPE nsType;
                browser.QueryOrganization(out nsType);

                browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_TO, "");

                if (!string.IsNullOrEmpty(nodeId))
                {
                    string[] parts = nodeId.Split('.');
                    foreach (string part in parts)
                    {
                        browser.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_DOWN, part);
                    }
                }

                result.AddRange(DoBrowse(browser, OPCBROWSETYPE.OPC_BRANCH, true));
                result.AddRange(DoBrowse(browser, OPCBROWSETYPE.OPC_LEAF, false));
            }
            catch (Exception ex)
            {
                _logger.Error(string.Format("浏览节点失败: {0}", nodeId ?? "(root)"), ex);
            }

            return result;
        }

        private List<OPCNode> DoBrowse(IOPCBrowseServerAddressSpace browser, OPCBROWSETYPE type, bool isFolder)
        {
            var result = new List<OPCNode>();

            OpcNetApi.Com.IEnumString enumStr;
            browser.BrowseOPCItemIDs(type, "", 0, 0, out enumStr);

            if (enumStr == null) return result;

            const int batch = 100;
            string[] buf = new string[batch];
            int fetched;

            do
            {
                enumStr.Next(batch, buf, out fetched);
                for (int i = 0; i < fetched; i++)
                {
                    string itemId = buf[i];
                    try
                    {
                        string fullId;
                        browser.GetItemID(buf[i], out fullId);
                        itemId = fullId ?? buf[i];
                    }
                    catch { }

                    result.Add(new OPCNode
                    {
                        NodeId = itemId,
                        Name = buf[i],
                        Description = isFolder ? "分支" : "标签",
                        IsFolder = isFolder,
                        HasChildren = isFolder,
                        Children = isFolder ? new List<OPCNode>() : null
                    });
                }
            } while (fetched == batch);

            return result;
        }

        public void Dispose()
        {
            if (_opcServer != null)
            {
                try
                {
                    if (Marshal.IsComObject(_opcServer))
                        Marshal.ReleaseComObject(_opcServer);
                }
                catch { }
                _opcServer = null;
            }
        }
    }
}
