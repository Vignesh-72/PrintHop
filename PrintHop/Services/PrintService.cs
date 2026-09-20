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

        public Models.PrinterCapabilities GetPrinterCapabilities(string printerName)
        {
            var caps = new Models.PrinterCapabilities();
            caps.PrinterName = printerName;

            try
            {
                var ps = new PrinterSettings();
                ps.PrinterName = printerName;
                caps.IsValid = ps.IsValid;

                if (ps.IsValid)
                {
                    caps.SupportsColor = ps.SupportsColor;
                    caps.CanDuplex = ps.CanDuplex;
                    caps.MaxCopies = Math.Max(1, (int)ps.MaximumCopies);

                    if (ps.DefaultPageSettings != null && ps.DefaultPageSettings.PaperSize != null)
                    {
                        caps.DefaultPaperSize = ps.DefaultPageSettings.PaperSize.PaperName;
                    }

                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (PaperSize size in ps.PaperSizes)
                    {
                        if (!string.IsNullOrEmpty(size.PaperName) && seen.Add(size.PaperName))
                        {
                            caps.PaperSizes.Add(size.PaperName);
                        }
                    }
                }
            }
            catch (Exception)
            {
                caps.IsValid = false;
            }

            // If no paper sizes were returned or query failed, provide standard defaults
            if (caps.PaperSizes.Count == 0)
            {
                caps.PaperSizes.AddRange(new string[] { "A4", "A5", "Letter", "Legal" });
            }

            return caps;
        }

        public Process PrintFile(string filePath, string printerName, Models.PrintJobOptions options)
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
                return null;
            }
            else
            {
                return PrintGenericDocument(filePath, printerName, options);
            }
        }

        private void PrintImage(string imagePath, string printerName, Models.PrintJobOptions options)
        {
            using (var doc = new PrintDocument())
            {
                doc.PrinterSettings.PrinterName = printerName;
                int copies = (options != null && options.Copies > 0) ? options.Copies : 1;
                doc.PrinterSettings.Copies = (short)copies;
                
                // Configure Paper Size if specified (e.g. "A5", "A4", "Letter", "Legal")
                if (options != null && !string.IsNullOrEmpty(options.PaperSize) && !options.PaperSize.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                    {
                        if (ps.PaperName.IndexOf(options.PaperSize, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            ps.Kind.ToString().IndexOf(options.PaperSize, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            doc.DefaultPageSettings.PaperSize = ps;
                            break;
                        }
                    }
                }

                // Configure Orientation
                if (options != null && !string.IsNullOrEmpty(options.Orientation))
                {
                    if (options.Orientation.Equals("Landscape", StringComparison.OrdinalIgnoreCase))
                    {
                        doc.DefaultPageSettings.Landscape = true;
                    }
                    else if (options.Orientation.Equals("Portrait", StringComparison.OrdinalIgnoreCase))
                    {
                        doc.DefaultPageSettings.Landscape = false;
                    }
                }

                // Configure Duplex if supported by the physical printer
                if (options != null && doc.PrinterSettings.CanDuplex && !string.IsNullOrEmpty(options.Duplex))
                {
                    if (options.Duplex.Equals("TwoSidedLongEdge", StringComparison.OrdinalIgnoreCase))
                        doc.PrinterSettings.Duplex = Duplex.Vertical;
                    else if (options.Duplex.Equals("TwoSidedShortEdge", StringComparison.OrdinalIgnoreCase))
                        doc.PrinterSettings.Duplex = Duplex.Horizontal;
                    else
                        doc.PrinterSettings.Duplex = Duplex.Simplex;
                }

                // Configure Color vs Grayscale
                bool isGrayscale = options != null && 
                    (string.Equals(options.ColorMode, "Grayscale", StringComparison.OrdinalIgnoreCase) || !options.Color);
                if (isGrayscale)
                {
                    doc.DefaultPageSettings.Color = false;
                }

                // If printing to a virtual PDF printer without a specified filename,
                // automatically route output to a dedicated file in %TEMP%\PrintHop to avoid blocking SaveFileDialog
                if (printerName.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    string.IsNullOrEmpty(doc.PrinterSettings.PrintFileName))
                {
                    string pdfDir = Path.Combine(Path.GetTempPath(), "PrintHop", "Output");
                    Directory.CreateDirectory(pdfDir);
                    doc.PrinterSettings.PrintToFile = true;
                    doc.PrinterSettings.PrintFileName = Path.Combine(pdfDir, string.Format("print_{0:yyyyMMdd_HHmmss}_{1}.pdf", DateTime.Now, Guid.NewGuid().ToString().Substring(0, 8)));
                }

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

                        if (isGrayscale)
                        {
                            // Convert to grayscale using ColorMatrix
                            var colorMatrix = new System.Drawing.Imaging.ColorMatrix(new float[][]
                            {
                                new float[] { 0.299f, 0.299f, 0.299f, 0, 0 },
                                new float[] { 0.587f, 0.587f, 0.587f, 0, 0 },
                                new float[] { 0.114f, 0.114f, 0.114f, 0, 0 },
                                new float[] { 0,      0,      0,      1, 0 },
                                new float[] { 0,      0,      0,      0, 1 }
                            });
                            using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                            {
                                attributes.SetColorMatrix(colorMatrix);
                                e.Graphics.DrawImage(img,
                                    new Rectangle(e.MarginBounds.Left, e.MarginBounds.Top, (int)drawWidth, (int)drawHeight),
                                    0, 0, img.Width, img.Height,
                                    GraphicsUnit.Pixel,
                                    attributes);
                            }
                        }
                        else
                        {
                            e.Graphics.DrawImage(img, e.MarginBounds.Left, e.MarginBounds.Top, drawWidth, drawHeight);
                        }
                    }
                };

                doc.Print();
            }
        }

        private Process PrintGenericDocument(string filePath, string printerName, Models.PrintJobOptions options)
        {
            // If the target is a virtual PDF printer, route directly to Output folder to avoid blocking SaveFileDialog
            if (printerName.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string pdfDir = Path.Combine(Path.GetTempPath(), "PrintHop", "Output");
                Directory.CreateDirectory(pdfDir);
                string dest = Path.Combine(pdfDir, string.Format("print_{0:yyyyMMdd_HHmmss}_{1}.pdf", DateTime.Now, Guid.NewGuid().ToString().Substring(0, 8)));
                File.Copy(filePath, dest, true);
                return null;
            }

            // BUG FIX #7: Sanitize printerName to prevent argument injection.
            string safePrinterName = printerName.Replace("\"", "");
            int copies = (options != null && options.Copies > 0) ? options.Copies : 1;

            // Attempt to set printer copies globally for this process
            try
            {
                var settings = new PrinterSettings { PrinterName = safePrinterName };
                if (settings.IsValid)
                {
                    settings.Copies = (short)copies;
                }
            }
            catch { }

            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "printto",
                // Wrap the sanitized printer name in quotes as required by some print handlers
                Arguments = string.Format("\"{0}\"", safePrinterName)
            };

            return Process.Start(psi);
        }
    }
}
