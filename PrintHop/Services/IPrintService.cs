using System.Collections.Generic;
using System.Diagnostics;

namespace PrintHop.Services
{
    public interface IPrintService
    {
        IEnumerable<string> GetPrinters();
        Models.PrinterCapabilities GetPrinterCapabilities(string printerName);
        Process PrintFile(string filePath, string printerName, Models.PrintJobOptions options);
    }
}
