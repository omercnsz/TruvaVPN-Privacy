using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Controls;
using TruvaDesktop.Core;
using System.Diagnostics;

namespace TruvaDesktop
{
    public partial class MainWindow : Window
    {
        private bool isConnected;
        private readonly Core.ApiManager _apiManager;
        private readonly Core.VpnService _vpnService;
        private readonly List<Core.Server> _allServersCache = [];
        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private DateTime _startTime;

        public MainWindow()
        {
            InitializeComponent();
            _apiManager = new Core.ApiManager();
            _vpnService = new Core.VpnService();
            InitializeStore();

            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;

            Spoofing.RegistryManager.InitializeFailsafe();
            LoadApps(); 
            _ = LoadServersAsync();

            this.Closing += (s, e) => 
            {
                if (isConnected) _vpnService.Disconnect();
                Spoofing.RegistryManager.RestoreOriginalSettings();
                Spoofing.BrowserPolicyManager.DisablePolicies();
                Spoofing.ProxyManager.DisableSystemProxy();
            };

            _vpnService.StatusChanged += (status) => 
            {
                Dispatcher.Invoke(() => {
                    // ANSI renk kodlarını temizle
                    string cleanStatus = AnsiRegex().Replace(status, "");

                    if (cleanStatus.StartsWith("[KOPARILDI]")) 
                    {
                        isConnected = false;
                        _timer.Stop();
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

        private bool _isPremium;

        private async void InitializeStore()
        {
            try
            {
                _isPremium = await StoreManager.Instance.IsUserSubscribedAsync();
                UpdatePremiumUi();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[STORE] Init hatası: {ex.Message}");
            }
        }

        private void UpdatePremiumUi()
        {
            Dispatcher.Invoke(() =>
            {
                btnPremium.Visibility = _isPremium ? Visibility.Collapsed : Visibility.Visible;
                badgePremium.Visibility = _isPremium ? Visibility.Visible : Visibility.Collapsed;
                premiumLockOverlay.Visibility = _isPremium ? Visibility.Collapsed : Visibility.Visible;
                
                if (_isPremium)
                {
                    txtStatus.Text = "PREMIUM AKTİF - Sınırsız Erişim";
                }
            });
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\x1B\[[^m]*m")]
        private static partial System.Text.RegularExpressions.Regex AnsiRegex();

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var duration = DateTime.Now - _startTime;
            txtTimer.Text = duration.ToString(@"hh\:mm\:ss");
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb)
            {
                paneConn.Visibility = Visibility.Collapsed;
                paneApps.Visibility = Visibility.Collapsed;
                paneSafety.Visibility = Visibility.Collapsed;

                if (rb.Name == "navConn") paneConn.Visibility = Visibility.Visible;
                else if (rb.Name == "navApps") paneApps.Visibility = Visibility.Visible;
                else if (rb.Name == "navSafety") paneSafety.Visibility = Visibility.Visible;
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
            txtBtnAppText.Text = "UYG. İLE BAĞLA";
            btnConnectApp.IsEnabled = true;

            cmbServers.IsEnabled = true;
            cmbServersApps.IsEnabled = true;
            toggleGameMode.IsEnabled = true;
            toggleSpoofing.IsEnabled = true;
            cmbSpoofingCountry.IsEnabled = true;
            lstApps.IsEnabled = true;
            btnBrowseApp.IsEnabled = true;
            btnClearApps.IsEnabled = true;
            
            txtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            txtStatus.Text = "Bağlantı Bekleniyor";
            txtTimer.Text = "00:00:00";
            _timer.Stop();
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
            if (e.Key == Key.Delete && lstApps.SelectedIndex != -1)
            {
                var selectedItems = lstApps.SelectedItems.Cast<string>().ToList();
                foreach (var item in selectedItems)
                {
                    lstApps.Items.Remove(item);
                }
                SaveApps();
            }
        }

        private void BtnClearApps_Click(object sender, RoutedEventArgs e)
        {
            lstApps.Items.Clear();
            SaveApps();
        }

        private void SaveApps()
        {
            try
            {
                var apps = lstApps.Items.Cast<string>().ToList();
                System.IO.File.WriteAllLines("apps.txt", apps);
            }
            catch { }
        }

        private void LoadApps()
        {
            try
            {
                if (System.IO.File.Exists("apps.txt"))
                {
                    var apps = System.IO.File.ReadAllLines("apps.txt");
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

                // Premium Kontrolü
                if (selectedServer.IsPremium && !_isPremium)
                {
                    MessageBoxResult res = MessageBox.Show(
                        $"{selectedServer.CountryName} sunucusu sadece Premium üyeler içindir.\n\nPremium'a geçerek yüksek hızlı sunuculara erişmek ister misiniz?", 
                        "Truva VPN Premium", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Information);

                    if (res == MessageBoxResult.Yes)
                    {
                        BtnPremium_Click(null!, null!);
                    }
                    return;
                }
                
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
            _timer.Stop();
            _vpnService.Disconnect();
            Spoofing.RegistryManager.RestoreOriginalSettings();
            Spoofing.BrowserPolicyManager.DisablePolicies();
            Spoofing.ProxyManager.DisableSystemProxy();
            isConnected = false;
            ResetUIState();
        }

        private async void BtnPremium_Click(object sender, RoutedEventArgs e)
        {
#if WINDOWS_STORE_SUPPORT
            try
            {
                var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var status = await StoreManager.Instance.PurchaseSubscriptionAsync(windowHandle);

                if (status is Windows.Services.Store.StorePurchaseStatus.Succeeded)
                {
                    _isPremium = true;
                    UpdatePremiumUi();
                    MessageBox.Show("Truva Premium üyeliğiniz aktif edildi!", "Tebrikler!", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (status is Windows.Services.Store.StorePurchaseStatus.AlreadyPurchased)
                {
                    _isPremium = true;
                    UpdatePremiumUi();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Satın alma hatası: {ex.Message}", "Mağaza Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
#else
            MessageBox.Show("Abonelik sistemi şu an geliştirme aşamasındadır (SDK yapılandırması gereklidir).", "Premium", MessageBoxButton.OK, MessageBoxImage.Information);
#endif
        }
    }
}