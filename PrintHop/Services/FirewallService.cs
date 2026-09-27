using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace PrintHop.Services
{
    public static class FirewallService
    {
        /// <summary>
        /// Checks if the current process is running with elevated Administrator privileges.
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detects if Windows Defender Firewall is currently enabled/active on the active network profile.
        /// </summary>
        public static bool IsFirewallActive()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall show currentprofile state",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return output.IndexOf("ON", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the PrintHop inbound firewall rules for Ports 4222–4230 already exist in Windows Firewall.
        /// </summary>
        public static bool IsRuleConfigured()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=\"PrintHop TCP\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    // Check that the base rule exists (covers 4222-4230)
                    return p.ExitCode == 0 && output.IndexOf("4222", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if URL ACL reservations for PrintHop ports already exist in Windows HTTP.SYS.
        /// </summary>
        public static bool IsUrlAclConfigured()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "http show urlacl url=http://+:4222/",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    // Check that the primary port is reserved; if it is, we likely set up all ports
                    return output.IndexOf("4222", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Automatically checks Windows Firewall status and URL ACL reservations for Port 4222.
        /// Configures both if missing so that mobile devices on Wi-Fi/Hotspot/USB can connect without 'Bad Request - Invalid Hostname'.
        /// </summary>
        public static void EnsureFirewallConfigured()
        {
            bool needFirewallRule = IsFirewallActive() && !IsRuleConfigured();
            bool needUrlAcl = !IsUrlAclConfigured();

            if (!needFirewallRule && !needUrlAcl)
                return;

            string cmd = "/c ";
            if (needFirewallRule)
            {
                // Cover all fallback ports 4222–4230 so LAN access works regardless of which port is bound
                cmd += "netsh advfirewall firewall add rule name=\"PrintHop TCP\" dir=in action=allow protocol=TCP localport=4222-4230 profile=any & " +
                       "netsh advfirewall firewall add rule name=\"PrintHop UDP\" dir=in action=allow protocol=UDP localport=4222 profile=any & ";
            }
            if (needUrlAcl)
            {
                // Reserve URL ACLs for all fallback ports so http://+:PORT/ binding works without admin
                for (int port = 4222; port <= 4230; port++)
                {
                    cmd += string.Format("netsh http add urlacl url=http://+:{0}/ user=Everyone & ", port);
                    cmd += string.Format("netsh http add urlacl url=http://*:{0}/ user=Everyone & ", port);
                }
            }
            cmd = cmd.TrimEnd(' ', '&');

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmd,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (IsAdministrator())
            {
                psi.UseShellExecute = false;
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit();
                    }
                }
                catch { }
            }
            else
            {
                // Trigger UAC elevation prompt once to allow the user to approve the firewall and URLACL rule
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        p.WaitForExit();
                    }
                }
                catch (Win32Exception)
                {
                    // User declined UAC elevation prompt; continue gracefully
                }
                catch { }
            }
        }
    }
}
