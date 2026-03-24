using Newtonsoft.Json;
using System.Collections.Generic;

namespace TruvaDesktop.Core
{
    public class Server
    {
        public string Id { get; set; } = string.Empty;
        public string CountryName { get; set; } = string.Empty;
        public string VlessLink { get; set; } = string.Empty;
        
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Uuid { get; set; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string Security { get; set; } = string.Empty;
        public string Sni { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string ShortId { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Flow { get; set; } = string.Empty;
        public string Alpn { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        
        public bool UdpSupported { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsPremium { get; set; }

        public string CountryCode => "US"; 
        
        // Kullanıcı dostu görünen ad (Favori, Premium ve UDP bilgisini içerir)
        public string DisplayName => $"{(IsFavorite ? "⭐ " : "")}{(IsPremium ? "👑 " : "")}{CountryName} ({Network?.ToUpper()}{(UdpSupported ? ", UDP" : "")})";
    }

    public class ScraperRoot
    {
        [JsonProperty("servers")]
        public ScraperGroups Servers { get; set; } = new ScraperGroups();
    }

    public class ScraperGroups
    {
        [JsonProperty("reality")]
        public List<ScraperNode> Reality { get; set; } = new List<ScraperNode>();
        [JsonProperty("vless_tls")]
        public List<ScraperNode> VlessTls { get; set; } = new List<ScraperNode>();
        [JsonProperty("vless_other")]
        public List<ScraperNode> VlessOther { get; set; } = new List<ScraperNode>();
    }

    public class ScraperNode
    {
        [JsonProperty("id")] public string Id { get; set; } = "";
        [JsonProperty("remark")] public string Remark { get; set; } = "";
        [JsonProperty("raw")] public string Raw { get; set; } = "";
        
        [JsonProperty("address")] public string Address { get; set; } = "";
        [JsonProperty("port")] public int Port { get; set; }
        [JsonProperty("uuid")] public string Uuid { get; set; } = "";
        [JsonProperty("network")] public string Network { get; set; } = "";
        [JsonProperty("security")] public string Security { get; set; } = "";
        [JsonProperty("sni")] public string Sni { get; set; } = "";
        [JsonProperty("fingerprint")] public string Fingerprint { get; set; } = "";
        [JsonProperty("publicKey")] public string PublicKey { get; set; } = "";
        [JsonProperty("shortId")] public string ShortId { get; set; } = "";
        [JsonProperty("path")] public string Path { get; set; } = "";
        [JsonProperty("host")] public string Host { get; set; } = "";
        [JsonProperty("flow")] public string Flow { get; set; } = "";
        [JsonProperty("alpn")] public string Alpn { get; set; } = "";
        [JsonProperty("serviceName")] public string ServiceName { get; set; } = "";
        
        [JsonProperty("udp_supported")] public bool? UdpSupported { get; set; }
    }
}
