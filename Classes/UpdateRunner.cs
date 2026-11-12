// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    UpdateRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-12
// Purpose: Runs LiteDeploy.Worker.exe on one computer through PsExec, as SYSTEM
//          (-s), the worker being copied by PsExec (-c -f): no share, no
//          scheduled task and no stored password on the computer. Returns the
//          worker messages, or why the computer could not be reached.
//          Also: where the package list and the worker are.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LiteSuite;

namespace LiteDeploy
{
    internal sealed class UpdateRunResult
    {
        public List<WorkerMessage> Messages = new List<WorkerMessage>();
        /// <summary>Why the run failed when the worker could not report (null otherwise).</summary>
        public String Failure;
    }

    internal static class UpdateRunner
    {
        public const String WorkerFileName = "LiteDeploy.Worker.exe";
        /// <summary>Minutes per winget call on the computer.</summary>
        public const int WingetTimeoutMinutes = 15;

        static String AppDir
        {
            get { return Path.GetDirectoryName(typeof(UpdateRunner).Assembly.Location); }
        }

        public static String WorkerPath
        {
            get { return Path.Combine(AppDir, WorkerFileName); }
        }

        /// <summary>The package list of the user (%LOCALAPPDATA%\LiteDeploy\packages.txt).</summary>
        public static String UserPackagesPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteDeploy", "packages.txt"); }
        }

        /// <summary>The example list shipped with LiteDeploy (updates\packages.txt).</summary>
        public static String DefaultPackagesPath
        {
            get { return Path.Combine(AppDir, "updates", "packages.txt"); }
        }

        /// <summary>The user's list when it exists, else the shipped one.</summary>
        public static List<UpdatePackage> LoadPackages()
        {
            return UpdatePackage.ParseFile(File.Exists(UserPackagesPath) ? UserPackagesPath : DefaultPackagesPath);
        }

        /// <summary>Saves the user's package list (%LOCALAPPDATA%\LiteDeploy\packages.txt), with its format explained on top.</summary>
        public static void SavePackages(IEnumerable<UpdatePackage> list)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# LiteDeploy - software updates: winget packages to keep up to date.");
            sb.AppendLine("# One package per line:  winget Id | processes to close (without .exe, comma-separated) | label");
            foreach (UpdatePackage p in list)
                sb.AppendLine(p.Id + " | " + String.Join(", ", p.Processes) + " | " + (p.Label ?? ""));
            Directory.CreateDirectory(Path.GetDirectoryName(UserPackagesPath));
            File.WriteAllText(UserPackagesPath, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Worker arguments: the command, then one --package per package.</summary>
        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages)
        {
            return BuildWorkerArguments(command, packages, false);
        }

        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages, bool allowUnsignedModules)
        {
            return BuildWorkerArguments(command, packages, allowUnsignedModules, null);
        }

        /// <summary>With <paramref name="cacheShare"/>: the shared cache of the installers of the modules (--cache).</summary>
        public static String BuildWorkerArguments(String command, IEnumerable<UpdatePackage> packages, bool allowUnsignedModules, String cacheShare)
        {
            StringBuilder sb = new StringBuilder(command);
            foreach (UpdatePackage p in packages)
                sb.Append(" --package ").Append(PsExecCommandLine.QuoteArgument(p.ToArgument(), true));
            sb.Append(" --timeout ").Append(WingetTimeoutMinutes);
            if (allowUnsignedModules && packages.Any(p => p.IsModule)) sb.Append(" --allow-unsigned-modules");
            if (!String.IsNullOrWhiteSpace(cacheShare) && packages.Any(p => p.IsModule))
                sb.Append(" --cache ").Append(PsExecCommandLine.QuoteArgument(cacheShare.Trim().TrimEnd('\\'), true));
            return sb.ToString();
        }

        public static PsExecOptions BuildOptions(String computer, String command, IList<UpdatePackage> packages)
        {
            return new PsExecOptions
            {
                Computer = computer,
                RunAsSystem = true,
                CopyExecutable = true,
                OverwriteExisting = true,
                ConnectionTimeout = 20,
                Executable = WorkerPath,
                Arguments = BuildWorkerArguments(command, packages, UserSettings.Current.AllowUnsignedModules, UserSettings.Current.UpdateCacheShare)
            };
        }

        /// <summary>Runs the worker on one computer. <paramref name="started"/> receives the PsExec process (to stop it).</summary>
        public static UpdateRunResult Run(String computer, String command, IList<UpdatePackage> packages, Action<Process> started)
        {
            return Run(computer, command, packages, started, null);
        }

        /// <summary><paramref name="onMessage"/> receives each worker message as soon as it arrives (live display).</summary>
        public static UpdateRunResult Run(String computer, String command, IList<UpdatePackage> packages, Action<Process> started, Action<WorkerMessage> onMessage)
        {
            UpdateRunResult result = new UpdateRunResult();
            if (PsExecLocator.Path == null) { result.Failure = "PsExec not found"; return result; }
            if (!File.Exists(WorkerPath)) { result.Failure = WorkerFileName + " not found next to LiteDeploy.exe"; return result; }
            // Update modules: copied first to the administrators-only folder of the computer
            String copyError = UpdateModules.CopyTo(computer, packages);
            if (copyError != null) { result.Failure = copyError; return result; }

            ProcessStartInfo psi = new ProcessStartInfo(PsExecLocator.Path, PsExecCommandBuilder.BuildArguments(BuildOptions(computer, command, packages)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Enough for every winget call of every package (list, upgrade, list), plus the copy
            int timeoutMs = (int)TimeSpan.FromMinutes(5 + packages.Count * WingetTimeoutMinutes * (command == "upgrade" ? 3 : 1)).TotalMilliseconds;

            using (Process p = Process.Start(psi))
            {
                if (started != null) started(p);
                // Read line by line: the worker flushes each message, PsExec relays it as it comes
                Task<String> stdout = Task.Run(() =>
                {
                    StringBuilder all = new StringBuilder();
                    String line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        all.AppendLine(line);
                        WorkerMessage m = WorkerProtocol.Parse(line);
                        if (m != null && onMessage != null)
                        {
                            try { onMessage(m); } catch (Exception) { }
                        }
                    }
                    return all.ToString();
                });
                Task<String> stderr = p.StandardError.ReadToEndAsync();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { p.Kill(); } catch (Exception) { }
                }
                Task.WaitAll(new Task[] { stdout, stderr }, 10000);
                String output = stdout.IsCompleted ? stdout.Result : "";
                String errors = stderr.IsCompleted ? stderr.Result : "";
                result.Messages = WorkerProtocol.ParseAll(output);

                if (!exited)
                    result.Failure = "Timed out";
                else if (result.Messages.Count == 0)
                    result.Failure = DescribeFailure(p.ExitCode, errors + "\n" + output);
            }
            return result;
        }

        // PsExec's own message (last meaningful line of its output), e.g. "The network path was not found."
        static String DescribeFailure(int exitCode, String text)
        {
            String last = text.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !PsExecCommandLine.IsStatusLine(l) && !l.StartsWith("PsExec v", StringComparison.OrdinalIgnoreCase)
                            && l.IndexOf("Sysinternals", StringComparison.OrdinalIgnoreCase) < 0 && !l.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase))
                .LastOrDefault();
            return "PsExec exit code " + exitCode + (last != null ? ": " + last : "");
        }
    }
}
