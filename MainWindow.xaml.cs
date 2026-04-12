using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Controls;
using TruvaDesktop.Core;
using TruvaDesktop.Models;
using System.Diagnostics;

namespace TruvaDesktop
{
    public partial class MainWindow : Window
    {
        private bool isConnected;
        private bool isNitroActive;
        private bool _isClosing;
        private readonly Core.ApiManager _apiManager;
        private readonly Core.VpnService _vpnService;
        private readonly Core.UpdateManager _updateManager;
        private readonly List<Core.Server> _allServersCache = [];
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private readonly Core.SessionManager _sessionManager;
        private DateTime _startTime;
        private string _currentLang = "tr-TR";

        public MainWindow()
        {
            InitializeComponent();
            ApplyLanguage(_currentLang);
            
            _apiManager = new Core.ApiManager();
            _vpnService = new Core.VpnService();
            lstNitroApps.ItemsSource = _vpnService.NitroService.Apps;


            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            Spoofing.RegistryManager.InitializeFailsafe();
            LoadApps();
            _ = LoadServersAsync();

            _sessionManager = new Core.SessionManager();
            _updateManager = new Core.UpdateManager();

            // Önce güncelleme kontrolü yap, sonra kilit ekranına geç
            _ = CheckForUpdatesAsync();
            
            // Oturum kontrolü için her saniye çalışan timer'ı başlat (bağlantıdan bağımsız)
            if (!_timer.IsEnabled) _timer.Start();



            this.Closing += async (s, e) => 
            {
                if (_isClosing) return;
                
                e.Cancel = true; 
                _isClosing = true;

                // Görsel Bildirim
                txtStatus.Text = "TEMİZLENİYOR... (TARAYICINIZI TAMAMEN KAPATIN)";
                txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8C00")); // Dark Orange

                // Arka planda temizliği başlat
                await Task.Run(() => _vpnService.Disconnect());
                
                // Kullanıcının görmesi ve işlemlerin tamamlanması için kısa bir bekleme
                await Task.Delay(800);

                // Uygulamayı tamamen kapat
                Application.Current.Shutdown();
            };

            _vpnService.StatusChanged += (status) => 
            {
                Dispatcher.Invoke(() => {
                    // ANSI renk kodlarını temizle
                    string cleanStatus = AnsiRegex().Replace(status, "");

                    if (cleanStatus.StartsWith("[KOPARILDI]")) 
                    {
                        isConnected = false;
                        isNitroActive = false;
                        // Timer'ı durdurma, oturum kontrolü devam etsin
                        ResetUIState();
                    }

                    if (cleanStatus.Contains("ERROR") || cleanStatus.Contains("FATAL") || cleanStatus.Contains("[HATA]")) 
                    {
                        txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"));
                    }
                    else if (cleanStatus.Contains("started") || cleanStatus.Contains("listening") || cleanStatus.Contains("[BILGI]"))
                    {
                        txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                    }
                    else
                    {
                        txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
                    }
                    
                    txtStatus.Text = cleanStatus;
                });
            };
        }

        private void ApplyLanguage(string langCode)
        {
            try
            {
                var dict = new ResourceDictionary();
                dict.Source = new Uri($"Resources/Strings.{langCode}.xaml", UriKind.Relative);

                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(dict);
                _currentLang = langCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LANG] Hata: {ex.Message}");
            }
        }

        private void BtnLang_Click(object sender, RoutedEventArgs e)
        {
            string newLang = _currentLang == "tr-TR" ? "en-US" : "tr-TR";
            ApplyLanguage(newLang);
        }



        [System.Text.RegularExpressions.GeneratedRegex(@"\x1B\[[^m]*m")]
        private static partial System.Text.RegularExpressions.Regex AnsiRegex();

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // VPN Bağlantı Süresi
                if (isConnected || isNitroActive)
                {
                    var duration = DateTime.Now - _startTime;
                    txtTimer.Text = duration.ToString(@"hh\:mm\:ss");
                }

