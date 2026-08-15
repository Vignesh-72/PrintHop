using System;

namespace PrintHop.Models
{
    public class AnnouncePacket
    {
        public int ProtocolVersion { get; set; } = 1;
        public string Type { get; set; } = "announce";
        public string Id { get; set; }
        public string Hostname { get; set; }
        public string Ip { get; set; }
        public int HttpPort { get; set; }
        public string[] Printers { get; set; }
    }
}
