// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteActions.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Actions on one remote computer offered by the right-click menus of
//          the main window and of the process reports: ping, remote desktop,
//          management console, admin share and public folders.
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace LiteDeploy
{
    static public class RemoteActions
    {
        /// <summary>Pings the computer in the background and shows the result.</summary>
        static public void Ping(String host)
        {
            if (String.IsNullOrWhiteSpace(host))
                return;

            RunInBackground(delegate
            {
                try
                {
                    PingReply reply;

                    using (Ping pingSender = new Ping())
                    {
                        reply = pingSender.Send(host, 5000);
                    }

                    MessageBox.Show(reply.Status == IPStatus.Success ? "Ping OK." : "Ping failed.", "Ping: " + host, MessageBoxButtons.OK,
                                    reply.Status == IPStatus.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }

                catch
                {
                    MessageBox.Show("Network device could not be found.", "Ping Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        static public void RemoteDesktop(String host)
        {
            using (Process.Start(new ProcessStartInfo("mstsc", "/v:" + host))) { }
        }

        /// <summary>Computer Management (RemoteManagement.msc, shipped next to LiteDeploy.exe) on the computer.</summary>
        static public void ManagementConsole(String host)
        {
            String msc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteManagement.msc");

            if (!File.Exists(msc))
            {
                MessageBox.Show("RemoteManagement.msc was not found next to LiteDeploy.exe:\n" + msc, "Management Console", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (Process.Start(new ProcessStartInfo("mmc", "\"" + msc + "\" /computer:" + host))) { }
        }

        /// <summary>Opens \\host\C$\subPath in Explorer once the computer answers the ping.</summary>
        static public void OpenAdminShare(String host, String subPath)
        {
            if (String.IsNullOrWhiteSpace(host))
                return;

            String path = "\\\\" + host + "\\C$" + (String.IsNullOrEmpty(subPath) ? "" : "\\" + subPath);

            RunInBackground(delegate
            {
                if (!IsAvailable(host))
                {
                    MessageBox.Show(host + " is not available. Try again later.", "Host Not Available", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }

                try
                {
                    using (Process.Start("explorer.exe", "\"" + path + "\"")) { }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, "Explorer Error: " + e.Source, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        static public bool IsAvailable(String host)
        {
            try
            {
                using (Ping pingSender = new Ping())
                {
                    return pingSender.Send(host, 2000).Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        static private void RunInBackground(ThreadStart work)
        {
            Thread t = new Thread(work);
            t.IsBackground = true;
            t.Start();
        }
    }
}
