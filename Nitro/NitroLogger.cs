using System;
using System.IO;
using System.Diagnostics;

namespace TruvaDesktop.Nitro
{
    public static class NitroLogger
    {
        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "nitro_debug.log");
        private static readonly object LogLock = new object();

        public static bool IsEnabled { get; set; } = false;

        public static void Log(string message)
        {
            if (!IsEnabled) return;
            try
            {
                lock (LogLock)
                {
                    string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, logLine);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NitroLogger] Write failed: {ex.Message}");
            }
        }

        public static void Clear()
        {
            try
            {
                lock (LogLock)
                {
                    if (File.Exists(LogPath))
                        File.Delete(LogPath);
                }
            }
            catch { }
        }
    }
}
