using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using PrintHop.Models;

namespace PrintHop.Services
{
    public class ActivityMonitorService
    {
        private readonly string _logsPath;
        private readonly string _devicesPath;
        private readonly string _legacyWhitelistPath;
        private readonly JavaScriptSerializer _serializer;
        private readonly object _lock = new object();

        private readonly List<PrintActivityLog> _logs = new List<PrintActivityLog>();
        private readonly Dictionary<string, DeviceInfo> _devices = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dispatchedJobs = new HashSet<string>();

        private const int MaxLogHistory = 100;

        public ActivityMonitorService()
        {
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _logsPath = Path.Combine(baseDir, "activity_logs.json");
            _devicesPath = Path.Combine(baseDir, "devices.json");
            _legacyWhitelistPath = Path.Combine(baseDir, "whitelist.json");

            LoadDevices();
            LoadLogs();
        }

        public void TrackDispatchedJob(string jobId)
        {
            lock (_lock)
            {
                _dispatchedJobs.Add(jobId);
                if (_dispatchedJobs.Count > 1000) _dispatchedJobs.Clear(); // simple bounded cache
            }
        }

        public bool IsJobDispatchedLocally(string jobId)
        {
            lock (_lock)
            {
                return _dispatchedJobs.Contains(jobId);
            }
        }

        public void LogActivity(PrintActivityLog log)
        {
            if (log == null) return;

            lock (_lock)
            {
                // Prepend latest entry to front
                _logs.Insert(0, log);
                if (_logs.Count > MaxLogHistory)
                {
                    _logs.RemoveRange(MaxLogHistory, _logs.Count - MaxLogHistory);
                }

                // Update device stats
                if (!string.IsNullOrEmpty(log.SenderId))
                {
                    DeviceInfo dev;
                    if (!_devices.TryGetValue(log.SenderId, out dev))
                    {
                        dev = new DeviceInfo
                        {
                            Id = log.SenderId,
                            Hostname = log.SenderHostname ?? "Unknown Device"
                        };
                        _devices[log.SenderId] = dev;
                    }

                    dev.LastSeen = DateTime.Now;
                    if (!string.IsNullOrEmpty(log.SenderHostname))
                    {
                        dev.Hostname = log.SenderHostname;
                    }

                    if (log.Status == "Success")
                    {
                        dev.TotalPrints++;
                    }
                }

                SaveLogs();
                SaveDevices();
            }
        }

        public List<PrintActivityLog> GetRecentLogs(int limit = 50)
        {
            lock (_lock)
            {
                return _logs.Take(limit).ToList();
            }
        }

        public void ClearLogs()
        {
            lock (_lock)
            {
                _logs.Clear();
                SaveLogs();
            }
        }

        public List<DeviceInfo> GetDevices()
        {
            lock (_lock)
            {
                return _devices.Values.OrderByDescending(d => d.LastSeen).ToList();
            }
        }

        public bool IsDeviceBlocked(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;

            lock (_lock)
            {
                DeviceInfo dev;
                if (_devices.TryGetValue(deviceId, out dev))
                {
                    return dev.IsBlocked;
                }
                return false;
            }
        }

        public bool IsDeviceApproved(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;

            lock (_lock)
            {
                DeviceInfo dev;
                if (_devices.TryGetValue(deviceId, out dev))
                {
                    return dev.IsApproved && !dev.IsBlocked;
                }
                return false;
            }
        }

        public void ApproveDevice(string deviceId, string hostname = null)
        {
            if (string.IsNullOrEmpty(deviceId)) return;

            lock (_lock)
            {
                DeviceInfo dev;
                if (!_devices.TryGetValue(deviceId, out dev))
                {
                    dev = new DeviceInfo { Id = deviceId };
                    _devices[deviceId] = dev;
                }

                dev.IsApproved = true;
                dev.IsBlocked = false;
                dev.LastSeen = DateTime.Now;
                if (!string.IsNullOrEmpty(hostname))
                {
                    dev.Hostname = hostname;
                }

                SaveDevices();
            }
        }

