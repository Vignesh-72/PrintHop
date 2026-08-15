using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;

namespace PrintHop.Services
{
    public class PrintService : IPrintService
    {
        public IEnumerable<string> GetPrinters()
        {
            var printers = new List<string>();
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                printers.Add(printer);
            }
            return printers;
        }

        public void PrintFile(string filePath, string printerName, Models.PrintJobOptions options)
        {
            if (string.IsNullOrEmpty(printerName))
            {
                throw new ArgumentException("Printer name must be provided.", "printerName");
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File to print was not found.", filePath);
            }

            // Simple extension check for images to use GDI+, else use ShellExecute
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif")
            {
                PrintImage(filePath, printerName, options);
            }
            else
            {
                PrintGenericDocument(filePath, printerName);
            }
        }

        private void PrintImage(string imagePath, string printerName, Models.PrintJobOptions options)
        {
            using (var doc = new PrintDocument())
            {
                doc.PrinterSettings.PrinterName = printerName;
                doc.PrinterSettings.Copies = (short)(options != null ? options.Copies : 1);
                
                doc.PrintPage += (sender, e) =>
                {
                    using (var img = Image.FromFile(imagePath))
                    {
                        // Scale to fit while maintaining aspect ratio
                        float scale = Math.Min(
                            (float)e.MarginBounds.Width / img.Width,
                            (float)e.MarginBounds.Height / img.Height);

                        float drawWidth = img.Width * scale;
                        float drawHeight = img.Height * scale;

                        e.Graphics.DrawImage(img, e.MarginBounds.Left, e.MarginBounds.Top, drawWidth, drawHeight);
                    }
                };

                doc.Print();
            }
        }

        private void PrintGenericDocument(string filePath, string printerName)
        {
            // For files like PDF, DOCX, XLSX, we use ShellExecute with "printto" verb.
            // Note: This requires the host machine to have a default application registered 
            // to handle the "printto" verb for the specific file extension.
            
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "printto",
                // Wrap the printer name in quotes as required by some print handlers
                Arguments = string.Format("\"{0}\"", printerName)
            };

            using (var process = Process.Start(psi))
            {
                // Process might be null if it reused an existing application instance
                if (process != null)
                {
                    // Wait a maximum of 30 seconds for the application to spool the print job
                    process.WaitForExit(30000);
                }
            }
        }
    }
}
