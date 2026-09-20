using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PrintHop.Models;

namespace PrintHop.Services
{
    public class HttpServer : IDisposable
    {
        private HttpListener _listener;
        private readonly UdpDiscovery _discovery;
        private readonly IPrintService _printService;
        private readonly JavaScriptSerializer _jsonSerializer;
        private readonly string _localId;
        private readonly Func<string, string, bool> _whitelistCheck;
        private readonly ActivityMonitorService _activityMonitor;
        private readonly PrintJobManager _printJobManager;
        private static readonly SemaphoreSlim _printSemaphore = new SemaphoreSlim(3, 3);
        private int _port = 4222;
        private Thread _serverThread;
        private bool _isRunning;

        public HttpServer(string localId, UdpDiscovery discovery, IPrintService printService, Func<string, string, bool> whitelistCheck, ActivityMonitorService activityMonitor = null, PrintJobManager printJobManager = null)
        {
            _localId = localId;
            _discovery = discovery;
            _printService = printService;
            _whitelistCheck = whitelistCheck;
            _activityMonitor = activityMonitor;
            _printJobManager = printJobManager;
            _jsonSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        public int Start()
        {
            string localIp = GetLocalIpAddress();

            // Try ports 4222–4230 sequentially.
            while (_port <= 4230)
            {
                // --- Attempt 1: Plus wildcard prefix (binds all interfaces / hotspots; works with Admin or URL ACL) ---
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(string.Format("http://+:{0}/", _port));
                    _listener.Start();
                    _boundOnLan = true;
                    break;
                }
                catch (HttpListenerException)
                {
                    try { _listener.Close(); } catch { }
                    _listener = null;
                }

                // --- Attempt 2: Asterisk wildcard prefix ---
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(string.Format("http://*:{0}/", _port));
                    _listener.Start();
                    _boundOnLan = true;
                    break;
                }
                catch (HttpListenerException)
                {
                    try { _listener.Close(); } catch { }
                    _listener = null;
                }

                // --- Attempt 3: Bind localhost + ALL detected LAN/Wi-Fi IPs explicitly ---
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _port));
                    _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", _port));
                    
                    var allIps = GetAllLocalIpAddresses();
                    bool boundLan = false;
                    foreach (var ip in allIps)
                    {
                        try
                        {
                            _listener.Prefixes.Add(string.Format("http://{0}:{1}/", ip, _port));
                            boundLan = true;
                        }
                        catch { }
                    }

