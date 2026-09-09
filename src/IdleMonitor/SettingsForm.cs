using System;
using System.Drawing;
using System.Windows.Forms;

namespace IdleMonitor
{
    public class SettingsForm : Form
    {
        private TextBox _txtStartupReport;
        private TextBox _txtConfigApi;
        private TextBox _txtIdleReport;
        private TextBox _txtApiToken;
        private Button _btnSave;
        private Button _btnCancel;

        public string StartupReportUrl { get; private set; }
        public string ConfigApiUrl { get; private set; }
        public string IdleReportUrl { get; private set; }
        public string ApiToken { get; private set; }

        public SettingsForm(string startupUrl, string configUrl, string idleUrl, string apiToken)
        {
            StartupReportUrl = startupUrl;
            ConfigApiUrl = configUrl;
            IdleReportUrl = idleUrl;
            ApiToken = apiToken;
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            Text = "IdleMonitor - 设置";
            Size = new Size(520, 340);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            Icon = SystemIcons.Information;

            Label title = new Label { Text = "服务端 API 配置", Location = new Point(12, 12), Size = new Size(480, 20), Font = new Font("Microsoft YaHei", 10, FontStyle.Bold) };
            _txtStartupReport = AddInput("启动上报:", 42, StartupReportUrl);
            _txtConfigApi = AddInput("配置接口:", 72, ConfigApiUrl);
            _txtIdleReport = AddInput("空闲上报:", 102, IdleReportUrl);
            _txtApiToken = AddInput("API Token:", 132, ApiToken);
            _txtApiToken.UseSystemPasswordChar = true;

            Label hint = new Label { Text = "远程服务必须使用 HTTPS。Token 仅保存在本机配置文件中。", Location = new Point(12, 166), Size = new Size(480, 36), ForeColor = Color.Gray };
            _btnSave = new Button { Text = "保存", Location = new Point(300, 210), Size = new Size(90, 30) };
            _btnSave.Click += OnSave;
            _btnCancel = new Button { Text = "取消", Location = new Point(405, 210), Size = new Size(90, 30) };
            _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(title); Controls.Add(hint); Controls.Add(_btnSave); Controls.Add(_btnCancel);
        }

        private TextBox AddInput(string label, int top, string value)
        {
            Controls.Add(new Label { Text = label, Location = new Point(12, top + 2), Size = new Size(80, 22) });
            var input = new TextBox { Text = value, Location = new Point(95, top), Size = new Size(400, 22) };
            Controls.Add(input);
            return input;
        }

        private void OnSave(object sender, EventArgs e)
        {
            string startup = _txtStartupReport.Text.Trim();
            string config = _txtConfigApi.Text.Trim();
            string idle = _txtIdleReport.Text.Trim();
            string token = _txtApiToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show("API Token 不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string normalized;
            string error;
            if (!ApiUrlValidator.TryValidate(startup, out normalized, out error)) { ShowUrlError("启动上报", error); return; }
            startup = normalized;
            if (!ApiUrlValidator.TryValidate(config, out normalized, out error)) { ShowUrlError("配置接口", error); return; }
            config = normalized;
            if (!ApiUrlValidator.TryValidate(idle, out normalized, out error)) { ShowUrlError("空闲上报", error); return; }
            StartupReportUrl = startup;
            ConfigApiUrl = config;
            IdleReportUrl = normalized;
            ApiToken = token;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowUrlError(string name, string error)
        {
            MessageBox.Show(name + "地址无效：" + error, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