        public void BlockDevice(string deviceId, string hostname = null)
        {
            if (string.IsNullOrEmpty(deviceId)) return;

            lock (_lock)
            {
                DeviceInfo dev;
                if (!_devices.TryGetValue(deviceId, out dev))
                {
                    dev = new DeviceInfo { Id = deviceId };
                    _devices[deviceId] = dev;
                }

                dev.IsBlocked = true;
                dev.IsApproved = false;
                dev.LastSeen = DateTime.Now;
                if (!string.IsNullOrEmpty(hostname))
                {
                    dev.Hostname = hostname;
                }

                SaveDevices();
            }
        }

        public void UnblockDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;

            lock (_lock)
            {
                DeviceInfo dev;
                if (_devices.TryGetValue(deviceId, out dev))
                {
                    dev.IsBlocked = false;
                    SaveDevices();
                }
            }
        }

        public void RevokeDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;

            lock (_lock)
            {
                DeviceInfo dev;
                if (_devices.TryGetValue(deviceId, out dev))
                {
                    dev.IsApproved = false;
                    SaveDevices();
                }
            }
        }

        public void RecordDeviceSeen(string deviceId, string hostname)
        {
            if (string.IsNullOrEmpty(deviceId)) return;

            lock (_lock)
            {
                DeviceInfo dev;
                if (!_devices.TryGetValue(deviceId, out dev))
                {
                    dev = new DeviceInfo
                    {
                        Id = deviceId,
                        Hostname = hostname ?? "Unknown Device"
                    };
                    _devices[deviceId] = dev;
                }
                else
                {
                    dev.LastSeen = DateTime.Now;
                    if (!string.IsNullOrEmpty(hostname))
                    {
                        dev.Hostname = hostname;
                    }
                }

                SaveDevices();
            }
        }

        private void LoadDevices()
        {
            try
            {
                if (File.Exists(_devicesPath))
                {
                    string json = File.ReadAllText(_devicesPath);
                    var list = _serializer.Deserialize<List<DeviceInfo>>(json);
                    if (list != null)
                    {
                        foreach (var d in list)
                        {
                            if (!string.IsNullOrEmpty(d.Id))
                            {
                                _devices[d.Id] = d;
                            }
                        }
                    }
                }

                // Seamless migration from legacy whitelist.json
                if (File.Exists(_legacyWhitelistPath))
                {
                    string wJson = File.ReadAllText(_legacyWhitelistPath);
                    var wList = _serializer.Deserialize<List<string>>(wJson);
                    if (wList != null)
                    {
                        foreach (var id in wList)
                        {
                            if (!string.IsNullOrEmpty(id) && !_devices.ContainsKey(id))
                            {
                                _devices[id] = new DeviceInfo
                                {
                                    Id = id,
                                    Hostname = "Authorized Device",
                                    IsApproved = true
                                };
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveDevices()
        {
            try
            {
                var list = _devices.Values.ToList();
                string json = _serializer.Serialize(list);
                AtomicWrite(_devicesPath, json);

                // Keep legacy whitelist.json in sync for backward compatibility
                var approvedIds = _devices.Values
                    .Where(d => d.IsApproved && !d.IsBlocked)
                    .Select(d => d.Id)
                    .ToList();
                string wJson = _serializer.Serialize(approvedIds);
                AtomicWrite(_legacyWhitelistPath, wJson);
            }
            catch { }
        }

        private void LoadLogs()
        {
            try
            {
                if (File.Exists(_logsPath))
                {
                    string json = File.ReadAllText(_logsPath);
                    var list = _serializer.Deserialize<List<PrintActivityLog>>(json);
                    if (list != null)
                    {
                        _logs.AddRange(list);
                    }
                }
            }
            catch { }
        }

        private void SaveLogs()
        {
            try
            {
                string json = _serializer.Serialize(_logs);
                AtomicWrite(_logsPath, json);
            }
            catch { }
        }

        private void AtomicWrite(string filePath, string content)
        {
            try
            {
                string temp = filePath + ".tmp";
                File.WriteAllText(temp, content);
                if (File.Exists(filePath))
                {
                    File.Replace(temp, filePath, filePath + ".bak");
                }
                else
                {
                    File.Move(temp, filePath);
                }
            }
            catch
            {
                try { File.WriteAllText(filePath, content); } catch { }
            }
        }
    }
}
