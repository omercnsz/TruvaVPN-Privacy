using System;
using System.Diagnostics;
using System.Linq;

namespace TruvaDesktop.Nitro
{
    /// <summary>
    /// Nitro modunun kesinlikle dokunmaması gereken kritik süreçleri yöneten servis.
    /// Vanguard, EasyAntiCheat gibi sistemleri koruma altına alır.
    /// </summary>
    public static class BlacklistService
    {
        // Bu süreçlerin trafiğine kesinlikle dokunulmaz.
        private static readonly string[] _protectedProcesses =
        {
            "vgc",              // Vanguard Client
            "vgk",              // Vanguard Kernel
            "valorant",         // Oyun
            "easyanticheat",    // EAC
            "beservice",        // BattlEye Service
            "battleye",         // BattlEye
            "faceit",           // Faceit Anti-Cheat
            "riotclientux"      // Riot Client
        };

        /// <summary>
        /// Verilen PID'ye sahip sürecin güvenli (Nitro kapsamına alınabilir) olup olmadığını kontrol eder.
        /// </summary>
        public static bool IsSafe(int pid)
        {
            if (pid <= 0) return false;

            try
            {
                using var process = Process.GetProcessById(pid);
                string name = process.ProcessName.ToLower();
                
                return !_protectedProcesses.Any(p => name.Contains(p));
            }
            catch
            {
                // Süreç artık mevcut değilse veya erişilemiyorsa güvenli kabul etme (risk alma)
                return false;
            }
        }

        /// <summary>
        /// Süreç isminin kara listede olup olmadığını kontrol eder.
        /// </summary>
        public static bool IsBlacklisted(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return true;
            
            string lowerName = processName.ToLower();
            return _protectedProcesses.Any(p => lowerName.Contains(p));
        }
    }
}
