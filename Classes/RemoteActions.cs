// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    RemoteActions.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-11
// Purpose: Actions on one remote computer offered by the right-click menus of
//          the main window and of the process reports: ping, remote desktop,
//          management console, admin share and public folders; the local
//          TeamViewer (connection to a TeamViewer ID).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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

                    MessageBox.Show(reply.Status == IPStatus.Success ? Localization.T("RemoteActions.PingOk", "Ping OK.") : Localization.T("RemoteActions.PingFailed", "Ping failed."), "Ping: " + host, MessageBoxButtons.OK,
                                    reply.Status == IPStatus.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }

                catch
                {
                    MessageBox.Show(Localization.T("RemoteActions.NotFound", "Network device could not be found."), Localization.T("RemoteActions.PingError.Title", "Ping Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        static public void RemoteDesktop(String host)
        {
            using (Process.Start(new ProcessStartInfo("mstsc", "/v:" + host))) { }
        }

        /// <summary>The TeamViewer of this computer (TeamViewer.exe), null when it is not installed.</summary>
        static public String LocalTeamViewer()
        {
            List<String> candidates = new List<String>();
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = hklm.OpenSubKey(@"SOFTWARE\TeamViewer"))
                    {
                        String dir = key == null ? null : key.GetValue("InstallationDirectory") as String;
                        if (!String.IsNullOrWhiteSpace(dir)) candidates.Add(Path.Combine(dir.Trim(), "TeamViewer.exe"));
                    }
                }
                catch (Exception) { }
            }
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                String root = Environment.GetFolderPath(folder);
                if (!String.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "TeamViewer", "TeamViewer.exe"));
            }
            return candidates.FirstOrDefault(File.Exists);
        }

        /// <summary>Arguments of TeamViewer.exe to connect to an ID (documented command line: -i | --id).</summary>
        static public String TeamViewerArguments(String id)
        {
            String digits = new String((id ?? "").Where(Char.IsDigit).ToArray());
            if (digits.Length == 0) throw new ArgumentException("A TeamViewer ID is required.", "id");
            return "-i " + digits;
        }

        /// <summary>Starts the local TeamViewer connected to that ID; false when TeamViewer is not installed here.</summary>
        static public bool ConnectTeamViewer(String id)
        {
            String exe = LocalTeamViewer();
            if (exe == null) return false;
            using (Process.Start(new ProcessStartInfo(exe, TeamViewerArguments(id)))) { }
            return true;
        }

        /// <summary>Computer Management (RemoteManagement.msc, shipped next to LiteDeploy.exe) on the computer.</summary>
        static public void ManagementConsole(String host)
        {
            String msc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteManagement.msc");

            if (!File.Exists(msc))
            {
                MessageBox.Show(Localization.F("RemoteActions.MscMissing", "RemoteManagement.msc was not found next to LiteDeploy.exe:\n{0}", msc), Localization.T("RemoteActions.MscMissing.Title", "Management Console"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                    MessageBox.Show(Localization.F("RemoteActions.HostUnavailable", "{0} is not available. Try again later.", host), Localization.T("RemoteActions.HostUnavailable.Title", "Host Not Available"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
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
