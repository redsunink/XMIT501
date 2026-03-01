using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Open.Nat;

namespace XMIT501_CS
{
    public class RadioServer
    {
        private UdpClient _udpServer;
        // Maps Channel ID (0-10) to a list of User IPs listening to it
        private Dictionary<byte, HashSet<IPEndPoint>> _listeners = new Dictionary<byte, HashSet<IPEndPoint>>();
        private CancellationTokenSource _cts = new CancellationTokenSource(); // The kill switch
        private Mapping? _upnpMapping;
        public event Action<bool>? OnUPnPResult;

        public RadioServer(int port = 5000)
        {
            _udpServer = new UdpClient(port);
            Console.WriteLine($"[SERVER] Radio Repeater Live on Port {port}");
            // Start UPnP asynchronously
            Task.Run(() => SetupUPnP(port));
            // Pass the token into the loop
            Task.Run(() => ListenLoop(_cts.Token));
        }

        private async Task SetupUPnP(int port)
        {
            try
            {
                var discoverer = new NatDiscoverer();
                // Search for UPnP devices with a 5-second timeout
                var cts = new CancellationTokenSource(5000); 
                var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
                
                // Create the mapping request
                _upnpMapping = new Mapping(Protocol.Udp, port, port, "XMIT501 Server");
                await device.CreatePortMapAsync(_upnpMapping);
                
                // Optional: You could fire an event here to tell the UI that UPnP succeeded
                OnUPnPResult?.Invoke(true);
                Console.WriteLine("[SERVER] UPnP Port Forwarding successful.");
            }
            catch
            {
                // Router does not support UPnP or has it disabled
                OnUPnPResult?.Invoke(false);
                Console.WriteLine("[SERVER] UPnP failed. Manual port forwarding required.");
            }
        }

        public void Shutdown()
        {
            _cts.Cancel();    // Tell the loop to stop
            _udpServer.Close(); // Break the ReceiveAsync wait
            if (_upnpMapping != null)
            {
                Task.Run(async () => 
                {
                    try 
                    {
                        var cts = new CancellationTokenSource(2000);
                        var discoverer = new NatDiscoverer();
                        var device = await discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts);
                        await device.DeletePortMapAsync(_upnpMapping);
                    } 
                    catch { /* Best effort cleanup */ }
                });
            }
            Console.WriteLine("[SERVER] Radio Repeater Shutdown.");
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
                        
                        // Intercept a "Ping" from a client (a single byte with value 255) and respond with a "Pong"
                        if (packet.Length == 1 && packet[0] == 255)
                        {
                            // Bounce a 255 Pong right back to the sender
                            await _udpServer.SendAsync(new byte[] { 255 }, 1, result.RemoteEndPoint);
                            continue; 
                        }

                        // Register this user to this channel
                        if (!_listeners.ContainsKey(channelId))
                            _listeners[channelId] = new HashSet<IPEndPoint>();
                            
                        _listeners[channelId].Add(result.RemoteEndPoint);

                        // If it's larger than 1 byte, it has Opus audio attached. Relay it!
                        if (packet.Length > 1) 
                        {
                            foreach (var listener in _listeners[channelId])
                            {
                                // Simplex: Don't echo the audio back to the person speaking
                                if (!listener.Equals(result.RemoteEndPoint))
                                    await _udpServer.SendAsync(packet, packet.Length, listener);
                            }
                        }
                    }
                }
                catch (ObjectDisposedException) 
                { 
                    // This happens when we call _udpServer.Close(), it's a "clean" exit
                    break; 
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                }
            }
        }
    }
}