using System;
using System.IO;

namespace IdleMonitor
{
    /// <summary>
    /// 设备唯一标识管理 - 首次运行生成 UUID 并持久化到本地配置文件
    /// </summary>
    public static class DeviceIdService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleMonitor");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "device.json");

        /// <summary>
        /// 获取或生成本地设备唯一标识
        /// </summary>
        public static string GetDeviceId()
        {
            // 尝试从文件读取已有 deviceId
            if (File.Exists(ConfigFile))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFile);
                    // 简单解析 {"deviceId":"xxx"} 格式
                    int start = json.IndexOf("\"deviceId\"", StringComparison.OrdinalIgnoreCase);
                    if (start >= 0)
                    {
                        start = json.IndexOf(':', start) + 1;
                        start = json.IndexOf('"', start) + 1;
                        int end = json.IndexOf('"', start);
                        if (end > start)
                        {
                            return json.Substring(start, end - start);
                        }
                    }
                }
                catch
                {
                    // 读取失败则重新生成
                }
            }

            // 生成新的 UUID
            string newId = Guid.NewGuid().ToString("D").ToLowerInvariant();
            SaveDeviceId(newId);
            return newId;
        }

        private static void SaveDeviceId(string deviceId)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }
                string json = "{\"deviceId\":\"" + deviceId + "\"}";
                File.WriteAllText(ConfigFile, json);
            }
            catch
            {
                // 写入失败不影响主流程
            }
        }
    }
}