                // Oturum Kontrolü (3 Saat Limiti)
                if (_sessionManager == null) return;

                if (_sessionManager.IsSessionActive)
                {
                    if (lockOverlay.Visibility == Visibility.Visible)
                        lockOverlay.Visibility = Visibility.Collapsed;

                    lblSessionInfo.Visibility = Visibility.Visible;
                    txtSessionRemaining.Visibility = Visibility.Visible;
                    
                    var remaining = _sessionManager.RemainingTime;
                    txtSessionRemaining.Text = remaining.ToString(@"hh\:mm\:ss");

                    // Son 1 dakika uyarısı
                    if (remaining.TotalSeconds < 60)
                        txtSessionRemaining.Foreground = Brushes.Red;
                    else
                        txtSessionRemaining.Foreground = Brushes.White;
                }
                else
                {
                    // OTURUM BİTTİ: HEMEN KİLİTLE
                    if (lockOverlay.Visibility != Visibility.Visible)
                    {
                        lockOverlay.Visibility = Visibility.Visible;
                        lblSessionInfo.Visibility = Visibility.Collapsed;
                        txtSessionRemaining.Visibility = Visibility.Collapsed;
                        
                        // Arka planda tüm bağlantıları zorla kes
                        _ = Task.Run(() => {
                            Dispatcher.Invoke(() => {
                                Disconnect();
                                MessageBox.Show((string)Application.Current.Resources["LockSessionExpired"], (string)Application.Current.Resources["LockTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                            });
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TIMER] Hata: {ex.Message}");
            }
        }

        private void CheckSessionState()
        {
            if (_sessionManager.IsSessionActive)
            {
                lockOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                lockOverlay.Visibility = Visibility.Visible;
            }
        }

        private async void BtnUnlock_Click(object sender, RoutedEventArgs e)
        {
            string code = txtAccessCode.Text.Trim();
            if (string.IsNullOrEmpty(code)) return;

            btnUnlock.IsEnabled = false;
            txtLockStatus.Text = (string)Application.Current.Resources["LockStatusValidating"];
            txtLockStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));

            var result = await _sessionManager.ValidateCodeAsync(code);

            if (result.success)
            {
                txtLockStatus.Text = result.message;
                txtLockStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                
                await Task.Delay(1500); // Başarı mesajını görsün
                
                txtAccessCode.Clear();
                txtLockStatus.Text = "";
                CheckSessionState();
            }
            else
            {
                txtLockStatus.Text = result.message;
                txtLockStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"));
            }

            btnUnlock.IsEnabled = true;
        }

        private void BtnGetCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://play.google.com/store/apps/details?id=com.kaziksavar.app";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Uygulama başlatılamadı: " + ex.Message);
            }
        }

        private void TxtAccessCode_GotFocus(object sender, RoutedEventArgs e)
        {
            txtAccessCode.SelectAll();
        }

        private async void BtnAddTime_Click(object sender, RoutedEventArgs e)
        {
            string code = txtAddCode.Text.Trim();
            if (string.IsNullOrEmpty(code)) return;

            btnAddTime.IsEnabled = false;
            string originalText = btnAddTime.Content.ToString() ?? "";
            btnAddTime.Content = "...";

            var result = await _sessionManager.ValidateCodeAsync(code);

            if (result.success)
            {
                txtAddCode.Clear();
                MessageBox.Show("Süreniz başarıyla 3 saat uzatıldı!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                CheckSessionState();
            }
            else
            {
                MessageBox.Show(result.message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            btnAddTime.IsEnabled = true;
            btnAddTime.Content = originalText;
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                paneConn.Visibility = Visibility.Collapsed;
                paneApps.Visibility = Visibility.Collapsed;
                paneSafety.Visibility = Visibility.Collapsed;
                paneNitro.Visibility = Visibility.Collapsed;
                paneGame.Visibility = Visibility.Collapsed;

                if (rb.Name == "navConn") paneConn.Visibility = Visibility.Visible;
                else if (rb.Name == "navApps") paneApps.Visibility = Visibility.Visible;
                else if (rb.Name == "navSafety") paneSafety.Visibility = Visibility.Visible;
                else if (rb.Name == "navNitro") paneNitro.Visibility = Visibility.Visible;
                else if (rb.Name == "navGame") paneGame.Visibility = Visibility.Visible;
            }
        }

        private void ResetUIState()
        {
            btnConnectSystem.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            btnConnectSystem.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121420"));
            txtBtnSysText.Text = "SİSTEME BAĞLA";
            btnConnectSystem.IsEnabled = true;

            btnConnectApp.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2FF"));
            btnConnectApp.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121420"));
            txtBtnAppText.Text = (string)Application.Current.Resources["BtnConnectAppAction"];
            btnConnectApp.IsEnabled = true;
            lstApps.IsEnabled = true;
            btnBrowseApp.IsEnabled = true;
            btnRemoveApp.IsEnabled = true;
            btnClearApps.IsEnabled = true;

            btnConnectNitro.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6F42C1"));
            txtBtnNitroText.Text = (string)Application.Current.Resources["BtnNitroStart"];
            btnConnectNitro.IsEnabled = true;
            lstNitroApps.IsEnabled = true;
            btnBrowseNitroApp.IsEnabled = true;
            btnRemoveNitroApp.IsEnabled = true;
            
            // Re-enable server selection and settings
            cmbServers.IsEnabled = true;
            cmbServersApps.IsEnabled = true;
            toggleGameMode.IsEnabled = true;
            toggleSpoofing.IsEnabled = true;
            cmbSpoofingCountry.IsEnabled = true;

            txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            txtStatus.Text = "Bağlantı Bekleniyor";
            txtTimer.Text = "00:00:00";
            // _timer.Stop(); // Oturum kontrolü için çalışmaya devam etmeli
        }

        private void CmbServers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbServers.SelectedItem != null && cmbServersApps.SelectedItem != cmbServers.SelectedItem)
            {
                cmbServersApps.SelectedItem = cmbServers.SelectedItem;
            }
        }

        private void CmbServersApps_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbServersApps.SelectedItem != null && cmbServers.SelectedItem != cmbServersApps.SelectedItem)
            {
                cmbServers.SelectedItem = cmbServersApps.SelectedItem;
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ToggleSpoofing_Changed(object sender, RoutedEventArgs e)
        {
            if (toggleSpoofing.IsChecked == true)
            {
                toggleSpoofing.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                toggleSpoofing.Content = "🛡️ Kimlik Gizleme (Aktif)";
            }
            else
            {
                toggleSpoofing.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AA3333"));
                toggleSpoofing.Content = "🛡️ Kimlik Gizleme (Pasif)";
            }
        }

        private void ToggleGameMode_Changed(object sender, RoutedEventArgs e)
        {
            ApplyServerFilter();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadServersAsync();
        }

        private void BtnFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (cmbServers.SelectedItem is Core.Server selectedServer)
            {
                _apiManager.ToggleFavorite(selectedServer);
                ApplyServerFilter();
                
                // Yeniden seçimi sağla
                var matching = cmbServers.Items.Cast<Core.Server>().FirstOrDefault(x => x.Id == selectedServer.Id);
                if (matching != null) cmbServers.SelectedItem = matching;
            }
        }

