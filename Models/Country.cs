namespace TruvaDesktop.Models
{
    public class Country
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // ISO 2-letter
        public string TimeZone { get; set; } = string.Empty; // For RegistryManager
        public string GeoId { get; set; } = string.Empty; // Windows GeoID
        public string Locale { get; set; } = string.Empty; // e.g., en-US

        public string DisplayName => $"{GetFlag(Code)} {Name}";

        private string GetFlag(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode)) return "🌐";
            return countryCode.ToUpper() switch
            {
                "US" => "🇺🇸",
                "DE" => "🇩🇪",
                "TR" => "🇹🇷",
                "NL" => "🇳🇱",
                "RU" => "🇷🇺",
                "LV" => "🇱🇻",
                "GB" => "🇬🇧",
                "FR" => "🇫🇷",
                "IT" => "🇮🇹",
                "ES" => "🇪🇸",
                "JP" => "🇯🇵",
                _ => "🌐"
            };
        }
    }
}
