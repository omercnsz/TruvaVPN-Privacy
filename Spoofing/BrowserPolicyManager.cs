using System;
using Microsoft.Win32;

namespace TruvaDesktop.Spoofing
{
    public static class BrowserPolicyManager
    {
        private const string ChromePolicyKey = @"SOFTWARE\Policies\Google\Chrome";
        private const string EdgePolicyKey = @"SOFTWARE\Policies\Microsoft\Edge";

        public static void EnablePolicies(string proxyServer)
        {
            try
            {
                ApplyToBrowser(ChromePolicyKey, proxyServer);
                ApplyToBrowser(EdgePolicyKey, proxyServer);
                Console.WriteLine("[BILGI] Tarayıcı Kurumsal Proxy ve Sızıntı önleme politikaları başarıyla uygulandı.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HATA] Tarayıcı politikaları uygulanamadı: {ex.Message}");
            }
        }

        public static void DisablePolicies()
        {
            try
            {
                RemoveFromBrowser(ChromePolicyKey);
                RemoveFromBrowser(EdgePolicyKey);
                Console.WriteLine("[BILGI] Tarayıcı politikaları başarıyla temizlendi.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HATA] Tarayıcı politikaları kaldırılamadı: {ex.Message}");
            }
        }

        private static void ApplyToBrowser(string basePath, string proxyServer)
        {
            // CurrentUser Ayarları (Oturum açan kullanıcı için anında etki eder)
            using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(basePath))
            {
                if (key != null)
                {
                    // Yeni nesil Chrome (Chrome 117+) ProxySettings sözlüğü (JSON) kullanır, eski ProxyMode iptal olmuştur.
                    string proxyJson = $"{{\"ProxyMode\":\"fixed_servers\",\"ProxyServer\":\"{proxyServer}\"}}";
                    key.SetValue("ProxySettings", proxyJson, RegistryValueKind.String);
                    key.SetValue("WebRtcIPHandlingPolicy", "disable_non_proxied_udp", RegistryValueKind.String);
                    key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                }
            }
            // LocalMachine Ayarları (Tüm sistem çapında garanti altına alır)
            using (RegistryKey? key = Registry.LocalMachine.CreateSubKey(basePath))
            {
                if (key != null)
                {
                    // Yeni nesil Chrome (Chrome 117+) ProxySettings sözlüğü (JSON) kullanır, eski ProxyMode iptal olmuştur.
                    string proxyJson = $"{{\"ProxyMode\":\"fixed_servers\",\"ProxyServer\":\"{proxyServer}\"}}";
                    key.SetValue("ProxySettings", proxyJson, RegistryValueKind.String);
                    key.SetValue("WebRtcIPHandlingPolicy", "disable_non_proxied_udp", RegistryValueKind.String);
                    key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("wininet.dll")]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private static void RemoveFromBrowser(string basePath)
        {
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (RegistryKey? key = hive.OpenSubKey(basePath, true))
                    {
                        if (key != null)
                        {
                            // Tüm olası proxy ve DNS politikalarını temizle
                            string[] valuesToDelete = { 
                                "ProxySettings", "ProxyMode", "ProxyServer", "ProxyBypassList", 
                                "ProxyPacUrl", "WebRtcIPHandlingPolicy", "DnsOverHttpsMode",
                                "DnsOverHttpsTemplates", "BuiltInDnsClientEnabled", "AutoConfigURL"
                            };

                            foreach (var value in valuesToDelete)
                            {
                                try { key.DeleteValue(value, false); } catch { }
                            }
                        }
                    }
                }
                catch { }
            }
            // Sinyal gönder
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); // SETTINGS_CHANGED
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0); // REFRESH
        }
    }
}