        private void BtnBrowseApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new AppPickerWindow();
            picker.Owner = this;
            if (picker.ShowDialog() == true && picker.SelectedApp != null)
            {
                string exeName = picker.SelectedApp.ExeName;
                if (!lstApps.Items.Contains(exeName))
                {
                    lstApps.Items.Add(exeName);
                    SaveApps();
                }
            }
        }
        
        private void LstApps_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                BtnRemoveApp_Click(sender, e);
            }
        }

        private void BtnClearApps_Click(object sender, RoutedEventArgs e)
        {
            lstApps.Items.Clear();
            SaveApps();
        }

        private void BtnRemoveApp_Click(object sender, RoutedEventArgs e)
        {
            if (lstApps.SelectedIndex != -1)
            {
                var selectedItems = lstApps.SelectedItems.Cast<string>().ToList();
                foreach (var item in selectedItems)
                {
                    lstApps.Items.Remove(item);
                }
                SaveApps();
            }
        }



        private void BtnBrowseNitroApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new AppPickerWindow();
            picker.Owner = this;
            if (picker.ShowDialog() == true && picker.SelectedApp != null)
            {
                var newApp = new NitroApp { Name = picker.SelectedApp.Name, ExeName = picker.SelectedApp.ExeName };
                if (!_vpnService.NitroService.Apps.Contains(newApp))
                {
                    _vpnService.NitroService.Apps.Add(newApp);
                }
            }
        }

        private void BtnRemoveNitroApp_Click(object sender, RoutedEventArgs e)
        {
            if (lstNitroApps.SelectedIndex != -1)
            {
                var selectedItems = lstNitroApps.SelectedItems.Cast<NitroApp>().ToList();
                foreach (var item in selectedItems)
                {
                    _vpnService.NitroService.Apps.Remove(item);
                }
            }
        }

        private void SaveApps()
        {
            try
            {
                var apps = lstApps.Items.Cast<string>().ToList();
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "apps.txt");
                System.IO.File.WriteAllLines(path, apps);
            }
            catch { }
        }

        private void LoadApps()
        {
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "apps.txt");
                if (System.IO.File.Exists(path))
                {
                    var apps = System.IO.File.ReadAllLines(path);
                    foreach (var app in apps)
                    {
                        if (!string.IsNullOrWhiteSpace(app) && !lstApps.Items.Contains(app))
                        {
                            lstApps.Items.Add(app);
                        }
                    }
                }
            }
            catch { }
        }

        private async Task LoadServersAsync()
        {
            try
            {
                var spoofCountries = _apiManager.GetSupportedCountries();
                cmbSpoofingCountry.ItemsSource = spoofCountries;
                if (spoofCountries.Count > 0) cmbSpoofingCountry.SelectedIndex = 0;

                var servers = await _apiManager.GetServersAsync();
                _allServersCache.Clear();
                _allServersCache.AddRange(servers);
                ApplyServerFilter();
                txtStatus.Text = "Bağlantı Bekleniyor";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Yükleme hatası: " + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyServerFilter()
        {
            var filtered = _allServersCache.AsEnumerable();
            
            // Oyun modu filtresi
            if (toggleGameMode.IsChecked == true)
            {
                filtered = filtered.Where(x => x.UdpSupported);
            }

            // Favorileri üste taşı
            filtered = filtered.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.CountryName);
            var filteredList = filtered.ToList();

            // Her iki ComboBox'ı da güncelle
            if (cmbServers.SelectedItem is Core.Server currentSelection)
            {
                cmbServers.ItemsSource = filteredList;
                cmbServersApps.ItemsSource = filteredList;
                
                var matching = filteredList.FirstOrDefault(x => x.Id == currentSelection.Id);
                cmbServers.SelectedItem = matching ?? (filteredList.Count > 0 ? filteredList[0] : null);
                cmbServersApps.SelectedItem = cmbServers.SelectedItem;
            }
            else
            {
                cmbServers.ItemsSource = filteredList;
                cmbServersApps.ItemsSource = filteredList;

                if (filteredList.Count > 0)
                {
                    cmbServers.SelectedIndex = 0;
                    cmbServersApps.SelectedIndex = 0;
                }
            }
        }

        private void BtnConnectSystem_Click(object sender, RoutedEventArgs e) { ConnectViaMode(false); }
        private void BtnConnectApp_Click(object sender, RoutedEventArgs e) { ConnectViaMode(true); }

        private void ConnectViaMode(bool isAppMode)
        {
            var activeCombo = isAppMode ? cmbServersApps : cmbServers;

            if (activeCombo.SelectedItem == null && !isConnected)
            {
                MessageBox.Show("Lütfen bağlanmadan önce bir sunucu seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

             if (!isConnected)
            {
                if (activeCombo.SelectedItem is not Core.Server selectedServer) return;

                
                if (toggleSpoofing.IsChecked == true && cmbSpoofingCountry.SelectedItem is Models.Country spoofCountry)
                {
                    Spoofing.RegistryManager.BackupOriginalSettings();
                    Spoofing.RegistryManager.SpoofSettings(spoofCountry.TimeZone, spoofCountry.GeoId, spoofCountry.Locale);
                    Spoofing.BrowserPolicyManager.EnablePolicies("127.0.0.1:2080");
                    Spoofing.ProxyManager.EnableSystemProxy("127.0.0.1:2080");
                }

                // Uygulama Listesini Virgüllü String Yap
                string processFilters = "";
                if (isAppMode && lstApps.Items.Count > 0)
                {
                    var apps = lstApps.Items.Cast<string>().ToList();
                    processFilters = string.Join(",", apps);
                }
                else if (isAppMode && lstApps.Items.Count == 0)
                {
                    MessageBox.Show("Lütfen en az bir uygulama seçin (Gözat) veya Sisteme Bağla'yı kullanın.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    return;
                }

                _vpnService.Connect(selectedServer, toggleGameMode.IsChecked == true, processFilters);

                isConnected = true;
                _startTime = DateTime.Now;
                _timer.Start();
                
                if (isAppMode)
                {
                    btnConnectApp.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                    txtBtnAppText.Text = "BAĞLANTIYI KES";
                    btnConnectSystem.IsEnabled = false; // Diğer butonu kapat
                }
                else
                {
                    btnConnectSystem.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                    txtBtnSysText.Text = "BAĞLANTIYI KES";
                    btnConnectApp.IsEnabled = false; // Diğer butonu kapat
                }
                
                txtStatus.Text = isAppMode ? "🛡️ Sadece Seçili Uygulamalar Korunuyor" : "🛡️ Tüm Sistem Korunuyor";
                txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                
                cmbServers.IsEnabled = false;
                cmbServersApps.IsEnabled = false;
                toggleGameMode.IsEnabled = false;
                toggleSpoofing.IsEnabled = false;
                cmbSpoofingCountry.IsEnabled = false;
                lstApps.IsEnabled = false;
                btnBrowseApp.IsEnabled = false;
                btnClearApps.IsEnabled = false;
            }
            else
            {
                Disconnect();
            }
        }

        private void Disconnect()
        {
            // _timer.Stop(); // Oturum varken timer durmamalı
            isConnected = false;
            isNitroActive = false;
            
            _vpnService.Disconnect();
            
            // Failsafe: VPN servisinin içinde olmayan manuel temizlikleri de yap
            Spoofing.RegistryManager.RestoreOriginalSettings();
            Spoofing.BrowserPolicyManager.DisablePolicies();
            Spoofing.ProxyManager.DisableSystemProxy();
            Spoofing.DnsManager.ResetToDhcp();
            
            ResetUIState();
        }

        private void BtnConnectNitro_Click(object sender, RoutedEventArgs e)
        {
            if (!isNitroActive)
            {
                // Nitro Geçit Hattı Başlat
                _vpnService.ConnectNitro();
                
                isNitroActive = true;
                isConnected = false;

                txtStatus.Text = (string)Application.Current.Resources["NitroStatusActive"];
                txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6F42C1"));

                btnConnectNitro.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                txtBtnNitroText.Text = (string)Application.Current.Resources["BtnNitroStop"];

                btnConnectSystem.IsEnabled = false;
                btnConnectApp.IsEnabled = false;
                
                lstNitroApps.IsEnabled = false;
                btnBrowseNitroApp.IsEnabled = false;
                btnRemoveNitroApp.IsEnabled = false;
            }
            else
            {
                Disconnect();
            }
        }

        private void BtnConnectGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var gameMode = GameMode.GameModeManager.Instance;
                if (!gameMode.IsRunning)
                {
                    gameMode.Start();
                    btnConnectGame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                    txtBtnGameText.Text = "OYUN MODUNU DURDUR";
                    txtStatus.Text = "🎮 Oyun Modu Aktif (IP Fragmented)";
                    txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3D00"));
                }
                else
                {
                    gameMode.Stop();
                    btnConnectGame.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF3D00"));
                    txtBtnGameText.Text = "OYUN MODUNU BAŞLAT";
                    txtStatus.Text = isConnected ? "🛡️ Sistem Korunuyor" : "Bağlantı Bekleniyor";
                    txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Oyun Modu Başlatılamadı:\n" + ex.Message, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ======== GÜNCELLEME SİSTEMİ ========

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                // Güncelleme overlay'ini göster
                updateOverlay.Visibility = Visibility.Visible;
                txtUpdateStatus.Text = "Güncelleme kontrol ediliyor...";
                txtUpdateVersion.Text = $"Mevcut sürüm: {Core.UpdateManager.GetCurrentVersion()}";
                updateProgressFill.Width = 0;
                btnSkipUpdate.Visibility = Visibility.Collapsed;

                // GitHub'dan versiyon bilgisini çek
                var updateInfo = await _updateManager.CheckForUpdateAsync();

                if (updateInfo == null)
                {
                    // İnternet yok veya hata — güncelleme atla
                    txtUpdateStatus.Text = "Güncelleme kontrolü başarısız, devam ediliyor...";
                    txtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107"));
                    btnSkipUpdate.Visibility = Visibility.Visible;
                    await Task.Delay(2000);
                    SkipUpdate();
                    return;
                }

                if (_updateManager.IsUpdateRequired(updateInfo))
                {
                    // Güncelleme gerekli!
                    txtUpdateVersion.Text = $"Mevcut: {Core.UpdateManager.GetCurrentVersion()} → Yeni: {updateInfo.Version}";
                    txtUpdateStatus.Text = "Yeni sürüm bulundu! İndirme başlıyor...";
                    txtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));

                    if (!string.IsNullOrEmpty(updateInfo.ReleaseNotes))
                    {
                        txtReleaseNotes.Text = updateInfo.ReleaseNotes;
                        txtReleaseNotes.Visibility = Visibility.Visible;
                    }

                    // Event handler'ları bağla
                    _updateManager.StatusChanged += (status) =>
                    {
                        Dispatcher.Invoke(() => txtUpdateStatus.Text = status);
                    };

                    _updateManager.ProgressChanged += (percent) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Progress bar genişliğini hesapla (parent border ~420px usable width)
                            double maxWidth = 420;
                            updateProgressFill.Width = (percent / 100.0) * maxWidth;
                            txtUpdatePercent.Text = $"%{percent}";
                        });
                    };

                    // İndirme ve yükleme
                    bool success = await _updateManager.DownloadAndInstallAsync(updateInfo);

                    if (success)
                    {
                        txtUpdateStatus.Text = "Yükleme başlatıldı! Uygulama kapanıyor...";
                        txtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                        await Task.Delay(2000);
                        Application.Current.Shutdown();
                        return;
                    }
                    else
                    {
                        // İndirme başarısız
                        txtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545"));
                        btnSkipUpdate.Visibility = Visibility.Visible;
                        btnSkipUpdate.Content = "Güncelleme Atla";
                    }
                }
                else
                {
                    // Uygulama güncel
                    txtUpdateStatus.Text = "✓ Uygulama güncel!";
                    txtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#198754"));
                    await Task.Delay(1200);
                    SkipUpdate();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UPDATE-UI] Hata: {ex.Message}");
                txtUpdateStatus.Text = "Güncelleme kontrolü başarısız.";
                txtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFC107"));
                btnSkipUpdate.Visibility = Visibility.Visible;
                await Task.Delay(2000);
                SkipUpdate();
            }
        }

        private void SkipUpdate()
        {
            updateOverlay.Visibility = Visibility.Collapsed;
            CheckSessionState();
        }

        private void BtnSkipUpdate_Click(object sender, RoutedEventArgs e)
        {
            SkipUpdate();
        }

    }
}