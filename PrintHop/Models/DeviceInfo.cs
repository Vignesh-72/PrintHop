using System;

namespace PrintHop.Models
{
    public class DeviceInfo
    {
        public DeviceInfo()
        {
            FirstSeen = DateTime.Now;
            LastSeen = DateTime.Now;
            IsApproved = false;
            IsBlocked = false;
            TotalPrints = 0;
        }

        public string Id { get; set; }
        public string Hostname { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsApproved { get; set; }
        public bool IsBlocked { get; set; }
        public int TotalPrints { get; set; }
    }
}
