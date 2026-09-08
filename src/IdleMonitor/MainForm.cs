using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IdleMonitor
{
    public class MainForm : Form
    {
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private IdleDetectionService _idleService;
        private ConfigService _configService;
        private Timer _configPollTimer;
        private Timer _uiTimer;
        private string _localIP;
        private string _deviceId;
        private bool _isExiting;
        private bool _telemetryEnabled = true;

        private string _startupReportUrl = "https://12332131.935282.xyz:8443/api/report";
        private string _configApiUrl = "https://12332131.935282.xyz:8443/api/config";
        private string _idleReportUrl = "https://12332131.935282.xyz:8443/api/idle-report";
        private string _apiToken = string.Empty;

        private static readonly string ApiConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleMonitor", "api_config.txt");

        public MainForm()
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            _deviceId = DeviceIdService.GetDeviceId();
            LoadApiUrls();
            BuildTrayIcon();

            _idleService = new IdleDetectionService(300);
            _idleService.IdleThresholdExceeded += OnIdleThresholdExceeded;
            _idleService.IdleEnded += OnIdleEnded;

            ReinitConfigService(false);
            _configPollTimer = new Timer { Interval = 300000 };
            _configPollTimer.Tick += OnConfigPollTick;
            _uiTimer = new Timer { Interval = 1000 };
            _uiTimer.Tick += OnUiTick;
            Load += MainForm_Load;
            FormClosing += OnFormClosingHidden;
        }

        private void LoadApiUrls()
        {
            try
            {
                if (!File.Exists(ApiConfigFile)) return;
                string[] lines = File.ReadAllLines(ApiConfigFile);
                string normalized;
                string error;
                if (lines.Length > 0 && ApiUrlValidator.TryValidate(lines[0], out normalized, out error)) _startupReportUrl = normalized;
                if (lines.Length > 1 && ApiUrlValidator.TryValidate(lines[1], out normalized, out error)) _configApiUrl = normalized;
                if (lines.Length > 2 && ApiUrlValidator.TryValidate(lines[2], out normalized, out error)) _idleReportUrl = normalized;
                if (lines.Length > 3) _apiToken = lines[3].Trim();
            }
            catch (Exception ex) { AppLogger.Error("API 配置读取失败", ex); }
        }

        private void SaveApiUrls()
        {
            try
            {
                string dir = Path.GetDirectoryName(ApiConfigFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(ApiConfigFile, new[] { _startupReportUrl, _configApiUrl, _idleReportUrl, _apiToken });
            }
            catch (Exception ex) { AppLogger.Error("API 配置保存失败", ex); }
        }

        private void BuildTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("打开设置", null, OnOpen);
            _trayMenu.Items.Add("查看状态", null, OnShowStatus);
            _trayMenu.Items.Add("暂停/恢复采集", null, OnToggleTelemetry);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出", null, OnExit);
            _trayIcon = new NotifyIcon { Icon = SystemIcons.Information, Text = "空闲监控 启动中...", ContextMenuStrip = _trayMenu, Visible = true };
            _trayIcon.DoubleClick += OnOpen;
            _trayIcon.ShowBalloonTip(3000, "空闲监控", "程序已启动。默认不采集公网 IP。", ToolTipIcon.Info);
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            Hide();
            _localIP = NetworkService.GetLocalIP();
            ReportStartupAsync();
            _idleService.Start();
            _uiTimer.Start();
            _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
            _configPollTimer.Start();
            await Task.Run(() => _configService.FetchConfig());
        }

        private void OnUiTick(object sender, EventArgs e)
        {
            if (_isExiting) return;
            int idleSec = _idleService.CurrentIdleSeconds;
            _trayIcon.Text = "空闲监控 空闲:" + idleSec + "s / 阈值:" + _idleService.ThresholdSeconds + "s";
            _idleService.CheckAndTrigger();
        }

        private void OnIdleThresholdExceeded(object sender, int idleSeconds)
        {
            if (_isExiting) return;
            _trayIcon.ShowBalloonTip(3000, "空闲监控", "电脑已空闲 " + idleSeconds + " 秒（阈值: " + _idleService.ThresholdSeconds + " 秒）", ToolTipIcon.Warning);
            if (!_telemetryEnabled) return;
            Task.Run(() =>
            {
                try
                {
                    ReportService.Post(_idleReportUrl, new ReportService.IdleReport
                    {
                        DeviceId = _deviceId, IdleSeconds = idleSeconds,
                        ThresholdSeconds = _idleService.ThresholdSeconds, Event = "idle_exceeded"
                    }, _apiToken);
                    AppLogger.Info("空闲事件上报成功");
                }
                catch (Exception ex) { AppLogger.Error("空闲事件上报失败", ex); }
            });
        }

        private void OnIdleEnded(object sender, EventArgs e)
        {
            if (!_isExiting) _trayIcon.ShowBalloonTip(2000, "空闲监控", "检测到用户活动，已重置空闲计数器", ToolTipIcon.Info);
        }

        private void OnConfigUpdated(object sender, EventArgs e)
        {
            _idleService.UpdateThreshold(_configService.IdleThresholdSeconds);
            if (IsDisposed || _isExiting) return;
            BeginInvoke(new Action(() =>
            {
                if (_isExiting || IsDisposed) return;
                _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
                _trayIcon.ShowBalloonTip(2000, "空闲监控", "配置已更新 - 空闲阈值: " + _configService.IdleThresholdSeconds + " 秒", ToolTipIcon.Info);
            }));
        }

        private void OnConfigPollTick(object sender, EventArgs e)
        {
            _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
            Task.Run(() => _configService.FetchConfig());
        }

        private void ReportStartupAsync()
        {
            if (!_telemetryEnabled) return;
            Task.Run(() =>
            {
                try
                {
                    ReportService.Post(_startupReportUrl, new ReportService.StartupReport
                    { DeviceId = _deviceId, LocalIp = _localIP, Event = "startup" }, _apiToken);
                    AppLogger.Info("启动事件上报成功");
                }
                catch (Exception ex) { AppLogger.Error("启动事件上报失败", ex); }
            });
        }

        private void ReinitConfigService(bool start)
        {
            if (_configService != null)
            {
                _configService.ConfigUpdated -= OnConfigUpdated;
                _configService.Dispose();
            }
            try
            {
                _configService = new ConfigService(_configApiUrl, _deviceId, _apiToken);
                _configService.ConfigUpdated += OnConfigUpdated;
                _idleService.UpdateThreshold(_configService.IdleThresholdSeconds);
                if (start && _configPollTimer != null) _configPollTimer.Start();
            }
            catch (Exception ex)
            {
                AppLogger.Error("配置服务初始化失败", ex);
                MessageBox.Show(ex.Message, "配置错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnToggleTelemetry(object sender, EventArgs e)
        {
            _telemetryEnabled = !_telemetryEnabled;
            _trayIcon.ShowBalloonTip(2000, "空闲监控", _telemetryEnabled ? "已恢复数据采集" : "已暂停数据采集", ToolTipIcon.Info);
        }

        private void OnOpen(object sender, EventArgs e)
        {
            using (var settingsForm = new SettingsForm(_startupReportUrl, _configApiUrl, _idleReportUrl, _apiToken))
            {
                if (settingsForm.ShowDialog() != DialogResult.OK) return;
                _startupReportUrl = settingsForm.StartupReportUrl;
                _configApiUrl = settingsForm.ConfigApiUrl;
                _idleReportUrl = settingsForm.IdleReportUrl;
                _apiToken = settingsForm.ApiToken;
                SaveApiUrls();
                _configPollTimer.Stop();
                ReinitConfigService(true);
                _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
                Task.Run(() => _configService.FetchConfig());
                _trayIcon.ShowBalloonTip(3000, "空闲监控", "API 地址已更新", ToolTipIcon.Info);
            }
        }

        private void OnShowStatus(object sender, EventArgs e)
        {
            string msg = "设备ID: " + _deviceId + "\n内网IP: " + (_localIP ?? "获取中") +
                "\n数据采集: " + (_telemetryEnabled ? "开启" : "暂停") + "\n当前空闲: " + _idleService.CurrentIdleSeconds +
                " 秒\n空闲阈值: " + _idleService.ThresholdSeconds + " 秒\n配置轮询: 每 " + _configService.ConfigPollIntervalSeconds +
                " 秒\n\n启动上报: " + _startupReportUrl + "\n配置接口: " + _configApiUrl + "\n空闲上报: " + _idleReportUrl;
            MessageBox.Show(msg, "IdleMonitor - 查看状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExit(object sender, EventArgs e)
        {
            _isExiting = true;
            _configPollTimer.Stop();
            _uiTimer.Stop();
            _idleService.Stop();
            if (_configService != null) _configService.Dispose();
            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
            Application.Exit();
        }

        private void OnFormClosingHidden(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !_isExiting) { e.Cancel = true; Hide(); }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_idleService != null) _idleService.Dispose();
                if (_configService != null) _configService.Dispose();
                if (_configPollTimer != null) _configPollTimer.Dispose();
                if (_uiTimer != null) _uiTimer.Dispose();
                if (_trayIcon != null) _trayIcon.Dispose();
                if (_trayMenu != null) _trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
