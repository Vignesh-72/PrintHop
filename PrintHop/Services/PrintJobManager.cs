using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly SemaphoreSlim _jobSignal = new SemaphoreSlim(0);
        private readonly Task _workerTask;
        
        private Process _currentPrintProcess;
        private string _currentPrintJobId;
        private volatile bool _cancelCurrentJob;

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
            _jobSignal.Release();
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
                else if (job != null && job.Status == "Printing")
                {
                    CancelCurrentJob(jobId);
                    return true;
                }
                return false;
            }
        }

        public bool CancelCurrentJob(string jobId)
        {
            lock (_lock)
            {
                if (_currentPrintJobId == jobId)
                {
                    _cancelCurrentJob = true;
                    try
                    {
                        if (_currentPrintProcess != null && !_currentPrintProcess.HasExited)
                        {
                            _currentPrintProcess.Kill();
                        }
                    }
                    catch { }
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
                        lock (_lock)
                        {
                            _currentPrintJobId = nextJob.Id;
                            _cancelCurrentJob = false;
                        }

                        var process = _printService.PrintFile(nextJob.TempFilePath, nextJob.PrinterName, nextJob.Options);
                        
                        if (process != null)
                        {
                            lock (_lock)
                            {
                                _currentPrintProcess = process;
                            }

                            // Wait up to 30 seconds for the application to spool the print job
                            bool exited = false;
                            for (int i = 0; i < 300; i++) // 30 seconds (100ms intervals)
                            {
                                if (_cancelCurrentJob || token.IsCancellationRequested) break;
                                if (process.WaitForExit(100))
                                {
                                    exited = true;
                                    break;
                                }
                            }

                            if (_cancelCurrentJob)
                            {
                                throw new Exception("Print job was cancelled by the user.");
                            }
                            if (!exited)
                            {
                                try { if (!process.HasExited) process.Kill(); } catch { }
                                throw new TimeoutException("Print application timed out after 30 seconds and was terminated.");
                            }
                        }

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
                            _currentPrintProcess = null;
                            _currentPrintJobId = null;
                            _cancelCurrentJob = false;
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
                    await _jobSignal.WaitAsync(5000, token).ConfigureAwait(false);
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

