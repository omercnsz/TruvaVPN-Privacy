using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TruvaDesktop.Core
{
    public class UpdateInfo
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "";

        [JsonProperty("minVersion")]
        public string MinVersion { get; set; } = "";

        [JsonProperty("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        [JsonProperty("releaseNotes")]
        public string ReleaseNotes { get; set; } = "";

        [JsonProperty("forceUpdate")]
        public bool ForceUpdate { get; set; } = true;
    }

    public class UpdateManager
    {
        // Maskelenmiş GitHub URL (Base64)
        private static readonly string _m = "aHR0cHM6Ly9yYXcuZ2l0aHVidXNlcmNvbnRlbnQuY29tL29tZXJjbnN6L3RydXZhZGVza3RvcC11cGRhdGUvbWFpbi92ZXJzaW9uLmpzb24=";
        private static string VersionJsonUrl => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_m));

        private static readonly HttpClient _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public event Action<string>? StatusChanged;
        public event Action<int>? ProgressChanged;

        static UpdateManager()
        {
            _client.DefaultRequestHeaders.Add("User-Agent", "TruvaVPN-Desktop-Updater");
            // GitHub CDN cache'ini bypass etmek için
            _client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        }

        /// <summary>
        /// Mevcut uygulamanın sürümünü döndürür.
        /// </summary>
        public static Version GetCurrentVersion()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                return ver ?? new Version(1, 0, 4, 0);
            }
            catch
            {
                return new Version(1, 0, 4, 0);
            }
        }

        /// <summary>
        /// GitHub'dan en son sürüm bilgisini çeker.
        /// </summary>
        public async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                StatusChanged?.Invoke("Güncelleme kontrol ediliyor...");

                // Cache bypass için timestamp ekle
                string url = $"{VersionJsonUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var response = await _client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[UPDATE] HTTP Hatası: {response.StatusCode}");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                var updateInfo = JsonConvert.DeserializeObject<UpdateInfo>(json);

                if (updateInfo == null || string.IsNullOrEmpty(updateInfo.Version))
                {
                    Debug.WriteLine("[UPDATE] version.json parse edilemedi.");
                    return null;
                }

                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[UPDATE] Ağ Hatası: {ex.Message}");
                StatusChanged?.Invoke("İnternet bağlantısı bulunamadı, güncelleme atlandı.");
                return null;
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("[UPDATE] Zaman aşımı.");
                StatusChanged?.Invoke("Güncelleme kontrolü zaman aşımına uğradı.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE] Beklenmeyen Hata: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Güncelleme gerekip gerekmediğini kontrol eder.
        /// </summary>
        public bool IsUpdateRequired(UpdateInfo updateInfo)
        {
            try
            {
                var current = GetCurrentVersion();
                var remote = new Version(updateInfo.Version);

                Debug.WriteLine($"[UPDATE] Mevcut: {current} | Uzak: {remote}");

                return remote > current;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE] Sürüm karşılaştırma hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// MSIX dosyasını indirir ve yükleyiciyi başlatır.
        /// </summary>
        public async Task<bool> DownloadAndInstallAsync(UpdateInfo updateInfo)
        {
            try
            {
                if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
                {
                    StatusChanged?.Invoke("İndirme URL'si bulunamadı!");
                    return false;
                }

                StatusChanged?.Invoke("Güncelleme indiriliyor...");
                ProgressChanged?.Invoke(0);

                // Temp klasörüne indir
                string tempDir = Path.Combine(Path.GetTempPath(), "TruvaVPN_Update");
                Directory.CreateDirectory(tempDir);

                string fileName = Path.GetFileName(new Uri(updateInfo.DownloadUrl).LocalPath);
                string filePath = Path.Combine(tempDir, fileName);

                // Eski dosya varsa sil
                if (File.Exists(filePath))
                    File.Delete(filePath);

                // İndirme işlemi (progress destekli)
                {
                    using var response = await _client.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        StatusChanged?.Invoke($"İndirme hatası: HTTP {(int)response.StatusCode}");
                        return false;
                    }

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    long receivedBytes = 0;

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                        receivedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            int percent = (int)((receivedBytes * 100) / totalBytes);
                            ProgressChanged?.Invoke(percent);

                            double downloadedMb = receivedBytes / (1024.0 * 1024.0);
                            double totalMb = totalBytes / (1024.0 * 1024.0);
                            StatusChanged?.Invoke($"İndiriliyor... {downloadedMb:F1} / {totalMb:F1} MB (%{percent})");
                        }
                    }
                } // Stream'ler burada kapanır, dosya üstündeki kilit kalkar.

                ProgressChanged?.Invoke(100);
                StatusChanged?.Invoke("İndirme tamamlandı, yükleniyor...");

                // MSIX veya EXE dosyasını yükle
                await Task.Delay(1000); 

                if (filePath.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
                {
                    return await InstallMsixAsync(filePath);
                }
                else if (filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return await LaunchExeInstallerAsync(filePath);
                }

                StatusChanged?.Invoke("Bilinmeyen dosya formatı!");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE] İndirme hatası: {ex.Message}");
                StatusChanged?.Invoke($"Güncelleme hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> InstallMsixAsync(string msixPath)
        {
            try
            {
                // PowerShell ile MSIX yükle
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -Command \"Add-AppPackage -Path '{msixPath}' -ForceApplicationShutdown\"",
                    UseShellExecute = true,
                    Verb = "runas" // Admin olarak çalıştır
                };

                Process.Start(psi);
                
                // Uygulamayı kapat (MSIX yükleyici halledecek)
                StatusChanged?.Invoke("Yükleme başlatıldı, uygulama kapanıyor...");
                await Task.Delay(2000); 
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE] MSIX yükleme hatası: {ex.Message}");
                StatusChanged?.Invoke($"Yükleme başlatılamadı: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> LaunchExeInstallerAsync(string downloadedExePath)
        {
            try
            {
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(currentExePath)) return false;

                // Eğer indirilen dosya adı "setup" veya "install" içeriyorsa, doğrudan çalıştır (tavsiye edilen yöntem budur)
                string fileName = Path.GetFileName(downloadedExePath).ToLower();
                if (fileName.Contains("setup") || fileName.Contains("install") || fileName.Contains("truvavpn_setup"))
                {
                    // Dosya kilidinin tam serbest bırakılması için kısa bir bekleme
                    await Task.Delay(1500);

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = downloadedExePath,
                        UseShellExecute = true,
                        Verb = "runas", // Admin yetkisi iste (Fix Error 740)
                        WorkingDirectory = Path.GetDirectoryName(downloadedExePath) // Kendi klasöründe çalıştır (Directory lock engelle)
                    };

                    Process.Start(startInfo);
                    StatusChanged?.Invoke("Yükleyici başlatıldı, uygulama kapanıyor...");
                    await Task.Delay(2000);
                    return true;
                }

                // Eğer doğrudan EXE indirildiyse (Single-File Publish), mevcut EXE'nin üzerine yazmamız lazım.
                // Çalışan EXE'nin üzerine yazılamayacağı için geçici bir .bat dosyası kullanıyoruz.
                string batchPath = Path.Combine(Path.GetTempPath(), "truva_updater.bat");
                
                string batchContent = $@"
@echo off
taskkill /f /im TruvaDesktop.exe >nul 2>&1
timeout /t 2 /nobreak >nul
copy /y ""{downloadedExePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""%~f0""
";
                File.WriteAllText(batchPath, batchContent);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                StatusChanged?.Invoke("Güncelleme betiği başlatıldı, uygulama kapanıyor...");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE] EXE başlatma hatası: {ex.Message}");
                StatusChanged?.Invoke($"Yükleyici başlatılamadı: {ex.Message}");
                return false;
            }
        }
    }
}
