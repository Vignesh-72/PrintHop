using System;
using System.Collections.Generic;

namespace PrintHop.Models
{
    public class PrinterCapabilities
    {
        public PrinterCapabilities()
        {
            PaperSizes = new List<string>();
            MaxCopies = 1;
            DefaultPaperSize = "Default";
        }

        public string PrinterName { get; set; }
        public bool IsValid { get; set; }
        public bool SupportsColor { get; set; }
        public bool CanDuplex { get; set; }
        public int MaxCopies { get; set; }
        public string DefaultPaperSize { get; set; }
        public List<string> PaperSizes { get; set; }
    }
}
