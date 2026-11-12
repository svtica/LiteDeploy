// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    Program.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Entry point of LiteDeploy.Worker.exe, started by LiteDeploy through
//          PsExec as SYSTEM on each target computer:
//            LiteDeploy.Worker.exe scan    --package "Id|proc1,proc2|Label" ...
//            LiteDeploy.Worker.exe upgrade --package "Id|proc1,proc2|Label" ...
//                                  [--timeout <minutes per winget call>]
//          scan reports the installed and available version of each package;
//          upgrade closes the package's processes and upgrades it when an
//          upgrade is available. Results: WorkerProtocol lines on stdout.
//          Exit code: number of failures (0: everything went well).
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace LiteDeploy.Worker
{
    internal static class Program
    {
        const int ExitUsage = 100;
        const int ExitNoWinget = 101;

        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);

            string command;
            List<UpdatePackage> packages;
            int timeoutMinutes;
            string usageError = ParseArguments(args, out command, out packages, out timeoutMinutes);
            if (usageError != null)
            {
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", usageError));
                Console.Error.WriteLine("Usage: LiteDeploy.Worker.exe scan|upgrade --package \"Id|processes|label\" ... [--timeout minutes]");
                return ExitUsage;
            }

            string wingetPath = WingetClient.Locate();
            Emit(WorkerProtocol.Format(
                "type", "start",
                "computer", Environment.MachineName,
                "user", WindowsIdentity.GetCurrent().Name,
                "command", command,
                "winget", wingetPath ?? ""));
            if (wingetPath == null)
            {
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeError, "message", "winget.exe not found (App Installer missing)"));
                Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "failed", packages.Count.ToString()));
                return ExitNoWinget;
            }

            WingetClient winget = new WingetClient(wingetPath, TimeSpan.FromMinutes(timeoutMinutes));
            int failed = 0, upgraded = 0;
            foreach (UpdatePackage package in packages)
            {
                PackageState state = Check(winget, package);
                Emit(state.ToMessage());
                if (state.Status == WorkerProtocol.StatusError) { failed++; continue; }
                if (command != "upgrade") continue;

                if (state.Status != WorkerProtocol.StatusOutdated)
                {
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultSkipped, null,
                        state.Status == WorkerProtocol.StatusNotInstalled ? "Not installed" : "Already up to date"));
                    continue;
                }

                string closeError = CloseProcesses(package);
                if (closeError != null)
                {
                    failed++;
                    Emit(UpgradeMessage(package, WorkerProtocol.ResultFailed, null, closeError));
                    continue;
                }

                WingetResult result = winget.Upgrade(package.Id);
                bool ok = result.ExitCode == 0;
                bool nothing = result.ExitCode == WingetClient.NoApplicableUpgrade;
                if (ok) upgraded++;
                if (!ok && !nothing) failed++;
                Emit(UpgradeMessage(package,
                    ok ? WorkerProtocol.ResultUpgraded : nothing ? WorkerProtocol.ResultSkipped : WorkerProtocol.ResultFailed,
                    result.Hex,
                    WingetClient.Describe(result.ExitCode) + (ok ? "" : LastLines(result.Output))));

                // State after the upgrade (new installed version)
                Emit(Check(winget, package).ToMessage());
            }

            Emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "upgraded", upgraded.ToString(), "failed", failed.ToString()));
            return Math.Min(failed, 99);
        }

        sealed class PackageState
        {
            public UpdatePackage Package;
            public string Status;
            public string Installed;
            public string Available;
            public string Message;

            public string ToMessage()
            {
                return WorkerProtocol.Format(
                    "type", WorkerProtocol.TypePackage,
                    "id", Package.Id,
                    "status", Status,
                    "installed", Installed,
                    "available", Available,
                    "message", Message);
            }
        }

        static PackageState Check(WingetClient winget, UpdatePackage package)
        {
            PackageState state = new PackageState { Package = package };
            WingetResult list = winget.List(package.Id);
            if (list.ExitCode == WingetClient.PackageNotInstalled)
            {
                state.Status = WorkerProtocol.StatusNotInstalled;
                return state;
            }
            WingetListRow row = list.ExitCode == 0 ? WingetListParser.FindRow(list.Output, package.Id) : null;
            if (row == null)
            {
                state.Status = WorkerProtocol.StatusError;
                state.Message = "winget list " + list.Hex + " " + WingetClient.Describe(list.ExitCode) + LastLines(list.Output);
                return state;
            }
            state.Installed = row.Version;
            state.Available = row.Available;
            state.Status = row.Available == null ? WorkerProtocol.StatusUpToDate : WorkerProtocol.StatusOutdated;
            return state;
        }

        // Force-closes the package's processes (SYSTEM cannot ask them to close); null when none is left running
        static string CloseProcesses(UpdatePackage package)
        {
            List<string> closed = new List<string>();
            foreach (string name in package.Processes)
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    using (p)
                    {
                        try { p.Kill(); closed.Add(name); }
                        catch (Exception) { }
                    }
                }
            }
            if (closed.Count == 0) return null;

            Thread.Sleep(3000);
            string[] still = package.Processes.Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
            if (still.Length > 0) return "Could not close " + string.Join(", ", still);
            Emit(WorkerProtocol.Format("type", "closed", "id", package.Id, "processes", string.Join(", ", closed.Distinct())));
            return null;
        }

        static string UpgradeMessage(UpdatePackage package, string result, string code, string message)
        {
            return WorkerProtocol.Format("type", WorkerProtocol.TypeUpgrade, "id", package.Id, "result", result, "code", code, "message", message);
        }

        // Last meaningful lines of a winget output (no spinner or progress bar), for a failure message
        static string LastLines(string output)
        {
            if (string.IsNullOrEmpty(output)) return "";
            string[] lines = output.Split('\n')
                .Select(l => { int cr = l.TrimEnd('\r').LastIndexOf('\r'); return (cr >= 0 ? l.Substring(cr + 1) : l).Trim(); })
                .Where(l => l.Length > 2 && l.Any(char.IsLetter) && l.IndexOf('█') < 0 && l.IndexOf('▒') < 0)
                .ToArray();
            return lines.Length == 0 ? "" : ": " + string.Join(" / ", lines.Skip(Math.Max(0, lines.Length - 3)));
        }

        static void Emit(string line)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
        }

        internal static string ParseArguments(string[] args, out string command, out List<UpdatePackage> packages, out int timeoutMinutes)
        {
            command = null;
            packages = new List<UpdatePackage>();
            timeoutMinutes = 15;
            if (args == null || args.Length == 0) return "No command";

            command = args[0].ToLowerInvariant();
            if (command != "scan" && command != "upgrade") return "Unknown command: " + args[0];

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--package" && i + 1 < args.Length)
                {
                    UpdatePackage p = UpdatePackage.Parse(args[++i]);
                    if (p == null) return "Invalid package: " + args[i];
                    if (!packages.Any(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) packages.Add(p);
                }
                else if (a == "--timeout" && i + 1 < args.Length)
                {
                    int t;
                    if (!int.TryParse(args[++i], out t) || t < 1 || t > 240) return "Invalid timeout: " + args[i];
                    timeoutMinutes = t;
                }
                else
                {
                    return "Unknown argument: " + a;
                }
            }
            if (packages.Count == 0) return "No package";
            return null;
        }
    }
}
