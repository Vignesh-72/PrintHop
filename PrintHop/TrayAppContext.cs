using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using PrintHop.Services;

namespace PrintHop
{
    public class TrayAppContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly string _whitelistPath;
        private readonly HashSet<string> _whitelist;
        private readonly JavaScriptSerializer _jsonSerializer;
        private readonly ActivityMonitorService _activityMonitor;
        private readonly Dictionary<string, System.Threading.Tasks.Task<bool>> _pendingDialogs = new Dictionary<string, System.Threading.Tasks.Task<bool>>(StringComparer.OrdinalIgnoreCase);
        private readonly object _dialogLock = new object();
        
        private HttpServer _httpServer;
        private UdpDiscovery _udpDiscovery;
        private IPrintService _printService;
        private string _localId;
        private int _httpPort;

        public TrayAppContext()
        {
            _jsonSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            _whitelistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whitelist.json");
            _whitelist = LoadWhitelist();
            _activityMonitor = new ActivityMonitorService();

            // Try to load the custom application icon, fallback to system icon if it fails
            Icon appIcon = SystemIcons.Application;
            try
            {
                appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch { }

            // Initialize tray icon
            _trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                ContextMenu = new ContextMenu(new[]
                {
                    new MenuItem("Open Web UI", OpenWebUI),
                    new MenuItem("-"),
                    new MenuItem("Exit", Exit)
                }),
                Visible = true,
                Text = "PrintHop"
            };
            
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    OpenWebUI(s, e);
                }
            };
            _trayIcon.DoubleClick += (s, e) => OpenWebUI(s, e);

            StartServices();
            
            // Auto-open the web UI on launch
            OpenWebUI(null, EventArgs.Empty);

            if (_httpServer.BoundOnLan)
            {
                string lanIp = _httpServer.GetLocalIpAddress();
                _trayIcon.ShowBalloonTip(4000, "PrintHop Started (LAN & Mobile Ready)", 
                    string.Format("Web UI & LAN printing live at http://{0}:{1}", lanIp, _httpPort), ToolTipIcon.Info);
            }
            else
            {
                _trayIcon.ShowBalloonTip(5000, "PrintHop Started (Localhost)", 
                    string.Format("Running at http://localhost:{0}. Note: For your phone or other PCs to print, right-click PrintHop.exe and choose 'Run as administrator'.", _httpPort), ToolTipIcon.Info);
            }
        }
        private void StartServices()
        {
            _localId = Guid.NewGuid().ToString();
            _printService = new PrintService();
            
            // 1. Ensure Windows Firewall and URL ACL reservations are configured first
            FirewallService.EnsureFirewallConfigured();

            // 2. Start HTTP server
            _udpDiscovery = new UdpDiscovery(_localId, 4222, _printService); // HTTP port passed, will update later if it changes
            var printJobManager = new PrintJobManager(_printService, _activityMonitor);
            _httpServer = new HttpServer(_localId, _udpDiscovery, _printService, WhitelistCheck, _activityMonitor, printJobManager);
            _httpPort = _httpServer.Start();

            // 3. If the port changed from 4222 because it was taken, restart discovery with the correct port
            _udpDiscovery.Dispose();
            _udpDiscovery = new UdpDiscovery(_localId, _httpPort, _printService);
            _udpDiscovery.Start();
        }

        private bool WhitelistCheck(string senderId, string senderHostname)
        {
            if (string.IsNullOrEmpty(senderId)) return false;

            // Automatically trust print jobs originating from this local instance
            if (senderId == _localId) return true;

            // Check if device is explicitly blocked
            if (_activityMonitor != null && _activityMonitor.IsDeviceBlocked(senderId))
            {
                return false;
            }

            // Check if device is already approved in activity monitor
            if (_activityMonitor != null && _activityMonitor.IsDeviceApproved(senderId))
            {
                return true;
            }

            lock (_whitelist)
            {
                if (_whitelist.Contains(senderId))
                {
                    if (_activityMonitor != null)
                    {
                        _activityMonitor.ApproveDevice(senderId, senderHostname);
                    }
                    return true;
                }
            }

            // Deduplicate concurrent approval dialogs for the same device
            System.Threading.Tasks.Task<bool> dialogTask;
            lock (_dialogLock)
            {
                if (_activityMonitor != null && _activityMonitor.IsDeviceApproved(senderId)) return true;
                if (_activityMonitor != null && _activityMonitor.IsDeviceBlocked(senderId)) return false;

                if (!_pendingDialogs.TryGetValue(senderId, out dialogTask))
                {
                    dialogTask = System.Threading.Tasks.Task.Run(() => PromptUserForApproval(senderId, senderHostname));
                    _pendingDialogs[senderId] = dialogTask;
                }
            }

            try
            {
                // Timeout after 45 seconds so worker thread isn't held open indefinitely
                if (dialogTask.Wait(TimeSpan.FromSeconds(45)))
                {
                    return dialogTask.Result;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                lock (_dialogLock)
                {
                    _pendingDialogs.Remove(senderId);
                }
            }
        }

        private bool PromptUserForApproval(string senderId, string senderHostname)
        {
            try
            {
                var dr = MessageBox.Show(
                    string.Format("Incoming print job from '{0}' (ID: {1}).\n\nDo you want to accept this and future print jobs from this device?", senderHostname, senderId), 
                    "PrintHop - New Device", 
                    MessageBoxButtons.YesNo, 
                    MessageBoxIcon.Question, 
                    MessageBoxDefaultButton.Button2);
                    
                if (dr == DialogResult.Yes)
                {
                    if (_activityMonitor != null)
                    {
                        _activityMonitor.ApproveDevice(senderId, senderHostname);
                    }
                    lock (_whitelist)
                    {
                        _whitelist.Add(senderId);
                        SaveWhitelist();
                    }
                    return true;
                }
                else
                {
                    if (_activityMonitor != null)
                    {
                        _activityMonitor.RecordDeviceSeen(senderId, senderHostname);
                    }
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private HashSet<string> LoadWhitelist()
        {
            try
            {
                if (File.Exists(_whitelistPath))
                {
                    string json = File.ReadAllText(_whitelistPath);
                    var list = _jsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                        return new HashSet<string>(list);
                }
            }
            catch (Exception) { }
            
            return new HashSet<string>();
        }

        private void SaveWhitelist()
        {
            try
            {
                var list = new List<string>(_whitelist);
                string json = _jsonSerializer.Serialize(list);
                
                // BUG FIX #6: Use atomic write-then-replace to prevent whitelist.json corruption
                // on crash or power loss. Previously File.WriteAllText wrote directly, leaving
                // a truncated/empty file if the process was killed mid-write, which silently
                // wiped all approved device authorizations on next launch.
                string tempPath = _whitelistPath + ".tmp";
                File.WriteAllText(tempPath, json);
                
                // File.Replace atomically swaps the temp file into place (kernel-level rename)
                if (File.Exists(_whitelistPath))
                    File.Replace(tempPath, _whitelistPath, _whitelistPath + ".bak");
                else
                    File.Move(tempPath, _whitelistPath);
            }
            catch (Exception) { }
        }

        private void OpenWebUI(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = string.Format("http://localhost:{0}", _httpPort),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to open web UI: " + ex.Message);
            }
        }

        private void Exit(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            
            if (_httpServer != null) _httpServer.Dispose();
            if (_udpDiscovery != null) _udpDiscovery.Dispose();
            
            Application.Exit();
        }
    }
}