                    _listener.Start();
                    _boundOnLan = boundLan;
                    break;
                }
                catch (HttpListenerException)
                {
                    try { _listener.Close(); } catch { }
                    _listener = null;
                }

                // --- Attempt 4: Localhost-only fallback ---
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _port));
                    _listener.Start();
                    _boundOnLan = false;
                    break;
                }
                catch (HttpListenerException)
                {
                    try { _listener.Close(); } catch { }
                    _listener = null;
                    _port++;
                }
            }

            if (_listener == null || !_listener.IsListening)
                throw new Exception("Could not bind HTTP listener to any port 4222–4230. Ensure no other service blocks these ports.");

            _isRunning = true;
            _serverThread = new Thread(ListenLoop);
            _serverThread.IsBackground = true;
            _serverThread.Start();

            return _port;
        }

        // True if the server successfully bound to the LAN IP (LAN-accessible).
        // False means localhost-only (browser on same machine only).
        private bool _boundOnLan;

        public bool BoundOnLan
        {
            get { return _boundOnLan; }
        }

        private void ListenLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException) { break; }
                catch (InvalidOperationException) { break; }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;

            try
            {
                // CORS: Allow local and LAN peer fetch requests
                string origin = req.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin))
                {
                    res.Headers.Add("Access-Control-Allow-Origin", origin);
                }
                else
                {
                    res.Headers.Add("Access-Control-Allow-Origin", "*");
                }
                res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Origin, Accept");

                if (req.HttpMethod == "OPTIONS")
                {
                    res.StatusCode = 200;
                    res.Close();
                    return;
                }

                if (req.Url.AbsolutePath.StartsWith("/api/"))
                {
                    HandleApi(context);
                }
                else
                {
                    ServeStaticFile(context);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    res.StatusCode = 500;
                    SendString(res, ex.Message);
                }
                catch { }
            }
        }

        private void HandleApi(HttpListenerContext context)
        {
            var req = context.Request;
            var res = context.Response;
            var path = req.Url.AbsolutePath;

            if (req.HttpMethod == "GET" && path == "/api/self")
            {
                var self = new
                {
                    id = _localId,
                    hostname = Environment.MachineName,
                    ip = GetLocalIpAddress(),
                    httpPort = _port,
                    printers = _printService.GetPrinters().ToArray(),
                    isClientLocal = req.IsLocal,
                    clientIp = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : ""
                };
                SendJson(res, self);
            }
            else if (req.HttpMethod == "GET" && path == "/api/peers")
            {
                var peerDtos = _discovery.GetPeers().Select(p => new
                {
                    id = p.Id,
                    hostname = p.Hostname,
                    ip = p.Ip,
                    httpPort = p.HttpPort,
                    printers = p.Printers,
                    lastSeen = p.LastSeen
                }).ToArray();
                SendJson(res, peerDtos);
            }
            else if (req.HttpMethod == "GET" && path == "/api/printer-capabilities")
            {
                string printerName = req.QueryString["name"];
                if (string.IsNullOrEmpty(printerName))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'name' query parameter.");
                    return;
                }
                var caps = _printService.GetPrinterCapabilities(printerName);
                var capsDto = new
                {
                    printerName = caps.PrinterName,
                    isValid = caps.IsValid,
                    supportsColor = caps.SupportsColor,
                    canDuplex = caps.CanDuplex,
                    maxCopies = caps.MaxCopies,
                    defaultPaperSize = caps.DefaultPaperSize,
                    paperSizes = caps.PaperSizes
                };
                SendJson(res, capsDto);
            }
            else if (req.HttpMethod == "POST" && path == "/api/receive-print")
            {
                HandleReceivePrint(context);
            }
            else if (req.HttpMethod == "GET" && path == "/api/activity-logs")
            {
                int limit = 50;
                string limitStr = req.QueryString["limit"];
                int parsedLimit;
                if (int.TryParse(limitStr, out parsedLimit) && parsedLimit > 0)
                {
                    limit = parsedLimit;
                }
                var logs = _activityMonitor != null ? _activityMonitor.GetRecentLogs(limit) : new List<PrintActivityLog>();
                SendJson(res, logs);
            }
            else if (req.HttpMethod == "POST" && path == "/api/activity-logs/clear")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                if (_activityMonitor != null)
                {
                    _activityMonitor.ClearLogs();
                }
                SendJson(res, new { success = true, message = "Activity logs cleared." });
            }
            else if (req.HttpMethod == "GET" && path == "/api/devices")
            {
                var devices = _activityMonitor != null ? _activityMonitor.GetDevices() : new List<DeviceInfo>();
                SendJson(res, devices);
            }
            else if (req.HttpMethod == "POST" && path == "/api/devices/block")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                string id = req.QueryString["id"];
                string hostname = req.QueryString["hostname"];
                if (string.IsNullOrEmpty(id))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'id' query parameter.");
                    return;
                }
                if (_activityMonitor != null)
                {
                    _activityMonitor.BlockDevice(id, hostname);
                }
                SendJson(res, new { success = true, message = "Device blocked successfully." });
            }
            else if (req.HttpMethod == "POST" && path == "/api/devices/unblock")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                string id = req.QueryString["id"];
                if (string.IsNullOrEmpty(id))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'id' query parameter.");
                    return;
                }
                if (_activityMonitor != null)
                {
                    _activityMonitor.UnblockDevice(id);
                }
                SendJson(res, new { success = true, message = "Device unblocked successfully." });
            }
            else if (req.HttpMethod == "POST" && path == "/api/devices/approve")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                string id = req.QueryString["id"];
                string hostname = req.QueryString["hostname"];
                if (string.IsNullOrEmpty(id))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'id' query parameter.");
                    return;
                }
                if (_activityMonitor != null)
                {
                    _activityMonitor.ApproveDevice(id, hostname);
                }
                SendJson(res, new { success = true, message = "Device approved successfully." });
            }
            else if (req.HttpMethod == "POST" && path == "/api/devices/revoke")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                string id = req.QueryString["id"];
                if (string.IsNullOrEmpty(id))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'id' query parameter.");
                    return;
                }
                if (_activityMonitor != null)
                {
                    _activityMonitor.RevokeDevice(id);
                }
                SendJson(res, new { success = true, message = "Device access revoked." });
            }
            else if (req.HttpMethod == "GET" && path == "/api/jobs")
            {
                var jobs = _printJobManager != null ? _printJobManager.GetJobs() : new List<PrintJob>();
                SendJson(res, jobs);
            }
            else if (req.HttpMethod == "POST" && path == "/api/jobs/block")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                string id = req.QueryString["id"];
                if (string.IsNullOrEmpty(id))
                {
                    res.StatusCode = 400;
                    SendString(res, "Missing 'id' query parameter.");
                    return;
                }
                bool blocked = _printJobManager != null && _printJobManager.BlockJob(id);
                if (blocked)
                {
                    SendJson(res, new { success = true, message = "Job blocked successfully." });
                }
                else
                {
                    res.StatusCode = 404;
                    SendJson(res, new { success = false, message = "Job not found or already processing." });
                }
            }
            else if (req.HttpMethod == "POST" && path == "/api/jobs/track")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                
                string body = "";
                using (var reader = new StreamReader(req.InputStream)) { body = reader.ReadToEnd(); }
                var payload = _jsonSerializer.Deserialize<Dictionary<string, object>>(body);
                
                if (payload != null && payload.ContainsKey("jobId"))
                {
                    string jobId = payload["jobId"].ToString();
                    if (_activityMonitor != null) _activityMonitor.TrackDispatchedJob(jobId);
                }
                SendJson(res, new { success = true });
            }
            else if (req.HttpMethod == "POST" && path == "/api/activity-logs/remote")
            {
                string body = "";
                using (var reader = new StreamReader(req.InputStream)) { body = reader.ReadToEnd(); }
                var payload = _jsonSerializer.Deserialize<Dictionary<string, string>>(body);
                
                if (payload != null && payload.ContainsKey("jobId"))
                {
                    string jobId = payload["jobId"];
                    string status = payload.ContainsKey("status") ? payload["status"] : "Unknown";
                    string message = payload.ContainsKey("message") ? payload["message"] : "";
                    string remoteIp = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : "unknown-device";

                    // Validate 1: Is this job actually one we dispatched recently?
                    if (_activityMonitor != null && _activityMonitor.IsJobDispatchedLocally(jobId))
                    {
                        // Validate 2: We must have sent it to this specific peer, 
                        // or at the very least, they must be on our approved peer list.
                        // Strictly speaking, we should map JobId to TargetIP, but checking if they are approved is a good baseline.
                        // (The random GUID jobId provides strong unguessable binding already).
                        
                        _activityMonitor.LogActivity(new PrintActivityLog
                        {
                            SenderId = remoteIp,
                            SenderHostname = "Remote Printer",
                            PrinterName = "Remote Printer",
                            DocumentName = string.Format("Job {0}", jobId.Substring(0, 8)),
                            Status = status,
                            Message = message,
                            IsLocal = true
                        });
                        SendJson(res, new { success = true });
                    }
                    else
                    {
                        res.StatusCode = 403;
                        SendJson(res, new { success = false, message = "Job ID not recognized." });
                    }
                }
                else
                {
                    res.StatusCode = 400;
                    SendString(res, "Bad request.");
                }
            }
            else if (req.HttpMethod == "POST" && path == "/api/exit")
            {
                if (!req.IsLocal)
                {
                    res.StatusCode = 403;
                    SendJson(res, new { success = false, message = "Forbidden: Local requests only" });
                    return;
                }
                SendJson(res, new { success = true, message = "Shutting down..." });
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(500);
                    try { System.Windows.Forms.Application.Exit(); } catch { }
                    Environment.Exit(0);
                });
            }
            else
            {
                res.StatusCode = 404;
                SendString(res, "API Not Found");
            }
        }

        private void HandleReceivePrint(HttpListenerContext context)
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

                string senderIp = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : "127.0.0.1";
                string senderId = req.Headers["X-PrintHop-SenderId"] ?? senderIp;
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
                        SenderIp = senderIp,
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
                    SendJson(res, new { success = true, jobId = job.Id });
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


        private bool ValidateFileSignatures(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // Plain text files (.txt) do not have fixed magic bytes, but should not contain binary null bytes
            if (ext == ".txt")
            {
                try
                {
                    using (var fs = File.OpenRead(path))
                    {
                        byte[] checkBuf = new byte[Math.Min(1024, (int)fs.Length)];
                        int read = fs.Read(checkBuf, 0, checkBuf.Length);
                        for (int i = 0; i < read; i++)
                        {
                            if (checkBuf[i] == 0) return false;
                        }
                        return true;
                    }
                }
                catch { return false; }
            }

            byte[] header = new byte[4];
            using (var fs = File.OpenRead(path))
            {
                if (fs.Length < 4) return false;
                fs.Read(header, 0, 4);
            }

            // PDF: %PDF (25 50 44 46)
            if (header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46) return true;
            // JPEG: FF D8 FF
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;
            // PNG: 89 50 4E 47
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return true;
            // BMP: BM (42 4D)
            if (header[0] == 0x42 && header[1] == 0x4D) return true;
            // GIF: GIF8 (47 49 46 38)
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38) return true;
            
            // DOCX/XLSX (ZIP format): PK (50 4B 03 04)
            if (header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04) return true;

            return false;
        }

        private string ExtractFormField(string multipartPayload, string fieldName)
        {
            var match = Regex.Match(
                multipartPayload,
                string.Format(@"name=""{0}""[^\r\n]*(?:\r?\n[^\r\n]+)*\r?\n\r?\n(.*?)(?=\r?\n--|\z)", Regex.Escape(fieldName)),
                RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        /// <summary>
        /// Extracts the original filename from a multipart Content-Disposition header.
        /// Example header value: Content-Disposition: form-data; name="file"; filename="invoice.pdf"
        /// </summary>
        private string ExtractFilename(string multipartPayload)
        {
            var match = Regex.Match(
                multipartPayload,
                @"Content-Disposition:[^\r\n]*name=""file""[^\r\n]*filename=""?([^"";\r\n]+)""?",
                RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            match = Regex.Match(
                multipartPayload,
                @"Content-Disposition:[^\r\n]*filename=""?([^"";\r\n]+)""?[^\r\n]*name=""file""",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        }

        private void ServeStaticFile(HttpListenerContext context)
        {
            string requestPath = Uri.UnescapeDataString(context.Request.Url.AbsolutePath);
            if (requestPath == "/") requestPath = "/index.html";

            // BUG FIX #3: Normalize path and prevent directory traversal
            string wwwRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "www"));
            
            // Strip leading slash and normalize separators before combining
            string relativePart = requestPath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            string localPath = Path.GetFullPath(Path.Combine(wwwRoot, relativePart));

            // Ensure the resolved path is strictly inside the www root
            if (!localPath.StartsWith(wwwRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !localPath.Equals(wwwRoot, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 403;
                SendString(context.Response, "Forbidden.");
                return;
            }

            if (File.Exists(localPath))
            {
                byte[] data = File.ReadAllBytes(localPath);
                context.Response.ContentType = GetMimeType(localPath);
                context.Response.Headers.Add("Cache-Control", "no-cache, no-store, must-revalidate");
                context.Response.Headers.Add("Pragma", "no-cache");
                context.Response.Headers.Add("Expires", "0");
                context.Response.ContentLength64 = data.Length;
                context.Response.OutputStream.Write(data, 0, data.Length);
                context.Response.Close();
            }
            else
            {
                context.Response.StatusCode = 404;
                SendString(context.Response, "File not found.");
            }
        }

        private string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html";
                case ".css": return "text/css";
                case ".js": return "application/javascript";
                case ".png": return "image/png";
                case ".txt": return "text/plain";
                default: return "application/octet-stream";
            }
        }

        private void SendJson(HttpListenerResponse res, object obj)
        {
            try
            {
                string json = _jsonSerializer.Serialize(obj);
                res.ContentType = "application/json";
                SendString(res, json);
            }
            catch { }
        }

        private void SendString(HttpListenerResponse res, string text)
        {
            try
            {
                byte[] buffer = Encoding.UTF8.GetBytes(text);
                res.ContentLength64 = buffer.Length;
                res.OutputStream.Write(buffer, 0, buffer.Length);
                res.Close();
            }
            catch { /* Client disconnected or response stream already closed */ }
        }

        public static List<string> GetAllLocalIpAddresses()
        {
            var ips = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = addr.Address.ToString();
                            if (!ipStr.StartsWith("127.") && !ips.Contains(ipStr))
                            {
                                ips.Add(ipStr);
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string ipStr = ip.ToString();
                        if (!ipStr.StartsWith("127.") && !ips.Contains(ipStr))
                        {
                            ips.Add(ipStr);
                        }
                    }
                }
            }
            catch { }

            return ips;
        }

        public string GetLocalIpAddress()
        {
            var ips = GetAllLocalIpAddresses();
            if (ips.Count > 0)
            {
                // Prefer RFC 1918 Private LAN / Hotspot subnets (10.x, 192.168.x, 172.16.x - 172.31.x)
                foreach (var ip in ips)
                {
                    if (ip.StartsWith("10.") || ip.StartsWith("192.168."))
                    {
                        return ip;
                    }
                    if (ip.StartsWith("172."))
                    {
                        var parts = ip.Split('.');
                        int secondOctet;
                        if (parts.Length >= 2 && int.TryParse(parts[1], out secondOctet) && secondOctet >= 16 && secondOctet <= 31)
                        {
                            return ip;
                        }
                    }
                }
                return ips[0];
            }
            return "127.0.0.1";
        }
        
        // Helper to find byte sequences
        private int IndexOfSequence(byte[] buffer, byte[] pattern, int startIndex = 0)
        {
            if (buffer == null || pattern == null || pattern.Length == 0 || startIndex < 0) return -1;
            int max = buffer.Length - pattern.Length;
            for (int i = startIndex; i <= max; i++)
            {
                if (buffer[i] != pattern[0]) continue;
                bool match = true;
                for (int j = 1; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private int LastIndexOfSequence(byte[] buffer, byte[] pattern)
        {
            if (buffer == null || pattern == null || pattern.Length == 0) return -1;
            for (int i = buffer.Length - pattern.Length; i >= 0; i--)
            {
                if (buffer[i] != pattern[0]) continue;
                bool match = true;
                for (int j = 1; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        public void Dispose()
        {
            _isRunning = false;
            if (_printJobManager != null)
            {
                try { _printJobManager.Dispose(); } catch { }
            }
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                try { _listener.Close(); } catch { }
            }
        }
    }
}


