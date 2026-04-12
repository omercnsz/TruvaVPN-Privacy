using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace TruvaDesktop.Spoofing
{
    public static class ProxyManager
    {
        [DllImport("wininet.dll")]
        public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public static void EnableSystemProxy(string proxyAddress)
        {
            try
            {
                using (RegistryKey? registry = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (registry != null)
                    {
                        registry.SetValue("ProxyEnable", 1);
                        registry.SetValue("ProxyServer", proxyAddress);
                        registry.SetValue("ProxyOverride", "<local>;localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*");
                    }
                }
                // Anında etki etmesi için Windows'u uyar
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
                Console.WriteLine($"[PROXY] Sistem Proxy Aktif: {proxyAddress}");
            }
            catch (Exception ex) { Console.WriteLine("Proxy aktifleştirilemedi: " + ex.Message); }
        }

        public static void DisableSystemProxy()
        {
            try
            {
                using (RegistryKey? registry = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                {
                    if (registry != null)
                    {
                        registry.SetValue("ProxyEnable", 0);
                        registry.DeleteValue("ProxyServer", false);
                        registry.DeleteValue("ProxyOverride", false);
                        registry.DeleteValue("AutoConfigURL", false);
                    }
                }
                
                // Kalıntı ikili (binary) bağlantı ayarlarını temizle
                try
                {
                    using (RegistryKey? connKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections", true))
                    {
                        if (connKey != null)
                        {
                            foreach (string val in connKey.GetValueNames())
                                connKey.DeleteValue(val, false);
                        }
                    }
                } catch { }

                // Anında etki etmesi için Windows'u uyar
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
                Console.WriteLine("[PROXY] Sistem Proxy ve Bağlantı Kalıntıları Kapatıldı.");
            }
            catch (Exception ex) { Console.WriteLine("Proxy kapatılamadı: " + ex.Message); }
        }
    }
}
