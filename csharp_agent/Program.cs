using System;
using System.Threading;


namespace OPC_DA_Agent
{
    class Program
    {
        private static Logger _logger;
        private static Config _config;
        private static OPCService _opcService;
        private static HttpServer _httpServer;
        private static bool _isRunning = true;

        static void Main(string[] args)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   OPC DA 数据采集代理程序");
            Console.WriteLine("   Version: 1.0.0");
            Console.WriteLine("========================================");
            Console.WriteLine();

            try
            {
                // 解析命令行参数
                var configPath = ParseCommandLineArgs(args);

                // 加载配置
                _config = Config.LoadFromFile(configPath);
                var errors = new System.Collections.Generic.List<string>();
                if (!_config.Validate(out errors))
                {
                    Console.WriteLine("配置验证失败:");
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                    Console.WriteLine();
                    Console.WriteLine("已生成示例配置文件，请修改后重新运行");
                    return;
                }

                // 初始化日志
                _logger = new Logger(_config.LogFile, _config.LogLevel);
                _logger.Info("程序启动");

                // 初始化OPC服务
                _opcService = new OPCService(_config, _logger);

                // 连接到OPC服务器
                if (!_opcService.Connect())
                {
                    _logger.Error("无法连接到OPC服务器，程序退出");
                    return;
                }

                // 启动数据采集
                if (!_opcService.Start())
                {
                    _logger.Error("无法启动数据采集，程序退出");
                    return;
                }

                // 初始化HTTP服务器
                _httpServer = new HttpServer(_config, _opcService, _logger);

                // 启动HTTP服务器
                if (!_httpServer.Start())
                {
                    _logger.Error("无法启动HTTP服务器，程序退出");
                    return;
                }

                // 注册控制台退出处理
                Console.CancelKeyPress += OnCancelKeyPress;

                // 显示系统信息
                DisplaySystemInfo();

                // 主循环
                while (_isRunning)
                {
                    Thread.Sleep(1000);
                    UpdateStatusDisplay();
                }

                _logger.Info("程序正常退出");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"程序异常退出: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                _logger?.Error("程序异常退出", ex);
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 解析命令行参数
        /// </summary>
        private static string ParseCommandLineArgs(string[] args)
        {
            var configPath = "config.json";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--config" && i + 1 < args.Length)
                {
                    configPath = args[i + 1];
                }
                else if (args[i] == "--help" || args[i] == "-h")
                {
                    ShowHelp();
                    Environment.Exit(0);
                }
                else if (args[i] == "--example-config")
                {
                    var example = Config.GetExampleConfig();
                    example.SaveToFile("config.example.json");
                    Console.WriteLine("示例配置文件已生成: config.example.json");
                    Environment.Exit(0);
                }
            }

            return configPath;
        }

        /// <summary>
        /// 显示帮助信息
        /// </summary>
        private static void ShowHelp()
        {
            Console.WriteLine("用法: OPC_DA_Agent.exe [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --config <path>        指定配置文件路径 (默认: config.json)");
            Console.WriteLine("  --example-config       生成示例配置文件");
            Console.WriteLine("  --help, -h             显示此帮助信息");
            Console.WriteLine();
            Console.WriteLine("示例:");
            Console.WriteLine("  OPC_DA_Agent.exe");
            Console.WriteLine("  OPC_DA_Agent.exe --config my_config.json");
            Console.WriteLine("  OPC_DA_Agent.exe --example-config");
        }

        /// <summary>
        /// 显示系统信息
        /// </summary>
        private static void DisplaySystemInfo()
        {
            Console.WriteLine();
            Console.WriteLine("系统信息:");
            Console.WriteLine($"  OPC服务器: {_config.OpcServerUrl}");
            Console.WriteLine($"  连接状态: {( _opcService.IsConnected ? "已连接" : "未连接")}");
            Console.WriteLine($"  标签数量: {_opcService.TagCount}");
            Console.WriteLine($"  HTTP端口: {_config.HttpPort}");
            Console.WriteLine($"  更新间隔: {_config.UpdateInterval}ms");
            Console.WriteLine($"  日志文件: {_config.LogFile}");
            Console.WriteLine();
            Console.WriteLine("可用API端点:");
            Console.WriteLine($"  GET  http://localhost:{_config.HttpPort}/api/status");
            Console.WriteLine($"  GET  http://localhost:{_config.HttpPort}/api/data");
            Console.WriteLine($"  POST http://localhost:{_config.HttpPort}/api/data/batch");
            Console.WriteLine();
            Console.WriteLine("按 Ctrl+C 停止程序");
            Console.WriteLine();
        }

        /// <summary>
        /// 更新状态显示
        /// </summary>
        private static void UpdateStatusDisplay()
        {
            var status = _opcService.GetStatus();
            Console.SetCursorPosition(0, 15);
            Console.WriteLine($"运行时间: {status.UptimeSeconds:F0}秒 | 数据读取: {status.TotalRequests}次 | 错误: {status.ErrorCount} | 内存: {status.MemoryUsageMb:F1}MB");
        }

        /// <summary>
        /// 取消按键处理
        /// </summary>
        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _isRunning = false;
            Console.WriteLine("\n正在停止程序...");
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private static void Cleanup()
        {
            _httpServer?.Dispose();
            _opcService?.Dispose();
            _logger?.Dispose();
        }
    }
}
