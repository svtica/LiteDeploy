// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ComputerInfo.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: "LiteDeploy.Worker.exe info": the computer card of LiteDeploy
//          (logged-on users, last restart, pending restart, Windows version,
//          operating system type (ServerPolicy), free space on C:, physical network adapters for Wake-on-LAN,
//          TeamViewer ID: empty when TeamViewer is not installed), as
//          one WorkerProtocol message. From the identity check of the
//          previous update panel.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>Users with a desktop (console or remote desktop): owners of explorer.exe; empty when none or unreadable.</summary>
        public static List<string> LoggedOnUsers()
        {
            List<string> users = new List<string>();
            try
            {
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
            return users;
        }

        public static string Collect()
        {
            List<string> users = new List<string>();
            string lastBoot = null, os;
            int productType = ReadOperatingSystem(out os);
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection results = s.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            lastBoot = ManagementDateTimeConverter.ToDateTime((string)o["LastBootUpTime"]).ToString("yyyy-MM-dd HH:mm");
                        }
                    }
                }
            }
            catch (Exception) { }
            users = LoggedOnUsers();

            string free = null;
            try
            {
                DriveInfo c = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                free = Math.Round(c.AvailableFreeSpace / 1073741824.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB";
            }
            catch (Exception) { }

            List<string> macs, ips;
            Network(out macs, out ips);

            List<string> fields = new List<string>
            {
                "type", TypeInfo,
                "computer", Environment.MachineName,
                "users", string.Join(", ", users),
                "lastboot", lastBoot,
                "pendingrestart", PendingRestart() ? "yes" : "no",
                "os", os,
                "ostype", ServerPolicy.OsType(productType),
                "freec", free,
                "macs", string.Join(" ", macs),
                "ips", string.Join(" ", ips),
                "teamviewer", ReadTeamViewerId() ?? ""
            };
            fields.AddRange(Hardware());
            return WorkerProtocol.Format(fields.ToArray());
        }

        /// <summary>
        /// Inventory of the hardware, as key/value pairs for a worker line (also on the start line of a check or an
        /// update): manufacturer, model, serial (BIOS serial number), biosdate (yyyy-MM-dd, release date of the
        /// BIOS: about the year of manufacture), cpu (name of the first processor), sockets, cores, threads, ram
        /// (installed memory, GB), disk and diskfree (system drive, GB). A value that cannot be read is left out.
        /// </summary>
        public static List<string> Hardware()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            List<string> pairs = new List<string>();
            Action<string, string> add = (k, v) => { if (!string.IsNullOrWhiteSpace(v)) { pairs.Add(k); pairs.Add(v.Trim()); } };
            try
            {
                foreach (ManagementObject o in Query("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    add("manufacturer", Convert.ToString(o["Manufacturer"], inv));
                    add("model", Convert.ToString(o["Model"], inv));
                }
            }
            catch (Exception) { }
            try
            {
                foreach (ManagementObject o in Query("SELECT SerialNumber, ReleaseDate FROM Win32_BIOS"))
                {
                    add("serial", Convert.ToString(o["SerialNumber"], inv));
                    string release = Convert.ToString(o["ReleaseDate"], inv);
                    if (!string.IsNullOrEmpty(release))
                    {
                        try { add("biosdate", ManagementDateTimeConverter.ToDateTime(release).ToString("yyyy-MM-dd", inv)); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            try
            {
                string cpu = null;
                int sockets = 0, cores = 0, threads = 0;
                foreach (ManagementObject o in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    sockets++;
                    if (cpu == null) cpu = Convert.ToString(o["Name"], inv);
                    cores += Convert.ToInt32(o["NumberOfCores"] ?? 0, inv);
                    threads += Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0, inv);
                }
                add("cpu", cpu == null ? null : System.Text.RegularExpressions.Regex.Replace(cpu, @"\s+", " "));
                if (sockets > 0) add("sockets", sockets.ToString(inv));
                if (cores > 0) add("cores", cores.ToString(inv));
                if (threads > 0) add("threads", threads.ToString(inv));
            }
            catch (Exception) { }
            try
            {
                ulong bytes = 0;
                foreach (ManagementObject o in Query("SELECT Capacity FROM Win32_PhysicalMemory"))
                    bytes += Convert.ToUInt64(o["Capacity"] ?? 0UL, inv);
                if (bytes > 0) add("ram", Math.Round(bytes / 1073741824.0).ToString(inv));
            }
            catch (Exception) { }
            try
            {
                DriveInfo system = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));
                add("disk", Math.Round(system.TotalSize / 1073741824.0).ToString(inv));
                add("diskfree", Math.Round(system.AvailableFreeSpace / 1073741824.0, 1).ToString(inv));
            }
            catch (Exception) { }
            return pairs;
        }

        // The objects of a WMI query, disposed after use by the caller's loop
        static IEnumerable<ManagementObject> Query(string wql)
        {
            using (ManagementObjectSearcher s = new ManagementObjectSearcher(wql))
            using (ManagementObjectCollection results = s.Get())
            {
                foreach (ManagementObject o in results)
                {
                    using (o) yield return o;
                }
            }
        }

        /// <summary>Keys where TeamViewer keeps its ID: 64-bit, then 32-bit installation.</summary>
        static readonly string[] TeamViewerKeys = { @"SOFTWARE\TeamViewer", @"SOFTWARE\WOW6432Node\TeamViewer" };

        /// <summary>
        /// Values holding the ID: ClientID, else ClientIDOfTSUser (the ID of the user session on a
        /// computer with Remote Desktop Services).
        /// </summary>
        static readonly string[] TeamViewerValues = { "ClientID", "ClientIDOfTSUser" };

        /// <summary>The TeamViewer ID of the computer (digits), null when TeamViewer is not installed.</summary>
        public static string ReadTeamViewerId()
        {
            return FindTeamViewerId(RegistryValue, SubKeyNames);
        }

        /// <summary>
        /// The first ID found under the TeamViewer keys: the key itself, then its "VersionN" subkeys
        /// (older versions), the newest first. <paramref name="read"/>(key, value) reads a value of
        /// HKLM, <paramref name="subKeys"/>(key) lists the subkeys (unit-tested with fakes).
        /// </summary>
        internal static string FindTeamViewerId(Func<string, string, object> read, Func<string, IEnumerable<string>> subKeys)
        {
            foreach (string root in TeamViewerKeys)
            {
                List<string> keys = new List<string> { root };
                keys.AddRange((subKeys(root) ?? Enumerable.Empty<string>())
                    .Where(n => n != null && n.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(n => VersionNumber(n))
                    .Select(n => root + @"\" + n));
                foreach (string key in keys)
                {
                    foreach (string value in TeamViewerValues)
                    {
                        string id = FormatTeamViewerId(read(key, value));
                        if (id != null) return id;
                    }
                }
            }
            return null;
        }

        static int VersionNumber(string subKey)
        {
            int n;
            return int.TryParse(subKey.Substring("Version".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        /// <summary>
        /// A registry value as a TeamViewer ID: a DWORD (read as a signed Int32: taken as unsigned), a
        /// QWORD or a string of digits (spaces allowed); null when empty, zero or not a number.
        /// </summary>
        internal static string FormatTeamViewerId(object value)
        {
            long id;
            if (value is int)
                id = unchecked((uint)(int)value);
            else if (value is long)
                id = (long)value;
            else if (value is string text)
            {
                text = text.Replace(" ", "").Trim();
                if (!long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id)) return null;
            }
            else
                return null;
            return id > 0 ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }

        static IEnumerable<string> SubKeyNames(string key)
        {
            try { using (RegistryKey k = Registry.LocalMachine.OpenSubKey(key)) return k == null ? new string[0] : k.GetSubKeyNames(); }
            catch (Exception) { return new string[0]; }
        }

        /// <summary>
        /// Windows caption, version and release (<paramref name="os"/>, null when unreadable) and
        /// Win32_OperatingSystem.ProductType (ServerPolicy.Product*): from WMI, else from the registry
        /// (ProductOptions); ServerPolicy.ProductUnknown when neither can be read.
        /// </summary>
        public static int ReadOperatingSystem(out string os)
        {
            os = null;
            int productType = ServerPolicy.ProductUnknown;
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Caption, Version, ProductType FROM Win32_OperatingSystem"))
                using (ManagementObjectCollection results = s.Get())
                {
                    foreach (ManagementObject o in results)
                    {
                        using (o)
                        {
                            os = o["Caption"] + " " + o["Version"];
                            if (o["ProductType"] != null) productType = Convert.ToInt32(o["ProductType"]);
                        }
                    }
                }
            }
            catch (Exception) { }
            if (ServerPolicy.OsType(productType) == null)
                productType = ServerPolicy.ProductTypeFromRegistry(RegistryString(@"SYSTEM\CurrentControlSet\Control\ProductOptions", "ProductType"));

            string release = RegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            if (os != null && release != null) os += " (" + release + ")";
            return productType;
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
