import sys

def patch():
    with open(r'c:\Users\Admin\Documents\PrintHop\PrintHop\Services\HttpServer.cs', 'r', encoding='utf-8') as f:
        content = f.read()
    
    start_str = '        private void HandleReceivePrint(HttpListenerContext context)\n        {'
    end_str = '        private bool ValidateFileSignatures(string path)'
    
    start_idx = content.find(start_str)
    end_idx = content.find(end_str)
    
    if start_idx == -1 or end_idx == -1:
        print('Error finding bounds')
        return
        
    new_method = '''        private void HandleReceivePrint(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            if (!_printSemaphore.Wait(2000))
            {
                res.StatusCode = 429;
                SendString(res, "Server is currently processing the maximum number of concurrent print uploads. Please retry shortly.");
                return;
            }

            try
            {
                const long MaxUploadBytes = 500L * 1024 * 1024;
                if (req.ContentLength64 > MaxUploadBytes)
                {
                    res.StatusCode = 413;
                    SendString(res, string.Format("Upload exceeds the 500 MB limit ({0} bytes received).", req.ContentLength64));
                    return;
                }

                string senderId = req.Headers["X-PrintHop-SenderId"] ?? (req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : "unknown-device");
                string senderHostname = req.Headers["X-PrintHop-SenderHostname"] ?? (req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : "Unknown Device");
                string printerName = req.Headers["X-PrintHop-PrinterName"];
                string originalFilename = req.Headers["X-PrintHop-OriginalFilename"];
                if (!string.IsNullOrEmpty(originalFilename))
                {
                    try { originalFilename = Uri.UnescapeDataString(originalFilename); } catch { }
                }

                if (string.IsNullOrEmpty(printerName))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'X-PrintHop-PrinterName' header.");
                    return;
                }

                var options = new PrintJobOptions
                {
                    Copies = int.TryParse(req.Headers["X-PrintHop-Copies"], out int c) ? c : 1,
                    PaperSize = req.Headers["X-PrintHop-PaperSize"] ?? "Default",
                    Orientation = req.Headers["X-PrintHop-Orientation"] ?? "Portrait",
                    ColorMode = req.Headers["X-PrintHop-ColorMode"] ?? "Color",
                    Duplex = req.Headers["X-PrintHop-Duplex"] ?? "Simplex",
                    PageRange = req.Headers["X-PrintHop-PageRange"]
                };

                string optionsSummary = string.Format("{0} copy{1}, {2}, {3}, {4}",
                    options.Copies,
                    options.Copies > 1 ? "ies" : "",
                    !string.IsNullOrEmpty(options.PaperSize) ? options.PaperSize : "Default Size",
                    !string.IsNullOrEmpty(options.Orientation) ? options.Orientation : "Portrait",
                    !string.IsNullOrEmpty(options.ColorMode) ? options.ColorMode : "Color");

                if (_activityMonitor != null && !string.IsNullOrEmpty(senderId))
                {
                    _activityMonitor.RecordDeviceSeen(senderId, senderHostname);
                }

                if (_activityMonitor != null && _activityMonitor.IsDeviceBlocked(senderId))
                {
                    _activityMonitor.LogActivity(new PrintActivityLog
                    {
                        SenderId = senderId,
                        SenderHostname = senderHostname,
                        PrinterName = printerName,
                        DocumentName = !string.IsNullOrEmpty(originalFilename) ? originalFilename : "Document",
                        OptionsSummary = optionsSummary,
                        Status = "Blocked",
                        Message = "Device is blocked by administrator.",
                        IsLocal = (senderId == _localId)
                    });

                    res.StatusCode = 403;
                    SendString(res, "Access denied: This device has been blocked from printing by the host.");
                    return;
                }

                if (_whitelistCheck != null && !_whitelistCheck(senderId, senderHostname))
                {
                    if (_activityMonitor != null)
                    {
                        _activityMonitor.LogActivity(new PrintActivityLog
                        {
                            SenderId = senderId,
                            SenderHostname = senderHostname,
                            PrinterName = printerName,
                            DocumentName = !string.IsNullOrEmpty(originalFilename) ? originalFilename : "Document",
                            OptionsSummary = optionsSummary,
                            Status = "Rejected",
                            Message = "Print request was rejected by host authorization.",
                            IsLocal = (senderId == _localId)
                        });
                    }

                    res.StatusCode = 403;
                    SendString(res, "Print job rejected by the target machine.");
                    return;
                }

                string originalExt = string.IsNullOrEmpty(originalFilename) ? ".tmp" : Path.GetExtension(originalFilename).ToLowerInvariant();
                string[] allowedExts = { ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".docx", ".xlsx", ".txt" };
                bool extAllowed = false;
                foreach (var ext in allowedExts)
                {
                    if (ext == originalExt) { extAllowed = true; break; }
                }
                if (!extAllowed) originalExt = ".tmp";

                string tempDir = Path.Combine(Path.GetTempPath(), "PrintHop");
                Directory.CreateDirectory(tempDir);
                string tempFilePath = Path.Combine(tempDir, string.Format("job_{0}{1}", Guid.NewGuid(), originalExt));

                long totalRead = 0;
                try
                {
                    using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = req.InputStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            totalRead += read;
                            if (totalRead > MaxUploadBytes)
                            {
                                throw new InvalidOperationException("Upload exceeds 500MB limit mid-stream.");
                            }
                            fs.Write(buffer, 0, read);
                        }
                    }
                }
                catch (Exception ex)
                {
                    try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                    res.StatusCode = 413;
                    SendString(res, "Upload failed or exceeded size limit: " + ex.Message);
                    return;
                }

                if (totalRead == 0)
                {
                    try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                    res.StatusCode = 400;
                    SendString(res, "File payload is empty.");
                    return;
                }

                if (!ValidateFileSignatures(tempFilePath))
                {
                    try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                    res.StatusCode = 415;
                    SendString(res, "Unsupported file format. Magic byte validation failed.");
                    return;
                }

                if (_printJobManager != null)
                {
                    var job = new PrintJob
                    {
                        Id = Guid.NewGuid().ToString(),
                        SenderId = senderId,
                        SenderHostname = senderHostname,
                        PrinterName = printerName,
                        DocumentName = !string.IsNullOrEmpty(originalFilename) ? originalFilename : Path.GetFileName(tempFilePath),
                        FileSizeBytes = totalRead,
                        TempFilePath = tempFilePath,
                        Timestamp = DateTime.Now,
                        Options = options,
                        OptionsSummary = optionsSummary,
                        IsLocal = (senderId == _localId)
                    };
                    _printJobManager.Enqueue(job);
                    
                    res.StatusCode = 200;
                    SendString(res, "Print job queued successfully.");
                }
                else
                {
                    try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); } catch { }
                    res.StatusCode = 500;
                    SendString(res, "Print queue system unavailable.");
                }
            }
            catch (Exception ex)
            {
                res.StatusCode = 500;
                SendString(res, "Server error during upload: " + ex.Message);
            }
            finally
            {
                _printSemaphore.Release();
            }
        }

'''
    new_content = content[:start_idx] + new_method + content[end_idx:]
    with open(r'c:\Users\Admin\Documents\PrintHop\PrintHop\Services\HttpServer.cs', 'w', encoding='utf-8') as f:
        f.write(new_content)
    print('Patched HandleReceivePrint!')

patch()
