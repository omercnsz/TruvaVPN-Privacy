using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using Newtonsoft.Json;
using TruvaDesktop.Models;

namespace TruvaDesktop.Nitro
{
    /// <summary>
    /// Selective Nitro Servisi.
    /// Belirli uygulamaların (Discord vb.) trafiğini hedefleyerek hızı korur ve sansürü aşar.
    /// </summary>
    public class NitroService : IDisposable
    {
        private readonly DoHResolver _dohResolver;
        private readonly DnsInterceptor _dnsInterceptor;
        private readonly SniFragmenter _sniFragmenter;
        private ProcessMonitor? _monitor;
        private readonly string _appsFilePath;

        public ObservableCollection<NitroApp> Apps { get; } = new();
        public bool IsRunning => _dnsInterceptor.IsRunning || _sniFragmenter.IsRunning;
        public event Action<string>? StatusChanged;

        public NitroService()
        {
            _dohResolver = new DoHResolver();
            _dnsInterceptor = new DnsInterceptor(_dohResolver);
            _sniFragmenter = new SniFragmenter();
            _appsFilePath = Path.Combine(AppContext.BaseDirectory, "nitro_apps.json");

            LoadApps();
            Apps.CollectionChanged += Apps_CollectionChanged;
        }

        private void Apps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            SaveApps();
            if (IsRunning)
            {
                // Eğer çalışma anında liste değişirse monitor'ü ve filtreleri yenile
                RestartMonitor();
            }
        }

