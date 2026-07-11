// OPC DA 通用 COM 接口定义
// IEnumString - COM 标准枚举接口，OPC 浏览功能依赖此接口
using System;
using System.Runtime.InteropServices;

namespace OpcNetApi.Com
{
    /// <summary>
    /// COM 标准字符串枚举接口 - IOPCBrowseServerAddressSpace.BrowseOPCItemIDs 返回此接口
    /// </summary>
    [ComImport]
    [Guid("00000101-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IEnumString
    {
        [PreserveSig]
        int Next(
            [MarshalAs(UnmanagedType.I4)] int celt,
            [Out][MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr, SizeParamIndex = 0)] string[] rgelt,
            [Out][MarshalAs(UnmanagedType.I4)] out int pceltFetched);

        void Skip([MarshalAs(UnmanagedType.I4)] int celt);
        void Reset();
        void Clone(out IEnumString ppEnum);
    }

    /// <summary>COM 错误码常量</summary>
    public static class ComErrorCodes
    {
        public const int S_OK = 0;
        public const int E_FAIL = -2147467259;
        public const int S_FALSE = 1;
        public const int E_INVALIDARG = -2147024809;
        public const int E_OUTOFMEMORY = -2147024882;
        public const int E_NOINTERFACE = -2147467262;
        public const int CONNECT_E_NOCONNECTION = -2147220992;
        public const int OPC_E_INVALIDITEMID = unchecked((int)0xC0040007);
        public const int OPC_E_UNKNOWNITEMID = unchecked((int)0xC0040008);
        public const int OPC_E_INVALIDPATH = unchecked((int)0xC0040009);
    }
}
