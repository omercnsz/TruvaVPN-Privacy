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
    /// WinDivert ile DNS paketlerini yakalayıp DoH üzerinden çözümleyen servis.
    /// SEÇİCİ MOD: Sadece belirlenen PID'lerin (Discord vb.) DNS trafiğini şifreler.
    /// </summary>
    public class DnsInterceptor
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
        private readonly DoHResolver _resolver;
        private volatile bool _isRunning;

        // 🚀 DNS işlemleri için kanal (Async HTTP isteklerini kuyruğa alır)
        private readonly Channel<(byte[] packet, WinDivertAddress addr)> _dnsChannel = 
            Channel.CreateBounded<(byte[] packet, WinDivertAddress addr)>(new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropWrite, // Doluysa yazma (Non-blocking)
                SingleReader = true
            });

        public bool IsRunning => _isRunning;

        public DnsInterceptor(DoHResolver resolver)
        {
            _resolver = resolver;
        }

        public void Start()
        {
            if (_handle != IntPtr.Zero) return;

            string initialFilter = FilterBuilder.BuildGlobalDnsFilter();
            _handle = WinDivertOpen(initialFilter, WINDIVERT_LAYER_NETWORK, 10, 0);

            if (_handle == (IntPtr)(-1))
            {
                int errorCode = Marshal.GetLastWin32Error();
                _handle = IntPtr.Zero;
                throw new Exception($"WinDivert DNS başlatılamadı (Hata Kodu: {errorCode}).");
            }

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));
            _processTask = Task.Run(() => ProcessLoop(_cts.Token));
            _isRunning = true;
        }

        public void UpdateFilters(IEnumerable<int> pids)
        {
            // DNS artık global (tüm sistem için şifreli DoH). 
            // PID listesine göre filtre değiştirmeye gerek kalmadı.
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
                IntPtr handle = _handle;

                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) { Thread.Sleep(10); continue; }

                if (WinDivertRecv(handle, buffer, buffer.Length, ref readLen, ref addr))
                {
                    byte[] packet = new byte[readLen];
                    Array.Copy(buffer, packet, readLen);

                    // ⚡ Kanal dolu ise DNS'i çözmeye çalışma, orijinali hemen gönder
                    if (!_dnsChannel.Writer.TryWrite((packet, addr)))
                    {
                        ReInject(packet, packet.Length, ref addr);
                    }
                }
            }
        }

        private async Task ProcessLoop(CancellationToken token)
        {
            while (await _dnsChannel.Reader.WaitToReadAsync(token))
            {
                while (_dnsChannel.Reader.TryRead(out var item))
                {
                    // 🚀 Paralel İşleme: DNS çözümleme beklerken kuyruğu tıkama
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessDnsPacket(item.packet, item.addr);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Nitro DNS] İşlem Hatası: {ex.Message}");
                            ReInject(item.packet, item.packet.Length, ref item.addr);
                        }
                    }, token);
                }
            }
        }

        private async Task ProcessDnsPacket(byte[] packet, WinDivertAddress addr)
        {
            // IPv4/IPv6 ayrımı (Flags üzerinden daha güvenilir)
            bool isIPv4 = addr.IsIPv4;
            bool isIPv6 = addr.IsIPv6;

            if (isIPv6)
            {
                // IPv6 DNS isteklerini dropla -> Uygulama otomatik olarak IPv4 deneyecek.
                return; 
            }

            if (!isIPv4) return;

            int ihl = (packet[0] & 0x0F) * 4;
            int udpPayloadOffset = ihl + 8;
            int dnsLength = packet.Length - udpPayloadOffset;

            if (dnsLength <= 12) { ReInject(packet, packet.Length, ref addr); return; }

            byte[] dnsQuery = new byte[dnsLength];
            Array.Copy(packet, udpPayloadOffset, dnsQuery, 0, dnsLength);

            // 🚀 DNS Purify: AAAA (IPv6) sorgularını temizle
            if (IsAaaaQuery(dnsQuery))
            {
                byte[] emptyResponse = BuildEmptyDnsResponse(dnsQuery);
                byte[] respPacket = BuildResponsePacket(packet, ihl, emptyResponse);
                WinDivertAddress aaaaAddr = addr;
                aaaaAddr.IsOutbound = false;

                IntPtr aaaaHandle = _handle;
                if (aaaaHandle != IntPtr.Zero)
                {
                    int sLen = 0;
                    WinDivertHelperCalcChecksums(respPacket, respPacket.Length, ref aaaaAddr, 0);
                    WinDivertSend(aaaaHandle, respPacket, respPacket.Length, ref sLen, ref aaaaAddr);
                    NitroLogger.Log("[DNS] AAAA Query Filtered (Forced IPv4 Fallback)");
                }
                return;
            }

            // DoH üzerinden çözümle
            var dnsResponse = await _resolver.ResolveAsync(dnsQuery);

            if (dnsResponse == null || !_isRunning)
            {
                ReInject(packet, packet.Length, ref addr);
                return;
            }

            // Yanıt enjeksiyonu
            byte[] responsePacket = BuildResponsePacket(packet, ihl, dnsResponse);
            WinDivertAddress respAddr = addr;
            respAddr.IsOutbound = false; // INBOUND

            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                int sendLen = 0;
                WinDivertHelperCalcChecksums(responsePacket, responsePacket.Length, ref respAddr, 0);
                WinDivertSend(handle, responsePacket, responsePacket.Length, ref sendLen, ref respAddr);
            }
        }

        private byte[] BuildResponsePacket(byte[] originalPacket, int ihl, byte[] dnsResponse)
        {
            int totalLength = ihl + 8 + dnsResponse.Length;
            byte[] response = new byte[totalLength];
            Array.Copy(originalPacket, 0, response, 0, ihl);

            // Src-Dst IP Swap
            for (int i = 0; i < 4; i++) { response[12 + i] = originalPacket[16 + i]; response[16 + i] = originalPacket[12 + i]; }
            response[2] = (byte)(totalLength >> 8); response[3] = (byte)(totalLength & 0xFF);
            response[8] = 64; // TTL
            response[10] = 0; response[11] = 0;

            // UDP Header
            int udpOffset = ihl;
            response[udpOffset] = originalPacket[udpOffset + 2]; response[udpOffset + 1] = originalPacket[udpOffset + 3];
            response[udpOffset + 2] = originalPacket[udpOffset]; response[udpOffset + 3] = originalPacket[udpOffset + 1];
            int udpLength = 8 + dnsResponse.Length;
            response[udpOffset + 4] = (byte)(udpLength >> 8); response[udpOffset + 5] = (byte)(udpLength & 0xFF);
            response[udpOffset + 6] = 0; response[udpOffset + 7] = 0;

            Array.Copy(dnsResponse, 0, response, udpOffset + 8, dnsResponse.Length);
            return response;
        }

        private void ReInject(byte[] packet, int length, ref WinDivertAddress addr)
        {
            IntPtr handle = _handle;
            if (handle != IntPtr.Zero)
            {
                int sendLen = 0;
                WinDivertSend(handle, packet, length, ref sendLen, ref addr);
            }
        }

        private bool IsAaaaQuery(byte[] dnsData)
        {
            try
            {
                if (dnsData.Length < 15) return false;
                int pos = 12; // Header skip
                while (pos < dnsData.Length && dnsData[pos] != 0)
                {
                    pos += dnsData[pos] + 1;
                }
                pos++; // Null terminator skip
                if (pos + 4 > dnsData.Length) return false;

                ushort qType = (ushort)((dnsData[pos] << 8) | dnsData[pos + 1]);
                return qType == 28; // Type 28 = AAAA
            }
            catch { return false; }
        }

        private byte[] BuildEmptyDnsResponse(byte[] query)
        {
            byte[] response = new byte[query.Length];
            Array.Copy(query, 0, response, 0, query.Length);

            // Flags: Response, NoError, Recursion Desired/Available
            response[2] = 0x81;
            response[3] = 0x80;

            // Question Count = 1 (Aynen kalsın), Answer Count = 0
            response[6] = 0; response[7] = 0;
            response[8] = 0; response[9] = 0;
            response[10] = 0; response[11] = 0;

            return response;
        }
    }
}
