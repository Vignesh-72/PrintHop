using System.Collections.Generic;

namespace PrintHop.Services
{
    public interface IPrintService
    {
        IEnumerable<string> GetPrinters();
        Models.PrinterCapabilities GetPrinterCapabilities(string printerName);
        void PrintFile(string filePath, string printerName, Models.PrintJobOptions options);
    }
}
