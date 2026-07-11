// OPC DA COM 互操作接口定义
// 基于 OPC Foundation 规范，MIT 许可证
// 这些是纯接口声明 [ComImport]，编译时不需要 COM 运行时，
// 运行时通过 QueryInterface 获取真实 OPC DA 服务器的接口指针。
using System;
using System.Runtime.InteropServices;

namespace OpcNetApi
{
    #region 枚举

    public enum OPCDATASOURCE
    {
        OPC_DS_CACHE = 1,
        OPC_DS_DEVICE
    }

    public enum OPCBROWSETYPE
    {
        OPC_BRANCH = 1,
        OPC_LEAF,
        OPC_FLAT
    }

    public enum OPCNAMESPACETYPE
    {
        OPC_NS_HIERARCHIAL = 1,
        OPC_NS_FLAT
    }

    public enum OPCBROWSEDIRECTION
    {
        OPC_BROWSE_UP = 1,
        OPC_BROWSE_DOWN,
        OPC_BROWSE_TO
    }

    public enum OPCEUTYPE
    {
        OPC_NOENUM = 0,
        OPC_ANALOG,
        OPC_ENUMERATED
    }

    public enum OPCSERVERSTATE
    {
        OPC_STATUS_RUNNING = 1,
        OPC_STATUS_FAILED,
        OPC_STATUS_NOCONFIG,
        OPC_STATUS_SUSPENDED,
        OPC_STATUS_TEST,
        OPC_STATUS_COMM_FAULT
    }

    public enum OPCENUMSCOPE
    {
        OPC_ENUM_PRIVATE_CONNECTIONS = 1,
        OPC_ENUM_PUBLIC_CONNECTIONS,
        OPC_ENUM_ALL_CONNECTIONS,
        OPC_ENUM_PRIVATE,
        OPC_ENUM_PUBLIC,
        OPC_ENUM_ALL
    }

    public enum OPCBROWSEFILTER
    {
        OPC_BROWSE_FILTER_ALL = 1,
        OPC_BROWSE_FILTER_BRANCHES,
        OPC_BROWSE_FILTER_ITEMS,
    }

    #endregion

