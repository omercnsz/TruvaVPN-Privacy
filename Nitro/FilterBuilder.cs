using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TruvaDesktop.Nitro
{
    /// <summary>
    /// WinDivert için uygulama bazlı dinamik filtreler oluşturan yardımcı sınıf.
    /// </summary>
    public static class FilterBuilder
    {
        private const int MAX_FILTER_LENGTH = 8192; // WinDivert limit

        /// <summary>
        /// Nitro açıkken tüm sitemin DNS sorgularını (UDP 53) şifrelemek için global filtre.
        /// Çok hafif ve oyunlar için güvenlidir.
        /// </summary>
        public static string BuildGlobalDnsFilter()
        {
            // Hem IPv4 hem IPv6 DNS sorgularını yakala (IPv6 olanlar engel için droplanacak)
            return "outbound and udp.DstPort == 53 and (ip or ipv6)";
        }

        /// <summary>
        /// Sadece seçilen uygulamalar için HTTPS (SNI) parçalama filtresi.
        /// Oyun trafiğine ve anti-cheat sistemlerine dokunmaz.
        /// </summary>
        public static string BuildSelectiveSniFilter(IEnumerable<int> pids)
        {
            // Artık PID filtrelemesini C# tarafında yapıyoruz (addr.ProcessId).
            // Bu yüzden driver seviyesinde sadece portları yakalamamız yeterli.
            return "outbound and (tcp.DstPort == 443 or udp.DstPort == 443)";
        }
    }
}
