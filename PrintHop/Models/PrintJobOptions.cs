using System;

namespace PrintHop.Models
{
    public class PrintJobOptions
    {
        public PrintJobOptions()
        {
            Copies = 1;
        }
        public string PaperSize { get; set; }
        public int Copies { get; set; }
        public string Duplex { get; set; }
        public bool Color { get; set; }
    }
}
