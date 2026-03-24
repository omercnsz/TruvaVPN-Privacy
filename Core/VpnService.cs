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
        public event Action<string>? StatusChanged;

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
                            try { File.AppendAllText("singbox_debug.log", $"[{DateTime.Now:HH:mm:ss}] {e.Data}\n"); } catch { }
                            if (e.Data.IndexOf("started", StringComparison.OrdinalIgnoreCase) >= 0 || e.Data.IndexOf("listening at", StringComparison.OrdinalIgnoreCase) >= 0)
                                StatusChanged?.Invoke($"[BILGI] VPN Tüneli Güvenle Kuruldu.");
                        }
                    };
                    
                    vpnProcess.ErrorDataReceived += (s, e) => { 
                        if (!string.IsNullOrEmpty(e.Data)) 
                        { 
                            Console.WriteLine($"[SINGBOX ERR]: {e.Data}"); 
                            try { File.AppendAllText("singbox_debug.log", $"[{DateTime.Now:HH:mm:ss}] ERROR: {e.Data}\n"); } catch { }
                            
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
                Spoofing.ProxyManager.DisableSystemProxy();
                Spoofing.BrowserPolicyManager.DisablePolicies();
                
                if (vpnProcess != null && !vpnProcess.HasExited)
                {
                    vpnProcess.Kill();
                    vpnProcess.Dispose();
                    vpnProcess = null;
                    StatusChanged?.Invoke("[BILGI] Bağlantı sonlandırıldı.");
                }

                foreach (var process in Process.GetProcessesByName("sing-box"))
                {
                    try { process.Kill(); } catch { }
                }

                if (vpnProcess == null || vpnProcess.HasExited)
                {
                    StatusChanged?.Invoke("Bağlantı kesildi.");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Durdurma Hatası: {ex.Message}");
            }
        }

        private void GenerateConfig(Server server, bool isGameMode, string splitApps)
        {
            try
            {
                object tunInbound;
                if (!string.IsNullOrWhiteSpace(splitApps))
                {
                    tunInbound = new
                    {
                        type = "tun",
                        tag = "tun-in",
                        interface_name = "truva-tun",
                        inet4_address = "172.19.0.1/30",
                        auto_route = true,
                        strict_route = true,
                        stack = "system",
                        sniff = true,
                        sniff_override_destination = true
                    };
                }
                else
                {
                    tunInbound = new
                    {
                        type = "tun",
                        tag = "tun-in",
                        interface_name = "truva-tun",
                        inet4_address = "172.19.0.1/30",
                        auto_route = true,
                        strict_route = true,
                        stack = "system",
                        sniff = true,
                        sniff_override_destination = true
                    };
                }

                var outboundsList = new System.Collections.Generic.List<object>();

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
                outboundsList.Add(new { type = "direct", tag = "direct" });
                outboundsList.Add(new { type = "dns", tag = "dns-out" });

                var routingRules = new System.Collections.Generic.List<object>
                {
                    new { protocol = "dns", outbound = "dns-out" },
                    new { ip_is_private = true, outbound = "direct" }
                };

                if (!string.IsNullOrWhiteSpace(splitApps))
                {
                    var processes = splitApps.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(p => p.Trim())
                                             .ToList();

                    // Sadece seçili işlemler proxy'e girsin
                    routingRules.Add(new { process_name = processes, outbound = "proxy" });
                }
                else
                {
                    // Global modda her şey proxy'e girsin
                    routingRules.Add(new { inbound = new[] { "tun-in" }, outbound = "proxy" });
                }

                var config = new
                {
                    log = new { level = "info", timestamp = true },
                    dns = new
                    {
                        servers = new[]
                        {
                            new { tag = "google", address = "8.8.8.8", detour = "proxy" },
                            new { tag = "local", address = "local", detour = "direct" }
                        },
                        rules = new[]
                        {
                             new { outbound = new[] { "any" }, server = "google" }
                        },
                        strategy = "prefer_ipv4"
                    },
                    inbounds = new[] { tunInbound },
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
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HATA] Config oluşturulurken hata: " + ex.Message);
                StatusChanged?.Invoke($"Config Hatası: {ex.Message}");
                throw;
            }
        }
    }
}
