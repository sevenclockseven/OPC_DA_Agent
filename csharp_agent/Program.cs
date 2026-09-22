using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace OPC_DA_Agent
{
    class Program
    {
        private static Logger _logger;
        private static Config _config;
        private static OPCService _opcService;
        private static HttpServer _httpServer;
        private static bool _exitOnClose;

        [STAThread]
        static void Main(string[] args)
        {
            // WinForms 初始化必须在任何控件（含 MessageBox）创建之前
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                // 解析命令行参数
                var configPath = ParseCommandLineArgs(args);

                // 加载配置（文件不存在时 LoadFromFile 会生成默认配置）
                _config = Config.LoadFromFile(configPath);
                var errors = new List<string>();
                if (!_config.Validate(out errors))
                {
                    var msg = "配置验证失败:" + Environment.NewLine;
                    foreach (var error in errors)
                    {
                        msg += "  - " + error + Environment.NewLine;
                    }
                    msg += Environment.NewLine + "已生成示例配置文件，请修改后重新运行";
                    ShowUserMessage(msg, "OPC DA 数据采集代理", MessageBoxIcon.Error);
                    return;
                }

                // 初始化日志
                _logger = new Logger(_config.LogFile, _config.LogLevel);
                _logger.Info("程序启动");

                // 初始化OPC服务
                _opcService = new OPCService(_config, _logger, configPath);

                // 连接到OPC服务器（连接失败不退出程序，仍然启动 Web UI）
                _opcService.Connect();

                // 启动数据采集（tags 为空或连接失败时也可以启动 Group）
                _opcService.Start();

                // 初始化HTTP服务器
                _httpServer = new HttpServer(_config, _opcService, _logger);

                // 启动HTTP服务器
                if (!_httpServer.Start())
                {
                    _logger.Error("无法启动HTTP服务器，程序退出");
                    ShowUserMessage($"无法启动HTTP服务器（端口 {_config.HttpPort} 可能已被占用）",
                        "OPC DA 数据采集代理", MessageBoxIcon.Error);
                    return;
                }

                _logger.Info($"系统信息 | OPC服务器: {_config.OpcServerUrl} | HTTP端口: {_config.HttpPort} | 更新间隔: {_config.UpdateInterval}ms | 日志文件: {_config.LogFile}");

                // 桌面消息循环：窗口关闭默认最小化到托盘继续运行，退出时才结束循环并走 finally Cleanup
                using (var mainForm = new MainForm(_config, _opcService, _logger, _exitOnClose))
                {
                    Application.Run(mainForm);
                }

                _logger.Info("程序正常退出");
            }
            catch (Exception ex)
            {
                _logger?.Error("程序异常退出", ex);
                ShowUserMessage($"程序异常退出: {ex.Message}", "OPC DA 数据采集代理", MessageBoxIcon.Error);
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
                else if (args[i] == "--exit-on-close")
                {
                    _exitOnClose = true;
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
                    ShowUserMessage("示例配置文件已生成: config.example.json",
                        "OPC DA 数据采集代理", MessageBoxIcon.Information);
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
            var help = "用法: OPC_DA_Agent.exe [选项]" + Environment.NewLine
                + Environment.NewLine
                + "选项:" + Environment.NewLine
                + "  --config <path>        指定配置文件路径 (默认: config.json)" + Environment.NewLine
                + "  --example-config       生成示例配置文件" + Environment.NewLine
                + "  --exit-on-close        点击窗口关闭按钮时直接退出（默认为最小化到托盘）" + Environment.NewLine
                + "  --help, -h             显示此帮助信息" + Environment.NewLine
                + Environment.NewLine
                + "示例:" + Environment.NewLine
                + "  OPC_DA_Agent.exe" + Environment.NewLine
                + "  OPC_DA_Agent.exe --config my_config.json" + Environment.NewLine
                + "  OPC_DA_Agent.exe --example-config";
            ShowUserMessage(help, "OPC DA 数据采集代理 - 帮助", MessageBoxIcon.Information);
        }

        /// <summary>
        /// 显示用户可见消息。服务会话(Session 0)无人点击，跳过 MessageBox 以免挂死。
        /// </summary>
        private static void ShowUserMessage(string text, string caption, MessageBoxIcon icon)
        {
            if (IsServiceSession()) return;
            MessageBox.Show(text, caption, MessageBoxButtons.OK, icon);
        }

        /// <summary>
        /// 是否运行在 Windows 服务会话(Session 0)。服务会话中窗口关闭必须直接退出，否则 NSSM stop 挂死。
        /// </summary>
        public static bool IsServiceSession()
        {
            try
            {
                return Process.GetCurrentProcess().SessionId == 0;
            }
            catch (Exception)
            {
                // SessionId 获取失败时按桌面会话处理（关闭→最小化到托盘）
                return false;
            }
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
