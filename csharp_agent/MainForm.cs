using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OPC_DA_Agent
{
    /// <summary>
    /// 桌面主窗口：状态展示 + 系统托盘。
    /// 关闭窗口默认最小化到托盘（后台继续采集）；服务会话(Session 0)或主动退出时才真正退出。
    /// </summary>
    public class MainForm : Form
    {
        private readonly Config _config;
        private readonly OPCService _opcService;
        private readonly Logger _logger;

        private readonly Timer _statusTimer;
        private NotifyIcon _trayIcon;

        private Label _lblConn;
        private Label _lblUptime;
        private Label _lblTags;
        private Label _lblReads;
        private Label _lblErrors;
        private Label _lblMemory;
        private Label _lblServer;
        private Label _lblPort;
        private Label _lblInterval;
        private Label _lblLog;

        private bool _exitRequested;

        public MainForm(Config config, OPCService opcService, Logger logger, bool exitOnClose)
        {
            _config = config;
            _opcService = opcService;
            _logger = logger;
            _exitRequested = exitOnClose;

            BuildUi();
            BuildTray();

            _statusTimer = new Timer();
            _statusTimer.Interval = 1000;
            _statusTimer.Tick += OnStatusTick;
            _statusTimer.Start();

            RefreshStatus();
        }

        private void BuildUi()
        {
            Text = "OPC DA 数据采集代理";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 310);
            Padding = new Padding(10);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 11;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 10; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

            grid.Controls.Add(MakeCaption("连接状态:"), 0, 0);
            _lblConn = MakeValue();
            grid.Controls.Add(_lblConn, 1, 0);

            grid.Controls.Add(MakeCaption("运行时间:"), 0, 1);
            _lblUptime = MakeValue();
            grid.Controls.Add(_lblUptime, 1, 1);

            grid.Controls.Add(MakeCaption("标签数量:"), 0, 2);
            _lblTags = MakeValue();
            grid.Controls.Add(_lblTags, 1, 2);

            grid.Controls.Add(MakeCaption("数据读取:"), 0, 3);
            _lblReads = MakeValue();
            grid.Controls.Add(_lblReads, 1, 3);

            grid.Controls.Add(MakeCaption("错误数:"), 0, 4);
            _lblErrors = MakeValue();
            grid.Controls.Add(_lblErrors, 1, 4);

            grid.Controls.Add(MakeCaption("内存占用:"), 0, 5);
            _lblMemory = MakeValue();
            grid.Controls.Add(_lblMemory, 1, 5);

            grid.Controls.Add(MakeCaption("OPC服务器:"), 0, 6);
            _lblServer = MakeValue();
            grid.Controls.Add(_lblServer, 1, 6);

            grid.Controls.Add(MakeCaption("HTTP端口:"), 0, 7);
            _lblPort = MakeValue();
            grid.Controls.Add(_lblPort, 1, 7);

            grid.Controls.Add(MakeCaption("更新间隔:"), 0, 8);
            _lblInterval = MakeValue();
            grid.Controls.Add(_lblInterval, 1, 8);

            grid.Controls.Add(MakeCaption("日志文件:"), 0, 9);
            _lblLog = MakeValue();
            grid.Controls.Add(_lblLog, 1, 9);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 8, 0, 0);

            var btnWeb = new Button();
            btnWeb.Text = "打开 Web UI";
            btnWeb.AutoSize = true;
            btnWeb.Click += OnBtnWebClick;
            buttons.Controls.Add(btnWeb);

            var btnLog = new Button();
            btnLog.Text = "打开日志";
            btnLog.AutoSize = true;
            btnLog.Click += OnBtnLogClick;
            buttons.Controls.Add(btnLog);

            grid.Controls.Add(buttons, 0, 10);
            grid.SetColumnSpan(buttons, 2);

            Controls.Add(grid);
        }

        private static Label MakeCaption(string text)
        {
            var label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static Label MakeValue()
        {
            var label = new Label();
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            return label;
        }

        private void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开主界面(&O)", null, OnTrayOpen);
            menu.Items.Add("打开 Web UI(&W)", null, OnTrayWeb);
            menu.Items.Add("打开日志文件(&L)", null, OnTrayLog);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出(&X)", null, OnTrayExit);

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = LoadAppIcon();
            _trayIcon.Text = "OPC DA 数据采集代理";
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += OnTrayOpen;
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch (Exception)
            {
                // 提取失败时回退到系统默认图标
            }
            return SystemIcons.Application;
        }

        private void OnStatusTick(object sender, EventArgs e)
        {
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            try
            {
                var status = _opcService.GetStatus();

                _lblConn.Text = status.IsConnected ? "已连接" : "未连接";
                _lblConn.ForeColor = status.IsConnected ? Color.ForestGreen : Color.Firebrick;
                _lblUptime.Text = $"{status.UptimeSeconds:F0} 秒";
                _lblTags.Text = status.TagCount.ToString();
                _lblReads.Text = $"{status.TotalRequests} 次";
                _lblErrors.Text = status.ErrorCount.ToString();
                _lblMemory.Text = $"{status.MemoryUsageMb:F1} MB";
                _lblServer.Text = _config.OpcServerUrl;
                _lblPort.Text = _config.HttpPort.ToString();
                _lblInterval.Text = $"{_config.UpdateInterval} ms";
                _lblLog.Text = _config.LogFile;
            }
            catch (Exception ex)
            {
                _logger?.Error("刷新状态显示失败", ex);
            }
        }

        /// <summary>
        /// 关闭行为：默认（桌面会话 + 用户点 X）最小化到托盘；服务会话/主动退出/系统关机直接退出。
        /// 服务会话必须直接退出，否则 NSSM stop 发来的 WM_CLOSE 会被取消、服务停不下来。
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exitRequested
                && e.CloseReason == CloseReason.UserClosing
                && !Program.IsServiceSession())
            {
                e.Cancel = true;
                Hide();
                try
                {
                    _trayIcon.ShowBalloonTip(2000, "OPC DA 数据采集代理",
                        "已最小化到托盘，程序继续运行。右键托盘图标可退出。", ToolTipIcon.Info);
                }
                catch (Exception)
                {
                    // 系统禁用通知时忽略气泡失败，不影响托盘驻留
                }
                _logger?.Info("窗口关闭，已最小化到托盘（程序继续运行）");
                return;
            }

            _exitRequested = true;
            _logger?.Info($"收到退出请求（原因: {e.CloseReason}），执行优雅退出");
            base.OnFormClosing(e);
        }

        public void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void OnTrayOpen(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void OnTrayWeb(object sender, EventArgs e)
        {
            OpenWebUi();
        }

        private void OnTrayLog(object sender, EventArgs e)
        {
            OpenLogFolder();
        }

        private void OnTrayExit(object sender, EventArgs e)
        {
            _exitRequested = true;
            _logger?.Info("托盘退出请求");
            Close();
        }

        private void OnBtnWebClick(object sender, EventArgs e)
        {
            OpenWebUi();
        }

        private void OnBtnLogClick(object sender, EventArgs e)
        {
            OpenLogFolder();
        }

        private void OpenWebUi()
        {
            try
            {
                Process.Start($"http://localhost:{_config.HttpPort}/");
            }
            catch (Exception ex)
            {
                _logger?.Error("打开Web UI失败", ex);
                MessageBox.Show(this, $"打开Web UI失败: {ex.Message}", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                var logPath = _config.LogFile;
                if (!Path.IsPathRooted(logPath))
                {
                    logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logPath);
                }

                if (File.Exists(logPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{logPath}\"");
                }
                else
                {
                    var dir = Path.GetDirectoryName(logPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                    else
                    {
                        MessageBox.Show(this, $"日志文件尚不存在: {logPath}", Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error("打开日志文件失败", ex);
                MessageBox.Show(this, $"打开日志文件失败: {ex.Message}", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_statusTimer != null)
                {
                    _statusTimer.Stop();
                    _statusTimer.Dispose();
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
