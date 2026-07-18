using System;
using System.IO;
using System.Net;
using System.Text;

namespace IdleMonitor
{
    /// <summary>
    /// 配置服务 - 从远端拉取监控规则配置
    /// </summary>
    public class ConfigService
    {
        // 默认配置
        private int _idleThresholdSeconds = 300; // 5分钟
        private int _configPollIntervalSeconds = 300; // 5分钟

        // 本地缓存路径
        private static readonly string CacheFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IdleMonitor",
            "config.json");

        private readonly string _apiUrl;
        private readonly string _deviceId;
        private readonly WebClient _client;

        /// <summary>
        /// 当前空闲阈值（秒）
        /// </summary>
        public int IdleThresholdSeconds
        {
            get { return _idleThresholdSeconds; }
        }

        /// <summary>
        /// 当前配置轮询间隔（秒）
        /// </summary>
        public int ConfigPollIntervalSeconds
        {
            get { return _configPollIntervalSeconds; }
        }

        /// <summary>
        /// 配置更新事件
        /// </summary>
        public event EventHandler ConfigUpdated;

        public ConfigService(string apiUrl, string deviceId)
        {
            _apiUrl = apiUrl;
            _deviceId = deviceId;
            _client = new WebClient();
            _client.Headers[HttpRequestHeader.UserAgent] = "IdleMonitor/1.0";
            _client.Encoding = Encoding.UTF8;

            // 加载本地缓存的配置
            LoadLocalConfig();
        }

        /// <summary>
        /// 从远端拉取最新配置
        /// </summary>
        public void FetchConfig()
        {
            try
            {
                string url = _apiUrl + "?deviceId=" + Uri.EscapeDataString(_deviceId);
                string response = _client.DownloadString(url);

                if (!string.IsNullOrWhiteSpace(response))
                {
                    ParseAndApplyConfig(response);
                    SaveLocalConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ConfigService] 拉取配置失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 异步拉取配置
        /// </summary>
        public void FetchConfigAsync()
        {
            try
            {
                string url = _apiUrl + "?deviceId=" + Uri.EscapeDataString(_deviceId);
                _client.DownloadStringCompleted += OnDownloadComplete;
                _client.DownloadStringAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ConfigService] 异步拉取配置失败: " + ex.Message);
            }
        }

        private void OnDownloadComplete(object sender, DownloadStringCompletedEventArgs e)
        {
            try
            {
                if (e.Error == null && !string.IsNullOrWhiteSpace(e.Result))
                {
                    ParseAndApplyConfig(e.Result);
                    SaveLocalConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[ConfigService] 解析配置失败: " + ex.Message);
            }
            finally
            {
                _client.DownloadStringCompleted -= OnDownloadComplete;
            }
        }

        /// <summary>
        /// 解析 JSON 配置并应用
        /// 期望格式: {"idleThreshold":300,"configPollInterval":600}
        /// </summary>
        private void ParseAndApplyConfig(string json)
        {
            bool changed = false;

            int idleTh = TryParseJsonInt(json, "idleThreshold");
            if (idleTh > 0 && idleTh != _idleThresholdSeconds)
            {
                _idleThresholdSeconds = idleTh;
                changed = true;
            }

            int pollInt = TryParseJsonInt(json, "configPollInterval");
            if (pollInt >= 60 && pollInt != _configPollIntervalSeconds)
            {
                _configPollIntervalSeconds = pollInt;
                changed = true;
            }

            if (changed)
            {
                EventHandler handler = ConfigUpdated;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// 从 JSON 中简单提取整数
        /// </summary>
        private static int TryParseJsonInt(string json, string key)
        {
            try
            {
                string search = "\"" + key + "\"";
                int idx = json.IndexOf(search, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return -1;

                idx = json.IndexOf(':', idx) + 1;
                // 跳过空格
                while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;

                // 读取数字 (可能为负数)
                bool negative = false;
                if (idx < json.Length && json[idx] == '-')
                {
                    negative = true;
                    idx++;
                }

                int val = 0;
                while (idx < json.Length && json[idx] >= '0' && json[idx] <= '9')
                {
                    val = val * 10 + (json[idx] - '0');
                    idx++;
                }

                return negative ? -val : val;
            }
            catch
            {
                return -1;
            }
        }

        private void LoadLocalConfig()
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    string json = File.ReadAllText(CacheFile);
                    ParseAndApplyConfig(json);
                }
            }
            catch
            {
                // 忽略
            }
        }

        private void SaveLocalConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(CacheFile);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = "{\"idleThreshold\":" + _idleThresholdSeconds
                    + ",\"configPollInterval\":" + _configPollIntervalSeconds + "}";
                File.WriteAllText(CacheFile, json);
            }
            catch
            {
                // 忽略
            }
        }
    }
}
