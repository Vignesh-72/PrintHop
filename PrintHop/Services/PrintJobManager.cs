using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PrintHop.Models;

namespace PrintHop.Services
{
    public class PrintJobManager : IDisposable
    {
        private readonly List<PrintJob> _jobs = new List<PrintJob>();
        private readonly object _lock = new object();
        private readonly IPrintService _printService;
        private readonly ActivityMonitorService _activityMonitor;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _workerTask;

        public PrintJobManager(IPrintService printService, ActivityMonitorService activityMonitor)
        {
            _printService = printService;
            _activityMonitor = activityMonitor;
            _workerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        public void Enqueue(PrintJob job)
        {
            lock (_lock)
            {
                job.Status = "Queued";
                _jobs.Add(job);
            }
        }

        public IEnumerable<PrintJob> GetJobs()
        {
            lock (_lock)
            {
                return new List<PrintJob>(_jobs);
            }
        }

        public bool BlockJob(string jobId)
        {
            lock (_lock)
            {
                var job = _jobs.Find(j => j.Id == jobId);
                if (job != null && job.Status == "Queued")
                {
                    job.Status = "Blocked";
                    _jobs.Remove(job);

                    try
                    {
                        if (!string.IsNullOrEmpty(job.TempFilePath) && File.Exists(job.TempFilePath))
                        {
                            File.Delete(job.TempFilePath);
                        }
                    }
                    catch { }

                    if (_activityMonitor != null)
                    {
                        _activityMonitor.LogActivity(new PrintActivityLog
                        {
                            SenderId = job.SenderId,
                            SenderHostname = job.SenderHostname,
                            PrinterName = job.PrinterName,
                            DocumentName = job.DocumentName,
                            OptionsSummary = job.OptionsSummary,
                            Status = "Blocked",
                            Message = "Print job was blocked by the host.",
                            IsLocal = job.IsLocal
                        });
                    }
                    SendWebhook(job, "Blocked", "Print job was blocked by the host.");
                    return true;
                }
                return false;
            }
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                PrintJob nextJob = null;

                lock (_lock)
                {
                    nextJob = _jobs.Find(j => j.Status == "Queued");
                    if (nextJob != null)
                    {
                        nextJob.Status = "Printing";
                    }
                }

                if (nextJob != null)
                {
                    try
                    {
                        _printService.PrintFile(nextJob.TempFilePath, nextJob.PrinterName, nextJob.Options);

                        if (_activityMonitor != null)
                        {
                            _activityMonitor.LogActivity(new PrintActivityLog
                            {
                                SenderId = nextJob.SenderId,
                                SenderHostname = nextJob.SenderHostname,
                                PrinterName = nextJob.PrinterName,
                                DocumentName = nextJob.DocumentName,
                                OptionsSummary = nextJob.OptionsSummary,
                                Status = "Success",
                                Message = "Print job dispatched successfully to printer.",
                                IsLocal = nextJob.IsLocal
                            });
                        }
                        SendWebhook(nextJob, "Success", "Print job dispatched successfully to printer.");
                    }
                    catch (Exception ex)
                    {
                        if (_activityMonitor != null)
                        {
                            _activityMonitor.LogActivity(new PrintActivityLog
                            {
                                SenderId = nextJob.SenderId,
                                SenderHostname = nextJob.SenderHostname,
                                PrinterName = nextJob.PrinterName,
                                DocumentName = nextJob.DocumentName,
                                OptionsSummary = nextJob.OptionsSummary,
                                Status = "Failed",
                                Message = ex.Message,
                                IsLocal = nextJob.IsLocal
                            });
                        }
                        SendWebhook(nextJob, "Failed", ex.Message);
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            _jobs.Remove(nextJob);
                        }

                        try
                        {
                            if (!string.IsNullOrEmpty(nextJob.TempFilePath) && File.Exists(nextJob.TempFilePath))
                            {
                                File.Delete(nextJob.TempFilePath);
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }
        }

            private void SendWebhook(PrintJob job, string status, string message)
        {
            if (job.IsLocal || string.IsNullOrEmpty(job.SenderIp)) return;
            if (_activityMonitor != null && !_activityMonitor.IsDeviceApproved(job.SenderId)) return;
            
            Task.Run(() =>
            {
                try
                {
                    string url = string.Format("http://{0}:4222/api/activity-logs/remote", job.SenderIp);
                    var payload = new { jobId = job.Id, status = status, message = message };
                    var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                    string json = serializer.Serialize(payload);
                    
                    using (var client = new System.Net.WebClient())
                    {
                        client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                        client.UploadString(url, "POST", json);
                    }
                }
                catch { }
            });
        }

    public void Dispose()
        {
            _cts.Cancel();
            try { _workerTask.Wait(2000); } catch { }
            _cts.Dispose();
        }
    }
}

