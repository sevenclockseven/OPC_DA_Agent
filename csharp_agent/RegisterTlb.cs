using System;
using System.Runtime.InteropServices;

class GenInterop
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode)]
    static extern int LoadTypeLibEx(string szFile, int regKind, out IntPtr pTlb);

    const int REGKIND_NONE = 0;
    const int REGKIND_REGISTER = 1;

    static void Main(string[] args)
    {
        string tlbPath = args.Length > 0 ? args[0] : @"D:\vscode\OPC_DA_Agent\csharp_agent\OPCDAAuto.dll";

        // 先注册类型库
        Console.WriteLine("Registering type library...");
        IntPtr tlb;
        int hr = LoadTypeLibEx(tlbPath, REGKIND_REGISTER, out tlb);
        Console.WriteLine("LoadTypeLibEx(REGISTER): " + (hr == 0 ? "OK" : "0x" + hr.ToString("X8")));

        // 验证注册
        Console.WriteLine("\nVerifying registration:");
        try { var t = Type.GetTypeFromProgID("OPCAutomation.OPCServer"); Console.WriteLine("  ProgID lookup: " + (t != null ? "FOUND" : "NOT FOUND")); } catch (Exception ex) { Console.WriteLine("  ProgID lookup error: " + ex.Message); }

        // 列出已注册的 CLSID
        Console.WriteLine("\nRegistered CLSIDs:");
        try
        {
            var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\{28E68F9A-8D75-11D1-8DC3-3C302A000000}");
            Console.WriteLine("  CLSID {28E68F9A-8D75-11D1-8DC3-3C302A000000}: " + (key != null ? "REGISTERED" : "NOT FOUND"));
            if (key != null) key.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine("  CLSID check error: " + ex.Message);
        }

        // 尝试使用已注册的类型库来创建 COM 对象
        Console.WriteLine("\nTesting COM creation:");
        try
        {
            Type serverType = Type.GetTypeFromProgID("OPCAutomation.OPCServer");
            if (serverType != null)
            {
                var obj = Activator.CreateInstance(serverType);
                Console.WriteLine("  OPCServer created: " + (obj != null ? "OK" : "FAIL"));
                if (obj != null) Marshal.ReleaseComObject(obj);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Creation error: " + ex.Message);
        }

        Console.WriteLine("\nDone. Press any key...");
        Console.ReadKey();
    }
}