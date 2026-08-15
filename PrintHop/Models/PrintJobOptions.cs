using System;

namespace PrintHop.Models
{
    public class PrintJobOptions
    {
        public string PaperSize { get; set; }
        public int Copies { get; set; } = 1;
        public string Duplex { get; set; }
        public bool Color { get; set; }
    }
}
