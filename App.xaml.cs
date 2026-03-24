using System;
using System.IO;
using System.Windows;

namespace TruvaDesktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show("HATA YAKALANDI:\n" + args.Exception.Message + "\n\n" + args.Exception.StackTrace, "Kritik Hata");
                args.Handled = true;
            };

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            base.OnStartup(e);

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Spoofing.ProxyManager.DisableSystemProxy();
            base.OnExit(e);
        }
    }
}
