using System;
using System.IO;
using System.Text;

namespace IdleMonitor
{
    internal static class AppLogger
    {
        private static readonly object Sync = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleMonitor");
        private static readonly string LogFile = Path.Combine(LogDirectory, "idlemonitor.log");

        public static void Info(string message) { Write("INFO", message); }
        public static void Warn(string message) { Write("WARN", message); }
        public static void Error(string message, Exception exception = null)
        {
            string detail = exception == null ? message : message + ": " + exception.GetType().Name + " - " + exception.Message;
            Write("ERROR", detail);
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
                    File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
                    FileInfo info = new FileInfo(LogFile);
                    if (info.Length > 1024 * 1024)
                    {
                        string old = LogFile + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(LogFile, old);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[IdleMonitor] log failure: " + ex.Message);
            }
        }
    }
}
