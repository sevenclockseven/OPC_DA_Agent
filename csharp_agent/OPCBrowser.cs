using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;

namespace OPC_DA_Agent
{
    /// <summary>
    /// OPC浏览器 - 用于浏览OPC服务器上的所有节点
    /// </summary>
    public class OPCBrowser : IDisposable
    {
        private Session _session;
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
                _logger.Info($"正在连接到OPC服务器: {_config.OpcServerUrl}");

                var application = new ApplicationConfiguration
                {
                    ApplicationName = "OPC DA Agent Browser",
                    ApplicationType = ApplicationType.Client,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        AutoAcceptUntrustedCertificates = true,
                        RejectSHA1SignedCertificates = false,
                        MinimumCertificateKeySize = 1024
                    },
                    TransportConfigurations = new TransportConfigurationCollection(),
                    UserTokenPolicyCollection = new UserTokenPolicyCollection()
                };

                await application.Validate(ApplicationType.Client);

                var endpointDescription = CoreClientUtils.SelectEndpoint(
                    _config.OpcServerUrl,
                    false,
                    15000
                );

                var endpointConfiguration = EndpointConfiguration.Create(application);
                var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

                _session = await Session.Create(
                    application,
                    endpoint,
                    false,
                    "OPC Browser Session",
                    60000,
                    new UserIdentity(new AnonymousIdentityToken()),
                    null
                );

                _logger.Info($"成功连接到OPC服务器: {_session.Endpoint.Server.ApplicationName}");
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
        /// </summary>
        public async Task<List<OPCNode>> BrowseRootAsync()
        {
            if (_session == null || !_session.Connected)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Info("开始浏览根节点...");

                var nodes = new List<OPCNode>();

                // 浏览ObjectsFolder（对象文件夹）
                var rootId = ObjectIds.ObjectsFolder;
                var browseDescription = new BrowseDescription
                {
                    NodeId = rootId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.ObjectType),
                    ResultMask = (uint)BrowseResultMask.All
                };

                var browseResults = _session.Browse(null, null, browseDescription, 1000, out var continuationPoint);
                nodes.AddRange(ProcessBrowseResults(browseResults, 0));

                // 如果有继续点，获取剩余结果
                while (continuationPoint != null && continuationPoint.Length > 0)
                {
                    var moreResults = _session.BrowseNext(null, false, continuationPoint, out continuationPoint);
                    nodes.AddRange(ProcessBrowseResults(moreResults, 0));
                }

