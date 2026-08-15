using System;
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
