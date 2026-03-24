using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace TruvaDesktop.Spoofing
{
    public static class RegistryManager
    {
        // Orijinal ayarları hafızada tutacağımız değişkenler
        private static string originalTimeZone = "";
        private static string originalGeoId = "";
        private static string originalLocaleName = "";

        // 1. ADIM: Bilgisayarın mevcut (orijinal) ayarlarını yedeğe al
        public static void BackupOriginalSettings()
        {
            try
            {
                // Saat Dilimi Yedeği (Windows'un gizli tzutil komutu ile çekiyoruz)
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "tzutil.exe",
                    Arguments = "/g",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (Process? process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        originalTimeZone = process.StandardOutput.ReadToEnd().Trim();
                    }
                }

                // Bölge (GeoID) Yedeği - Registry'den
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\Geo"))
                {
                    if (key != null)
                        originalGeoId = key.GetValue("Nation")?.ToString() ?? "235"; // 235 = Türkiye
                }

                // Dil ve Format Yedeği - Registry'den
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International"))
                {
                    if (key != null)
                        originalLocaleName = key.GetValue("LocaleName")?.ToString() ?? "tr-TR";
                }

                Console.WriteLine($"[YEDEK ALINDI] Saat: {originalTimeZone}, Bölge: {originalGeoId}, Dil: {originalLocaleName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Yedekleme hatası: " + ex.Message);
            }
        }

        // 2. ADIM: Hedef ülkeye göre bilgisayarı manipüle et (Spoof)
        public static void SpoofSettings(string targetTimeZone, string targetGeoId, string targetLocale)
        {
            try
            {
                // Saat Dilimini Değiştir
                ExecuteCommand("tzutil.exe", $"/s \"{targetTimeZone}\"");

                // Bölgeyi Değiştir (GeoID)
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\Geo", true))
                {
                    if (key != null) key.SetValue("Nation", targetGeoId);
                }

                // Dili ve Formatı Değiştir
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International", true))
                {
                    if (key != null) key.SetValue("LocaleName", targetLocale);
                }

                Console.WriteLine($"[SPOOF AKTİF] Bilgisayar artık bu ayarlarda -> Saat: {targetTimeZone}, Bölge: {targetGeoId}, Dil: {targetLocale}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Spoofing hatası: " + ex.Message);
            }
        }

        // 3. ADIM: VPN kapanırken orijinal ayarlara geri dön (Restore)
        public static void RestoreOriginalSettings()
        {
            try
            {
                // Eğer daha önce yedek alınmamışsa (program ilk kez açılıyorsa) işlemi iptal et
                if (string.IsNullOrEmpty(originalTimeZone)) return;

                ExecuteCommand("tzutil.exe", $"/s \"{originalTimeZone}\"");

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International\Geo", true))
                {
                    if (key != null) key.SetValue("Nation", originalGeoId);
                }

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\International", true))
                {
                    if (key != null) key.SetValue("LocaleName", originalLocaleName);
                }

                Console.WriteLine("[GÜVENLİK] Bilgisayar orijinal ayarlarına (Türkiye) döndürüldü.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Geri yükleme hatası: " + ex.Message);
            }
        }

        // 4. ADIM: Güvenlik Kalkanı (Program aniden çökerse veya Görev Yöneticisinden kapatılırsa tetiklenir)
        public static void InitializeFailsafe()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                RestoreOriginalSettings();
            };
        }

        // Arka planda siyah CMD ekranı çıkartmadan komut çalıştırma yardımcısı
        private static void ExecuteCommand(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit();
        }
    }
}
