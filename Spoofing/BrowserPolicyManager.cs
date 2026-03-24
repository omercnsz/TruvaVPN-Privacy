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

        private static void RemoveFromBrowser(string basePath)
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(basePath, true))
            {
                if (key != null)
                {
                    key.DeleteValue("ProxySettings", false);
                    key.DeleteValue("ProxyMode", false);
                    key.DeleteValue("ProxyServer", false);
                    key.DeleteValue("WebRtcIPHandlingPolicy", false);
                    key.DeleteValue("DnsOverHttpsMode", false);
                }
            }
            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(basePath, true))
            {
                if (key != null)
                {
                    key.DeleteValue("ProxySettings", false);
                    key.DeleteValue("ProxyMode", false);
                    key.DeleteValue("ProxyServer", false);
                    key.DeleteValue("WebRtcIPHandlingPolicy", false);
                    key.DeleteValue("DnsOverHttpsMode", false);
                }
            }
        }
    }
}
