using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace TruvaDesktop.Nitro
{
    /// <summary>
    /// DNS over HTTPS (DoH) resolver with in-memory cache.
    /// Sends DNS wire format queries to Google DoH and caches responses.
    /// </summary>
    public class DoHResolver : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, CachedResponse> _cache = new();

        private class CachedResponse
        {
            public byte[] Data { get; set; } = Array.Empty<byte>();
            public DateTime ExpiresAt { get; set; }
        }

        public DoHResolver()
        {
            var handler = new HttpClientHandler
            {
                // DoH sunucusu için sistem proxy'sini kullanma
                UseProxy = false
            };

            _httpClient = new HttpClient(handler)
            {
                DefaultRequestVersion = new Version(2, 0), // HTTP/2 for performance
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// DNS sorgusunu DoH üzerinden çözümler.
        /// Cache'te varsa ve süresi dolmamışsa cache'ten döner.
        /// </summary>
        /// <param name="dnsQuery">DNS wire format sorgusu (RFC 1035)</param>
        /// <returns>DNS wire format yanıtı veya null (hata durumunda)</returns>
        public async Task<byte[]?> ResolveAsync(byte[] dnsQuery)
        {
            try
            {
                if (dnsQuery.Length < 12) return null; // DNS header minimum 12 bytes

                string domain = ExtractDomainName(dnsQuery);
                string cacheKey = Convert.ToBase64String(dnsQuery, 12, dnsQuery.Length - 12);

                if (_cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
                {
                    // Cache hit — transaction ID'yi mevcut sorgudan kopyala
                    var response = (byte[])cached.Data.Clone();
                    response[0] = dnsQuery[0]; // Transaction ID byte 1
                    response[1] = dnsQuery[1]; // Transaction ID byte 2
                    NitroLogger.Log($"[DNS] {domain} -> DoH Cache Hit (Global)");
                    return response;
                }

                NitroLogger.Log($"[DNS] {domain} -> DoH Resolving... (Global)");
                // Cache miss — DoH üzerinden çözümle (Google -> Cloudflare fallback)
                byte[]? responseData = await TryResolveDoH("https://dns.google/dns-query", dnsQuery);
                
                if (responseData == null)
                {
                    // Fallback to Cloudflare
                    responseData = await TryResolveDoH("https://1.1.1.1/dns-query", dnsQuery);
                }

                if (responseData == null)
                {
                    // Fallback to Quad9
                    responseData = await TryResolveDoH("https://9.9.9.9/dns-query", dnsQuery);
                }

                if (responseData == null) return null;

                // DNS yanıtından TTL'yi çıkar ve cache süresi olarak kullan
                int cacheTtlSeconds = ExtractMinTtl(responseData);
                if (cacheTtlSeconds < 30) cacheTtlSeconds = 30;     // Minimum 30 saniye
                if (cacheTtlSeconds > 600) cacheTtlSeconds = 600;   // Maximum 10 dakika

                _cache[cacheKey] = new CachedResponse
                {
                    Data = responseData,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(cacheTtlSeconds)
                };

                return responseData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nitro DoH] Çözümleme hatası: {ex.Message}");
                return null;
            }
        }

        private async Task<byte[]?> TryResolveDoH(string url, byte[] dnsQuery)
        {
            try
            {
                var content = new ByteArrayContent(dnsQuery);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

                var httpResponse = await _httpClient.SendAsync(request);
                if (!httpResponse.IsSuccessStatusCode) return null;

                return await httpResponse.Content.ReadAsByteArrayAsync();
            }
            catch { return null; }
        }

        /// <summary>
        /// DNS yanıtından minimum TTL değerini çıkarır.
        /// </summary>
        private int ExtractMinTtl(byte[] response)
        {
            try
            {
                if (response.Length < 12) return 300;

                // Answer count (bytes 6-7)
                int answerCount = (response[6] << 8) | response[7];
                if (answerCount == 0) return 60; // No answers, cache kısa süre

                // Question section'ı atla
                int offset = 12;

                // Question count
                int questionCount = (response[4] << 8) | response[5];
                for (int i = 0; i < questionCount; i++)
                {
                    // Domain name (labels)
                    while (offset < response.Length)
                    {
                        byte labelLen = response[offset];
                        if (labelLen == 0) { offset++; break; }
                        if ((labelLen & 0xC0) == 0xC0) { offset += 2; break; } // Pointer
                        offset += labelLen + 1;
                    }
                    offset += 4; // QTYPE (2) + QCLASS (2)
                }

                // Answer section — TTL'yi bul
                int minTtl = int.MaxValue;
                for (int i = 0; i < answerCount && offset < response.Length; i++)
                {
                    // Name
                    while (offset < response.Length)
                    {
                        byte labelLen = response[offset];
                        if (labelLen == 0) { offset++; break; }
                        if ((labelLen & 0xC0) == 0xC0) { offset += 2; break; }
                        offset += labelLen + 1;
                    }

                    if (offset + 10 > response.Length) break;

                    // TYPE (2) + CLASS (2) = 4 bytes
                    offset += 4;

                    // TTL (4 bytes, big-endian)
                    int ttl = (response[offset] << 24) | (response[offset + 1] << 16) |
                              (response[offset + 2] << 8) | response[offset + 3];
                    if (ttl < minTtl) minTtl = ttl;
                    offset += 4;

                    // RDLENGTH (2 bytes) + RDATA
                    int rdLength = (response[offset] << 8) | response[offset + 1];
                    offset += 2 + rdLength;
                }

                return minTtl == int.MaxValue ? 300 : minTtl;
            }
            catch
            {
                return 300; // Parse hatası durumunda 5 dakika
            }
        }

        public void ClearCache()
        {
            _cache.Clear();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        private string ExtractDomainName(byte[] dnsQuery)
        {
            try
            {
                if (dnsQuery.Length <= 12) return "unknown";
                
                int offset = 12;
                var domain = new System.Text.StringBuilder();
                
                while (offset < dnsQuery.Length)
                {
                    int len = dnsQuery[offset];
                    if (len == 0) break;
                    
                    if (domain.Length > 0) domain.Append(".");
                    
                    if (offset + 1 + len > dnsQuery.Length) break;
                    
                    domain.Append(System.Text.Encoding.ASCII.GetString(dnsQuery, offset + 1, len));
                    offset += 1 + len;
                }
                
                return domain.Length == 0 ? "unknown" : domain.ToString();
            }
            catch { return "error"; }
        }
    }
}
