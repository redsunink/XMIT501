using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace XMIT501_Server
{
    public class RadioServer
    {
        private UdpClient _udpServer;
        private Dictionary<byte, HashSet<IPEndPoint>> _listeners = new Dictionary<byte, HashSet<IPEndPoint>>();
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public RadioServer(int port = 5000)
        {
            _udpServer = new UdpClient(port);
            Console.WriteLine($"[SERVER] Radio Repeater Live on Port {port}");
            Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Shutdown()
        {
            _cts.Cancel();
            _udpServer.Close();
            Console.WriteLine("[SERVER] Offline.");
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync();
                    byte[] packet = result.Buffer;

                    if (packet.Length > 0)
                    {
                        byte channelId = packet[0];

                        if (packet.Length == 1 && packet[0] == 255)
                        {
                            await _udpServer.SendAsync(new byte[] { 255 }, 1, result.RemoteEndPoint);
                            continue;
                        }

                        if (!_listeners.ContainsKey(channelId))
                            _listeners[channelId] = new HashSet<IPEndPoint>();

                        _listeners[channelId].Add(result.RemoteEndPoint);

                        if (packet.Length > 1)
                        {
                            foreach (var listener in _listeners[channelId])
                            {
                                if (!listener.Equals(result.RemoteEndPoint))
                                    await _udpServer.SendAsync(packet, packet.Length, listener);
                            }
                        }
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception)
                {
                    if (token.IsCancellationRequested) break;
                }
            }
        }
    }
}