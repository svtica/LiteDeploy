// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    WingetClient.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Finds and runs winget.exe. As SYSTEM, winget is not on the PATH: it
//          is taken from the newest App Installer folder under WindowsApps
//          (finding of the MAJ-Logiciels tool, which runs it as SYSTEM).
// -----------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiteDeploy.Worker
{
    internal sealed class WingetResult
    {
        public int ExitCode;
        public string Output = "";
        public bool TimedOut;

        public string Hex { get { return WingetClient.Hex(ExitCode); } }
    }

    internal sealed class WingetClient
    {
        public const int NoApplicableUpgrade = unchecked((int)0x8A15002B);
        public const int PackageNotInstalled = unchecked((int)0x8A150014);
        public const int NoPackageFound = unchecked((int)0x8A150010);
        public const int SourcesUnavailable = unchecked((int)0x8A15000F);

        readonly string path;
        readonly TimeSpan timeout;

        public WingetClient(string path, TimeSpan timeout)
        {
            this.path = path;
            this.timeout = timeout;
        }

        public string Path { get { return path; } }

        /// <summary>winget.exe of the newest App Installer for x64, else "winget" when it is on the PATH; null when not found.</summary>
        public static string Locate()
        {
            string apps = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            try
            {
                string newest = Directory.GetDirectories(apps, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                    .Select(d => System.IO.Path.Combine(d, "winget.exe"))
                    .Where(File.Exists)
                    .OrderBy(VersionOf)
                    .LastOrDefault();
                if (newest != null) return newest;
            }
            catch (Exception) { }   // no access to WindowsApps (not SYSTEM): try the PATH

            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    string candidate = System.IO.Path.Combine(dir.Trim(), "winget.exe");
                    if (dir.Trim().Length > 0 && File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        // Microsoft.DesktopAppInstaller_1.23.1911.0_x64__8wekyb3d8bbwe -> 1.23.1911.0
        static Version VersionOf(string wingetPath)
        {
            string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(wingetPath));
            string[] parts = folder.Split('_');
            Version v;
            return parts.Length > 1 && Version.TryParse(parts[1], out v) ? v : new Version(0, 0);
        }

        public WingetResult List(string id)
        {
            return Run("list --id " + id + " -e --source winget --accept-source-agreements --disable-interactivity");
        }

        public WingetResult Upgrade(string id)
        {
            return Run("upgrade --id " + id + " -e --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
        }

        WingetResult Run(string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (Process p = Process.Start(psi))
            {
                // Both streams read at the same time: reading one after the other can block winget
                Task<string> stdout = p.StandardOutput.ReadToEndAsync();
                Task<string> stderr = p.StandardError.ReadToEndAsync();
                WingetResult result = new WingetResult();
                if (!p.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { p.Kill(); } catch (Exception) { }
                    result.TimedOut = true;
                    result.ExitCode = -1;
                }
                else
                {
                    p.WaitForExit();
                    result.ExitCode = p.ExitCode;
                }
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                result.Output = (stdout.IsCompleted ? stdout.Result : "") + (stderr.IsCompleted ? stderr.Result : "");
                return result;
            }
        }

        public static string Hex(int code)
        {
            return "0x" + code.ToString("X8");
        }

        /// <summary>Readable meaning of the winget exit codes met in practice.</summary>
        public static string Describe(int code)
        {
            switch (code)
            {
                case 0: return "OK";
                case NoApplicableUpgrade: return "No applicable upgrade (already up to date)";
                case PackageNotInstalled: return "Package not installed";
                case NoPackageFound: return "No package found in the winget source";
                case SourcesUnavailable: return "winget sources unavailable";
                case unchecked((int)0x80190193): return "Download forbidden (HTTP 403): check the web filter or proxy";
                case -1: return "Timed out";
                default: return "Unknown code";
            }
        }
    }
}
