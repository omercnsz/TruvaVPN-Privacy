using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TruvaDesktop.Core
{
    public class VpnService
    {
        private Process? vpnProcess;
        public Nitro.NitroService NitroService { get; }
        public event Action<string>? StatusChanged;

        public VpnService()
        {
            NitroService = new Nitro.NitroService();
            NitroService.StatusChanged += (status) => StatusChanged?.Invoke(status);
        }

        public void Connect(Server server, bool isGameMode, string splitApps)
        {
            try
            {
                Disconnect();
                GenerateConfig(server, isGameMode, splitApps);

                try { File.WriteAllText("singbox_debug.log", $"=== TRUVA SING-BOX LOG ===\n[{DateTime.Now}] Başlatılıyor...\n"); } catch { }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "sing-box.exe",
                    Arguments = "run -c config.json",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                vpnProcess = Process.Start(psi);
                if (vpnProcess != null)
                {
                    vpnProcess.EnableRaisingEvents = true;
                    vpnProcess.Exited += (s, e) => {
                        StatusChanged?.Invoke($"[KOPARILDI] Sing-Box beklenmedik şekilde kapandı (Kod: {vpnProcess.ExitCode})");
                    };

                    vpnProcess.BeginOutputReadLine();
                    vpnProcess.BeginErrorReadLine();
                    
                    vpnProcess.OutputDataReceived += (s, e) => { 
                        if (!string.IsNullOrEmpty(e.Data)) 
                        {
                            // try { File.AppendAllText("singbox_debug.log", $"[{DateTime.Now:HH:mm:ss}] {e.Data}\n"); } catch { }
                            if (e.Data.IndexOf("started", StringComparison.OrdinalIgnoreCase) >= 0 || e.Data.IndexOf("listening at", StringComparison.OrdinalIgnoreCase) >= 0)
                                StatusChanged?.Invoke($"[BILGI] VPN Tüneli Güvenle Kuruldu.");
                        }
                    };
                    
                    vpnProcess.ErrorDataReceived += (s, e) => { 
                        if (!string.IsNullOrEmpty(e.Data)) 
                        { 
                            Console.WriteLine($"[SINGBOX ERR]: {e.Data}"); 
                            // try { File.AppendAllText("singbox_debug.log", $"[{DateTime.Now:HH:mm:ss}] ERROR: {e.Data}\n"); } catch { }
                            
                            if (e.Data.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0 || e.Data.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                                StatusChanged?.Invoke($"[HATA] {e.Data}"); 
                            else if (e.Data.IndexOf("started", StringComparison.OrdinalIgnoreCase) >= 0)
                                StatusChanged?.Invoke($"[BILGI] VPN Tüneli Güvenle Kuruldu.");
                        } 
                    };
                }

                StatusChanged?.Invoke($"Sing-Box motoru başlatıldı: {server.CountryName}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Bağlantı Hatası: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                // Nitro servisini durdur (WinDivert tabanlı)
                if (NitroService.IsRunning)
                {
                    NitroService.Stop();
                }

                // Her ihtimale karşı tüm ağ ayarlarını eski haline döndür (Sıralama Önemli)
                Spoofing.ProxyManager.DisableSystemProxy();
                Spoofing.BrowserPolicyManager.DisablePolicies();
                Spoofing.RegistryManager.RestoreOriginalSettings();
                Spoofing.DnsManager.ResetToDhcp();
                FlushNetworkCache();
                
                // 1. Ana süreci durdurmayı dene
                if (vpnProcess != null && !vpnProcess.HasExited)
                {
                    try { vpnProcess.Kill(true); } catch { }
                    vpnProcess.Dispose();
                    vpnProcess = null;
                }

                // 2. Arka planda kalmış tüm sing-box'ları zorla temizle
                var strayProcesses = Process.GetProcessesByName("sing-box");
                foreach (var process in strayProcesses)
                {
                    try 
                    { 
                        process.Kill(true); 
                        process.WaitForExit(2000); 
                    } catch { }
                }

                StatusChanged?.Invoke("Bağlantı başarıyla kesildi.");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Durdurma sırasında hata: {ex.Message}");
            }
        }

        private void FlushNetworkCache()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ipconfig.exe",
                    Arguments = "/flushdns",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi)?.WaitForExit();
                
                // ARP tablosu ve diğer önbellekler için opsiyonel ama güvenli komutlar
                Process.Start(new ProcessStartInfo { FileName = "nbtstat.exe", Arguments = "-R", WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
            }
            catch { }
        }

        /// <summary>
        /// Nitro Geçit Hattı'nı başlatır.
        /// sing-box/TUN kullanmaz — WinDivert ile doğrudan DNS + SNI bypass yapar.
        /// Voice/oyun trafiğine sıfır etki.
        /// </summary>
        public void ConnectNitro()
        {
            try
            {
                Disconnect();
                NitroService.Start();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Nitro Hatası: {ex.Message}");
            }
        }

        private void GenerateConfig(Server? server, bool isGameMode, string splitApps, bool isNitroOnly = false)
        {
            try
            {
                // ============================================================
                // NITRO MODU: Tamamen ayrı, minimal config
                // Amacı: Seçili uygulamaların DNS'ini DoH üzerinden çözümlemek
                // Proxy yok, sadece DNS değiştirme
                // ============================================================
                if (isNitroOnly)
                {
                    GenerateNitroConfig(splitApps);
                    return;
                }

                // ============================================================
                // NORMAL VPN MODU (Full VPN / Split Tunnel / Game Mode)
                // ============================================================
                var inboundsList = new System.Collections.Generic.List<object>();

                var tunConfig = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "type", "tun" },
                    { "tag", "tun-in" },
                    { "interface_name", "truva-tun" },
                    { "inet4_address", "172.19.0.1/24" },
                    { "auto_route", true },
                    { "strict_route", true },
                    { "stack", "system" },
                    { "mtu", 1400 },
                    { "sniff", true },
                    { "sniff_override_destination", true }
                };

                inboundsList.Add(tunConfig);

                inboundsList.Add(new
                {
                    type = "mixed",
                    tag = "mixed-in",
                    listen = "127.0.0.1",
                    listen_port = 2080,
                    sniff = true,
                    sniff_override_destination = true
                });

                var outboundsList = new System.Collections.Generic.List<object>();

                if (server != null)
                {
                    var vlessOutbound = new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "type", "vless" },
                        { "tag", "proxy" },
                        { "server", server.Address },
                        { "server_port", server.Port },
                        { "uuid", server.Uuid },
                        { "packet_encoding", "xudp" }
                    };

                    if (!string.IsNullOrEmpty(server.Flow))
                        vlessOutbound["flow"] = server.Flow;

                    if (server.Security == "reality")
                    {
                        vlessOutbound["tls"] = new
                        {
                            enabled = true,
                            server_name = server.Sni,
                            utls = new { enabled = true, fingerprint = string.IsNullOrEmpty(server.Fingerprint) ? "chrome" : server.Fingerprint },
                            reality = new
                            {
                                enabled = true,
                                public_key = server.PublicKey,
                                short_id = server.ShortId
                            }
                        };
                    }
                    else if (server.Security == "tls")
                    {
                        vlessOutbound["tls"] = new
                        {
                            enabled = true,
                            server_name = server.Sni,
                            utls = new { enabled = true, fingerprint = string.IsNullOrEmpty(server.Fingerprint) ? "chrome" : server.Fingerprint },
                            alpn = string.IsNullOrEmpty(server.Alpn) ? null : server.Alpn.Split(',')
                        };
                    }

                    if (server.Network == "ws")
                    {
                        vlessOutbound["transport"] = new
                        {
                            type = "ws",
                            path = string.IsNullOrEmpty(server.Path) ? "/" : server.Path,
                            headers = string.IsNullOrEmpty(server.Host) ? null : new System.Collections.Generic.Dictionary<string, string> { { "Host", server.Host } }
                        };
                    }
                    else if (server.Network == "grpc")
                    {
                        vlessOutbound["transport"] = new
                        {
                            type = "grpc",
                            service_name = server.ServiceName
                        };
                    }

                    outboundsList.Add(vlessOutbound);
                }
                outboundsList.Add(new { type = "direct", tag = "direct" });
                outboundsList.Add(new { type = "dns", tag = "dns-out" });
                outboundsList.Add(new { type = "block", tag = "block" });

                // === ROUTING KURALLARI ===
                var routingRules = new System.Collections.Generic.List<object>
                {
                    new { protocol = "dns", outbound = "dns-out" },
                    new { ip_is_private = true, outbound = "direct" }
                };

                routingRules.Add(new { ip_version = 6, outbound = "block" });

                if (!string.IsNullOrWhiteSpace(splitApps))
                {
                    var processes = splitApps.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(p => p.Trim())
                                             .ToList();

                    routingRules.Add(new { process_name = processes, outbound = "proxy" });
                    routingRules.Add(new { inbound = new[] { "mixed-in" }, outbound = "proxy" });
                    routingRules.Add(new { inbound = new[] { "tun-in" }, outbound = "direct" });
                }
                else
                {
                    routingRules.Add(new { inbound = new[] { "tun-in", "mixed-in" }, outbound = "proxy" });
                }

                // === DNS ===
                var dnsRules = new System.Collections.Generic.List<object>
                {
                    new { outbound = new[] { "any" }, server = "local" }
                };

                var dnsConfig = new
                {
                    servers = new object[]
                    {
                        new { tag = "google", address = "8.8.8.8", detour = (server != null ? "proxy" : "direct") },
                        new { tag = "cloudflare", address = "1.1.1.1", detour = (server != null ? "proxy" : "direct") },
                        new { tag = "local", address = "local", detour = "direct" }
                    },
                    rules = dnsRules,
                    strategy = "prefer_ipv4",
                    independent_cache = true
                };

                var config = new
                {
                    log = new { level = "info", timestamp = true },
                    dns = dnsConfig,
                    inbounds = inboundsList,
                    outbounds = outboundsList,
                    route = new
                    {
                        rules = routingRules,
                        final = "direct",
                        auto_detect_interface = true
                    }
                };

                string jsonOutput = JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                File.WriteAllText("config.json", jsonOutput);
                Console.WriteLine("[CONFIG] Sing-Box config.json başarıyla oluşturuldu.");
                // try { File.AppendAllText("singbox_debug.log", $"\n=== CONFIG ===\n{jsonOutput}\n=== /CONFIG ===\n"); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HATA] Config oluşturulurken hata: " + ex.Message);
                StatusChanged?.Invoke($"Config Hatası: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Nitro Geçit Hattı için özel sing-box config oluşturur.
        /// 
        /// MİMARİ:
        /// - TUN tüm trafiği yakalar (auto_route)
        /// - DNS kuralları: seçili uygulamalar → DoH (Google), diğerleri → 8.8.8.8 (Direct DNS)
        /// - Tüm trafik direct outbound ile çıkar (proxy yok)
        /// 
        /// ÖNEMLİ TASARIM KARARLARI:
        /// 1. "address: local" KULLANILMAZ — TUN auto_route ile çakışır ve DNS döngüsü oluşturur
        /// 2. Hem fallback hem bootstrap DNS olarak 8.8.8.8 (IP) kullanılır — domain çözümleme gerektirmez
        /// 3. DoH sunucusunun domain'i (dns.google) → address_resolver ile 8.8.8.8 üzerinden çözümlenir
        /// </summary>
        private void GenerateNitroConfig(string splitApps)
        {
            var nitroProcesses = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrWhiteSpace(splitApps))
            {
                nitroProcesses = splitApps.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(p => p.Trim())
                                         .ToList();

                // Discord seçiliyse tüm varyantlarını ekle
                if (nitroProcesses.Any(p => p.Equals("Discord.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!nitroProcesses.Contains("DiscordCanary.exe")) nitroProcesses.Add("DiscordCanary.exe");
                    if (!nitroProcesses.Contains("DiscordPTB.exe")) nitroProcesses.Add("DiscordPTB.exe");
                    if (!nitroProcesses.Contains("Update.exe")) nitroProcesses.Add("Update.exe");
                }
            }

            // DNS sunucuları
            var dnsServers = new object[]
            {
                // DoH sunucusu — seçili uygulamalar için şifreli DNS
                // address_resolver: DoH URL'sindeki "dns.google" domain'ini çözümlemek için bootstrap DNS
                new { tag = "doh", address = "https://dns.google/dns-query", address_resolver = "direct-dns", detour = "direct" },
                // Bootstrap & fallback DNS — düz IP, hiçbir domain çözümlemesi gerektirmez
                // TUN döngüsü oluşturmaz çünkü sing-box kendi trafiğini TUN'dan hariç tutar
                new { tag = "direct-dns", address = "8.8.8.8", detour = "direct" }
            };

            // DNS kuralları
            var dnsRules = new System.Collections.Generic.List<object>();
            if (nitroProcesses.Count > 0)
            {
                // Seçili uygulamaların DNS sorguları → DoH (şifreli, ISP engelini aşar)
                dnsRules.Add(new { process_name = nitroProcesses, server = "doh" });
            }

            var dnsConfig = new
            {
                servers = dnsServers,
                rules = dnsRules,
                final = "direct-dns", // Seçili olmayan uygulamalar → 8.8.8.8 (düz DNS)
                strategy = "ipv4_only",
                independent_cache = true
            };

            // TUN inbound — tüm trafiği yakalar
            // PERFORMANS OPTİMİZASYONU:
            // - stack=system: Kernel seviyesinde paket işleme (gVisor userspace yerine)
            //   → Discord UDP gibi düşük latency trafikte spike oluşturmaz
            // - sniff=false: DNS kuralları zaten process_name ile çalışır,
            //   paket içeriği incelemesine gerek yok → gereksiz overhead kaldırıldı
            // - mtu=1500: Standart MTU, gereksiz fragmentasyonu önler
            var tunInbound = new
            {
                type = "tun",
                tag = "tun-in",
                interface_name = "truva-tun",
                inet4_address = "172.19.0.1/30",
                auto_route = true,
                strict_route = false,
                stack = "system",
                mtu = 1500,
                sniff = false,
                sniff_override_destination = false
            };

            // Outboundlar — Nitro'da proxy yok, sadece direct
            var outbounds = new object[]
            {
                new { type = "direct", tag = "direct" },
                new { type = "dns", tag = "dns-out" },
                new { type = "block", tag = "block" }
            };

            // Route kuralları — minimal
            // NOT: port=53 kullanılıyor (protocol="dns" yerine) çünkü sniff=false
            // sniff kapalıyken protocol tespiti yapılamaz, port bazlı eşleşme sniff gerektirmez
            var routeRules = new object[]
            {
                new { port = 53, outbound = "dns-out" },
                new { ip_is_private = true, outbound = "direct" },
                new { ip_version = 6, outbound = "block" }
            };

            var config = new
            {
                log = new { level = "info", timestamp = true },
                dns = dnsConfig,
                inbounds = new object[] { tunInbound },
                outbounds = outbounds,
                route = new
                {
                    rules = routeRules,
                    final = "direct",
                    auto_detect_interface = true
                }
            };

            string jsonOutput = JsonConvert.SerializeObject(config, Formatting.Indented, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            File.WriteAllText("config.json", jsonOutput);
            Console.WriteLine("[CONFIG] Nitro config.json başarıyla oluşturuldu.");
            // try { File.AppendAllText("singbox_debug.log", $"\n=== NITRO CONFIG ===\n{jsonOutput}\n=== /NITRO CONFIG ===\n"); } catch { }
        }
    }
}
