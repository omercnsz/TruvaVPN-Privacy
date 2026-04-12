using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;

namespace TruvaDesktop.Core
{
    public class ApiManager
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public async Task<List<Server>> GetServersAsync()
        {
            List<Server> servers = new List<Server>();
            string jsonResponse = string.Empty;

            string pyPath = @"c:\Users\User\Desktop\py\main.py";
            string outputPath = @"c:\Users\User\Desktop\py\output\servers.json";
            
            try
            {
                if (File.Exists(pyPath))
                {
                    Console.WriteLine("[SCRAPER] Yerel Python güncelleyici başlatıldı...");
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{pyPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                }
            }
            catch(Exception ex) { Console.WriteLine($"[SCRAPER HATA] {ex.Message}"); }

            try 
            {
                if (File.Exists(outputPath)) 
                {
                    jsonResponse = await File.ReadAllTextAsync(outputPath);
                    Console.WriteLine("[SCRAPER] Yerel JSON yüklendi.");
                }
            }
            catch { }

            if (string.IsNullOrEmpty(jsonResponse))
            {
                string url = "https://raw.githubusercontent.com/omercnsz/truva-servers/refs/heads/main/output/servers.json";
                try
                {
                    jsonResponse = await client.GetStringAsync(url);
                    Console.WriteLine("[GITHUB] Veri GitHub'dan çekildi.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[HATA] GitHub yedeğine erişilemedi: " + ex.Message);
                    return servers;
                }
            }

            try
            {
                var root = JsonConvert.DeserializeObject<ScraperRoot>(jsonResponse);
                if (root?.Servers != null)
                {
                    var allNodes = new List<ScraperNode>();
                    if (root.Servers.Reality != null) allNodes.AddRange(root.Servers.Reality);
                    if (root.Servers.VlessTls != null) allNodes.AddRange(root.Servers.VlessTls);
                    if (root.Servers.VlessOther != null) allNodes.AddRange(root.Servers.VlessOther);

                    var favorites = LoadFavorites();

                    foreach (var node in allNodes)
                    {
                        if (string.IsNullOrEmpty(node.Raw)) continue;
                        
                        string extractedCountry = "Bilinmiyor";
                        string remarkLower = (node.Remark ?? "").ToLower();
                        
                        if (remarkLower.Contains("🇩🇪") || remarkLower.Contains("almanya") || remarkLower.Contains("de")) extractedCountry = "Almanya";
                        else if (remarkLower.Contains("🇺🇸") || remarkLower.Contains("amerika") || remarkLower.Contains("us")) extractedCountry = "Amerika";
                        else if (remarkLower.Contains("🇹🇷") || remarkLower.Contains("türkiye") || remarkLower.Contains("tr")) extractedCountry = "Türkiye";
                        else if (remarkLower.Contains("nl") || remarkLower.Contains("🇳🇱") || remarkLower.Contains("netherland")) extractedCountry = "Hollanda";
                        else if (remarkLower.Contains("🇷🇺") || remarkLower.Contains("ru")) extractedCountry = "Rusya";
                        else if (remarkLower.Contains("🇱🇻") || remarkLower.Contains("lv")) extractedCountry = "Letonya";
                        else extractedCountry = node.Remark != null && node.Remark.Length > 20 ? node.Remark.Substring(0, 20) : (node.Remark ?? "Uluslararası");

                        string finalId = string.IsNullOrEmpty(node.Id) ? Guid.NewGuid().ToString() : node.Id;

                        servers.Add(new Server 
                        { 
                            Id = finalId,
                            IsFavorite = favorites.Contains(finalId),
                            UdpSupported = node.UdpSupported ?? false,
                            CountryName = extractedCountry,
                            VlessLink = node.Raw,
                            Address = node.Address,
                            Port = node.Port,
                            Uuid = node.Uuid,
                            Network = node.Network,
                            Security = node.Security,
                            Sni = node.Sni,
                            Fingerprint = node.Fingerprint,
                            PublicKey = node.PublicKey,
                            ShortId = node.ShortId,
                            Path = node.Path,
                            Host = node.Host,
                            Flow = node.Flow,
                            Alpn = node.Alpn,
                            ServiceName = node.ServiceName
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[JSON HATA] Sunucular dönüştürülemedi: " + ex.Message);
            }

            return servers;
        }

        private List<string> LoadFavorites()
        {
            try
            {
                if (File.Exists("favorites.json"))
                    return JsonConvert.DeserializeObject<List<string>>(File.ReadAllText("favorites.json")) ?? new List<string>();
            }
            catch { }
            return new List<string>();
        }
        
        public void ToggleFavorite(Server server)
        {
            var favs = LoadFavorites();
            if (favs.Contains(server.Id)) favs.Remove(server.Id);
            else favs.Add(server.Id);
            
            try 
            {
                File.WriteAllText("favorites.json", JsonConvert.SerializeObject(favs));
                server.IsFavorite = favs.Contains(server.Id);
            } 
            catch { }
        }

        public List<Models.Country> GetSupportedCountries()
        {
            return new List<Models.Country>
            {
                new Models.Country { Name = "Amerika", Code = "US", TimeZone = "Eastern Standard Time", GeoId = "244", Locale = "en-US" },
                new Models.Country { Name = "Almanya", Code = "DE", TimeZone = "W. Europe Standard Time", GeoId = "94", Locale = "de-DE" },
                new Models.Country { Name = "Türkiye", Code = "TR", TimeZone = "Turkey Standard Time", GeoId = "235", Locale = "tr-TR" },
                new Models.Country { Name = "Hollanda", Code = "NL", TimeZone = "W. Europe Standard Time", GeoId = "176", Locale = "nl-NL" },
                new Models.Country { Name = "İngiltere", Code = "GB", TimeZone = "GMT Standard Time", GeoId = "242", Locale = "en-GB" },
                new Models.Country { Name = "Fransa", Code = "FR", TimeZone = "Romance Standard Time", GeoId = "84", Locale = "fr-FR" },
                new Models.Country { Name = "İtalya", Code = "IT", TimeZone = "W. Europe Standard Time", GeoId = "118", Locale = "it-IT" },
                new Models.Country { Name = "İspanya", Code = "ES", TimeZone = "Romance Standard Time", GeoId = "217", Locale = "es-ES" },
                new Models.Country { Name = "Japonya", Code = "JP", TimeZone = "Tokyo Standard Time", GeoId = "122", Locale = "ja-JP" },
                new Models.Country { Name = "Rusya", Code = "RU", TimeZone = "Russian Standard Time", GeoId = "203", Locale = "ru-RU" },
                new Models.Country { Name = "Letonya", Code = "LV", TimeZone = "E. Europe Standard Time", GeoId = "140", Locale = "lv-LV" }
            };
        }
    }
}
