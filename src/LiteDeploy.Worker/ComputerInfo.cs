// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerInfo.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: "LiteDeploy.Worker.exe info": the computer card of LiteDeploy
//          (logged-on users, last restart, pending restart, Windows version,
//          free space on C:, physical network adapters for Wake-on-LAN), as
//          one WorkerProtocol message. From the identity check of the
//          MAJ-Logiciels panel.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace LiteDeploy.Worker
{
    internal static class ComputerInfo
    {
        public const string TypeInfo = "info";
        public const string TypeProcess = "process";

        /// <summary>
        /// "LiteDeploy.Worker.exe processes": one message per process, the most handles first (handle
        /// and memory monitoring of a computer, as LitePM shows them locally), then the totals.
        /// </summary>
        public static List<string> Processes(int top)
        {
            List<string> lines = new List<string>();
            List<System.Diagnostics.Process> all = System.Diagnostics.Process.GetProcesses().ToList();
            long totalHandles = 0;
            List<Tuple<string, int, int, long, int>> rows = new List<Tuple<string, int, int, long, int>>();
            foreach (System.Diagnostics.Process p in all)
            {
                using (p)
                {
                    try
                    {
                        int handles = p.HandleCount;
                        totalHandles += handles;
                        rows.Add(Tuple.Create(p.ProcessName, p.Id, handles, p.PrivateMemorySize64, p.Threads.Count));
                    }
                    catch (Exception) { }   // process ended meanwhile, or protected
                }
            }
            foreach (Tuple<string, int, int, long, int> r in rows.OrderByDescending(r => r.Item3).Take(top))
            {
                lines.Add(WorkerProtocol.Format(
                    "type", TypeProcess,
                    "name", r.Item1,
                    "pid", r.Item2.ToString(),
                    "handles", r.Item3.ToString(),
                    "privatemb", Math.Round(r.Item4 / 1048576.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "threads", r.Item5.ToString()));
            }
            lines.Add(WorkerProtocol.Format("type", "processes", "count", rows.Count.ToString(), "handles", totalHandles.ToString()));
            return lines;
        }

        public static string Collect()
        {
            List<string> users = new List<string>();
            string lastBoot = null, os = null;
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT LastBootUpTime, Caption, Version FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection results = s.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            lastBoot = ManagementDateTimeConverter.ToDateTime((string)o["LastBootUpTime"]).ToString("yyyy-MM-dd HH:mm");
                            os = o["Caption"] + " " + o["Version"];
                        }
                    }
                }
                // Users with a desktop (console or remote desktop): owners of explorer.exe
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Handle FROM Win32_Process WHERE Name = 'explorer.exe'"))
                using (ManagementObjectCollection results = s.Get())
                {
                    foreach (ManagementObject p in results)
                    {
                        using (p)
                        {
                            object[] owner = new object[2];
                            if (Convert.ToInt32(p.InvokeMethod("GetOwner", owner)) == 0 && owner[0] != null)
                            {
                                string name = (owner[1] != null ? owner[1] + "\\" : "") + owner[0];
                                if (!users.Contains(name, StringComparer.OrdinalIgnoreCase)) users.Add(name);
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            string release = RegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            if (os != null && release != null) os += " (" + release + ")";

            string free = null;
            try
            {
                DriveInfo c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                free = Math.Round(c.AvailableFreeSpace / 1073741824.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB";
            }
            catch (Exception) { }

            List<string> macs, ips;
            Network(out macs, out ips);

            return WorkerProtocol.Format(
                "type", TypeInfo,
                "computer", Environment.MachineName,
                "users", string.Join(", ", users),
                "lastboot", lastBoot,
                "pendingrestart", PendingRestart() ? "yes" : "no",
                "os", os,
                "freec", free,
                "macs", string.Join(" ", macs),
                "ips", string.Join(" ", ips));
        }

        /// <summary>Physical Ethernet and Wi-Fi adapters: MAC addresses (Wake-on-LAN) and IPv4 addresses of those that are up.</summary>
        public static void Network(out List<string> macs, out List<string> ips)
        {
            macs = new List<string>();
            ips = new List<string>();
            try
            {
                foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.NetworkInterfaceType != NetworkInterfaceType.Ethernet && n.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    string description = (n.Description ?? "").ToLowerInvariant();
                    if (description.Contains("virtual") || description.Contains("hyper-v") || description.Contains("vpn") || description.Contains("loopback")) continue;
                    string mac = string.Join("-", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                    if (mac.Length == 17 && !macs.Contains(mac)) macs.Add(mac);
                    if (n.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (UnicastIPAddressInformation a in n.GetIPProperties().UnicastAddresses)
                            if (a.Address.AddressFamily == AddressFamily.InterNetwork) ips.Add(a.Address.ToString());
                    }
                }
            }
            catch (Exception) { }
        }

        // The usual signs of a pending restart (Windows Update, servicing, file renames)
        static bool PendingRestart()
        {
            return KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending")
                || KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
                || RegistryValue(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") != null;
        }

        static bool KeyExists(string key)
        {
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key)) return k != null; }
            catch (Exception) { return false; }
        }

        static object RegistryValue(string key, string name)
        {
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key)) return k?.GetValue(name); }
            catch (Exception) { return null; }
        }

        static string RegistryString(string key, string name)
        {
            return RegistryValue(key, name) as string;
        }
    }
}
