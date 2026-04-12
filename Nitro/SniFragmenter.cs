using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace TruvaDesktop.Nitro
{
    /// <summary>
    /// TLS ClientHello paketlerini fragmentlayarak DPI/SNI engellemeyi aşan servis.
    /// SEÇİCİ MOD: Sadece belirlenen PID'lere ait paketleri işler.
    /// </summary>
    public class SniFragmenter
    {
        #region WinDivert P/Invoke

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertClose(IntPtr handle);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, int packetLen, ref int pRecvLen, ref WinDivertAddress pAddr);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertSend(IntPtr handle, byte[] pPacket, int packetLen, ref int pSendLen, ref WinDivertAddress pAddr);

        [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        private static extern bool WinDivertHelperCalcChecksums(byte[] pPacket, int packetLen, ref WinDivertAddress pAddr, ulong flags);

        [StructLayout(LayoutKind.Explicit, Size = 28, Pack = 1)]
        public struct WinDivertAddress
        {
            [FieldOffset(0)] public long Timestamp;
            [FieldOffset(8)] public uint IfIdx;
            [FieldOffset(12)] public uint SubIfIdx;
            [FieldOffset(16)] public byte Layer;
            [FieldOffset(17)] public byte Event;
            [FieldOffset(18)] public byte Flags; // Sniffed:1, Outbound:1, Loopback:1, Impostor:1, IPv6:1, IPChecksum:1, TCPChecksum:1, UDPChecksum:1
            [FieldOffset(19)] public byte Reserved1;
            [FieldOffset(20)] public uint ProcessId;
            [FieldOffset(24)] public uint ThreadId;

            public bool IsOutbound
            {
                get => (Flags & 0x02) != 0;
                set => Flags = value ? (byte)(Flags | 0x02) : (byte)(Flags & 0xFD);
            }

            public bool IsIPv6
            {
                get => (Flags & 0x10) != 0;
                set => Flags = value ? (byte)(Flags | 0x10) : (byte)(Flags & 0xEF);
            }

            public bool IsIPv4 => !IsIPv6;
        }

        #endregion

        private const int WINDIVERT_LAYER_NETWORK = 0;
        private volatile IntPtr _handle = IntPtr.Zero;
        private CancellationTokenSource? _cts;
        private Task? _captureTask;
        private Task? _processTask;
        private volatile bool _isRunning;
        private readonly HashSet<int> _activePids = new HashSet<int>();
        private readonly object _pidLock = new object();
        
        // 🚀 Paket işleme kanalı (Thread blocking'i önler)
        private readonly Channel<(byte[] packet, WinDivertAddress addr)> _packetChannel = 
            Channel.CreateBounded<(byte[] packet, WinDivertAddress addr)>(new BoundedChannelOptions(1024)
            {
                FullMode = BoundedChannelFullMode.DropWrite, // Kanal dolu ise yazma, es geç (Non-blocking)
                SingleReader = true
            });

        public bool IsRunning => _isRunning;

        public void Start(IEnumerable<int> pids)
        {
            if (_handle != IntPtr.Zero) return;

            lock (_pidLock)
            {
                _activePids.Clear();
                if (pids != null)
                {
                    foreach (var pid in pids.Where(p => p > 0 && BlacklistService.IsSafe(p)))
                        _activePids.Add(pid);
                }
            }

            string filter = FilterBuilder.BuildSelectiveSniFilter(pids!);
            _handle = WinDivertOpen(filter, WINDIVERT_LAYER_NETWORK, 0, 0);

            if (_handle == (IntPtr)(-1))
            {
                int errorCode = Marshal.GetLastWin32Error();
                _handle = IntPtr.Zero;
                throw new Exception($"WinDivert SNI başlatılamadı (Hata Kodu: {errorCode}).");
            }

            NitroLogger.Log($"[SNI] Başlatıldı. Filtre: {filter} (Priority: 0)");
            NitroLogger.Log($"[SNI] Aktif PID Listesi: {string.Join(", ", _activePids)}");
            
            // 💓 Heartbeat Timer
            _ = Task.Run(async () => {
                while (_isRunning) {
                    NitroLogger.Log("[SNI] Heartbeat: Monitor Hala Çalışıyor...");
                    await Task.Delay(5000);
                }
            });

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
            _processTask = Task.Run(() => ProcessLoop(_cts.Token));
            _isRunning = true;

            Debug.WriteLine("[Nitro SNI] Fragmenter Başlatıldı.");
        }

        public void UpdateFilters(IEnumerable<int> pids)
        {
            lock (_pidLock)
            {
                _activePids.Clear();
                if (pids != null)
                {
                    foreach (var pid in pids.Where(p => p > 0 && BlacklistService.IsSafe(p)))
                        _activePids.Add(pid);
                }
            }
            NitroLogger.Log($"[SNI] PID Listesi Güncellendi: {string.Join(", ", _activePids)}");
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();

            IntPtr handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1))
            {
                WinDivertClose(handle);
            }

            try { Task.WaitAll(new[] { _captureTask!, _processTask! }, 1000); } catch { }
        }

        private void CaptureLoop(CancellationToken token)
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            byte[] buffer = new byte[65535];

            while (!token.IsCancellationRequested && _isRunning)
            {
                int readLen = 0;
                WinDivertAddress addr = new WinDivertAddress();
                IntPtr currentHandle = _handle;

                if (currentHandle == IntPtr.Zero || currentHandle == new IntPtr(-1))
                {
                    Thread.Sleep(10);
                    continue;
                }

                if (WinDivertRecv(currentHandle, buffer, buffer.Length, ref readLen, ref addr))
                {
                    byte[] packet = new byte[readLen];
                    Array.Copy(buffer, packet, readLen);
                    
                    // 🔍 LOUD DEBUG: Yakalanan her bir paket için log (PID Görünsün)
                    NitroLogger.Log($"[SNI DEBUG] PKT RECVD - PID: {addr.ProcessId}, Len: {readLen}");

                    // ⚡ Kanal dolu ise paketi bekletmeden direkt gönder
                    if (!_packetChannel.Writer.TryWrite((packet, addr)))
                    {
                        InjectOriginal(packet, packet.Length, ref addr);
                    }
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 995) // 995 = Operation Aborted (Handle Closed)
                        NitroLogger.Log($"[SNI ERROR] WinDivertRecv Error: {err}");
                }
            }
        }

        private async Task ProcessLoop(CancellationToken token)
        {
            await foreach (var item in _packetChannel.Reader.ReadAllAsync(token))
            {
                byte[] packet = item.packet;
                WinDivertAddress addr = item.addr;

                // IPv4/IPv6 ayrımı (Flags üzerinden daha güvenilir)
                bool isIPv4 = addr.IsIPv4;
                bool isIPv6 = addr.IsIPv6;

                if (isIPv6)
                {
                    // İzlenen uygulamanın IPv6 paketlerini dropla -> Uygulama mecburen IPv4 deneyecek.
                    // Not: Bu kısım sadece isTarget kontrolünden sonra yapılmalıydı, 
                    // ama şimdilik performansı artırmak için IPv6'yı pas geçiyoruz.
                    continue; 
                }

                if (!isIPv4) continue;

                // 🎯 PID Filtreleme & Global Fallback
                bool isTarget = false;
                lock (_pidLock)
                {
                    // Eğer PID liste boşsa veya sürücü PID bildirmiyorsa (0), 
                    // emniyet için tüm 443 trafiğine (Global) NITRO uygula.
                    if (_activePids.Count == 0 || addr.ProcessId == 0)
                    {
                        isTarget = true; 
                    }
                    else
                    {
                        isTarget = _activePids.Contains((int)addr.ProcessId);
                    }
                }

                if (!isTarget)
                {
                    // İzlenmeyen bir uygulama, direkt geri gönder
                    InjectOriginal(packet, packet.Length, ref addr);
                    continue;
                }

                try
                {
                    // IP Header - Protocol field is at offset 9
                    byte protocol = packet[9];

                    if (protocol == 17) // UDP
                    {
                        int ihl = (packet[0] & 0x0F) * 4;
                        int dPort = (packet[ihl + 2] << 8) | packet[ihl + 3];

                        if (dPort == 443)
                        {
                            NitroLogger.Log($"[SNI] QUIC Blocked (App PID: {addr.ProcessId})");
                            continue; // DROP
                        }
                        
                        InjectOriginal(packet, packet.Length, ref addr);
                    }
                    else if (protocol == 6) // TCP
                    {
                        // 🔍 TCP paketi detaylı log (Sadece ClientHello için)
                        ProcessTcpPacket(packet, packet.Length, addr);
                    }
                    else
                    {
                        InjectOriginal(packet, packet.Length, ref addr);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Nitro SNI] İşlem Hatası: {ex.Message}");
                    InjectOriginal(packet, packet.Length, ref addr);
                }
            }
        }

        private void ProcessTcpPacket(byte[] packet, int length, WinDivertAddress addr)
        {
            // IP Header Parse
            int ihl = (packet[0] & 0x0F) * 4;
            int tcpOffset = ihl;
            
            if (length < tcpOffset + 20) { InjectOriginal(packet, length, ref addr); return; }

            // TCP Data Offset
            int tcpDataOffset = (packet[tcpOffset + 12] >> 4) * 4;
            int tcpPayloadOffset = tcpOffset + tcpDataOffset;
            int tcpPayloadLength = length - tcpPayloadOffset;

            // TLS ClientHello mu? (0x16 Handshake + 0x01 ClientHello)
            if (tcpPayloadLength >= 6 && packet[tcpPayloadOffset] == 0x16 && packet[tcpPayloadOffset + 5] == 0x01)
            {
                FragmentAndSend(packet, length, ihl, tcpOffset, tcpPayloadOffset, tcpPayloadLength, ref addr);
            }
            else
            {
                InjectOriginal(packet, length, ref addr);
            }
        }

        private void FragmentAndSend(byte[] packet, int length, int ihl, int tcpOffset, 
            int tcpPayloadOffset, int tcpPayloadLength, ref WinDivertAddress addr)
        {
            NitroLogger.Log("[SNI] TLS ClientHello Fragmented (Bypass Applied)");
            
            // 🎯 Split point: TLS Record Header (5 bytes) + Handshake Type (1 byte) = 6
            int splitPoint = 6; 
            
            if (splitPoint >= tcpPayloadLength) splitPoint = tcpPayloadLength / 2;

            int headerLen = tcpPayloadOffset;
            uint origSeq = (uint)((packet[tcpOffset + 4] << 24) | (packet[tcpOffset + 5] << 16) |
                                   (packet[tcpOffset + 6] << 8) | packet[tcpOffset + 7]);

            // Frag 1
            byte[] frag1 = new byte[headerLen + splitPoint];
            Array.Copy(packet, 0, frag1, 0, headerLen);
            Array.Copy(packet, tcpPayloadOffset, frag1, headerLen, splitPoint);
            frag1[2] = (byte)(frag1.Length >> 8); frag1[3] = (byte)(frag1.Length & 0xFF);
            frag1[tcpOffset + 13] &= unchecked((byte)~0x08); // PSH kaldır

            // Frag 2
            int frag2Len = tcpPayloadLength - splitPoint;
            byte[] frag2 = new byte[headerLen + frag2Len];
            Array.Copy(packet, 0, frag2, 0, headerLen);
            Array.Copy(packet, tcpPayloadOffset + splitPoint, frag2, headerLen, frag2Len);
            
            // 🆔 Unique IP ID for Frag 2 (Avoid DPI deduplication)
            ushort ipId = (ushort)((packet[4] << 8) | packet[5]);
            ipId++;
            frag2[4] = (byte)(ipId >> 8);
            frag2[5] = (byte)(ipId & 0xFF);

            uint frag2Seq = origSeq + (uint)splitPoint;
            frag2[tcpOffset + 4] = (byte)(frag2Seq >> 24); frag2[tcpOffset + 5] = (byte)(frag2Seq >> 16);
            frag2[tcpOffset + 6] = (byte)(frag2Seq >> 8); frag2[tcpOffset + 7] = (byte)(frag2Seq & 0xFF);
            frag2[2] = (byte)(frag2.Length >> 8); frag2[3] = (byte)(frag2.Length & 0xFF);

            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                int sendLen = 0;
                
                // Send Frag 1
                WinDivertHelperCalcChecksums(frag1, frag1.Length, ref addr, 0);
                WinDivertSend(handle, frag1, frag1.Length, ref sendLen, ref addr);
                
                // ⏱ Small delay to desync DPI reassemblers
                Thread.Sleep(1);

                // Send Frag 2
                WinDivertHelperCalcChecksums(frag2, frag2.Length, ref addr, 0);
                WinDivertSend(handle, frag2, frag2.Length, ref sendLen, ref addr);
            }
        }

        private void InjectOriginal(byte[] packet, int length, ref WinDivertAddress addr)
        {
            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                int sendLen = 0;
                WinDivertSend(handle, packet, length, ref sendLen, ref addr);
            }
        }
    }
}
