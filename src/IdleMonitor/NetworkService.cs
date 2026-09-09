using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IdleMonitor
{
    public static class NetworkService
    {
        public static string GetLocalIP()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                        ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork && !ip.Address.Equals(System.Net.IPAddress.Loopback))
                            return ip.Address.ToString();
                    }
                }
            }
            catch (Exception ex) { AppLogger.Error("本机 IP 获取失败", ex); }
            return "unknown";
        }
    }
}
