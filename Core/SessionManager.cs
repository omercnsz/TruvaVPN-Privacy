using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TruvaDesktop.Core
{
    public class SessionManager
    {
        private const string FirebaseUrl = "https://truvavpn-default-rtdb.firebaseio.com/keys";
        private const string AuthUrl = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=AIzaSyDbgOsARmQlWx1peRZvsSIwbASFVWiOrw8";
        private const string SessionFile = "session.bin";
        private static readonly HttpClient client = new HttpClient();
        
        private string? _idToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        static SessionManager()
        {
            client.DefaultRequestHeaders.Add("User-Agent", "TruvaVPN-Desktop-Client");
        }

        public DateTime? SessionExpiry { get; private set; }

        public SessionManager()
        {
            LoadSession();
        }

        private async Task<bool> EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_idToken) && DateTime.UtcNow < _tokenExpiry)
                return true;

            try
            {
                var authData = new { returnSecureToken = true };
                var content = new StringContent(JsonConvert.SerializeObject(authData), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(AuthUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var data = JObject.Parse(json);
                    _idToken = data["idToken"]?.ToString();
                    int expiresIn = int.Parse(data["expiresIn"]?.ToString() ?? "3600");
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // 1 dk pay bırakalım
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AUTH] Hata: {ex.Message}");
            }
            return false;
        }

        public bool IsSessionActive
        {
            get
            {
                if (!SessionExpiry.HasValue) return false;
                return DateTime.UtcNow < SessionExpiry.Value;
            }
        }

        public TimeSpan RemainingTime
        {
            get
            {
                if (!SessionExpiry.HasValue) return TimeSpan.Zero;
                var remaining = SessionExpiry.Value - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Firebase üzerinden kodu doğrular ve oturumu uzatır.
        /// </summary>
        public async Task<(bool success, string message)> ValidateCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 4)
                return (false, "Geçersiz kod formatı.");

            try
            {
                // 0. Kimlik Doğrula (Anonim Giriş)
                if (!await EnsureAuthenticatedAsync())
                    return (false, "Kimlik doğrulaması başarısız oldu (Auth Error).");

                // 1. Kodu kontrol et
                string url = $"{FirebaseUrl}/{code}.json?auth={_idToken}";
                var response = await client.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = await response.Content.ReadAsStringAsync();
                    return (false, $"Erişim Hatası: {(int)response.StatusCode}\nLütfen internetinizi kontrol edin.");
                }

                string json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(json) || json == "null")
                    return (false, "Hatalı veya geçersiz bir kod girdiniz.");

                var data = JObject.Parse(json);
                bool isUsed = data["isUsed"]?.Value<bool>() ?? true;

                if (isUsed)
                    return (false, "Bu kod daha önce kullanılmış.");

                // 2. Kodu 'kullanıldı' olarak işaretle (isUsed = true)
                var patchData = new { isUsed = true, usedAt = DateTime.UtcNow.ToString("O") };
                var patchContent = new StringContent(JsonConvert.SerializeObject(patchData), Encoding.UTF8, "application/json");
                
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = patchContent };
                var patchResponse = await client.SendAsync(request);

                if (patchResponse.IsSuccessStatusCode)
                {
                    // 3. Oturumu başlat/uzat
                    GrantAccess(3);
                    return (true, "Erişim onaylandı! +3 Saat Koruma Başladı.");
                }

                return (false, "Kod işlenirken bir hata oluştu.");
            }
            catch (Exception ex)
            {
                return (false, "Bağlantı Hatası: " + ex.Message);
            }
        }

        public void GrantAccess(int hours)
        {
            if (IsSessionActive && SessionExpiry.HasValue)
            {
                SessionExpiry = SessionExpiry.Value.AddHours(hours);
            }
            else
            {
                SessionExpiry = DateTime.UtcNow.AddHours(hours);
            }
            SaveSession();
        }

        private void SaveSession()
        {
            try
            {
                if (SessionExpiry.HasValue)
                {
                    string data = SessionExpiry.Value.Ticks.ToString();
                    // Basit bir obfuscation (şifreleme değil, sadece okunmasın diye)
                    byte[] bytes = Encoding.UTF8.GetBytes(data);
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(bytes[i] ^ 0x5A);
                    File.WriteAllBytes(SessionFile, bytes);
                }
            }
            catch { }
        }

        private void LoadSession()
        {
            try
            {
                if (File.Exists(SessionFile))
                {
                    byte[] bytes = File.ReadAllBytes(SessionFile);
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(bytes[i] ^ 0x5A);
                    string data = Encoding.UTF8.GetString(bytes);
                    if (long.TryParse(data, out long ticks))
                    {
                        SessionExpiry = new DateTime(ticks, DateTimeKind.Utc);
                    }
                }
            }
            catch { }
        }

        public void ClearSession()
        {
            SessionExpiry = null;
            if (File.Exists(SessionFile)) File.Delete(SessionFile);
        }
    }
}
