using System;

namespace PrintHop.Models
{
    public class Peer
    {
        public string Id { get; set; }
        public string Hostname { get; set; }
        public string Ip { get; set; }
        public int HttpPort { get; set; }
        public string[] Printers { get; set; }
        public DateTime LastSeen { get; set; }
    }
}
