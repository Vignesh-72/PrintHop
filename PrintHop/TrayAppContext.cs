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
        
        private HttpServer _httpServer;
        private UdpDiscovery _udpDiscovery;
        private IPrintService _printService;
        private string _localId;
        private int _httpPort;

        public TrayAppContext()
        {
            _jsonSerializer = new JavaScriptSerializer();
            _whitelistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whitelist.json");
            _whitelist = LoadWhitelist();

            // Initialize tray icon
            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenu = new ContextMenu(new[]
                {
                    new MenuItem("Open Web UI", OpenWebUI),
                    new MenuItem("-"),
                    new MenuItem("Exit", Exit)
                }),
                Visible = true,
                Text = "PrintHop"
            };
            
            _trayIcon.DoubleClick += OpenWebUI;

            StartServices();
            
            _trayIcon.ShowBalloonTip(3000, "PrintHop Started", string.Format("Web UI available at http://localhost:{0}", _httpPort), ToolTipIcon.Info);
        }

        private void StartServices()
        {
            _localId = Guid.NewGuid().ToString();
            _printService = new PrintService();
            
            // We pass the whitelist check function to HttpServer
            _udpDiscovery = new UdpDiscovery(_localId, 4222, _printService); // HTTP port passed, will update later if it changes
            _httpServer = new HttpServer(_localId, _udpDiscovery, _printService, WhitelistCheck);
            
            _httpPort = _httpServer.Start();
            
            // If the port changed from 4222 because it was taken, we need to restart discovery with the correct port
            _udpDiscovery.Dispose();
            _udpDiscovery = new UdpDiscovery(_localId, _httpPort, _printService);
            _udpDiscovery.Start();
        }

        private bool WhitelistCheck(string senderId, string senderHostname)
        {
            if (string.IsNullOrEmpty(senderId)) return false;

            lock (_whitelist)
            {
                if (_whitelist.Contains(senderId))
                {
                    return true;
                }
            }

            // If not in whitelist, we must ask the user on the UI thread or a new thread.
            // Since HttpServer calls this from a background thread, we can block its thread
            // with a MessageBox, but it's safer to Invoke it on the main thread.
            
            bool approved = false;
            
            // Wait for user interaction
            var dr = MessageBox.Show(
                string.Format("Incoming print job from '{0}' (ID: {1}).\n\nDo you want to accept this and future print jobs from this device?", senderHostname, senderId), 
                "PrintHop - New Device", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Question, 
                MessageBoxDefaultButton.Button2,
                MessageBoxOptions.DefaultDesktopOnly);
                
            if (dr == DialogResult.Yes)
            {
                approved = true;
                lock (_whitelist)
                {
                    _whitelist.Add(senderId);
                    SaveWhitelist();
                }
            }

            return approved;
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
                File.WriteAllText(_whitelistPath, json);
            }
            catch (Exception) { }
        }

        private void OpenWebUI(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = string.Format("http://localhost:{0}", _httpPort),
                UseShellExecute = true
            });
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
