using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PrintHop.Models;

namespace PrintHop.Services
{
    public class UdpDiscovery : IDisposable
    {
        private const int Port = 4222;
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, Peer> _peers = new ConcurrentDictionary<string, Peer>();
        private readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        
        private readonly string _localId;
        private readonly string _localHostname;
        private readonly int _localHttpPort;
        private readonly IPrintService _printService;

        public UdpDiscovery(string localId, int localHttpPort, IPrintService printService)
        {
            _localId = localId;
            _localHostname = Environment.MachineName;
            _localHttpPort = localHttpPort;
            _printService = printService;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            
            // Allow multiple instances on the same machine to bind to the same port for local testing
            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            _udpClient.EnableBroadcast = true;

            Task.Run(() => ListenLoop(_cts.Token), _cts.Token);
            Task.Run(() => BroadcastLoop(_cts.Token), _cts.Token);
            Task.Run(() => CleanupLoop(_cts.Token), _cts.Token);
        }

        public IEnumerable<Peer> GetPeers()
        {
            return _peers.Values.ToList();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    
                    var packet = _jsonSerializer.Deserialize<AnnouncePacket>(json);
                    if (packet != null && packet.Type == "announce" && packet.Id != _localId)
                    {
                        // Verify real IP from UDP socket sender endpoint to prevent IP spoofing
                        string verifiedIp = result.RemoteEndPoint.Address.ToString();
                        int validPort = (packet.HttpPort >= 1024 && packet.HttpPort <= 65535) ? packet.HttpPort : 4222;

                        _peers.AddOrUpdate(packet.Id, 
                            id => new Peer 
                            { 
                                Id = packet.Id, 
                                Hostname = packet.Hostname, 
                                Ip = verifiedIp, 
                                HttpPort = validPort, 
                                Printers = packet.Printers, 
                                LastSeen = DateTime.UtcNow 
                            },
                            (id, existing) => 
                            {
                                existing.Hostname = packet.Hostname;
                                existing.Ip = verifiedIp;
                                existing.HttpPort = validPort;
                                existing.Printers = packet.Printers;
                                existing.LastSeen = DateTime.UtcNow;
                                return existing;
                            });
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { /* Ignore parsing or network errors */ }
            }
        }

        private async Task BroadcastLoop(CancellationToken token)
        {
            var random = new Random();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string localIp = GetLocalIpAddress();
                    var packet = new AnnouncePacket
                    {
                        Id = _localId,
                        Hostname = _localHostname,
                        Ip = localIp,
                        HttpPort = _localHttpPort,
                        Printers = _printService.GetPrinters().ToArray()
                    };

                    string json = _jsonSerializer.Serialize(packet);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    
                    var endPoint = new IPEndPoint(IPAddress.Broadcast, Port);
                    await _udpClient.SendAsync(bytes, bytes.Length, endPoint);
                }
                catch (Exception) { /* Ignore broadcast errors */ }

                // 10s ± 1.5s jitter
                int jitter = random.Next(-1500, 1500);
                await Task.Delay(10000 + jitter, token);
            }
        }

        private async Task CleanupLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var threshold = DateTime.UtcNow.AddSeconds(-30);
                var deadPeers = _peers.Where(p => p.Value.LastSeen < threshold).Select(p => p.Key).ToList();
                
                foreach (var deadId in deadPeers)
                {
                    Peer dummy;
                    _peers.TryRemove(deadId, out dummy);
                }

                await Task.Delay(5000, token);
            }
        }

        private string GetLocalIpAddress()
        {
            var ips = new List<string>();
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    if (iface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        iface.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    {
                        var props = iface.GetIPProperties();
                        foreach (var addr in props.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string ipStr = addr.Address.ToString();
                                if (!ipStr.StartsWith("127.") && !ips.Contains(ipStr))
                                {
                                    ips.Add(ipStr);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.ToString();
                        if (!ipStr.StartsWith("127.") && !ips.Contains(ipStr))
                        {
                            ips.Add(ipStr);
                        }
                    }
                }
            }
            catch { }

            if (ips.Count > 0)
            {
                // Prefer RFC 1918 Private LAN / Hotspot subnets (10.x, 192.168.x, 172.16.x - 172.31.x)
                foreach (var ip in ips)
                {
                    if (ip.StartsWith("10.") || ip.StartsWith("192.168."))
                    {
                        return ip;
                    }
                    if (ip.StartsWith("172."))
                    {
                        var parts = ip.Split('.');
                        int secondOctet;
                        if (parts.Length >= 2 && int.TryParse(parts[1], out secondOctet) && secondOctet >= 16 && secondOctet <= 31)
                        {
                            return ip;
                        }
                    }
                }
                return ips[0];
            }
            return "127.0.0.1";
        }

        public void Dispose()
        {
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }
            }
            catch { }

            try
            {
                if (_udpClient != null)
                {
                    _udpClient.Close();
                    _udpClient.Dispose();
                    _udpClient = null;
                }
            }
            catch { }
        }
    }
}
