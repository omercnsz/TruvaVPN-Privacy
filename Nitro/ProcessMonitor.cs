using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Threading;

namespace TruvaDesktop.Nitro
{
    /// <summary>
    /// Seçili uygulamaların PID'lerini (Process ID) gerçek zamanlı izleyen servis.
    /// WMI Eventleri (Win32_ProcessStartTrace) kullanarak sıfır gecikmeli tespit sağlar.
    /// </summary>
    public class ProcessMonitor : IDisposable
    {
        private readonly List<string> _targetProcessNames;
        private readonly Dictionary<string, List<int>> _activePids = new();
        private ManagementEventWatcher? _startWatcher;
        private ManagementEventWatcher? _stopWatcher;
        private bool _isRunning;

        public event Action<List<int>>? OnPidListChanged;

        public ProcessMonitor(IEnumerable<string> targetAppNames)
        {
            _targetProcessNames = targetAppNames
                .Select(n => n.ToLower().Replace(".exe", ""))
                .ToList();
        }

        public void Start()
        {
            if (_isRunning) return;

            if (!IsRunningAsAdmin())
            {
                throw new UnauthorizedAccessException("Nitro modu (ProcessMonitor) yönetici yetkisi gerektirir.");
            }

            // Mevcut süreçleri tara
            InitialScan();

            try
            {
                // 🚀 WMI Event: Yeni süreç başladığında yakala
                string startQuery = "SELECT * FROM Win32_ProcessStartTrace";
                _startWatcher = new ManagementEventWatcher(new WqlEventQuery(startQuery));
                _startWatcher.EventArrived += (s, e) => HandleProcessEvent(e, true);
                _startWatcher.Start();

                // 🛑 WMI Event: Süreç kapandığında yakala
                string stopQuery = "SELECT * FROM Win32_ProcessStopTrace";
                _stopWatcher = new ManagementEventWatcher(new WqlEventQuery(stopQuery));
                _stopWatcher.EventArrived += (s, e) => HandleProcessEvent(e, false);
                _stopWatcher.Start();

                _isRunning = true;
                NitroLogger.Log($"Nitro Monitor Başlatıldı. İzlenenler: {string.Join(", ", _targetProcessNames)}");
            }
            catch (Exception ex)
            {
                NitroLogger.Log($"ERR: WMI başlatma hatası: {ex.Message}");
                _isRunning = false;
                throw;
            }
        }

        // NitroLogger kullanıldığı için eski Log metodu kaldırıldı.

        public void Stop()
        {
            _isRunning = false;
            _startWatcher?.Stop();
            _startWatcher?.Dispose();
            _stopWatcher?.Stop();
            _stopWatcher?.Dispose();
        }

        public List<int> GetActivePids()
        {
            lock (_activePids)
            {
                return _activePids.Values.SelectMany(x => x).Distinct().ToList();
            }
        }

        private void InitialScan()
        {
            lock (_activePids)
            {
                _activePids.Clear();
                foreach (var targetName in _targetProcessNames)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(targetName);
                        _activePids[targetName] = processes.Select(p => p.Id).ToList();
                    }
                    catch { }
                }
            }
            NotifyChanged();
        }

        private void HandleProcessEvent(EventArrivedEventArgs e, bool isStart)
        {
            try
            {
                string processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
                int processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);

                string cleanName = processName.ToLower().Replace(".exe", "");

                if (_targetProcessNames.Contains(cleanName))
                {
                    lock (_activePids)
                    {
                        if (!_activePids.ContainsKey(cleanName)) _activePids[cleanName] = new List<int>();

                        if (isStart)
                        {
                            if (!_activePids[cleanName].Contains(processId))
                                _activePids[cleanName].Add(processId);
                        }
                        else
                        {
                            _activePids[cleanName].Remove(processId);
                        }
                    }
                    NitroLogger.Log($"Event: {processName} ({(isStart ? "START" : "STOP")}) PID: {processId}");
                    NotifyChanged();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nitro Monitor] Event işleme hatası: {ex.Message}");
            }
        }

        private void NotifyChanged()
        {
            OnPidListChanged?.Invoke(GetActivePids());
        }

        private static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
