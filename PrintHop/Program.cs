using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PrintHop
{
    static class Program
    {
        private static Mutex _mutex;

        /// <summary>
        /// Appends a message to %TEMP%\PrintHop\crash.log, rotating the file when it exceeds 1 MB.
        /// Writing here avoids UAC-protected Program Files paths where AppendAllText would throw.
        /// </summary>
        private static void AppendCrashLog(string message)
        {
            try
            {
                string logDir = Path.Combine(Path.GetTempPath(), "PrintHop");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "crash.log");
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > 1024 * 1024) // Rotate at 1 MB
                    File.Delete(logPath);
                File.AppendAllText(logPath, message);
            }
            catch { /* Cannot log the logger */ }
        }

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                string msg = DateTime.Now + ": UNHANDLED EXCEPTION: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null") + "\n";
                AppendCrashLog(msg);
            };

            Application.ThreadException += (s, e) =>
            {
                string msg = DateTime.Now + ": THREAD EXCEPTION: " + (e.Exception != null ? e.Exception.ToString() : "null") + "\n";
                AppendCrashLog(msg);
            };

            const string appName = "Global\\PrintHop_SingleInstance";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running
                MessageBox.Show("PrintHop is already running.", "PrintHop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            try 
            {
                Application.Run(new TrayAppContext());
            }
            catch (Exception ex)
            {
                AppendCrashLog(DateTime.Now + ": STARTUP ERROR: " + ex.ToString() + "\n");
                MessageBox.Show(ex.ToString(), "PrintHop Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                }
            }
        }
    }
}
