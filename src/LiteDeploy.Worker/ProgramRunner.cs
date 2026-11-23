// -----------------------------------------------------------------------------
// LiteDeploy - part of LiteSuite. Released under The Unlicense (see LICENSE).
// File:    ProgramRunner.cs
// Author:  LiteDeploy contributors
// Date:    2026-11-23
// Purpose: Worker commands of "Uninstall another program" (ProgramPolicy):
//          "programs" lists the entries of the Uninstall keys of HKLM (64 and
//          32-bit views) with how each can be removed (read only, every
//          computer); "uninstall-program" reads the entry again, checks that it
//          still has the expected name and is removable, then runs msiexec /x
//          {GUID} /qn /norestart (MSI) or the quiet uninstall command of the
//          vendor (EXE under Program Files or Windows), with a time limit, and
//          checks that the entry is gone. Same server guard as the updates.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace LiteDeploy.Worker
{
    internal static class ProgramRunner
    {
        const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>Program Files, Program Files (x86) and Windows: where a quiet uninstall executable may be.</summary>
        public static List<string> AllowedRoots()
        {
            return new List<string>
            {
                Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };
        }

        public static List<ProgramEntry> ReadAll()
        {
            List<ProgramEntry> list = new List<ProgramEntry>();
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey root = baseKey.OpenSubKey(UninstallPath))
                {
                    if (root == null) continue;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        ProgramEntry e = Read(root, name, view);
                        if (e != null) list.Add(e);
                    }
                }
            }
            return list;
        }

        public static ProgramEntry Find(string viewText, string key)
        {
            RegistryView view = viewText == "32" ? RegistryView.Registry32 : RegistryView.Registry64;
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey root = baseKey.OpenSubKey(UninstallPath))
                return root == null ? null : Read(root, key, view);
        }

        static ProgramEntry Read(RegistryKey root, string name, RegistryView view)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey(name))
                {
                    if (k == null) return null;
                    return new ProgramEntry
                    {
                        Key = name,
                        View = view == RegistryView.Registry32 ? "32" : "64",
                        Name = Text(k, "DisplayName"),
                        Version = Text(k, "DisplayVersion"),
                        Publisher = Text(k, "Publisher"),
                        WindowsInstaller = Number(k, "WindowsInstaller") == 1,
                        QuietUninstallString = Text(k, "QuietUninstallString"),
                        SystemComponent = Number(k, "SystemComponent") == 1,
                        NoRemove = Number(k, "NoRemove") == 1,
                        IsUpdate = Text(k, "ParentKeyName").Length > 0
                                   || Text(k, "ReleaseType").IndexOf("Update", StringComparison.OrdinalIgnoreCase) >= 0
                                   || Text(k, "ReleaseType").IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0
                    };
                }
            }
            catch (Exception) { return null; }
        }

        static string Text(RegistryKey k, string value)
        {
            object v = k.GetValue(value);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        static int Number(RegistryKey k, string value)
        {
            object v = k.GetValue(value);
            if (v is int) return (int)v;
            int n;
            return v != null && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out n) ? n : 0;
        }

        /// <summary>"programs": one line per listed entry (hidden ones left out), then a done line.</summary>
        public static int List(Action<string> emit)
        {
            List<string> roots = AllowedRoots();
            int count = 0;
            foreach (ProgramEntry e in ReadAll())
            {
                string reason;
                ProgramKind kind = ProgramPolicy.Classify(e, roots, out reason);
                if (kind == ProgramKind.Hidden) continue;
                emit(ProgramPolicy.ToLine(e, kind, reason));
                count++;
            }
            emit(WorkerProtocol.Format("type", WorkerProtocol.TypeDone, "programs", count.ToString(CultureInfo.InvariantCulture), "computer", Environment.MachineName));
            return 0;
        }

        /// <summary>"uninstall-program": one entry, read again here. Returns the exit code of the worker.</summary>
        public static int Uninstall(string view, string key, string expectedName, bool allowServers, bool allowDomainControllers, Action<string> emit)
        {
            string os;
            int productType = ComputerInfo.ReadOperatingSystem(out os);
            ServerDecision guard = ServerPolicy.Decide(ProgramPolicy.UninstallCommand, productType, allowServers, allowDomainControllers);
            if (guard != ServerDecision.Allowed)
                return Result(emit, false, false, null, "Uninstall refused on this server (ProductType " + productType + "): it needs 'Include servers' in LiteDeploy.", 102);

            ProgramEntry e = Find(view, key);
            if (e == null) return Result(emit, true, false, null, "Not installed (the entry is gone).", 0);
            if (!string.Equals(e.Name, expectedName, StringComparison.Ordinal))
                return Result(emit, false, false, null, "The entry " + key + " is now \"" + e.Name + "\", not \"" + expectedName + "\": nothing done.", 1);
            List<string> roots = AllowedRoots();
            string reason;
            ProgramKind kind = ProgramPolicy.Classify(e, roots, out reason);
            if (kind != ProgramKind.Msi && kind != ProgramKind.QuietExe)
                return Result(emit, false, false, null, "Not removable by LiteDeploy (" + (reason ?? kind.ToString()) + "): " + e.Name, 1);

            string work = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiteDeploy", "work");
            string log = null;
            ProcessStartInfo psi;
            if (kind == ProgramKind.Msi)
            {
                Directory.CreateDirectory(work);
                log = Path.Combine(work, "uninstall-" + key.Trim('{', '}') + ".log");
                psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), ProgramPolicy.MsiArguments(key, log));
            }
            else
            {
                string[] command = ProgramPolicy.SplitCommand(e.QuietUninstallString);
                if (!File.Exists(command[0])) return Result(emit, false, false, null, "Uninstaller not found: " + command[0], 1);
                psi = new ProcessStartInfo(command[0], command[1]) { WorkingDirectory = Path.GetDirectoryName(command[0]) };
            }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            int code;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit((int)TimeSpan.FromMinutes(ProgramPolicy.TimeoutMinutes).TotalMilliseconds))
                {
                    try { p.Kill(); } catch (Exception) { }
                    return Result(emit, false, false, null, "The uninstaller did not finish within " + ProgramPolicy.TimeoutMinutes + " minutes.", 1460);
                }
                code = p.ExitCode;
            }
            bool restart;
            if (!ProgramPolicy.IsSuccess(code, out restart))
                return Result(emit, false, false, code, "Uninstall of " + e.Name + " failed, exit code " + code + (log != null ? " (log: " + log + ")" : ""), 1);
            // Some EXE uninstallers start a copy of themselves and return at once: wait a little for the entry to go
            for (int i = 0; i < 12 && Find(view, key) != null; i++) System.Threading.Thread.Sleep(5000);
            if (Find(view, key) != null)
                return Result(emit, false, restart, code, "The uninstaller of " + e.Name + " ended (code " + code + ") but the program is still listed.", 1);
            if (log != null) { try { File.Delete(log); } catch (Exception) { } }
            return Result(emit, true, restart, code, "Uninstalled " + e.Name + " " + e.Version + (restart ? " (restart needed)" : ""), restart ? 3010 : 0);
        }

        static int Result(Action<string> emit, bool ok, bool restart, int? code, string message, int exit)
        {
            emit(WorkerProtocol.Format("type", ProgramPolicy.TypeResult, "result", ok ? "ok" : "failed", "restart", restart ? "yes" : "no",
                "code", code.HasValue ? code.Value.ToString(CultureInfo.InvariantCulture) : "", "computer", Environment.MachineName, "message", message));
            return exit;
        }
    }
}