        private void LoadApps()
        {
            try
            {
                if (File.Exists(_appsFilePath))
                {
                    string json = File.ReadAllText(_appsFilePath);
                    var list = JsonConvert.DeserializeObject<List<NitroApp>>(json);
                    if (list != null)
                    {
                        foreach (var app in list) Apps.Add(app);
                    }
                }
                
                // Varsayılan Uygulama: Discord ve Roblox (Eğer listede yoksa ekle)
                // Varsayılan Uygulamalar
                AddDefaultApp("Discord", "Discord");
                AddDefaultApp("Discord Update", "Update"); // Kritik: Discord güncelleyici
                AddDefaultApp("Discord PTB", "DiscordPTB");
                AddDefaultApp("Discord Canary", "DiscordCanary");
                AddDefaultApp("Roblox", "RobloxPlayerBeta");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nitro] Yükleme hatası: {ex.Message}");
            }
        }

        private void AddDefaultApp(string name, string exe)
        {
            string cleanExe = exe.Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
            if (!Apps.Any(a => a.ExeName.Equals(cleanExe, StringComparison.OrdinalIgnoreCase)))
            {
                Apps.Add(new NitroApp { Name = name, ExeName = cleanExe, IsEnabled = true });
            }
        }

        private void SaveApps()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Apps.ToList(), Formatting.Indented);
                File.WriteAllText(_appsFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nitro] Kaydetme hatası: {ex.Message}");
            }
        }

        public void Start()
        {
            if (IsRunning) return;

            try
            {
                if (!IsRunningAsAdmin())
                    throw new UnauthorizedAccessException("Nitro modu yönetici yetkisi gerektirir.");

                // 0. Sürücü dosyası kontrolü
                string sysPath = Path.Combine(AppContext.BaseDirectory, "WinDivert64.sys");
                string dllPath = Path.Combine(AppContext.BaseDirectory, "WinDivert.dll");
                
                if (!File.Exists(sysPath) || !File.Exists(dllPath))
                {
                    throw new FileNotFoundException("Kritik sürücü dosyaları (WinDivert64.sys veya WinDivert.dll) bulunamadı. Lütfen kurulumun tam olduğundan emin olun.");
                }

                // Sürücü Kaydı Failsafe (Özellikle Windows 10 için kritik)
                EnsureWinDivertRegistered(sysPath);

                // 1. Motorları Ayarla
                _dnsInterceptor.Start();
                
                StartMonitor();
                
                var currentPids = _monitor?.GetActivePids() ?? new List<int>();
                _sniFragmenter.Start(currentPids);

                StatusChanged?.Invoke("Nitro Aktif - Hibrit Geçit Devrede");
            }
            catch (Exception ex)
            {
                Stop();
                string userFriendlyMsg = MapWinDivertError(ex.Message);
                StatusChanged?.Invoke(userFriendlyMsg);
            }
        }

        private string MapWinDivertError(string originalMsg)
        {
            if (originalMsg.Contains("Hata Kodu: 5"))
                return "Hata: Erişim Engellendi. Lütfen uygulamayı 'Yönetici Olarak Çalıştırın'.";
            
            if (originalMsg.Contains("Hata Kodu: 1275"))
                return "Hata: Sürücü Engellendi. Windows 'Çekirdek Yalıtımı' (Core Isolation) ayarını veya 'Secure Boot' engelini kapatmanız gerekebilir.";
            
            if (originalMsg.Contains("Hata Kodu: 1722"))
                return "Hata: RPC Hizmeti kullanılamıyor. Windows sürücü servisi başlatılamadı.";

            if (originalMsg.Contains("Hata Kodu: 2"))
                return "Hata: Sürücü dosyası (WinDivert64.sys) bulunamadı.";

            return originalMsg; // Bilinmeyen hata
        }

        private bool IsRunningAsAdmin()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public void Stop()
        {
            _monitor?.Stop();
            _dnsInterceptor.Stop();
            _sniFragmenter.Stop();
            _dohResolver.ClearCache();
            StatusChanged?.Invoke("Nitro Geçit Hattı durduruldu.");
        }

        private void StartMonitor()
        {
            var targetExes = Apps.Where(a => a.IsEnabled).Select(a => a.ExeName).ToList();
            if (targetExes.Count == 0) return;

            _monitor = new ProcessMonitor(targetExes);
            _monitor.OnPidListChanged += (pids) => 
            {
                if (IsRunning) UpdateFilters(pids);
            };
            _monitor.Start();
        }

        private void RestartMonitor()
        {
            _monitor?.Stop();
            StartMonitor();
            UpdateFilters(_monitor?.GetActivePids() ?? new List<int>());
        }

        private void UpdateFilters(List<int> pids)
        {
            if (!IsRunning) return;

            // DNS global olduğu için güncellenmeye ihtiyacı yok.
            // Sadece SniFragmenter'ı seçili PID'lerle güncelliyoruz.
            _sniFragmenter.UpdateFilters(pids);
        }

        public void Dispose()
        {
            Stop();
            _monitor?.Dispose();
            _dohResolver.Dispose();
        }

        private void EnsureWinDivertRegistered(string driverPath)
        {
            try
            {
                // Sürücünün kayıtlı olup olmadığını kontrol et (Hata kodu 1060 = servis yok)
                var checkPsi = new ProcessStartInfo
                {
                    FileName = "sc",
                    Arguments = "query WinDivert",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(checkPsi))
                {
                    string output = process?.StandardOutput.ReadToEnd() ?? "";
                    process?.WaitForExit();

                    if (output.Contains("1060")) // Hizmet bulunamadı
                    {
                        Debug.WriteLine("[Nitro] WinDivert servisi bulunamadı, oluşturuluyor...");
                        
                        // Sürücüyü oluştur (Admin yetkisi zaten var)
                        var createPsi = new ProcessStartInfo
                        {
                            FileName = "sc",
                            Arguments = $"create WinDivert type= kernel start= demand binPath= \"{driverPath}\" DisplayName= \"WinDivert 2.2\"",
                            UseShellExecute = true,
                            Verb = "runas",
                            CreateNoWindow = true
                        };
                        Process.Start(createPsi)?.WaitForExit();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nitro] Sürücü kayıt hatası: {ex.Message}");
            }
        }
    }
}
