using System;
using System.Drawing;
using System.Windows.Forms;

namespace IdleMonitor
{
    /// <summary>
    /// 设置窗口 - 用于编辑 API 接口地址
    /// </summary>
    public class SettingsForm : Form
    {
        private TextBox _txtStartupReport;
        private TextBox _txtConfigApi;
        private TextBox _txtIdleReport;
        private Button _btnSave;
        private Button _btnCancel;

        public string StartupReportUrl { get; private set; }
        public string ConfigApiUrl { get; private set; }
        public string IdleReportUrl { get; private set; }

        public SettingsForm(string startupUrl, string configUrl, string idleUrl)
        {
            StartupReportUrl = startupUrl;
            ConfigApiUrl = configUrl;
            IdleReportUrl = idleUrl;

            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "IdleMonitor - 设置";
            Size = new Size(520, 260);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            Icon = SystemIcons.Information;

            Label lblTitle = new Label
            {
                Text = "API 接口地址配置",
                Location = new Point(12, 12),
                Size = new Size(480, 20),
                Font = new Font("Microsoft YaHei", 10, FontStyle.Bold)
            };

            // 启动上报
            Label lblStartup = new Label { Text = "启动上报:", Location = new Point(12, 42), Size = new Size(80, 22) };
            _txtStartupReport = new TextBox
            {
                Text = StartupReportUrl,
                Location = new Point(95, 40),
                Size = new Size(400, 22)
            };

            // 配置接口
            Label lblConfig = new Label { Text = "配置接口:", Location = new Point(12, 72), Size = new Size(80, 22) };
            _txtConfigApi = new TextBox
            {
                Text = ConfigApiUrl,
                Location = new Point(95, 70),
                Size = new Size(400, 22)
            };

            // 空闲上报
            Label lblIdle = new Label { Text = "空闲上报:", Location = new Point(12, 102), Size = new Size(80, 22) };
            _txtIdleReport = new TextBox
            {
                Text = IdleReportUrl,
                Location = new Point(95, 100),
                Size = new Size(400, 22)
            };

            // 提示
            Label lblHint = new Label
            {
                Text = "修改后点击保存，程序将自动使用新地址",
                Location = new Point(12, 132),
                Size = new Size(480, 18),
                ForeColor = Color.Gray
            };

            // 按钮
            _btnSave = new Button { Text = "保存", Location = new Point(300, 160), Size = new Size(90, 30) };
            _btnSave.Click += OnSave;

            _btnCancel = new Button { Text = "取消", Location = new Point(405, 160), Size = new Size(90, 30) };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(lblTitle);
            Controls.Add(lblStartup);
            Controls.Add(_txtStartupReport);
            Controls.Add(lblConfig);
            Controls.Add(_txtConfigApi);
            Controls.Add(lblIdle);
            Controls.Add(_txtIdleReport);
            Controls.Add(lblHint);
            Controls.Add(_btnSave);
            Controls.Add(_btnCancel);
        }

        private void OnSave(object sender, EventArgs e)
        {
            string startup = _txtStartupReport.Text.Trim();
            string config = _txtConfigApi.Text.Trim();
            string idle = _txtIdleReport.Text.Trim();

            if (string.IsNullOrEmpty(startup) || string.IsNullOrEmpty(config) || string.IsNullOrEmpty(idle))
            {
                MessageBox.Show("API 地址不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartupReportUrl = startup;
            ConfigApiUrl = config;
            IdleReportUrl = idle;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