                _logger.Info($"浏览完成，找到 {nodes.Count} 个顶级节点");
                return nodes;
            }
            catch (Exception ex)
            {
                _logger.Error($"浏览根节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 浏览指定节点的子节点
        /// </summary>
        public async Task<List<OPCNode>> BrowseNodeAsync(string nodeId, int depth = 1)
        {
            if (_session == null || !_session.Connected)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Debug($"浏览节点: {nodeId}, 深度: {depth}");

                var nodes = new List<OPCNode>();
                var browseDescription = new BrowseDescription
                {
                    NodeId = new NodeId(nodeId),
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.ObjectType),
                    ResultMask = (uint)BrowseResultMask.All
                };

                var browseResults = _session.Browse(null, null, browseDescription, 1000, out var continuationPoint);
                nodes.AddRange(ProcessBrowseResults(browseResults, depth));

                // 如果有继续点，获取剩余结果
                while (continuationPoint != null && continuationPoint.Length > 0)
                {
                    var moreResults = _session.BrowseNext(null, false, continuationPoint, out continuationPoint);
                    nodes.AddRange(ProcessBrowseResults(moreResults, depth));
                }

                return nodes;
            }
            catch (Exception ex)
            {
                _logger.Error($"浏览节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 递归浏览节点树
        /// </summary>
        public async Task<OPCNode> BrowseTreeAsync(string nodeId, int maxDepth = 3, int currentDepth = 0)
        {
            if (_session == null || !_session.Connected)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            if (currentDepth >= maxDepth)
            {
                return null;
            }

            try
            {
                var browseDescription = new BrowseDescription
                {
                    NodeId = new NodeId(nodeId),
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.ObjectType),
                    ResultMask = (uint)BrowseResultMask.All
                };

                var browseResults = _session.Browse(null, null, browseDescription, 1000, out var continuationPoint);
                var children = ProcessBrowseResults(browseResults, currentDepth + 1);

                // 递归浏览子节点
                foreach (var child in children)
                {
                    if (child.NodeClass == NodeClass.Object || child.NodeClass == NodeClass.ObjectType)
                    {
                        var childNode = await BrowseTreeAsync(child.NodeId, maxDepth, currentDepth + 1);
                        if (childNode != null)
                        {
                            child.Children.Add(childNode);
                        }
                    }
                }

                // 处理继续点
                while (continuationPoint != null && continuationPoint.Length > 0)
                {
                    var moreResults = _session.BrowseNext(null, false, continuationPoint, out continuationPoint);
                    var moreChildren = ProcessBrowseResults(moreResults, currentDepth + 1);

                    foreach (var child in moreChildren)
                    {
                        if (child.NodeClass == NodeClass.Object || child.NodeClass == NodeClass.ObjectType)
                        {
                            var childNode = await BrowseTreeAsync(child.NodeId, maxDepth, currentDepth + 1);
                            if (childNode != null)
                            {
                                child.Children.Add(childNode);
                            }
                        }
                    }
                }

                return new OPCNode
                {
                    NodeId = nodeId,
                    DisplayName = nodeId,
                    NodeClass = NodeClass.Object,
                    Children = children
                };
            }
            catch (Exception ex)
            {
                _logger.Error($"浏览节点树失败: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// 搜索包含指定名称的节点
        /// </summary>
        public async Task<List<OPCNode>> SearchNodesAsync(string searchTerm, int maxResults = 1000)
        {
            if (_session == null || !_session.Connected)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                _logger.Info($"搜索节点: {searchTerm}");

                var results = new List<OPCNode>();
                await SearchNodesRecursive(ObjectIds.ObjectsFolder, searchTerm, results, maxResults);

                _logger.Info($"搜索完成，找到 {results.Count} 个匹配节点");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error($"搜索节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 递归搜索节点
        /// </summary>
        private async Task SearchNodesRecursive(NodeId parentNodeId, string searchTerm, List<OPCNode> results, int maxResults)
        {
            if (results.Count >= maxResults) return;

            try
            {
                var browseDescription = new BrowseDescription
                {
                    NodeId = parentNodeId,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.ObjectType),
                    ResultMask = (uint)BrowseResultMask.All
                };

                var browseResults = _session.Browse(null, null, browseDescription, 1000, out var continuationPoint);
                var nodes = ProcessBrowseResults(browseResults, 0);

                foreach (var node in nodes)
                {
                    // 检查是否匹配搜索词
                    if (node.DisplayName.ToLower().Contains(searchTerm.ToLower()) ||
                        node.NodeId.ToLower().Contains(searchTerm.ToLower()))
                    {
                        results.Add(node);
                    }

                    // 递归搜索子节点
                    if (node.NodeClass == NodeClass.Object || node.NodeClass == NodeClass.ObjectType)
                    {
                        await SearchNodesRecursive(new NodeId(node.NodeId), searchTerm, results, maxResults);
                    }
                }

                // 处理继续点
                while (continuationPoint != null && continuationPoint.Length > 0)
                {
                    var moreResults = _session.BrowseNext(null, false, continuationPoint, out continuationPoint);
                    var moreNodes = ProcessBrowseResults(moreResults, 0);

                    foreach (var node in moreNodes)
                    {
                        if (node.DisplayName.ToLower().Contains(searchTerm.ToLower()) ||
                            node.NodeId.ToLower().Contains(searchTerm.ToLower()))
                        {
                            results.Add(node);
                        }

                        if (node.NodeClass == NodeClass.Object || node.NodeClass == NodeClass.ObjectType)
                        {
                            await SearchNodesRecursive(new NodeId(node.NodeId), searchTerm, results, maxResults);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"递归搜索节点失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取节点详细信息
        /// </summary>
        public async Task<OPCNodeDetail> GetNodeDetailAsync(string nodeId)
        {
            if (_session == null || !_session.Connected)
            {
                throw new InvalidOperationException("未连接到OPC服务器");
            }

            try
            {
                var node = new NodeId(nodeId);

                // 读取节点属性
                var nodesToRead = new ReadValueIdCollection
                {
                    new ReadValueId { NodeId = node, AttributeId = Attributes.DisplayName },
                    new ReadValueId { NodeId = node, AttributeId = Attributes.Description },
                    new ReadValueId { NodeId = node, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = node, AttributeId = Attributes.ValueRank },
                    new ReadValueId { NodeId = node, AttributeId = Attributes.AccessLevel },
                    new ReadValueId { NodeId = node, AttributeId = Attributes.UserAccessLevel }
                };

                var responseHeader = _session.Read(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    out var results,
                    out var diagnosticInfos
                );

                var detail = new OPCNodeDetail
                {
                    NodeId = nodeId
                };

                if (results.Count > 0 && StatusCode.IsGood(results[0].StatusCode))
                {
                    detail.DisplayName = results[0].Value?.ToString() ?? nodeId;
                }

                if (results.Count > 1 && StatusCode.IsGood(results[1].StatusCode))
                {
                    detail.Description = results[1].Value?.ToString() ?? "";
                }

                if (results.Count > 2 && StatusCode.IsGood(results[2].StatusCode))
                {
                    var dataTypeId = results[2].Value as NodeId;
                    if (dataTypeId != null)
                    {
                        detail.DataType = GetDataTypeName(dataTypeId);
                    }
                }

                if (results.Count > 3 && StatusCode.IsGood(results[3].StatusCode))
                {
                    detail.ValueRank = (int)results[3].Value;
                }

                if (results.Count > 4 && StatusCode.IsGood(results[4].StatusCode))
                {
                    var accessLevel = (byte)results[4].Value;
                    detail.AccessLevel = GetAccessLevelString(accessLevel);
                }

                if (results.Count > 5 && StatusCode.IsGood(results[5].StatusCode))
                {
                    var userAccessLevel = (byte)results[5].Value;
                    detail.UserAccessLevel = GetAccessLevelString(userAccessLevel);
                }

                // 尝试读取当前值（如果是变量节点）
                try
                {
                    var valueResult = _session.ReadValue(node);
                    if (StatusCode.IsGood(valueResult.StatusCode))
                    {
                        detail.CurrentValue = valueResult.Value;
                        detail.CurrentQuality = "Good";
                        detail.CurrentTimestamp = valueResult.SourceTimestamp;
                    }
                    else
                    {
                        detail.CurrentQuality = StatusCode.ToString(valueResult.StatusCode);
                    }
                }
                catch
                {
                    // 忽略读取值的错误
                }

                return detail;
            }
            catch (Exception ex)
            {
                _logger.Error($"获取节点详细信息失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 处理浏览结果
        /// </summary>
        private List<OPCNode> ProcessBrowseResults(BrowseResultCollection results, int depth)
        {
            var nodes = new List<OPCNode>();

            foreach (var result in results)
            {
                if (StatusCode.IsGood(result.StatusCode))
                {
                    foreach (var reference in result.References)
                    {
                        var node = new OPCNode
                        {
                            NodeId = reference.NodeId.ToString(),
                            DisplayName = reference.DisplayName.Text,
                            NodeClass = reference.NodeClass,
                            BrowseName = reference.BrowseName?.Name ?? "",
                            Description = reference.Description?.Text ?? "",
                            IsForward = reference.IsForward,
                            ReferenceTypeId = reference.ReferenceTypeId?.ToString() ?? "",
                            Depth = depth
                        };

                        nodes.Add(node);
                    }
                }
            }

            return nodes;
        }

        /// <summary>
        /// 获取数据类型名称
        /// </summary>
        private string GetDataTypeName(NodeId dataTypeId)
        {
            try
            {
                // 尝试获取内置数据类型名称
                if (dataTypeId.NamespaceIndex == 0)
                {
                    var builtInType = (BuiltInType)dataTypeId.Identifier;
                    return builtInType.ToString();
                }

                // 尝试解析自定义数据类型
                var node = _session.NodeCache.Find(dataTypeId);
                if (node != null)
                {
                    return node.DisplayName.Text;
                }

                return dataTypeId.ToString();
            }
            catch
            {
                return dataTypeId.ToString();
            }
        }

        /// <summary>
        /// 获取访问级别字符串
        /// </summary>
        private string GetAccessLevelString(byte accessLevel)
        {
            var levels = new List<string>();

            if ((accessLevel & (byte)AccessLevels.CurrentRead) != 0)
                levels.Add("Read");
            if ((accessLevel & (byte)AccessLevels.CurrentWrite) != 0)
                levels.Add("Write");
            if ((accessLevel & (byte)AccessLevels.HistoryRead) != 0)
                levels.Add("HistoryRead");
            if ((accessLevel & (byte)AccessLevels.HistoryWrite) != 0)
                levels.Add("HistoryWrite");

            return string.Join(", ", levels);
        }

        /// <summary>
        /// 导出所有变量节点到文件
        /// </summary>
        public async Task<List<TagConfig>> ExportAllVariablesAsync(int maxDepth = 3)
        {
            var tags = new List<TagConfig>();

            try
            {
                _logger.Info("开始导出所有变量节点...");

                // 浏览根节点
                var rootNodes = await BrowseRootAsync();

                foreach (var rootNode in rootNodes)
                {
                    await CollectVariablesRecursive(rootNode.NodeId, tags, maxDepth, 0);
                }

                _logger.Info($"导出完成，共找到 {tags.Count} 个变量节点");
                return tags;
            }
            catch (Exception ex)
            {
                _logger.Error($"导出变量节点失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 递归收集变量节点
        /// </summary>
        private async Task CollectVariablesRecursive(string nodeId, List<TagConfig> tags, int maxDepth, int currentDepth)
        {
            if (currentDepth >= maxDepth) return;

            try
            {
                var children = await BrowseNodeAsync(nodeId, currentDepth + 1);

                foreach (var child in children)
                {
                    if (child.NodeClass == NodeClass.Variable)
                    {
                        // 获取详细信息
                        var detail = await GetNodeDetailAsync(child.NodeId);

                        tags.Add(new TagConfig
                        {
                            NodeId = child.NodeId,
                            Name = child.DisplayName,
                            Description = detail.Description,
                            DataType = detail.DataType,
                            Enabled = true
                        });
                    }
                    else if (child.NodeClass == NodeClass.Object || child.NodeClass == NodeClass.ObjectType)
                    {
                        // 递归浏览子节点
                        await CollectVariablesRecursive(child.NodeId, tags, maxDepth, currentDepth + 1);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"收集变量节点失败: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            _session?.Disconnect();
            _session?.Dispose();
        }
    }

    /// <summary>
    /// OPC节点信息
    /// </summary>
    public class OPCNode
    {
        public string NodeId { get; set; }
        public string DisplayName { get; set; }
        public NodeClass NodeClass { get; set; }
        public string BrowseName { get; set; }
        public string Description { get; set; }
        public bool IsForward { get; set; }
        public string ReferenceTypeId { get; set; }
        public int Depth { get; set; }
        public List<OPCNode> Children { get; set; } = new List<OPCNode>();

        public string GetNodeClassName()
        {
            return NodeClass switch
            {
                NodeClass.Object => "对象",
                NodeClass.Variable => "变量",
                NodeClass.ObjectType => "对象类型",
                NodeClass.VariableType => "变量类型",
                NodeClass.ReferenceType => "引用类型",
                NodeClass.DataType => "数据类型",
                NodeClass.View => "视图",
                _ => "未知"
            };
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
