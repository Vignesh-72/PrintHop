using System.Collections.Generic;

namespace PrintHop.Services
{
    public interface IPrintService
    {
        IEnumerable<string> GetPrinters();
        void PrintFile(string filePath, string printerName, Models.PrintJobOptions options);
    }
}
