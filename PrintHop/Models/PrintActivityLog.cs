using System;

namespace PrintHop.Models
{
    public class PrintActivityLog
    {
        public PrintActivityLog()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
        }

        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string SenderId { get; set; }
        public string SenderHostname { get; set; }
        public string PrinterName { get; set; }
        public string DocumentName { get; set; }
        public string OptionsSummary { get; set; }
        public string Status { get; set; } // "Success", "Blocked", "Rejected", "Failed"
        public string Message { get; set; }
        public bool IsLocal { get; set; }
    }
}
