using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace IdleMonitor
{
    internal static class ReportService
    {
        public static void Post(string url, object payload, string apiToken)
        {
            var serializer = new DataContractJsonSerializer(payload.GetType());
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, payload);
                string json = Encoding.UTF8.GetString(stream.ToArray());
                using (var client = new NetworkClient(apiToken)) client.PostJson(url, json);
            }
        }

        [DataContract]
        public sealed class StartupReport
        {
            [DataMember(Name = "deviceId")] public string DeviceId { get; set; }
            [DataMember(Name = "localIP")] public string LocalIp { get; set; }
            [DataMember(Name = "event")] public string Event { get; set; }
        }

        [DataContract]
        public sealed class IdleReport
        {
            [DataMember(Name = "deviceId")] public string DeviceId { get; set; }
            [DataMember(Name = "idleSeconds")] public int IdleSeconds { get; set; }
            [DataMember(Name = "thresholdSeconds")] public int ThresholdSeconds { get; set; }
            [DataMember(Name = "event")] public string Event { get; set; }
        }
    }
}
