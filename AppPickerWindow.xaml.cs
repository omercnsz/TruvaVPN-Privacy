using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace TruvaDesktop
{
    public partial class AppPickerWindow : Window
    {
        private List<Core.AppItem> _allApps = new List<Core.AppItem>();
        public Core.AppItem? SelectedApp { get; private set; }

        public AppPickerWindow()
        {
            InitializeComponent();
            LoadAppsAsync();
        }

        private async void LoadAppsAsync()
        {
            try
            {
                loadingPanel.Visibility = Visibility.Visible;
                lstInstalledApps.Visibility = Visibility.Collapsed;
                
                _allApps = await System.Threading.Tasks.Task.Run(() => Core.AppListManager.GetInstalledApps());
                lstInstalledApps.ItemsSource = _allApps;
            }
            finally
            {
                loadingPanel.Visibility = Visibility.Collapsed;
                lstInstalledApps.Visibility = Visibility.Visible;
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = txtSearch.Text.ToLower().Trim();
            if (string.IsNullOrEmpty(filter))
            {
                lstInstalledApps.ItemsSource = _allApps;
            }
            else
            {
                lstInstalledApps.ItemsSource = _allApps.Where(x => 
                    x.Name.ToLower().Contains(filter) || 
                    x.ExeName.ToLower().Contains(filter)).ToList();
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void LstInstalledApps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmSelection();
        }

        private void ConfirmSelection()
        {
            if (lstInstalledApps.SelectedItem is Core.AppItem app)
            {
                SelectedApp = app;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Lütfen VPN listesine eklemek için bir uygulama seçin.", "Seçim Yapılmadı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BtnFileBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".exe",
                Filter = "Uygulama Çalıştırılabilir Dosyası (.exe)|*.exe"
            };

            if (dlg.ShowDialog() == true)
            {
                SelectedApp = new Core.AppItem 
                { 
                    ExeName = System.IO.Path.GetFileName(dlg.SafeFileName), 
                    Name = System.IO.Path.GetFileNameWithoutExtension(dlg.SafeFileName) 
                };
                this.DialogResult = true;
                this.Close();
            }
        }
    }
}
