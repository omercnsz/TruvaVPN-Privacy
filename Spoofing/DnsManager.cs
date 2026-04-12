using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace TruvaDesktop.Spoofing
{
    public static class DnsManager
    {
        /// <summary>
        /// Tüm aktif ağ adaptörlerini Otomatik DNS (DHCP) moduna çeker.
        /// VPN kapandığında kalıntı DNS'leri temizlemek için kullanılır.
        /// </summary>
        public static void ResetToDhcp()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                                  (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                   nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                foreach (var nic in interfaces)
                {
                    string interfaceName = nic.Name;
                    Console.WriteLine($"[DNS] Sıfırlanıyor: {interfaceName}");
                    
                    // IPv4 DNS'i otomatiğe çek
                    ExecuteNetsh($"interface ipv4 set dns name=\"{interfaceName}\" source=dhcp");
                    // IPv6 DNS'i otomatiğe çek (opsiyonel ama güvenli)
                    ExecuteNetsh($"interface ipv6 set dns name=\"{interfaceName}\" source=dhcp");
                }
                
                // DNS Önbelleğini temizle
                ExecuteCommand("ipconfig", "/flushdns");
                Console.WriteLine("[DNS] Tüm adaptörler varsayılan ayarlara döndürüldü.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HATA] DNS sıfırlama başarısız: {ex.Message}");
            }
        }

        /// <summary>
        /// Belirli bir DNS adresini (örn: 8.8.8.8) tüm aktif adaptörlere statik olarak atar.
        /// </summary>
        public static void SetStaticDns(string primaryDns, string secondaryDns = "8.8.4.4")
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                                  (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                   nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                foreach (var nic in interfaces)
                {
                    string interfaceName = nic.Name;
                    Console.WriteLine($"[DNS] Ayarlanıyor ({primaryDns}): {interfaceName}");

                    ExecuteNetsh($"interface ipv4 set dns name=\"{interfaceName}\" source=static addr={primaryDns} register=primary");
                    if (!string.IsNullOrEmpty(secondaryDns))
                    {
                        ExecuteNetsh($"interface ipv4 add dns name=\"{interfaceName}\" addr={secondaryDns} index=2");
                    }
                }
                ExecuteCommand("ipconfig", "/flushdns");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HATA] Statik DNS atama başarısız: {ex.Message}");
            }
        }

        private static void ExecuteNetsh(string args)
        {
            ExecuteCommand("netsh", args);
        }

        private static void ExecuteCommand(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit(3000); // 3 saniye zaman aşımı
            }
            catch { }
        }
    }
}
