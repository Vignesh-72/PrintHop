using System;

namespace PrintHop.Models
{
    public class PrintJobOptions
    {
        public PrintJobOptions()
        {
            Copies = 1;
            PaperSize = "Default";
            Orientation = "Portrait";
            Duplex = "Simplex";
            Color = true;
            ColorMode = "Color";
        }

        public int Copies { get; set; }
        public string PaperSize { get; set; }
        public string Orientation { get; set; }
        public string Duplex { get; set; }
        public bool Color { get; set; }
        public string ColorMode { get; set; }
        public string PageRange { get; set; }
    }
}
