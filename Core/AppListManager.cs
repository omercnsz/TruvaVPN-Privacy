using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TruvaDesktop.Core
{
    public class AppItem
    {
        public string Name { get; set; } = string.Empty;
        public string ExeName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public ImageSource? Icon { get; set; }
    }

    public static class AppListManager
    {
        public static List<AppItem> GetInstalledApps()
        {
            var apps = new List<AppItem>();
            var seenExes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] searchPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic? shell = null;
            if (shellType != null)
                shell = Activator.CreateInstance(shellType);

            foreach (var path in searchPaths)
            {
                    foreach (var lnk in SafeGetFiles(path, "*.lnk"))
                    {
                        try
                        {
                            string targetPath = string.Empty;
                            if (shell != null)
                            {
                                dynamic shortcut = shell.CreateShortcut(lnk);
                                targetPath = shortcut.TargetPath;
                            }

                            if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath) || !targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                continue;

                            AddAppIfNew(apps, seenExes, Path.GetFileNameWithoutExtension(lnk), targetPath);
                        }
                        catch { }
                    }
            }

            // --- EK OLARAK: Kayıt Defterinden Yüklü Programları Tara ---
            ScanRegistryApps(apps, seenExes);

            // --- ÖZEL TARAMA: Discord vb. AppData içinde saklananlar ---
            ScanSpecialApps(apps, seenExes);

            // Alfabetik sırala (Tekrarları temizleyerek)
            return apps.GroupBy(x => x.ExeName.ToLower()).Select(g => g.First()).OrderBy(a => a.Name).ToList();
        }

        private static void ScanSpecialApps(List<AppItem> apps, HashSet<string> seenExes)
        {
            try
            {
                // Discord Genelde %LocalAppData%\Discord\app-x.y.z\Discord.exe içindedir
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string discordPath = Path.Combine(localAppData, "Discord");
                
                if (Directory.Exists(discordPath))
                {
                    // En derinlerdeki Discord.exe'yi güvenli bir şekilde bul
                    var files = SafeGetFiles(discordPath, "Discord.exe");
                    var bestDiscord = files.OrderByDescending(f => f.Length).FirstOrDefault();
                    if (bestDiscord != null)
                    {
                        AddAppIfNew(apps, seenExes, "Discord", bestDiscord);
                    }
                }
            }
            catch { }
        }

        private static void AddAppIfNew(List<AppItem> apps, HashSet<string> seenExes, string friendlyName, string fullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return;
                string exeName = Path.GetFileName(fullPath);

                // Gereksiz sistem araçlarını ele
                if (exeName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                    exeName.Contains("update", StringComparison.OrdinalIgnoreCase) ||
                    exeName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                    exeName.Contains("helper", StringComparison.OrdinalIgnoreCase) ||
                    exeName.StartsWith("vc_redist") ||
                    exeName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                    exeName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!seenExes.Contains(exeName.ToLower()))
                {
                    seenExes.Add(exeName.ToLower());
                    apps.Add(new AppItem
                    {
                        Name = friendlyName,
                        ExeName = exeName,
                        FullPath = fullPath,
                        Icon = GetIcon(fullPath)
                    });
                }
            }
            catch { }
        }

        private static void ScanRegistryApps(List<AppItem> apps, HashSet<string> seenExes)
        {
            string[] registryPaths = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var regPipe in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                foreach (var regPath in registryPaths)
                {
                    try
                    {
                        using var key = regPipe.OpenSubKey(regPath);
                        if (key == null) continue;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey == null) continue;

                            var displayName = subKey.GetValue("DisplayName")?.ToString();
                            var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                            var displayIcon = subKey.GetValue("DisplayIcon")?.ToString();

                            if (string.IsNullOrEmpty(displayName)) continue;

                            // Eğer InstallLocation varsa içindeki ilk EXE'yi bulmaya çalışalım
                            if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                            {
                                var firstExe = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                if (firstExe != null)
                                {
                                    AddAppIfNew(apps, seenExes, displayName, firstExe);
                                }
                            }
                            // DisplayIcon bazen doğrudan EXE yolu olur
                            else if (!string.IsNullOrEmpty(displayIcon))
                            {
                                var iconPath = displayIcon.Split(',')[0].Trim('"');
                                if (File.Exists(iconPath) && iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    AddAppIfNew(apps, seenExes, displayName, iconPath);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private static List<string> SafeGetFiles(string path, string searchPattern)
        {
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly));
                foreach (var directory in Directory.GetDirectories(path))
                {
                    files.AddRange(SafeGetFiles(directory, searchPattern));
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception) { }
            return files;
        }

        private static ImageSource? GetIcon(string filePath)
        {
            try
            {
                using (Icon? sysicon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    if (sysicon != null)
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            sysicon.Handle,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bitmapSource.Freeze(); // Diğer thread'lerden erişim için dondur
                        return bitmapSource;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
