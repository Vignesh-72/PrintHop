using System;

namespace PrintHop.Models
{
    public class PrintJob
    {
        public string Id { get; set; }
        public string SenderId { get; set; }
        public string SenderIp { get; set; }
        public string SenderHostname { get; set; }
        public string PrinterName { get; set; }
        public string DocumentName { get; set; }
        public long FileSizeBytes { get; set; }
        public string TempFilePath { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; }
        public PrintJobOptions Options { get; set; }
        public string OptionsSummary { get; set; }
        public bool IsLocal { get; set; }
    }
}
