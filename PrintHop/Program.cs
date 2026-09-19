using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PrintHop
{
    static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                string msg = DateTime.Now + ": UNHANDLED EXCEPTION: " + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "null") + "\n";
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), msg);
            };

            Application.ThreadException += (s, e) =>
            {
                string msg = DateTime.Now + ": THREAD EXCEPTION: " + (e.Exception != null ? e.Exception.ToString() : "null") + "\n";
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), msg);
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
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), ex.ToString());
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
