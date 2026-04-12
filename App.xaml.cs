using System;
using System.IO;
using System.Windows;

namespace TruvaDesktop
{
    public partial class App : Application
    {
        private static System.Threading.Mutex? _mutex;
        private const string MutexName = "Global\\TruvaVPN_SingleInstance_Mutex_D37B2A91";

        protected override void OnStartup(StartupEventArgs e)
        {
            // Tekil oturum kontrolü (Mutex)
            _mutex = new System.Threading.Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // Uygulama zaten çalışıyor, uyar ve kapat
                MessageBox.Show("Truva VPN zaten çalışıyor. Lütfen sistem tepsisinden kontrol edin.", "Truva VPN");
                App.Current.Shutdown();
                return;
            }

            // Anti-Debug: Release modunda bir debugger varsa kapat
#if !DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                MessageBox.Show("Güvenlik ihlali: Hata ayıklayıcı (Debugger) algılandı. Uygulama kapatılıyor.", "Truva VPN Güvenlik");
                Environment.Exit(0);
                return;
            }
#endif

            this.DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show("HATA YAKALANDI:\n" + args.Exception.Message + "\n\n" + args.Exception.StackTrace, "Kritik Hata");
                args.Handled = true;
            };

            // Çalışma dizinini EXE'nin bulunduğu klasör olarak sabitle (Single-file extraction sorunlarını engelle)
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                Directory.SetCurrentDirectory(Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory);
            }
            else
            {
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
            }

            base.OnStartup(e);

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Failsafe: Tüm servisleri ve ayarları kapat
                var vpn = new Core.VpnService();
                vpn.Disconnect();
                
                Spoofing.RegistryManager.RestoreOriginalSettings();
                Spoofing.BrowserPolicyManager.DisablePolicies();
                Spoofing.ProxyManager.DisableSystemProxy();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
