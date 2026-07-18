using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IdleMonitor
{
    /// <summary>
    /// 网络信息服务 - 获取内网IP、外网IP
    /// </summary>
    public static class NetworkService
    {
        /// <summary>
        /// 获取本机内网 IPv4 地址（首选非回环地址）
        /// </summary>
        public static string GetLocalIP()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 跳过环回和隧道接口
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    // 只取已连接的接口
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            return ip.Address.ToString();
                        }
                    }
                }
            }
            catch
            {
                // 忽略
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// 获取外网 IP（通过公共 API）
        /// </summary>
        public static string GetExternalIP()
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "IdleMonitor/1.0";
                    client.Encoding = Encoding.UTF8;
                    // 使用 ipify.org 公共 API
                    string result = client.DownloadString("https://api.ipify.org");
                    return (result != null) ? result.Trim() : "unknown";
                }
            }
            catch
            {
                try
                {
                    // 备用接口
                    using (WebClient client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        string result = client.DownloadString("https://ipv4.icanhazip.com");
                        return (result != null) ? result.Trim() : "unknown";
                    }
                }
                catch
                {
                    return "unknown";
                }
            }
        }
    }
}
