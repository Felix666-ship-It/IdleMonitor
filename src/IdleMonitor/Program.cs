using System;
using System.Threading;
using System.Windows.Forms;

namespace IdleMonitor
{
    static class Program
    {
        private static Mutex _mutex;

        /// <summary>
        /// 应用程序主入口点
        /// </summary>
        [STAThread]
        static void Main()
        {
            // 单实例保护 —— 防止多次双击创建多个进程
            bool createdNew;
            _mutex = new Mutex(true, "IdleMonitor_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("IdleMonitor 已在运行中，请查看系统托盘。",
                    "IdleMonitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // 设置开机自启（写入注册表 HKCU\...\Run）
                SetAutoStart();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (MainForm form = new MainForm())
                {
                    Application.Run(form);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("程序异常: " + ex.Message + "\n\n" + ex.StackTrace,
                    "IdleMonitor 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // 释放 Mutex
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
        }

        /// <summary>
        /// 设置开机自启 - 写入 HKCU\Software\Microsoft\Windows\CurrentVersion\Run
        /// </summary>
        private static void SetAutoStart()
        {
            try
            {
                string appPath = Application.ExecutablePath;
                if (string.IsNullOrEmpty(appPath))
                {
                    appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                }

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        string existing = key.GetValue("IdleMonitor") as string;
                        if (!string.Equals(existing, appPath, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue("IdleMonitor", appPath);
                        }
                    }
                }
            }
            catch
            {
                // 注册表写入失败不影响程序运行
            }
        }
    }
}
