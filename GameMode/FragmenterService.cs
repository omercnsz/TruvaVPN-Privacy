using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TruvaDesktop.GameMode
{
    public class FragmenterService
    {
        // Simple P/Invoke for standard WinDivert
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

        [StructLayout(LayoutKind.Sequential)]
        public struct WinDivertAddress
        {
            public long Timestamp;
            public byte Layer;
            public byte Event;
            public byte Sniffed;
            public byte Outbound;
            public byte Loopback;
            public byte Impostor;
            public byte IPv6;
            public byte IPChecksum;
            public byte TCPChecksum;
            public byte UDPChecksum;
            public uint IfIdx;
            public uint SubIfIdx;
        }

        private IntPtr _divertHandle = IntPtr.Zero;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _divertTask;
        private bool _isRunning;

        // Constants
        private const int WINDIVERT_LAYER_NETWORK = 0;
        private const ulong WINDIVERT_FLAG_SNIFF = 1;
        private const ulong WINDIVERT_FLAG_DROP = 2; // Default is just inject, wait, standard open drops matched packets if we don't Sniff.
        
        public bool IsRunning => _isRunning;

        public void Start()
        {
            if (_isRunning) return;

            string filter = "outbound and ip and udp";
            // Normal open, packets matching the filter are dropped/diverted from the network stack to us.
            // We must inject them back using WinDivertSend.
            _divertHandle = WinDivertOpen(filter, WINDIVERT_LAYER_NETWORK, 0, 0);

            if (_divertHandle == IntPtr.Zero || _divertHandle == new IntPtr(-1))
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[GameMode] WinDivertOpen failed. Error: {error}");
                throw new Exception($"WinDivert başlatılamadı. Hata Kodu: {error}");
            }

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _divertTask = Task.Run(() => WorkerLoop(_cancellationTokenSource.Token), _cancellationTokenSource.Token);
            
            Debug.WriteLine("[GameMode] FragmenterService Started.");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            if (_divertHandle != IntPtr.Zero && _divertHandle != new IntPtr(-1))
            {
                WinDivertClose(_divertHandle);
                _divertHandle = IntPtr.Zero;
            }

            try
            {
                _divertTask?.Wait(1000);
            }
            catch (Exception ex)
            {
               Debug.WriteLine($"[GameMode] Hata (Stop): {ex.Message}");
            }
            
            Debug.WriteLine("[GameMode] FragmenterService Stopped.");
        }

        private void WorkerLoop(CancellationToken token)
        {
            int maxBufLen = 65535;
            byte[] packet = new byte[maxBufLen];

            while (!token.IsCancellationRequested && _isRunning)
            {
                int readLen = 0;
                WinDivertAddress addr = new WinDivertAddress();

                bool result = WinDivertRecv(_divertHandle, packet, packet.Length, ref readLen, ref addr);
                if (!result)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 995) // ERROR_IO_INCOMPLETE (Cancelled)
                        break;
                    continue; 
                }

                if (readLen > 0)
                {
                    try
                    {
                        ProcessAndInjectPacket(packet, readLen, ref addr);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[GameMode] ProcessPacket Hata: {ex.Message}");
                        // Hata olursa orijinal paketi göndererek veri kaybını önle
                        int sendLen = 0;
                        WinDivertSend(_divertHandle, packet, readLen, ref sendLen, ref addr);
                    }
                }
            }
        }

        private void ProcessAndInjectPacket(byte[] packet, int length, ref WinDivertAddress addr)
        {
            // IP Başlığını (IPv4) parse et
            if (length < 20)
            {
                InjectOriginal(packet, length, ref addr);
                return;
            }

            byte versionAndIhl = packet[0];
            int version = versionAndIhl >> 4;
            int ihl = (versionAndIhl & 0x0F) * 4;

            if (version != 4 || length < ihl + 8) // Sadece IPv4 ve UDP için
            {
                InjectOriginal(packet, length, ref addr);
                return;
            }

            int totalLength = (packet[2] << 8) | packet[3];
            
            if (totalLength > length)
                totalLength = length; // Güvenlik kontrolü

            // Fragmentation kontrolü
            int id = (packet[4] << 8) | packet[5];
            int flagsAndOffset = (packet[6] << 8) | packet[7];
            int fragmentOffset = flagsAndOffset & 0x1FFF;

            if (fragmentOffset > 0 || (flagsAndOffset & 0x2000) != 0)
            {
                // Zaten fragmante olmuş veya More Fragments ayarlı, dokunma.
                InjectOriginal(packet, length, ref addr);
                return;
            }

            int payloadLength = totalLength - ihl;
            if (payloadLength <= 8) // Asgari bölünecek boyut
            {
                InjectOriginal(packet, length, ref addr);
                return;
            }

            // Frag 1 (İlk Parça)
            // Normalde 8 byte katları şeklinde bölünmeli (IP parçalama kuralı)
            int frag1PayloadSize = ((payloadLength / 2) / 8) * 8; 
            if (frag1PayloadSize == 0) frag1PayloadSize = 8;
            int frag2PayloadSize = payloadLength - frag1PayloadSize;

            byte[] frag1 = new byte[ihl + frag1PayloadSize];
            byte[] frag2 = new byte[ihl + frag2PayloadSize];

            // Header kopyala
            Array.Copy(packet, 0, frag1, 0, ihl);
            Array.Copy(packet, 0, frag2, 0, ihl);

            // Payload kopyala
            Array.Copy(packet, ihl, frag1, ihl, frag1PayloadSize);
            Array.Copy(packet, ihl + frag1PayloadSize, frag2, ihl, frag2PayloadSize);

            // Frag 1 Header Güncelle
            int frag1TotalLen = ihl + frag1PayloadSize;
            frag1[2] = (byte)(frag1TotalLen >> 8);
            frag1[3] = (byte)(frag1TotalLen & 0xFF);
            // More Fragments (MF) = 1 -> Flag 0x2000
            int frag1Flags = 0x2000;
            frag1[6] = (byte)(frag1Flags >> 8);
            frag1[7] = (byte)(frag1Flags & 0xFF);

            // Frag 2 Header Güncelle
            int frag2TotalLen = ihl + frag2PayloadSize;
            frag2[2] = (byte)(frag2TotalLen >> 8);
            frag2[3] = (byte)(frag2TotalLen & 0xFF);
            // Fragment offset (8 byte biriminde)
            int offsetUnits = frag1PayloadSize / 8;
            frag2[6] = (byte)((offsetUnits >> 8) & 0x1F); // No MF flag
            frag2[7] = (byte)(offsetUnits & 0xFF);

            // Checksum Hesapla ve Gönder
            int sendLen = 0;
            WinDivertHelperCalcChecksums(frag1, frag1.Length, ref addr, 0);
            WinDivertSend(_divertHandle, frag1, frag1.Length, ref sendLen, ref addr);

            WinDivertHelperCalcChecksums(frag2, frag2.Length, ref addr, 0);
            WinDivertSend(_divertHandle, frag2, frag2.Length, ref sendLen, ref addr);
        }

        private void InjectOriginal(byte[] packet, int length, ref WinDivertAddress addr)
        {
            int sendLen = 0;
            WinDivertSend(_divertHandle, packet, length, ref sendLen, ref addr);
        }
    }
}
