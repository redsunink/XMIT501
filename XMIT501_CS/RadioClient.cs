using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace XMIT501_CS
{
    public class RadioClient
    {
        private UdpClient _udpSocket;
        private IPEndPoint _serverEndPoint;
        private MainWindow _ui;
        private CancellationTokenSource _cts;

        // Fired when an Opus packet arrives from the server
        public event Action<byte, byte[]> OnAudioReceived;
        public event Action? OnPongReceived;
        private System.Diagnostics.Stopwatch _pingStopwatch = new System.Diagnostics.Stopwatch();
        public event Action<long>? OnPingUpdated;

        public RadioClient(MainWindow ui, string serverIp, int port)
        {
            _ui = ui;
            _udpSocket = new UdpClient(0); // 0 means bind to any available local port
            _serverEndPoint = new IPEndPoint(IPAddress.Parse(serverIp), port);
            _cts = new CancellationTokenSource();
            
            Task.Run(ReceiveLoop, _cts.Token);
            Task.Run(KeepAliveLoop, _cts.Token);
        }

        public void TransmitAudio(byte[] opusData)
        {
            // 1. OVERRIDE: If a specific channel hotkey is held, send ONLY to that channel
            if (_ui.ActiveOverrideChannel.HasValue)
            {
                byte channelId = _ui.ActiveOverrideChannel.Value;
                byte[] packet = new byte[opusData.Length + 1];
                packet[0] = channelId;
                Buffer.BlockCopy(opusData, 0, packet, 1, opusData.Length);
                
                _udpSocket.SendAsync(packet, packet.Length, _serverEndPoint);
            }
            // 2. GLOBAL PTT: Otherwise, send to all physically toggled TX switches
            else
            {
                foreach (byte channelId in _ui.ActiveTxChannels)
                {
                    byte[] packet = new byte[opusData.Length + 1];
                    packet[0] = channelId;
                    Buffer.BlockCopy(opusData, 0, packet, 1, opusData.Length);
                    
                    _udpSocket.SendAsync(packet, packet.Length, _serverEndPoint);
                }
            }
        }

        private async Task ReceiveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpSocket.ReceiveAsync();
                    byte[] packet = result.Buffer;

                    // Intercept a "Pong" from the server (a single byte with value 255) and fire an event
                    if (packet.Length == 1 && packet[0] == 255)
                    {
                        _pingStopwatch.Stop();
                        OnPingUpdated?.Invoke(_pingStopwatch.ElapsedMilliseconds);
                        OnPongReceived?.Invoke();
                        continue;
                    }
                    if (packet.Length > 1)
                    {
                        byte channelId = packet[0];
                        
                        // Strict Gate: Only pass it to the Audio Engine if our UI switch is ON
                        if (_ui.ActiveRxChannels.Contains(channelId))
                        {
                            byte[] opusData = new byte[packet.Length - 1];
                            Buffer.BlockCopy(packet, 1, opusData, 0, opusData.Length);
                            OnAudioReceived?.Invoke(channelId, opusData);
                        }
                    }
                }
                catch { /* Socket closed gracefully */ }
            }
        }

        private async Task KeepAliveLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                // Fire the Ping
                _pingStopwatch.Restart(); 
                await _udpSocket.SendAsync(new byte[] { 255 }, 1, _serverEndPoint);

                foreach (byte channelId in _ui.ActiveRxChannels)
                {
                    await _udpSocket.SendAsync(new byte[] { channelId }, 1, _serverEndPoint);
                }
                await Task.Delay(2000, _cts.Token);
            }
        }

        public void Shutdown()
        {
            _cts.Cancel();
            _udpSocket.Close();
        }
    }
}