    #region 结构体

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPCITEMSTATE
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        public FILETIME ftTimeStamp;
        [MarshalAs(UnmanagedType.I2)]
        public short wQuality;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.Struct)]
        public object vDataValue;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPCSERVERSTATUS
    {
        public FILETIME ftStartTime;
        public FILETIME ftCurrentTime;
        public FILETIME ftLastUpdateTime;
        public OPCSERVERSTATE dwServerState;
        [MarshalAs(UnmanagedType.I4)]
        public int dwGroupCount;
        [MarshalAs(UnmanagedType.I4)]
        public int dwBandWidth;
        [MarshalAs(UnmanagedType.I2)]
        public short wMajorVersion;
        [MarshalAs(UnmanagedType.I2)]
        public short wMinorVersion;
        [MarshalAs(UnmanagedType.I2)]
        public short wBuildNumber;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szVendorInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPCITEMDEF
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szAccessPath;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string szItemID;
        [MarshalAs(UnmanagedType.I4)]
        public int bActive;
        [MarshalAs(UnmanagedType.I4)]
        public int hClient;
        [MarshalAs(UnmanagedType.I4)]
        public int dwBlobSize;
        public IntPtr pBlob;
        [MarshalAs(UnmanagedType.I2)]
        public short vtRequestedDataType;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OPCITEMRESULT
    {
        [MarshalAs(UnmanagedType.I4)]
        public int hServer;
        [MarshalAs(UnmanagedType.I2)]
        public short vtCanonicalDataType;
        [MarshalAs(UnmanagedType.I2)]
        public short wReserved;
        [MarshalAs(UnmanagedType.I4)]
        public int dwAccessRights;
        [MarshalAs(UnmanagedType.I4)]
        public int dwBlobSize;
        public IntPtr pBlob;
    }

    #endregion

    #region COM 接口定义

    /// <summary>
    /// IOPCServerList - 用于远程枚举 OPC 服务器（OPCEnum）
    /// </summary>
    [ComImport]
    [GuidAttribute("13486D51-4821-11D2-A494-3CB306C10000")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCServerList
    {
        [PreserveSig]
        int EnumClasses(
            [MarshalAs(UnmanagedType.U4)] int dwReserved,
            [Out] out OpcNetApi.Com.IEnumGUID ppClsidEnumerator);

        [PreserveSig]
        int GetClassDetails(
            ref Guid clsid,
            [Out][MarshalAs(UnmanagedType.LPWStr)] out string ppszProgID,
            [Out][MarshalAs(UnmanagedType.LPWStr)] out string ppszUserType,
            [Out][MarshalAs(UnmanagedType.LPWStr)] out string ppszVerIndProgID);
    }

    /// <summary>
    /// IEnumGUID - 枚举 GUID
    /// </summary>
    [ComImport]
    [GuidAttribute("0002E000-0000-0000-C000-000000000046")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumGUID
    {
        [PreserveSig]
        int Next(
            [MarshalAs(UnmanagedType.U4)] int celt,
            [Out] out Guid rgelt,
            [Out][MarshalAs(UnmanagedType.U4)] out int pceltFetched);

        [PreserveSig]
        int Skip([MarshalAs(UnmanagedType.U4)] int celt);

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int Clone([Out] out OpcNetApi.Com.IEnumGUID ppenum);
    }

    /// <summary>
    /// IOPCServer - OPC DA 服务器主接口
    /// </summary>
    [ComImport]
    [GuidAttribute("39c13a4d-011e-11d0-9675-0020afd8adb3")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCServer
    {
        void AddGroup(
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            [MarshalAs(UnmanagedType.I4)] int bActive,
            [MarshalAs(UnmanagedType.I4)] int dwRequestedUpdateRate,
            [MarshalAs(UnmanagedType.I4)] int hClientGroup,
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            [MarshalAs(UnmanagedType.I4)] int dwLCID,
            [Out][MarshalAs(UnmanagedType.I4)] out int phServerGroup,
            [Out][MarshalAs(UnmanagedType.I4)] out int pRevisedUpdateRate,
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 9)] out object ppUnk);

        void GetErrorString(
            [MarshalAs(UnmanagedType.I4)] int dwError,
            [MarshalAs(UnmanagedType.I4)] int dwLocale,
            [Out][MarshalAs(UnmanagedType.LPWStr)] out string ppString);

        void GetGroupByName(
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 1)] out object ppUnk);

        void GetStatus(
            [Out] out IntPtr ppServerStatus);

        void RemoveGroup(
            [MarshalAs(UnmanagedType.I4)] int hServerGroup,
            [MarshalAs(UnmanagedType.I4)] int bForce);

        void CreateGroupEnumerator(
            OPCENUMSCOPE dwScope,
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 1)] out object ppUnk);
    }

    /// <summary>
    /// IOPCBrowseServerAddressSpace - 浏览 OPC 服务器地址空间
    /// </summary>
    [ComImport]
    [GuidAttribute("39c13a4f-011e-11d0-9675-0020afd8adb3")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCBrowseServerAddressSpace
    {
        void QueryOrganization(
            [Out] out OPCNAMESPACETYPE pNameSpaceType);

        void ChangeBrowsePosition(
            OPCBROWSEDIRECTION dwBrowseDirection,
            [MarshalAs(UnmanagedType.LPWStr)] string szString);

        [PreserveSig]
        int BrowseOPCItemIDs(
            OPCBROWSETYPE dwBrowseFilterType,
            [MarshalAs(UnmanagedType.LPWStr)] string szFilterCriteria,
            [MarshalAs(UnmanagedType.U2)] short vtDataTypeFilter,
            [MarshalAs(UnmanagedType.U4)] int dwAccessRightsFilter,
            [Out, MarshalAs(UnmanagedType.Interface)] out OpcNetApi.Com.IEnumString ppIEnumString);

        void GetItemID(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemDataID,
            [Out][MarshalAs(UnmanagedType.LPWStr)] out string szItemID);

        void BrowseAccessPaths(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemID,
            [Out] out OpcNetApi.Com.IEnumString pIEnumString);
    }

    /// <summary>
    /// IOPCGroupStateMgt - Group 状态管理
    /// </summary>
    [ComImport]
    [GuidAttribute("39c13a50-011e-11d0-9675-0020afd8adb3")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCGroupStateMgt
    {
        void GetState(
            [Out][MarshalAs(UnmanagedType.I4)] out int pUpdateRate,
            [Out][MarshalAs(UnmanagedType.I4)] out int pActive,
            [Out][MarshalAs(UnmanagedType.LPWStr)] out string ppName,
            [Out][MarshalAs(UnmanagedType.I4)] out int pTimeBias,
            [Out][MarshalAs(UnmanagedType.R4)] out float pPercentDeadband,
            [Out][MarshalAs(UnmanagedType.I4)] out int pLCID,
            [Out][MarshalAs(UnmanagedType.I4)] out int phClientGroup,
            [Out][MarshalAs(UnmanagedType.I4)] out int phServerGroup);

        void SetState(
            IntPtr pRequestedUpdateRate,
            [Out][MarshalAs(UnmanagedType.I4)] out int pRevisedUpdateRate,
            IntPtr pActive,
            IntPtr pTimeBias,
            IntPtr pPercentDeadband,
            IntPtr pLCID,
            IntPtr phClientGroup);

        void SetName(
            [MarshalAs(UnmanagedType.LPWStr)] string szName);

        void CloneGroup(
            [MarshalAs(UnmanagedType.LPWStr)] string szName,
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 1)] out object ppUnk);
    }

    /// <summary>
    /// IOPCSyncIO - 同步读写
    /// </summary>
    [ComImport]
    [GuidAttribute("39c13a52-011e-11d0-9675-0020afd8adb3")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCSyncIO
    {
        void Read(
            OPCDATASOURCE dwSource,
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 1)] int[] phServer,
            [Out] out IntPtr ppItemValues,
            [Out] out IntPtr ppErrors);

        void Write(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 0)] int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 0)] object[] pItemValues,
            [Out] out IntPtr ppErrors);
    }

    /// <summary>
    /// IOPCItemMgt - Item 管理（添加/删除/验证标签）
    /// </summary>
    [ComImport]
    [GuidAttribute("39c13a54-011e-11d0-9675-0020afd8adb3")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCItemMgt
    {
        void AddItems(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStruct, SizeParamIndex = 0)] OPCITEMDEF[] pItemArray,
            [Out] out IntPtr ppAddResults,
            [Out] out IntPtr ppErrors);

        void ValidateItems(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStruct, SizeParamIndex = 0)] OPCITEMDEF[] pItemArray,
            [MarshalAs(UnmanagedType.I4)] int bBlobUpdate,
            [Out] out IntPtr ppValidationResults,
            [Out] out IntPtr ppErrors);

        void RemoveItems(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 0)] int[] phServer,
            [Out] out IntPtr ppErrors);

        void SetActiveState(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 0)] int[] phServer,
            [MarshalAs(UnmanagedType.I4)] int bActive,
            [Out] out IntPtr ppErrors);

        void SetClientHandles(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 0)] int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 0)] int[] phClient,
            [Out] out IntPtr ppErrors);

        void SetDatatypes(
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 0)] int[] phServer,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I2, SizeParamIndex = 0)] short[] pRequestedDatatypes,
            [Out] out IntPtr ppErrors);

        void CreateEnumerator(
            ref Guid riid,
            [Out][MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 0)] out object ppUnk);
    }

    /// <summary>
    /// IOPCItemProperties - 查询 Item 属性
    /// </summary>
    [ComImport]
    [GuidAttribute("39c13a72-011e-11d0-9675-0020afd8adb3")]
    [InterfaceTypeAttribute(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOPCItemProperties
    {
        void QueryAvailableProperties(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemID,
            [Out][MarshalAs(UnmanagedType.I4)] out int pdwCount,
            [Out] out IntPtr ppPropertyIDs,
            [Out] out IntPtr ppDescriptions,
            [Out] out IntPtr ppvtDataTypes);

        void GetItemProperties(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemID,
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 1)] int[] pdwPropertyIDs,
            [Out] out IntPtr ppvData,
            [Out] out IntPtr ppErrors);

        void LookupItemIDs(
            [MarshalAs(UnmanagedType.LPWStr)] string szItemID,
            [MarshalAs(UnmanagedType.I4)] int dwCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.I4, SizeParamIndex = 1)] int[] pdwPropertyIDs,
            [Out] out IntPtr ppszNewItemIDs,
            [Out] out IntPtr ppErrors);
    }

    #endregion

    #region 包装类（保持与 OPCService/OPCBrowser 代码兼容的接口）

    /// <summary>
    /// OPC DA 服务器包装类 - 封装真实的 COM 互操作
    /// </summary>
    public class Server : IDisposable
    {
        private object _comServer;      // IOPCServer COM 对象
        private string _progId;
        private string _host;
        private string _clsid;
        private bool _connected;

        /// <summary>获取底层 COM 对象（用于 QueryInterface 获取浏览等接口）</summary>
        public object ComObject { get { return _comServer; } }

        public string Name { get { return _progId; } }
        public bool IsConnected { get { return _connected; } }

        /// <summary>
        /// 创建服务器实例
        /// </summary>
        /// <param name="progIdOrUrl">OPC 服务器 ProgID（如 "KEPware.KEPServerEx.V4"）
        /// 或 opcda://host/ProgID 格式的 URL
        /// 或 opcda://host/{CLSID} 格式（使用花括号包裹 GUID）</param>
        public Server(string progIdOrUrl)
        {
            if (string.IsNullOrEmpty(progIdOrUrl))
                throw new ArgumentNullException("progIdOrUrl");

            // 解析 opcda://host/xxx 格式
            if (progIdOrUrl.StartsWith("opcda://"))
            {
                var uri = new Uri(progIdOrUrl);
                _host = uri.Host;
                string path = uri.AbsolutePath.TrimStart('/');

                // 检查是否是 CLSID 格式 {GUID}
                if (path.StartsWith("{") && path.EndsWith("}"))
                {
                    _clsid = path;
                    _progId = null;
                }
                else
                {
                    _progId = path;
                    _clsid = null;
                }
            }
            else
            {
                _progId = progIdOrUrl;
                _host = null;
                _clsid = null;
            }
        }

        /// <summary>
        /// 连接到 OPC 服务器
        /// </summary>
        public void Connect()
        {
            Type serverType = null;

            if (!string.IsNullOrEmpty(_clsid))
            {
                // 使用 CLSID 直接连接
                try
                {
                    Guid clsid = new Guid(_clsid);
                    if (string.IsNullOrEmpty(_host))
                    {
                        serverType = Type.GetTypeFromCLSID(clsid);
                    }
                    else
                    {
                        serverType = Type.GetTypeFromCLSID(clsid, _host);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format("解析 CLSID 失败: {0}, 错误: {1}", _clsid, ex.Message));
                }
            }
            else if (string.IsNullOrEmpty(_host))
            {
                // 本机连接：通过 ProgID 获取 CLSID
                Console.WriteLine(string.Format("[OPC] 本机连接 ProgID: {0}", _progId));
                serverType = Type.GetTypeFromProgID(_progId);
                if (serverType == null)
                    throw new Exception(string.Format("无法找到 OPC 服务器 ProgID: {0}（请确认 OPC 服务器已安装并注册）", _progId));
            }
            else
            {
                // 远程连接
                Console.WriteLine(string.Format("[OPC] 远程连接: ProgID={0}, Host={1}", _progId, _host));

                // 方法1: 使用 IOPCServerList 远程查询 CLSID（OPC Client 使用的方式）
                Console.WriteLine("[OPC] 尝试通过 IOPCServerList 远程查询 CLSID...");
                Guid remoteClsid = QueryRemoteClsidViaOpcEnum(_progId, _host);
                if (remoteClsid != Guid.Empty)
                {
                    Console.WriteLine(string.Format("[OPC] 远程查询到 CLSID: {0}", remoteClsid));
                    serverType = Type.GetTypeFromCLSID(remoteClsid, _host);
                    if (serverType != null)
                    {
                        Console.WriteLine("[OPC] 通过远程 CLSID 获取 Type 成功");
                    }
                }

                // 方法2: 使用 Type.GetTypeFromProgID 远程查询
                if (serverType == null)
                {
                    Console.WriteLine("[OPC] 尝试 Type.GetTypeFromProgID 远程查询...");
                    serverType = Type.GetTypeFromProgID(_progId, _host);
                }

                // 方法3: 尝试从本地注册表查找（可能已手动注册）
                if (serverType == null)
                {
                    Console.WriteLine("[OPC] 尝试本地注册表查找...");
                    serverType = Type.GetTypeFromProgID(_progId);
                    if (serverType != null)
                    {
                        Console.WriteLine("[OPC] 从本地注册表找到 ProgID，将通过 DCOM 远程连接");
                    }
                }

                if (serverType == null)
                {
                    throw new Exception(string.Format(
                        "无法找到 OPC 服务器 ProgID: {0}\n" +
                        "远程主机: {1}\n" +
                        "已尝试: IOPCServerList查询、Type.GetTypeFromProgID远程查询、本地注册表查找\n" +
                        "解决方案:\n" +
                        "- 确认远程机器 OPC Enum 服务正在运行\n" +
                        "- 确认 DCOM 权限配置正确\n" +
                        "- 在本地注册远程 ProgID（从能连接的机器导出注册表）\n" +
                        "- 或使用 CLSID 连接: opcda://host/{{CLSID}}",
                        _progId, _host));
                }
            }

            Console.WriteLine(string.Format("[OPC] 创建 COM 实例: {0}", serverType.FullName));
            _comServer = Activator.CreateInstance(serverType);
            _connected = true;
            Console.WriteLine("[OPC] 连接成功");
        }

        /// <summary>
        /// 通过 IOPCServerList 远程查询 ProgID 对应的 CLSID
        /// </summary>
        private Guid QueryRemoteClsidViaOpcEnum(string progId, string host)
        {
            object opcEnum = null;
            try
            {
                // IOPCServerList 的 CLSID: {13486D51-4821-11D2-A494-3CB306C10000}
                Guid clsidOpcEnum = new Guid("13486D51-4821-11D2-A494-3CB306C10000");
                Type opcEnumType = Type.GetTypeFromCLSID(clsidOpcEnum, host);
                if (opcEnumType == null)
                {
                    Console.WriteLine("[OPC] 无法获取 OpcEnum 类型");
                    return Guid.Empty;
                }

                opcEnum = Activator.CreateInstance(opcEnumType);
                IOPCServerList serverList = (IOPCServerList)opcEnum;

                // 枚举所有 OPC 服务器，查找匹配的 ProgID
                OpcNetApi.Com.IEnumGUID enumGuid;
                int hr = serverList.EnumClasses(0, out enumGuid);
                if (hr != 0 || enumGuid == null)
                {
                    Console.WriteLine(string.Format("[OPC] EnumClasses 失败: hr={0}", hr));
                    return Guid.Empty;
                }

                Guid currentClsid;
                int fetched;
                while (true)
                {
                    hr = enumGuid.Next(1, out currentClsid, out fetched);
                    if (hr != 0 || fetched == 0) break;

                    string serverProgId, userType, verIndProgId;
                    hr = serverList.GetClassDetails(ref currentClsid, out serverProgId, out userType, out verIndProgId);
                    if (hr == 0)
                    {
                        Console.WriteLine(string.Format("[OPC] 发现服务器: ProgID={0}, CLSID={1}", serverProgId, currentClsid));
                        if (string.Equals(serverProgId, progId, StringComparison.OrdinalIgnoreCase))
                        {
                            return currentClsid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[OPC] IOPCServerList 查询异常: {0}", ex.Message));
            }
            finally
            {
                if (opcEnum != null && Marshal.IsComObject(opcEnum))
                    Marshal.ReleaseComObject(opcEnum);
            }

            return Guid.Empty;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (_comServer != null)
            {
                try
                {
                    // 尝试调用 IOPCServer.RemoveGroup 释放所有组
                    // COM 对象由 GC 释放
                    if (Marshal.IsComObject(_comServer))
                        Marshal.ReleaseComObject(_comServer);
                }
                catch { }
                _comServer = null;
            }
            _connected = false;
        }

        /// <summary>
        /// 创建 OPC Group
        /// </summary>
        public Group CreateGroup(string name, bool active, int updateRate,
            object timeBias, object percentDeadband, object localeID, object clid)
        {
            if (_comServer == null)
                throw new InvalidOperationException("未连接到 OPC 服务器");

            var iopcServer = (IOPCServer)_comServer;
            int hServerGroup;
            int revisedUpdateRate;
            object ppUnk;
            Guid iid = typeof(IOPCItemMgt).GUID;

            iopcServer.AddGroup(
                name,
                active ? 1 : 0,
                updateRate,
                0,                  // hClientGroup
                IntPtr.Zero,        // pTimeBias
                IntPtr.Zero,        // pPercentDeadband
                0,                  // dwLCID
                out hServerGroup,
                out revisedUpdateRate,
                ref iid,
                out ppUnk);

            return new Group(ppUnk, name, active, revisedUpdateRate);
        }

        /// <summary>
        /// 获取服务器浏览接口
        /// </summary>
        public IOPCBrowseServerAddressSpace GetBrowser()
        {
            if (_comServer == null)
                throw new InvalidOperationException("未连接到 OPC 服务器");
            return (IOPCBrowseServerAddressSpace)_comServer;
        }

        /// <summary>
        /// 获取服务器状态
        /// </summary>
        public IOPCServer GetServerInterface()
        {
            if (_comServer == null)
                throw new InvalidOperationException("未连接到 OPC 服务器");
            return (IOPCServer)_comServer;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    /// <summary>
    /// OPC DA Group 包装类 - 封装 IOPCItemMgt / IOPCSyncIO
    /// </summary>
    public class Group : IDisposable
    {
        private object _comGroup;       // IOPCItemMgt / IOPCSyncIO COM 对象
        private string _name;
        private bool _active;
        private int _updateRate;
        private int _serverGroupHandle;
        private int _clientHandleCounter = 1000;

        // 标签映射：ItemID → (serverHandle, clientHandle)
        private System.Collections.Generic.Dictionary<string, int> _itemHandles =
            new System.Collections.Generic.Dictionary<string, int>();

        // 反向映射：serverHandle → ItemID
        private System.Collections.Generic.Dictionary<int, string> _handleToItemId =
            new System.Collections.Generic.Dictionary<int, string>();

        public string Name { get { return _name; } }
        public bool IsActive { get { return _active; } set { _active = value; } }
        public bool IsSubscribed { get; set; }
        public int UpdateRate { get { return _updateRate; } }

        internal Group(object comGroup, string name, bool active, int updateRate)
        {
            _comGroup = comGroup;
            _name = name;
            _active = active;
            _updateRate = updateRate;
        }

        /// <summary>
        /// 添加标签项到 Group
        /// </summary>
        /// <param name="itemIds">OPC Item ID 数组</param>
        /// <returns>添加成功的 ItemID 列表</returns>
        public string[] AddItems(string[] itemIds)
        {
            var itemMgt = (IOPCItemMgt)_comGroup;
            int count = itemIds.Length;
            var defs = new OPCITEMDEF[count];

            for (int i = 0; i < count; i++)
            {
                defs[i] = new OPCITEMDEF
                {
                    szAccessPath = null,
                    szItemID = itemIds[i],
                    bActive = 1,
                    hClient = _clientHandleCounter++,
                    dwBlobSize = 0,
                    pBlob = IntPtr.Zero,
                    vtRequestedDataType = 0,  // 使用原生类型
                    wReserved = 0
                };
            }

            IntPtr pAddResults;
            IntPtr pErrors;
            itemMgt.AddItems(count, defs, out pAddResults, out pErrors);

            // 解析结果
            int[] errors = new int[count];
            Marshal.Copy(pErrors, errors, 0, count);

            // 解析 OPCITEMRESULT
            int resultSize = Marshal.SizeOf(typeof(OPCITEMRESULT));
            var addedItems = new System.Collections.Generic.List<string>();

            for (int i = 0; i < count; i++)
            {
                if (errors[i] == 0) // S_OK
                {
                    IntPtr ptr = new IntPtr(pAddResults.ToInt64() + i * resultSize);
                    OPCITEMRESULT result = (OPCITEMRESULT)Marshal.PtrToStructure(ptr, typeof(OPCITEMRESULT));
                    _itemHandles[itemIds[i]] = result.hServer;
                    _handleToItemId[result.hServer] = itemIds[i];
                    addedItems.Add(itemIds[i]);
                }
            }

            Marshal.FreeCoTaskMem(pAddResults);
            Marshal.FreeCoTaskMem(pErrors);

            return addedItems.ToArray();
        }

        /// <summary>
        /// 同步读取所有已添加标签的值
        /// </summary>
        public System.Collections.Generic.Dictionary<string, object> SyncReadAll()
        {
            var result = new System.Collections.Generic.Dictionary<string, object>();

            if (_itemHandles.Count == 0)
                return result;

            var syncIO = (IOPCSyncIO)_comGroup;
            int count = _itemHandles.Count;
            int[] serverHandles = new int[count];
            _itemHandles.Values.CopyTo(serverHandles, 0);

            IntPtr pItemValues;
            IntPtr pErrors;
            syncIO.Read(OPCDATASOURCE.OPC_DS_CACHE, count, serverHandles, out pItemValues, out pErrors);

            // 解析结果
            int[] errors = new int[count];
            Marshal.Copy(pErrors, errors, 0, count);

            int stateSize = Marshal.SizeOf(typeof(OPCITEMSTATE));
            string[] itemIds = new string[count];
            _itemHandles.Keys.CopyTo(itemIds, 0);

            for (int i = 0; i < count; i++)
            {
                if (errors[i] == 0)
                {
                    IntPtr ptr = new IntPtr(pItemValues.ToInt64() + i * stateSize);
                    OPCITEMSTATE state = (OPCITEMSTATE)Marshal.PtrToStructure(ptr, typeof(OPCITEMSTATE));
                    result[itemIds[i]] = state.vDataValue;
                }
            }

            Marshal.FreeCoTaskMem(pItemValues);
            Marshal.FreeCoTaskMem(pErrors);

            return result;
        }

        /// <summary>
        /// 移除所有标签
        /// </summary>
        public void RemoveAllItems()
        {
            if (_itemHandles.Count == 0) return;

            var itemMgt = (IOPCItemMgt)_comGroup;
            int count = _itemHandles.Count;
            int[] serverHandles = new int[count];
            _itemHandles.Values.CopyTo(serverHandles, 0);

            IntPtr pErrors;
            itemMgt.RemoveItems(count, serverHandles, out pErrors);
            Marshal.FreeCoTaskMem(pErrors);

            _itemHandles.Clear();
            _handleToItemId.Clear();
        }

        /// <summary>
        /// 获取当前已添加的标签列表
        /// </summary>
        public System.Collections.Generic.List<string> GetItemIds()
        {
            return new System.Collections.Generic.List<string>(_itemHandles.Keys);
        }

        public void Dispose()
        {
            try
            {
                RemoveAllItems();
                if (_comGroup != null && Marshal.IsComObject(_comGroup))
                    Marshal.ReleaseComObject(_comGroup);
            }
            catch { }
            _comGroup = null;
        }
    }

    /// <summary>
    /// 静态数据源常量（兼容旧代码）
    /// </summary>
    public static class OpcDataSource
    {
        public const int CacheOrDevice = 1;
    }

    #endregion
}
