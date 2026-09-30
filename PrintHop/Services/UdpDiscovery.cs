using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
        private const int MaxDiagnosticLogs = 200;
        // Link-local multicast group for PrintHop discovery — works even when
        // broadcast is blocked by the network (enterprise switches, Wi-Fi AP isolation, etc.)
        private static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.72.80");
        private UdpClient _udpClient;
        private CancellationTokenSource _cts;
        private readonly ConcurrentDictionary<string, Peer> _peers = new ConcurrentDictionary<string, Peer>();
        private readonly ConcurrentQueue<DiagnosticLogEntry> _diagnosticLogs = new ConcurrentQueue<DiagnosticLogEntry>();
        private readonly JavaScriptSerializer _jsonSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        
        private readonly string _localId;
        private readonly string _localHostname;
        private readonly int _localHttpPort;
        private readonly IPrintService _printService;
        private int _broadcastsSent;
        private int _packetsReceived;
        private int _errorsCount;

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
            Log("INFO", "Starting UDP discovery service on port " + Port);
            Log("INFO", string.Format("Local ID: {0}, Hostname: {1}, HTTP Port: {2}", _localId.Substring(0, 8), _localHostname, _localHttpPort));
            
            // Allow multiple instances on the same machine to bind to the same port for local testing
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                _udpClient.EnableBroadcast = true;
                Log("OK", string.Format("UDP socket bound to 0.0.0.0:{0} with broadcast enabled", Port));
            }
            catch (Exception ex)
            {
                Log("ERROR", string.Format("Failed to bind UDP socket to port {0}: {1}", Port, ex.Message));
                throw;
            }

            // Log all detected network interfaces for diagnostics
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    var props = iface.GetIPProperties();
                    var ipv4Addrs = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => string.Format("{0}/{1}", a.Address, a.IPv4Mask))
                        .ToArray();
                    string ipsStr = ipv4Addrs.Length > 0 ? string.Join(", ", ipv4Addrs) : "(none)";
                    string status = iface.OperationalStatus.ToString();
                    string ifType = iface.NetworkInterfaceType.ToString();
                    Log("INFO", string.Format("Interface: {0} | Type: {1} | Status: {2} | IPs: {3}", 
                        iface.Name, ifType, status, ipsStr));
                }
            }
            catch (Exception ex)
            {
                Log("WARN", "Failed to enumerate network interfaces: " + ex.Message);
            }

            // Join multicast group on every active IPv4 interface so we receive
            // multicast announcements even when broadcast is blocked
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    if (iface.OperationalStatus != OperationalStatus.Up) continue;
                    if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (!iface.SupportsMulticast) continue;

                    var props = iface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !addr.Address.ToString().StartsWith("127."))
                        {
                            try
                            {
                                _udpClient.JoinMulticastGroup(MulticastGroup, addr.Address);
                                Log("OK", string.Format("Joined multicast group {0} on interface {1} ({2})", 
                                    MulticastGroup, iface.Name, addr.Address));
                            }
                            catch (Exception ex)
                            {
                                Log("WARN", string.Format("Failed to join multicast on {0} ({1}): {2}", 
                                    iface.Name, addr.Address, ex.Message));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "Failed during multicast group join: " + ex.Message);
            }

            Log("INFO", "Starting listener, broadcaster, and cleanup loops");
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
            Log("INFO", "Listen loop started — waiting for incoming UDP packets on port " + Port);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    string senderAddr = result.RemoteEndPoint.Address.ToString();
                    int senderPort = result.RemoteEndPoint.Port;
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    
                    var packet = _jsonSerializer.Deserialize<AnnouncePacket>(json);
                    if (packet == null)
                    {
                        Log("WARN", string.Format("Received unparseable packet from {0}:{1} ({2} bytes)", 
                            senderAddr, senderPort, result.Buffer.Length));
                        continue;
                    }

                    if (packet.Id == _localId)
                    {
                        // Own packet echoed back — this is normal
                        continue;
                    }

                    if (packet.Type == "announce")
                    {
                        Interlocked.Increment(ref _packetsReceived);
                        // Verify real IP from UDP socket sender endpoint to prevent IP spoofing
                        string verifiedIp = senderAddr;
                        int validPort = (packet.HttpPort >= 1024 && packet.HttpPort <= 65535) ? packet.HttpPort : 4222;

                        bool isNew = !_peers.ContainsKey(packet.Id);
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

                        if (isNew)
                        {
                            int printerCount = packet.Printers != null ? packet.Printers.Length : 0;
                            Log("PEER", string.Format("NEW PEER DISCOVERED: {0} ({1}) at {2}:{3} with {4} printer(s)",
                                packet.Hostname, packet.Id.Substring(0, 8), verifiedIp, validPort, printerCount));
                        }
                    }
                    else
                    {
                        Log("WARN", string.Format("Received unknown packet type '{0}' from {1}:{2}", 
                            packet.Type, senderAddr, senderPort));
                    }
                }
                catch (ObjectDisposedException)
                {
                    Log("INFO", "Listen loop stopped — socket disposed");
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _errorsCount);
                    Log("ERROR", "Listen error: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Collects all subnet-directed broadcast addresses and the multicast group endpoint
        /// for every active IPv4 interface. This ensures discovery packets reach peers even when
        /// the limited broadcast (255.255.255.255) is dropped by switches or routers.
        /// </summary>
        private List<IPEndPoint> GetAllBroadcastEndpoints()
        {
            var endpoints = new List<IPEndPoint>();

            // Always include the limited broadcast as a baseline
            endpoints.Add(new IPEndPoint(IPAddress.Broadcast, Port));

            // Always include the multicast group for networks that block broadcast
            endpoints.Add(new IPEndPoint(MulticastGroup, Port));

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    if (iface.OperationalStatus != OperationalStatus.Up) continue;
                    if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = iface.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (addr.Address.ToString().StartsWith("127.")) continue;

                        // Compute subnet-directed broadcast: IP | ~SubnetMask
                        // e.g. 10.11.212.71 with mask 255.255.255.0 → 10.11.212.255
                        try
                        {
                            byte[] ipBytes = addr.Address.GetAddressBytes();
                            byte[] maskBytes = addr.IPv4Mask.GetAddressBytes();
                            byte[] broadcastBytes = new byte[4];
                            for (int i = 0; i < 4; i++)
                            {
                                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
                            }
                            var subnetBroadcast = new IPAddress(broadcastBytes);

                            // Skip if it's the same as 255.255.255.255 (already added)
                            if (!subnetBroadcast.Equals(IPAddress.Broadcast))
                            {
                                endpoints.Add(new IPEndPoint(subnetBroadcast, Port));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return endpoints;
        }

        private async Task BroadcastLoop(CancellationToken token)
        {
            var random = new Random();

            // Send a rapid burst of 3 announcements at startup (1s apart) so peers
            // discover each other within seconds of launching
            int burstRemaining = 3;
            Log("INFO", "Broadcast loop started — will send 3 rapid bursts then every ~10s");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    string localIp = GetLocalIpAddress();
                    var printers = _printService.GetPrinters().ToArray();
                    var packet = new AnnouncePacket
                    {
                        Id = _localId,
                        Hostname = _localHostname,
                        Ip = localIp,
                        HttpPort = _localHttpPort,
                        Printers = printers
                    };

                    string json = _jsonSerializer.Serialize(packet);
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    
                    // Send to every discovered broadcast/multicast endpoint
                    var endpoints = GetAllBroadcastEndpoints();
                    int sentCount = 0;
                    int failCount = 0;
                    var targetsSummary = new List<string>();
                    foreach (var ep in endpoints)
                    {
                        try
                        {
                            await _udpClient.SendAsync(bytes, bytes.Length, ep);
                            sentCount++;
                            targetsSummary.Add(ep.Address.ToString());
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Log("ERROR", string.Format("Failed to send to {0}:{1} — {2}", ep.Address, ep.Port, ex.Message));
                        }
                    }
                    Interlocked.Add(ref _broadcastsSent, sentCount);
                    if (failCount > 0) Interlocked.Add(ref _errorsCount, failCount);
                    Log("SEND", string.Format("Broadcast from {0} — sent to {1}/{2} targets: [{3}] ({4} bytes, {5} printers)",
                        localIp, sentCount, endpoints.Count, string.Join(", ", targetsSummary), bytes.Length, printers.Length));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _errorsCount);
                    Log("ERROR", "Broadcast loop error: " + ex.Message);
                }

                if (burstRemaining > 0)
                {
                    burstRemaining--;
                    await Task.Delay(1000, token);
                }
                else
                {
                    // 10s ± 1.5s jitter
                    int jitter = random.Next(-1500, 1500);
                    await Task.Delay(10000 + jitter, token);
                }
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
                    if (_peers.TryRemove(deadId, out dummy))
                    {
                        Log("WARN", string.Format("Peer expired (no announce in 30s): {0} ({1}) at {2}", 
                            dummy.Hostname, deadId.Substring(0, 8), dummy.Ip));
                    }
                }

                await Task.Delay(5000, token);
            }
        }

        private string GetLocalIpAddress()
        {
            var ips = new List<string>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    if (iface.OperationalStatus == OperationalStatus.Up &&
                        iface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
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

        // =====================================================
        // Diagnostic Logging System
        // =====================================================

        private void Log(string level, string message)
        {
            var entry = new DiagnosticLogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Level = level,
                Message = message
            };
            _diagnosticLogs.Enqueue(entry);

            // Trim ring buffer to MaxDiagnosticLogs
            DiagnosticLogEntry discard;
            while (_diagnosticLogs.Count > MaxDiagnosticLogs)
            {
                _diagnosticLogs.TryDequeue(out discard);
            }
        }

        public List<DiagnosticLogEntry> GetDiagnosticLogs()
        {
            return _diagnosticLogs.ToArray().ToList();
        }

        /// <summary>
        /// Returns a snapshot of all detected network interfaces with their IPs,
        /// subnet masks, gateway info, and operational status for the diagnostics panel.
        /// </summary>
        public List<NetworkInterfaceInfo> GetNetworkInterfaceInfo()
        {
            var result = new List<NetworkInterfaceInfo>();
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var iface in interfaces)
                {
                    var props = iface.GetIPProperties();
                    var ipv4Addrs = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString())
                        .ToArray();
                    var masks = props.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(a => a.IPv4Mask != null ? a.IPv4Mask.ToString() : "")
                        .ToArray();
                    var gateways = props.GatewayAddresses
                        .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(g => g.Address.ToString())
                        .ToArray();

                    result.Add(new NetworkInterfaceInfo
                    {
                        Name = iface.Name,
                        Description = iface.Description,
                        Type = iface.NetworkInterfaceType.ToString(),
                        Status = iface.OperationalStatus.ToString(),
                        IPv4Addresses = ipv4Addrs,
                        SubnetMasks = masks,
                        Gateways = gateways,
                        SupportsMulticast = iface.SupportsMulticast,
                        MacAddress = iface.GetPhysicalAddress().ToString()
                    });
                }
            }
            catch { }
            return result;
        }

        public object GetDiagnosticSummary()
        {
            var endpoints = GetAllBroadcastEndpoints();
            return new
            {
                localId = _localId,
                localHostname = _localHostname,
                localIp = GetLocalIpAddress(),
                httpPort = _localHttpPort,
                udpPort = Port,
                multicastGroup = MulticastGroup.ToString(),
                activePeers = _peers.Count,
                totalBroadcastsSent = _broadcastsSent,
                totalPacketsReceived = _packetsReceived,
                totalErrors = _errorsCount,
                broadcastTargets = endpoints.Select(e => e.Address.ToString()).ToArray(),
                networkInterfaces = GetNetworkInterfaceInfo(),
                logs = GetDiagnosticLogs()
            };
        }

        public void Dispose()
        {
            Log("INFO", "Disposing UDP discovery service");
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
                    // Leave multicast group gracefully before closing
                    try { _udpClient.DropMulticastGroup(MulticastGroup); } catch { }
                    _udpClient.Close();
                    _udpClient.Dispose();
                    _udpClient = null;
                }
            }
            catch { }
        }
    }

    // Helper DTOs for diagnostics
    public class DiagnosticLogEntry
    {
        public string Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }

    public class NetworkInterfaceInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string[] IPv4Addresses { get; set; }
        public string[] SubnetMasks { get; set; }
        public string[] Gateways { get; set; }
        public bool SupportsMulticast { get; set; }
        public string MacAddress { get; set; }
    }
}
