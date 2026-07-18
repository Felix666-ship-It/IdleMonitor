using System;
using System.Drawing;
using System.IO;
using System.Text;
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
        private Timer _uiTimer; // 主 UI 定时器：负责更新托盘 + 触发空闲检测

        private string _localIP;
        private string _externalIP;
        private string _deviceId;

        private string _startupReportUrl = "http://localhost:5000/api/report";
        private string _configApiUrl = "http://localhost:5000/api/config";
        private string _idleReportUrl = "http://localhost:5000/api/idle-report";

        private static readonly string ApiConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleMonitor",
            "api_config.txt");

        public MainForm()
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;

            // 初始化顺序：设备ID → API 地址 → 托盘 → 服务
            _deviceId = DeviceIdService.GetDeviceId();
            LoadApiUrls();
            BuildTrayIcon();

            _idleService = new IdleDetectionService(300);
            _idleService.IdleThresholdExceeded += OnIdleThresholdExceeded;
            _idleService.IdleEnded += OnIdleEnded;

            _configService = new ConfigService(_configApiUrl, _deviceId);
            _configService.ConfigUpdated += OnConfigUpdated;

            // 配置轮询定时器（WinForms Timer，UI 线程触发）
            _configPollTimer = new Timer();
            _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
            _configPollTimer.Tick += OnConfigPollTick;

            // 主 UI 定时器：每秒执行一次，在 UI 线程运行
            // 负责：更新托盘文本 + 调用 CheckAndTrigger 触发空闲事件
            _uiTimer = new Timer();
            _uiTimer.Interval = 1000;
            _uiTimer.Tick += OnUiTick;

            Load += MainForm_Load;
            FormClosing += OnFormClosingHidden;
        }

        // ===================================================================
        //  API 地址持久化
        // ===================================================================
        private void LoadApiUrls()
        {
            try
            {
                if (File.Exists(ApiConfigFile))
                {
                    string[] lines = File.ReadAllLines(ApiConfigFile);
                    if (lines.Length >= 1 && !string.IsNullOrWhiteSpace(lines[0]))
                        _startupReportUrl = lines[0].Trim();
                    if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[1]))
                        _configApiUrl = lines[1].Trim();
                    if (lines.Length >= 3 && !string.IsNullOrWhiteSpace(lines[2]))
                        _idleReportUrl = lines[2].Trim();
                }
            }
            catch { }
        }

        private void SaveApiUrls()
        {
            try
            {
                string dir = Path.GetDirectoryName(ApiConfigFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ApiConfigFile,
                    _startupReportUrl + Environment.NewLine +
                    _configApiUrl + Environment.NewLine +
                    _idleReportUrl);
            }
            catch { }
        }

        // ===================================================================
        //  托盘图标
        // ===================================================================
        private void BuildTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("打开", null, OnOpen);
            _trayMenu.Items.Add("查看状态", null, OnShowStatus);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出", null, OnExit);

            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = SystemIcons.Information;
            _trayIcon.Text = "空闲监控 启动中...";
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += OnOpen;

            // 启动弹窗
            _trayIcon.ShowBalloonTip(3000, "空闲监控", "程序已启动", ToolTipIcon.Info);
        }

        // ===================================================================
        //  初始化
        // ===================================================================
        private async void MainForm_Load(object sender, EventArgs e)
        {
            Hide();

            _localIP = NetworkService.GetLocalIP();
            _externalIP = "获取中...";

            try
            {
                _externalIP = await System.Threading.Tasks.Task.Run(() => NetworkService.GetExternalIP());
            }
            catch { _externalIP = "获取失败"; }

            ReportStartup();

            // 启动空闲检测（独立后台线程）
            _idleService.Start();

            // 启动 UI 定时器（每秒触发 OnUiTick，在 UI 线程）
            _uiTimer.Start();

            // 启动配置轮询定时器
            _configPollTimer.Start();

            // 首次拉取配置
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { _configService.FetchConfig(); });
        }

        // ===================================================================
        //  UI 主定时器回调（每秒执行，UI 线程）
        //  - 更新托盘文本
        //  - 调用 CheckAndTrigger 触发空闲事件
        // ===================================================================
        private void OnUiTick(object sender, EventArgs e)
        {
            // 1. 更新托盘文本
            int idleSec = _idleService.CurrentIdleSeconds;
            _trayIcon.Text = "空闲监控 空闲:" + idleSec + "s / 阈值:" + _idleService.ThresholdSeconds + "s";

            // 2. 检测空闲状态并触发事件（全都在 UI 线程，不需要 Invoke）
            _idleService.CheckAndTrigger();
        }

        // ===================================================================
        //  空闲事件（UI 线程触发）
        // ===================================================================
        private void OnIdleThresholdExceeded(object sender, int idleSeconds)
        {
            // 弹窗通知（UI 线程，直接调用）
            try
            {
                _trayIcon.ShowBalloonTip(3000, "空闲监控",
                    "电脑已空闲 " + idleSeconds + " 秒（阈值: " + _idleService.ThresholdSeconds + " 秒）",
                    ToolTipIcon.Warning);
            }
            catch { }

            // HTTP 上报（开后台线程执行，不阻塞 UI）
            string json = "{\"deviceId\":\"" + EscapeJson(_deviceId)
                + "\",\"idleSeconds\":" + idleSeconds
                + ",\"thresholdSeconds\":" + _idleService.ThresholdSeconds
                + ",\"event\":\"idle_exceeded\"}";

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                        client.Headers[System.Net.HttpRequestHeader.UserAgent] = "IdleMonitor/1.0";
                        client.Encoding = Encoding.UTF8;
                        client.UploadString(_idleReportUrl, "POST", json);
                    }
                }
                catch { }
            });
        }

        private void OnIdleEnded(object sender, EventArgs e)
        {
            try
            {
                _trayIcon.ShowBalloonTip(2000, "空闲监控",
                    "检测到用户活动，已重置空闲计数器", ToolTipIcon.Info);
            }
            catch { }
        }

        // ===================================================================
        //  配置更新（可在非 UI 线程触发，通过 _uiTimer 的 CheckAndTrigger 完成）
        // ===================================================================
        private void OnConfigUpdated(object sender, EventArgs e)
        {
            _idleService.UpdateThreshold(_configService.IdleThresholdSeconds);

            // 配置更新可能在后台线程触发，UI 操作封送
            BeginInvoke(new Action(delegate
            {
                _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
                _trayIcon.ShowBalloonTip(2000, "空闲监控",
                    "配置已更新 - 空闲阈值: " + _configService.IdleThresholdSeconds + " 秒",
                    ToolTipIcon.Info);
            }));
        }

        // ===================================================================
        //  配置轮询定时器
        // ===================================================================
        private void OnConfigPollTick(object sender, EventArgs e)
        {
            _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                _configService.FetchConfig();
            });
        }

        // ===================================================================
        //  启动上报
        // ===================================================================
        private void ReportStartup()
        {
            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.Headers[System.Net.HttpRequestHeader.UserAgent] = "IdleMonitor/1.0";
                    client.Encoding = Encoding.UTF8;

                    string json = "{\"deviceId\":\"" + EscapeJson(_deviceId)
                        + "\",\"localIP\":\"" + EscapeJson(_localIP)
                        + "\",\"externalIP\":\"" + EscapeJson(_externalIP)
                        + "\",\"event\":\"startup\"}";

                    client.UploadString(_startupReportUrl, "POST", json);
                }
            }
            catch { }
        }

        // ===================================================================
        //  重新初始化配置服务
        // ===================================================================
        private void ReinitConfigService()
        {
            _configPollTimer.Stop();
            if (_configService != null)
                _configService.ConfigUpdated -= OnConfigUpdated;

            _configService = new ConfigService(_configApiUrl, _deviceId);
            _configService.ConfigUpdated += OnConfigUpdated;

            _configPollTimer.Interval = _configService.ConfigPollIntervalSeconds * 1000;
            _configPollTimer.Start();

            System.Threading.ThreadPool.QueueUserWorkItem(delegate { _configService.FetchConfig(); });
        }

        // ===================================================================
        //  菜单操作
        // ===================================================================
        private void OnOpen(object sender, EventArgs e)
        {
            using (SettingsForm settingsForm = new SettingsForm(
                _startupReportUrl, _configApiUrl, _idleReportUrl))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    bool changed = false;
                    if (_startupReportUrl != settingsForm.StartupReportUrl) { _startupReportUrl = settingsForm.StartupReportUrl; changed = true; }
                    if (_configApiUrl != settingsForm.ConfigApiUrl) { _configApiUrl = settingsForm.ConfigApiUrl; changed = true; }
                    if (_idleReportUrl != settingsForm.IdleReportUrl) { _idleReportUrl = settingsForm.IdleReportUrl; changed = true; }

                    if (changed)
                    {
                        SaveApiUrls();
                        ReinitConfigService();
                        _trayIcon.ShowBalloonTip(3000, "空闲监控",
                            "API 地址已更新，配置服务已重新连接", ToolTipIcon.Info);
                    }
                }
            }
        }

        private void OnShowStatus(object sender, EventArgs e)
        {
            int idleSec = _idleService.CurrentIdleSeconds;
            string msg = "设备ID: " + _deviceId + "\n"
                + "内网IP: " + _localIP + "\n"
                + "外网IP: " + _externalIP + "\n"
                + "当前空闲: " + idleSec + " 秒\n"
                + "空闲阈值: " + _configService.IdleThresholdSeconds + " 秒\n"
                + "配置轮询: 每 " + _configService.ConfigPollIntervalSeconds + " 秒\n\n"
                + "--- API 地址 ---\n"
                + "启动上报: " + _startupReportUrl + "\n"
                + "配置接口: " + _configApiUrl + "\n"
                + "空闲上报: " + _idleReportUrl;

            MessageBox.Show(msg, "IdleMonitor - 查看状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExit(object sender, EventArgs e)
        {
            _idleService.Stop();
            _configPollTimer.Stop();
            _uiTimer.Stop();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            Application.Exit();
        }

        private void OnFormClosingHidden(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _idleService.Dispose();
                _configPollTimer.Dispose();
                _uiTimer.Dispose();
                _trayIcon.Dispose();
                _trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
