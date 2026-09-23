using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace OPC_DA_Agent
{
    /// <summary>
    /// 配置对话框：OPC 地址 / HTTP 端口 / 采集频率 / 日志级别。
    /// 校验通过后逐段热应用（日志级别→OPC重连→频率→端口），最后写回 config.json。
    /// </summary>
    public class SettingsForm : Form
    {
        private readonly Config _config;
        private readonly string _configPath;
        private readonly Logger _logger;
        private readonly OPCService _opcService;
        private readonly HttpServer _httpServer;

        private TextBox _txtProgId;
        private TextBox _txtHost;
        private NumericUpDown _numPort;
        private NumericUpDown _numUpdate;
        private NumericUpDown _numSnapshot;
        private ComboBox _cmbLevel;

        public SettingsForm(Config config, string configPath, Logger logger, OPCService opcService, HttpServer httpServer)
        {
            _config = config;
            _configPath = configPath;
            _logger = logger;
            _opcService = opcService;
            _httpServer = httpServer;

            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            Text = "代理配置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(450, 260);
            Padding = new Padding(10);

            var grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 7;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
            }
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));

            grid.Controls.Add(MakeCaption("OPC ProgID:"), 0, 0);
            _txtProgId = new TextBox();
            _txtProgId.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtProgId, 1, 0);

            grid.Controls.Add(MakeCaption("OPC 主机:"), 0, 1);
            _txtHost = new TextBox();
            _txtHost.Dock = DockStyle.Fill;
            grid.Controls.Add(_txtHost, 1, 1);

            grid.Controls.Add(MakeCaption("HTTP 端口:"), 0, 2);
            _numPort = MakeNumeric(1, 65535);
            grid.Controls.Add(_numPort, 1, 2);

            grid.Controls.Add(MakeCaption("更新间隔(ms):"), 0, 3);
            _numUpdate = MakeNumeric(100, 600000);
            grid.Controls.Add(_numUpdate, 1, 3);

            grid.Controls.Add(MakeCaption("SSE快照间隔(ms,0=关):"), 0, 4);
            _numSnapshot = MakeNumeric(0, 600000);
            grid.Controls.Add(_numSnapshot, 1, 4);

            grid.Controls.Add(MakeCaption("日志级别:"), 0, 5);
            _cmbLevel = new ComboBox();
            _cmbLevel.Dock = DockStyle.Fill;
            _cmbLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLevel.Items.AddRange(new object[] { "Debug", "Info", "Warn", "Error", "Fatal" });
            grid.Controls.Add(_cmbLevel, 1, 5);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 8, 0, 0);

            var btnOk = new Button();
            btnOk.Text = "应用并保存";
            btnOk.AutoSize = true;
            btnOk.Click += OnApply;
            buttons.Controls.Add(btnOk);
            AcceptButton = btnOk;

            var btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.AutoSize = true;
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            buttons.Controls.Add(btnCancel);
            CancelButton = btnCancel;

            grid.Controls.Add(buttons, 0, 6);
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

        private static NumericUpDown MakeNumeric(decimal min, decimal max)
        {
            var num = new NumericUpDown();
            num.Dock = DockStyle.Fill;
            num.Minimum = min;
            num.Maximum = max;
            num.ThousandsSeparator = true;
            return num;
        }

        private void LoadValues()
        {
            _txtProgId.Text = _config.OpcServerProgId;
            _txtHost.Text = _config.OpcServerHost;
            _numPort.Value = _config.HttpPort;
            _numUpdate.Value = _config.UpdateInterval;
            _numSnapshot.Value = Math.Max(0, _config.SseSnapshotIntervalMs);

            var level = _config.LogLevel;
            var index = _cmbLevel.FindStringExact(level);
            _cmbLevel.SelectedIndex = index >= 0 ? index : 1;
        }

        private void OnApply(object sender, EventArgs e)
        {
            // 深拷贝当前配置作为候选：校验不通过时不污染运行中的 _config
            var candidate = JsonConvert.DeserializeObject<Config>(JsonConvert.SerializeObject(_config));
            candidate.OpcServerProgId = _txtProgId.Text.Trim();
            candidate.OpcServerHost = _txtHost.Text.Trim();
            candidate.HttpPort = (int)_numPort.Value;
            candidate.UpdateInterval = (int)_numUpdate.Value;
            candidate.SseSnapshotIntervalMs = (int)_numSnapshot.Value;
            candidate.LogLevel = _cmbLevel.SelectedItem != null ? _cmbLevel.SelectedItem.ToString() : "Info";

            List<string> errors;
            if (!candidate.Validate(out errors))
            {
                MessageBox.Show(this, "配置验证失败:" + Environment.NewLine
                    + "  - " + string.Join(Environment.NewLine + "  - ", errors.ToArray()),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var messages = new List<string>();

            if (candidate.LogLevel != _config.LogLevel)
            {
                _config.LogLevel = candidate.LogLevel;
                _logger.SetLevel(candidate.LogLevel);
                messages.Add("日志级别已生效: " + candidate.LogLevel);
            }

            var opcChanged = candidate.OpcServerProgId != _config.OpcServerProgId
                || candidate.OpcServerHost != _config.OpcServerHost;
            if (opcChanged)
            {
                _config.OpcServerProgId = candidate.OpcServerProgId;
                _config.OpcServerHost = candidate.OpcServerHost;
                // 必须清空旧 URL 私有字段：否则下次加载配置时旧 opc_server_url 会反向覆盖新 ProgID
                _config.OpcServerUrl = null;
                bool ok = _opcService.Reconnect();
                messages.Add(ok
                    ? "OPC 已按新地址重连成功: " + _config.OpcServerUrl
                    : "OPC 重连失败（配置将照常保存，可修正服务器后再次应用）");
            }

            if (candidate.UpdateInterval != _config.UpdateInterval)
            {
                _config.UpdateInterval = candidate.UpdateInterval;
                _opcService.ApplyUpdateRate();
                messages.Add("更新间隔已生效: " + candidate.UpdateInterval + "ms");
            }

            if (candidate.SseSnapshotIntervalMs != _config.SseSnapshotIntervalMs)
            {
                _config.SseSnapshotIntervalMs = candidate.SseSnapshotIntervalMs;
                _opcService.ApplySseInterval();
                messages.Add(candidate.SseSnapshotIntervalMs > 0
                    ? "SSE快照间隔已生效: " + candidate.SseSnapshotIntervalMs + "ms"
                    : "SSE秒级快照已关闭（仅变化推送）");
            }

            if (candidate.HttpPort != _config.HttpPort)
            {
                int oldPort = _config.HttpPort;
                _config.HttpPort = candidate.HttpPort;
                _httpServer.Stop();
                if (!_httpServer.Start())
                {
                    _config.HttpPort = oldPort;
                    _httpServer.Start();
                    messages.Add("端口 " + candidate.HttpPort + " 绑定失败，已回滚到 " + oldPort);
                }
                else
                {
                    messages.Add("HTTP端口已切换: " + candidate.HttpPort
                        + "（采集器数据源 URL 请同步改为新端口）");
                }
            }

            try
            {
                _config.SaveToFile(_configPath);
                messages.Add("配置已保存: " + _configPath);
            }
            catch (Exception ex)
            {
                _logger.Error("保存配置文件失败", ex);
                messages.Add("保存配置文件失败: " + ex.Message);
            }

            MessageBox.Show(this, string.Join(Environment.NewLine, messages.ToArray()),
                "应用结果", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
