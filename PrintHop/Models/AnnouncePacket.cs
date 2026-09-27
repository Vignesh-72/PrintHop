using System;

namespace PrintHop.Models
{
    public class AnnouncePacket
    {
        public AnnouncePacket()
        {
            ProtocolVersion = 1;
            Type = "announce";
        }
        public int ProtocolVersion { get; set; }
        public string Type { get; set; }
        public string Id { get; set; }
        public string Hostname { get; set; }
        public string Ip { get; set; }
        public int HttpPort { get; set; }
        public string[] Printers { get; set; }
    }
}
