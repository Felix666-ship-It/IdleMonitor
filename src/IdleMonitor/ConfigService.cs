using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace IdleMonitor
{
    public class ConfigService : IDisposable
    {
        private const int DefaultIdleThresholdSeconds = 300;
        private const int DefaultPollIntervalSeconds = 300;
        private const int MinPollIntervalSeconds = 60;
        private static readonly string CacheFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleMonitor", "config.json");

        private readonly string _apiUrl;
        private readonly string _deviceId;
        private readonly NetworkClient _networkClient;
        private readonly object _sync = new object();
        private int _idleThresholdSeconds = DefaultIdleThresholdSeconds;
        private int _configPollIntervalSeconds = DefaultPollIntervalSeconds;
        private bool _disposed;

        public int IdleThresholdSeconds { get { lock (_sync) return _idleThresholdSeconds; } }
        public int ConfigPollIntervalSeconds { get { lock (_sync) return _configPollIntervalSeconds; } }
        public event EventHandler ConfigUpdated;

        public ConfigService(string apiUrl, string deviceId, string apiToken)
        {
            string normalized;
            string error;
            if (!ApiUrlValidator.TryValidate(apiUrl, out normalized, out error))
                throw new ArgumentException(error, "apiUrl");
            _apiUrl = normalized;
            _deviceId = deviceId ?? string.Empty;
            _networkClient = new NetworkClient(apiToken);
            LoadLocalConfig();
        }

        public void FetchConfig()
        {
            if (_disposed) return;
            try
            {
                string separator = _apiUrl.IndexOf('?') >= 0 ? "&" : "?";
                string response = _networkClient.Get(_apiUrl + separator + "deviceId=" + Uri.EscapeDataString(_deviceId));
                if (!string.IsNullOrWhiteSpace(response) && ParseAndApplyConfig(response)) SaveLocalConfig();
            }
            catch (Exception ex) { AppLogger.Warn("配置拉取失败"); AppLogger.Error("配置请求异常", ex); }
        }

        private bool ParseAndApplyConfig(string json)
        {
            RemoteConfig config;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(RemoteConfig));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    config = (RemoteConfig)serializer.ReadObject(stream);
            }
            catch (Exception ex) { AppLogger.Warn("配置响应不是有效 JSON"); AppLogger.Error("配置解析异常", ex); return false; }

            bool changed = false;
            lock (_sync)
            {
                if (config != null && config.IdleThreshold > 0 && config.IdleThreshold <= 86400 && config.IdleThreshold != _idleThresholdSeconds)
                { _idleThresholdSeconds = config.IdleThreshold; changed = true; }
                if (config != null && config.ConfigPollInterval >= MinPollIntervalSeconds && config.ConfigPollInterval <= 86400 && config.ConfigPollInterval != _configPollIntervalSeconds)
                { _configPollIntervalSeconds = config.ConfigPollInterval; changed = true; }
            }
            if (changed)
            {
                EventHandler handler = ConfigUpdated;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            return true;
        }

        private void LoadLocalConfig()
        {
            try
            {
                if (File.Exists(CacheFile)) ParseAndApplyConfig(File.ReadAllText(CacheFile));
            }
            catch (Exception ex) { AppLogger.Warn("本地配置读取失败，使用安全默认值"); AppLogger.Error("本地配置异常", ex); }
        }

        private void SaveLocalConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(CacheFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var config = new RemoteConfig { IdleThreshold = IdleThresholdSeconds, ConfigPollInterval = ConfigPollIntervalSeconds };
                var serializer = new DataContractJsonSerializer(typeof(RemoteConfig));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, config);
                    File.WriteAllText(CacheFile, Encoding.UTF8.GetString(stream.ToArray()), Encoding.UTF8);
                }
            }
            catch (Exception ex) { AppLogger.Error("本地配置保存失败", ex); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _networkClient.Dispose();
        }

        [DataContract]
        private sealed class RemoteConfig
        {
            [DataMember(Name = "idleThreshold")] public int IdleThreshold { get; set; }
            [DataMember(Name = "configPollInterval")] public int ConfigPollInterval { get; set; }
        }
    }
}
